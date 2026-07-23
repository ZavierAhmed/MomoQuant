using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;
using Microsoft.Extensions.Logging;

namespace MomoQuant.Application.ValidationLab;

public sealed class ValidationTrainingPreflightResult
{
    public bool Passed { get; init; }
    public IReadOnlyList<string> Failures { get; init; } = [];
}

public interface IValidationTrainingPreflightService
{
    Task<ValidationTrainingPreflightResult> CheckAsync(
        ValidationExperiment experiment,
        bool requireNoActiveLease,
        CancellationToken cancellationToken = default);
}

public sealed class ValidationTrainingPreflightService : IValidationTrainingPreflightService
{
    private readonly IValidationTrainingDatabaseProbe _database;
    private readonly IValidationExperimentExecutionLeaseRepository _leases;

    public ValidationTrainingPreflightService(
        IValidationTrainingDatabaseProbe database,
        IValidationExperimentExecutionLeaseRepository leases)
    {
        _database = database;
        _leases = leases;
    }

    public async Task<ValidationTrainingPreflightResult> CheckAsync(
        ValidationExperiment experiment,
        bool requireNoActiveLease,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();

        var connect = await _database.CanConnectAsync(cancellationToken);
        if (!connect.Succeeded)
        {
            failures.Add(connect.ErrorMessage ?? "MySQL connectivity failed.");
        }

        var pending = await _database.GetPendingMigrationNamesAsync(cancellationToken);
        if (pending.Count > 0)
        {
            failures.Add($"Pending migrations: {string.Join(", ", pending)}");
        }

        if (experiment.TotalEligibleCandleCount <= 0)
        {
            failures.Add("Candle coverage is missing. Prepare data first.");
        }

        if (requireNoActiveLease)
        {
            var lease = await _leases.GetByExperimentIdAsync(experiment.Id, cancellationToken);
            if (lease is not null && lease.ExpiresAtUtc > DateTime.UtcNow)
            {
                failures.Add(
                    $"Experiment {experiment.Id} has an active training lease owned by '{lease.LeaseOwner}' until {lease.ExpiresAtUtc:O}.");
            }
        }

        return new ValidationTrainingPreflightResult
        {
            Passed = failures.Count == 0,
            Failures = failures
        };
    }
}

public interface IValidationTrainingExecutionLeaseService
{
    Task<(bool Acquired, string? ConflictMessage)> TryAcquireAsync(
        long experimentId,
        string leaseOwner,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    Task<ValidationLeaseOperationResult> HeartbeatAsync(
        long experimentId,
        string leaseOwner,
        TimeSpan ttl,
        CancellationToken cancellationToken = default);

    Task<ValidationLeaseOperationResult> ReleaseAsync(
        long experimentId,
        string leaseOwner,
        CancellationToken cancellationToken = default);

    Task<bool> IsActiveAsync(long experimentId, CancellationToken cancellationToken = default);
}

public sealed class ValidationTrainingExecutionLeaseService : IValidationTrainingExecutionLeaseService
{
    private readonly IValidationExperimentExecutionLeaseRepository _leases;

    public ValidationTrainingExecutionLeaseService(IValidationExperimentExecutionLeaseRepository leases) =>
        _leases = leases;

    public async Task<(bool Acquired, string? ConflictMessage)> TryAcquireAsync(
        long experimentId,
        string leaseOwner,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var acquired = await _leases.TryAcquireAtomicAsync(
            experimentId,
            leaseOwner,
            acquiredAtUtc: now,
            expiresAtUtc: now.Add(ttl),
            heartbeatAtUtc: now,
            cancellationToken);

        if (acquired)
        {
            return (true, null);
        }

        var existing = await _leases.GetByExperimentIdAsync(experimentId, cancellationToken);
        var conflict = existing is null
            ? "Lease acquisition conflict."
            : $"Active lease held by '{existing.LeaseOwner}' until {existing.ExpiresAtUtc:O}.";
        return (false, conflict);
    }

    public async Task<ValidationLeaseOperationResult> HeartbeatAsync(
        long experimentId,
        string leaseOwner,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var ok = await _leases.TryHeartbeatOwnedAsync(
            experimentId,
            leaseOwner,
            expiresAtUtc: now.Add(ttl),
            heartbeatAtUtc: now,
            cancellationToken);

        if (ok)
        {
            return ValidationLeaseOperationResult.Ok();
        }

        var existing = await _leases.GetByExperimentIdAsync(experimentId, cancellationToken);
        if (existing is null)
        {
            return ValidationLeaseOperationResult.NotFound($"No lease found for experiment {experimentId}.");
        }

        return ValidationLeaseOperationResult.Conflict(
            $"Heartbeat rejected: lease owned by '{existing.LeaseOwner}', caller '{leaseOwner}'.");
    }

    public async Task<ValidationLeaseOperationResult> ReleaseAsync(
        long experimentId,
        string leaseOwner,
        CancellationToken cancellationToken = default)
    {
        var ok = await _leases.TryReleaseOwnedAsync(experimentId, leaseOwner, cancellationToken);
        if (ok)
        {
            return ValidationLeaseOperationResult.Ok();
        }

        var existing = await _leases.GetByExperimentIdAsync(experimentId, cancellationToken);
        if (existing is null)
        {
            return ValidationLeaseOperationResult.NotFound($"No lease found for experiment {experimentId}.");
        }

        return ValidationLeaseOperationResult.Conflict(
            $"Release rejected: lease owned by '{existing.LeaseOwner}', caller '{leaseOwner}'.");
    }

    public async Task<bool> IsActiveAsync(long experimentId, CancellationToken cancellationToken = default)
    {
        var lease = await _leases.GetByExperimentIdAsync(experimentId, cancellationToken);
        return lease is not null && lease.ExpiresAtUtc > DateTime.UtcNow;
    }
}

public sealed class ValidationTrainingProgressDto
{
    public int RequestedTrialCount { get; init; }
    public int GeneratedTrialCount { get; init; }
    public int PendingTrialCount { get; init; }
    public int RunningTrialCount { get; init; }
    public int CompletedTrialCount { get; init; }
    public int FailedTrialCount { get; init; }
    public int InterruptedTrialCount { get; init; }
    public int SkippedCompletedTrialCount { get; init; }
    public decimal ProgressPercent { get; init; }
    public DateTime? LastProgressAtUtc { get; init; }
    public int? ActiveTrialNumber { get; init; }
    public long? ActiveStrategyLabRunId { get; init; }
}

public static class ValidationTrainingProgressCalculator
{
    public static ValidationTrainingProgressDto Calculate(
        ValidationExperiment experiment,
        IReadOnlyList<ValidationParameterTrial> trials,
        int generatedTrialCount)
    {
        var terminal = trials.Count(t => IsTerminal(t.Status));
        var completed = trials.Count(t =>
            t.Status is ValidationTrialStatus.Completed or ValidationTrialStatus.GuardrailRejected);
        var failed = trials.Count(t =>
            t.Status is ValidationTrialStatus.Failed or ValidationTrialStatus.LeakageFailed);
        var interrupted = trials.Count(t => t.Status == ValidationTrialStatus.Interrupted);
        var running = trials.Count(t => t.Status == ValidationTrialStatus.Running);
        var pending = trials.Count(t => t.Status == ValidationTrialStatus.Pending);
        var active = trials.FirstOrDefault(t => t.Status == ValidationTrialStatus.Running);

        var requested = experiment.MaximumTrials;
        var generated = Math.Max(generatedTrialCount, trials.Count);
        var progress = generated == 0
            ? experiment.PercentComplete
            : 25m + 50m * terminal / generated;

        var last = trials
            .Where(t => t.CompletedAtUtc.HasValue || t.StartedAtUtc.HasValue)
            .Select(t => t.CompletedAtUtc ?? t.StartedAtUtc)
            .DefaultIfEmpty(experiment.UpdatedAtUtc)
            .Max();

        return new ValidationTrainingProgressDto
        {
            RequestedTrialCount = requested,
            GeneratedTrialCount = generated,
            PendingTrialCount = pending,
            RunningTrialCount = running,
            CompletedTrialCount = completed,
            FailedTrialCount = failed,
            InterruptedTrialCount = interrupted,
            SkippedCompletedTrialCount = completed,
            ProgressPercent = progress,
            LastProgressAtUtc = last,
            ActiveTrialNumber = active?.TrialNumber,
            ActiveStrategyLabRunId = active?.StrategyLabRunId
        };
    }

    private static bool IsTerminal(ValidationTrialStatus status) =>
        status is ValidationTrialStatus.Completed
            or ValidationTrialStatus.GuardrailRejected
            or ValidationTrialStatus.Failed
            or ValidationTrialStatus.LeakageFailed
            or ValidationTrialStatus.Interrupted;
}

/// <summary>
/// Validation training DB retry — MaxAttempts means total attempts (exact).
/// Delegates to <see cref="TransientDatabaseRetryPolicy"/>.
/// </summary>
public static class ValidationTrainingDbRetry
{
    public const int MaxAttempts = TransientDatabaseRetryPolicy.DefaultMaxAttempts;

    public static Task ExecuteAsync(
        Func<Task> action,
        CancellationToken cancellationToken = default,
        ILogger? logger = null,
        string? correlationId = null) =>
        TransientDatabaseRetryPolicy.ExecuteAsync(
            action,
            MaxAttempts,
            operationName: "ValidationTraining",
            correlationId,
            logger,
            cancellationToken);

    public static bool IsTransient(Exception ex) => TransientDatabaseRetryPolicy.IsTransient(ex);
}
