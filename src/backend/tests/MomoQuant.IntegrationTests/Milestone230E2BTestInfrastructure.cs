using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;
using MomoQuant.Persistence.Repositories;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0E2B — factory wiring deterministic commit/confirmation fault injection around the
/// REAL production repository, transaction, and MySQL database. The production repository algorithm
/// executes unmodified; only the narrow transaction-boundary and confirmation-reader seams are
/// replaced with scriptable implementations.
/// </summary>
public sealed class E2BSeamFactory : MomoQuantWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ScriptableTransactionBoundary>();
            services.RemoveAll<IValidationAccessAuditTransactionBoundary>();
            services.AddScoped<IValidationAccessAuditTransactionBoundary>(sp =>
                sp.GetRequiredService<ScriptableTransactionBoundary>());

            services.AddSingleton<ScriptableConfirmationReader>(sp =>
                new ScriptableConfirmationReader(sp.GetRequiredService<IServiceScopeFactory>()));
            services.RemoveAll<IValidationAccessAuditConfirmationReader>();
            services.AddScoped<IValidationAccessAuditConfirmationReader>(sp =>
                sp.GetRequiredService<ScriptableConfirmationReader>());

            // Deterministic zero-delay bounded retry for tests (same attempt limits as production).
            services.RemoveAll<IValidationAccessPersistenceRetryPolicy>();
            services.AddSingleton<IValidationAccessPersistenceRetryPolicy>(
                new ValidationAccessPersistenceRetryPolicy(
                    recoveryConfirmationTimeout: TimeSpan.FromSeconds(15),
                    delay: (_, _) => Task.CompletedTask));
        });
    }

    public ScriptableTransactionBoundary Boundary =>
        Services.GetRequiredService<ScriptableTransactionBoundary>();

    public ScriptableConfirmationReader Reader =>
        Services.GetRequiredService<ScriptableConfirmationReader>();

    public void ResetSeams()
    {
        Boundary.Reset();
        Reader.Reset();
    }
}

public enum CommitFaultMode
{
    CommitNormally = 0,

    /// <summary>Throws a transient failure without calling the underlying commit (known no-commit).</summary>
    ThrowBeforeCommit = 1,

    /// <summary>Performs the REAL MySQL commit, then throws a typed ambiguous-outcome exception.</summary>
    CommitThenThrowOutcomeUnknown = 2,

    /// <summary>Performs the REAL MySQL commit, then throws OperationCanceledException.</summary>
    CommitThenThrowOperationCanceled = 3
}

public sealed class ScriptableTransactionBoundary : IValidationAccessAuditTransactionBoundary
{
    private readonly ValidationAccessAuditTransactionBoundary _production = new();

    public CommitFaultMode Mode { get; set; } = CommitFaultMode.CommitNormally;

    /// <summary>Number of commit calls the fault applies to before reverting to normal commits.</summary>
    public int RemainingFaults { get; set; }

    public int CommitCalls { get; private set; }
    public int RealCommits { get; private set; }
    public int RollbackCalls { get; private set; }

    public void Reset()
    {
        Mode = CommitFaultMode.CommitNormally;
        RemainingFaults = 0;
        CommitCalls = 0;
        RealCommits = 0;
        RollbackCalls = 0;
    }

    public async Task CommitAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        CommitCalls++;

        if (RemainingFaults > 0 && Mode != CommitFaultMode.CommitNormally)
        {
            RemainingFaults--;
            switch (Mode)
            {
                case CommitFaultMode.ThrowBeforeCommit:
                    throw new TimeoutException("Simulated transient failure before commit reached the server.");

                case CommitFaultMode.CommitThenThrowOutcomeUnknown:
                    await _production.CommitAsync(transaction, cancellationToken);
                    RealCommits++;
                    throw new ValidationAccessCommitOutcomeUnknownException(
                        "Simulated connection loss after the server committed.",
                        new TimeoutException("Simulated transient network failure."));

                case CommitFaultMode.CommitThenThrowOperationCanceled:
                    await _production.CommitAsync(transaction, cancellationToken);
                    RealCommits++;
                    throw new OperationCanceledException("Simulated cancellation observed during commit.");
            }
        }

        await _production.CommitAsync(transaction, cancellationToken);
        RealCommits++;
    }

    public Task RollbackAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        RollbackCalls++;
        return _production.RollbackAsync(transaction, cancellationToken);
    }
}

public sealed class ScriptableConfirmationReader : IValidationAccessAuditConfirmationReader
{
    private readonly IServiceScopeFactory _scopeFactory;
    private ValidationAccessAuditConfirmationReader? _inner;

    public ScriptableConfirmationReader(IServiceScopeFactory scopeFactory) =>
        _scopeFactory = scopeFactory;

    /// <summary>Reads with index greater than this value are eligible for scripted failures.</summary>
    public int HealthyReadsBeforeFault { get; set; } = int.MaxValue;

    /// <summary>Number of faulted reads remaining (throw or subset depending on DropOneRowInsteadOfThrow).</summary>
    public int RemainingFaults { get; set; }

    /// <summary>When true, faulted reads return a subset (one row dropped) instead of throwing.</summary>
    public bool DropOneRowInsteadOfThrow { get; set; }

    public int ReadCalls { get; private set; }
    public List<Guid> ObservedContextIds { get; } = new();

    public bool UsesFreshContext => true;

    public Guid LastConfirmationContextId => Inner.LastConfirmationContextId;

    private ValidationAccessAuditConfirmationReader Inner =>
        _inner ??= new ValidationAccessAuditConfirmationReader(_scopeFactory);

    public void Reset()
    {
        HealthyReadsBeforeFault = int.MaxValue;
        RemainingFaults = 0;
        DropOneRowInsteadOfThrow = false;
        ReadCalls = 0;
        ObservedContextIds.Clear();
    }

    public async Task<IReadOnlyList<ValidationCandleAccessAudit>> ReadAsync(
        IReadOnlyCollection<Guid> accessEventIds,
        CancellationToken cancellationToken)
    {
        ReadCalls++;
        var faulted = ReadCalls > HealthyReadsBeforeFault && RemainingFaults > 0;
        if (faulted && !DropOneRowInsteadOfThrow)
        {
            RemainingFaults--;
            throw new InvalidOperationException(
                "Simulated confirmation outage — transient failure reading durable audit state.");
        }

        var rows = await Inner.ReadAsync(accessEventIds, cancellationToken);
        ObservedContextIds.Add(Inner.LastConfirmationContextId);

        if (faulted && DropOneRowInsteadOfThrow)
        {
            RemainingFaults--;
            return rows.OrderBy(r => r.ScopeSequenceNumber).Take(Math.Max(0, rows.Count - 1)).ToList();
        }

        return rows;
    }
}

internal static class E2BAuditFixtures
{
    public static ValidationCandleAccessAudit NewAudit(
        long experimentId,
        Guid accessEventId,
        Guid scopeExecutionId,
        long seq,
        string caller,
        bool wasDenied = false,
        string? fingerprint = "ABCD1234",
        DateTime? accessedAtUtc = null,
        string? recorderVersion = null) =>
        new()
        {
            AccessEventId = accessEventId,
            ScopeExecutionId = scopeExecutionId,
            ScopeSequenceNumber = seq,
            ValidationExperimentId = experimentId,
            TrialNumber = 1,
            CallerComponent = caller,
            AccessPurpose = "EvaluationRange",
            DatasetPartition = "Training",
            CandleContentFingerprint = fingerprint,
            AccessedAtUtc = accessedAtUtc ?? new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            WasDenied = wasDenied,
            DenialCode = wasDenied ? "DENIED" : null,
            ReturnedCandleCount = 3,
            FlushAttemptCount = 1,
            PersistedAtUtc = DateTime.UtcNow,
            RecorderVersion = recorderVersion ?? ValidationCandleAccessRecorder.RecorderVersion,
            CreatedAtUtc = DateTime.UtcNow
        };

    public static async Task CleanupAsync(MomoQuantWebApplicationFactory factory, long experimentId)
    {
        await using var cleanup = factory.Services.CreateAsyncScope();
        var db = cleanup.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        await db.ValidationCandleAccessAudits
            .Where(a => a.ValidationExperimentId == experimentId)
            .ExecuteDeleteAsync();
    }
}
