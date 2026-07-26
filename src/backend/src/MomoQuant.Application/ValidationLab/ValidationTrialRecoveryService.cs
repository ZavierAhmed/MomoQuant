using System.Text.Json;
using System.Text.RegularExpressions;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.StrategyLab.Dtos;
using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public sealed class ValidationTrialRecoveryReport
{
    public IReadOnlyList<int> RecoveredTrialNumbers { get; init; } = [];
    public IReadOnlyList<int> UnrecoverableTrialNumbers { get; init; } = [];
    public IReadOnlyList<int> SkippedAlreadyPersisted { get; init; } = [];
    public IReadOnlyList<int> AuditRecoveryRequiredTrialNumbers { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
}

public interface IValidationTrialRecoveryService
{
    Task<ValidationTrialRecoveryReport> RecoverFromStrategyLabRunsAsync(
        ValidationExperiment experiment,
        IReadOnlyList<Dictionary<string, string>> combos,
        ValidationQualificationProfile profile,
        CancellationToken cancellationToken = default);
}

public sealed class ValidationTrialRecoveryService : IValidationTrialRecoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex TrialNameRegex = new(
        @"^VL-Train-(?<exp>\d+)-T(?<trial>\d+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IStrategyLabRunRepository _labRuns;
    private readonly IStrategyResearchCandidateRepository _candidates;
    private readonly IValidationParameterTrialRepository _trials;
    private readonly IValidationTrialMetricsRouter _trialMetricsRouter;

    public ValidationTrialRecoveryService(
        IStrategyLabRunRepository labRuns,
        IStrategyResearchCandidateRepository candidates,
        IValidationParameterTrialRepository trials,
        IValidationTrialMetricsRouter trialMetricsRouter)
    {
        _labRuns = labRuns;
        _candidates = candidates;
        _trials = trials;
        _trialMetricsRouter = trialMetricsRouter;
    }

    public async Task<ValidationTrialRecoveryReport> RecoverFromStrategyLabRunsAsync(
        ValidationExperiment experiment,
        IReadOnlyList<Dictionary<string, string>> combos,
        ValidationQualificationProfile profile,
        CancellationToken cancellationToken = default)
    {
        var prefix = $"VL-Train-{experiment.Id}-T";
        var runs = await _labRuns.GetByNamePrefixAsync(prefix, cancellationToken);
        var existing = await _trials.GetByExperimentIdAsync(experiment.Id, cancellationToken);
        var existingByFp = existing.ToDictionary(t => t.ParameterFingerprint, StringComparer.OrdinalIgnoreCase);

        var recovered = new List<int>();
        var skipped = new List<int>();
        var unrecoverable = new List<int>();
        var auditRecoveryRequired = new List<int>();

        for (var i = 0; i < combos.Count; i++)
        {
            var trialNumber = i + 1;
            var combo = combos[i];
            var fingerprint = ValidationLabService.ParameterFingerprint(combo);

            if (existingByFp.TryGetValue(fingerprint, out var persisted)
                && persisted.Status is ValidationTrialStatus.Completed or ValidationTrialStatus.GuardrailRejected)
            {
                skipped.Add(trialNumber);
                continue;
            }

            var runName = $"{prefix}{trialNumber}";
            var candidates = runs
                .Where(r => string.Equals(r.Name, runName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.CompletedAtUtc ?? r.CreatedAtUtc)
                .ToList();

            var run = candidates.FirstOrDefault(r => r.Status == StrategyLabRunStatus.Completed);
            if (run is null)
            {
                if (candidates.Count > 0)
                {
                    unrecoverable.Add(trialNumber);
                }

                continue;
            }

            if (!ParametersMatch(run.ParametersJson, combo))
            {
                unrecoverable.Add(trialNumber);
                continue;
            }

            var trial = await BuildRecoveredTrialAsync(
                experiment, trialNumber, combo, fingerprint, run, profile, cancellationToken);

            // Milestone 23.0E2C1 WP9 — copy durable audit identity from persisted row FIRST,
            // then gate Completed on authoritative + Complete.
            ValidationParameterTrial? update = null;
            if (existingByFp.TryGetValue(fingerprint, out update))
            {
                if (update.AuthoritativeAuditExecutionId is not null)
                {
                    trial.AuthoritativeAuditExecutionId = update.AuthoritativeAuditExecutionId;
                    trial.AuditAttemptNumber = update.AuditAttemptNumber;
                    trial.AuditCompletionStatus = update.AuditCompletionStatus;
                }

                trial.Id = update.Id;
            }

            // GuardrailRejected keeps that status; Completed requires durable audit Complete.
            if (trial.Status == ValidationTrialStatus.Completed)
            {
                var auditAllowsCompleted = trial.AuthoritativeAuditExecutionId is not null
                    && trial.AuditCompletionStatus == ValidationAuditCompletionStatus.Complete;

                if (!auditAllowsCompleted)
                {
                    trial.Status = ValidationTrialStatus.Interrupted;
                    trial.ErrorMessage =
                        "StrategyLab recovery restored metrics but durable audit completion is required before Completed.";
                    if (trial.AuthoritativeAuditExecutionId is not null)
                    {
                        trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;
                    }

                    auditRecoveryRequired.Add(trialNumber);
                }
            }

            if (update is not null)
            {
                await _trials.UpdateAsync(trial, cancellationToken);
            }
            else
            {
                await _trials.AddAsync(trial, cancellationToken);
                existingByFp[fingerprint] = trial;
            }

            if (!auditRecoveryRequired.Contains(trialNumber))
            {
                recovered.Add(trialNumber);
            }
            else
            {
                unrecoverable.Add(trialNumber);
            }
        }

        return new ValidationTrialRecoveryReport
        {
            RecoveredTrialNumbers = recovered,
            UnrecoverableTrialNumbers = unrecoverable,
            SkippedAlreadyPersisted = skipped,
            AuditRecoveryRequiredTrialNumbers = auditRecoveryRequired,
            Summary =
                $"Recovered {recovered.Count} trial(s); skipped {skipped.Count}; unrecoverable {unrecoverable.Count}; " +
                $"audit-recovery-required {auditRecoveryRequired.Count}."
        };
    }

    private async Task<ValidationParameterTrial> BuildRecoveredTrialAsync(
        ValidationExperiment experiment,
        int trialNumber,
        IReadOnlyDictionary<string, string> combo,
        string fingerprint,
        StrategyLabRun run,
        ValidationQualificationProfile profile,
        CancellationToken cancellationToken)
    {
        var candidateRows = await _candidates.GetByRunIdAsync(run.Id, cancellationToken);
        var trial = new ValidationParameterTrial
        {
            ValidationExperimentId = experiment.Id,
            TrialNumber = trialNumber,
            ParameterSnapshotJson = JsonSerializer.Serialize(combo, JsonOptions),
            ParameterFingerprint = fingerprint
        };

        // Explicit MetricsVersion routing (WP23): v1.3.2 uses the trial metrics calculator
        // (persisted snapshot, applicability-aware guardrails); older versions keep the
        // legacy summary/candidate mapping.
        _trialMetricsRouter.ApplyTrialMetrics(experiment, trial, run, candidateRows, profile);

        trial.StartedAtUtc = run.StartedAtUtc ?? run.CreatedAtUtc;
        trial.CompletedAtUtc = run.CompletedAtUtc ?? DateTime.UtcNow;
        trial.RecoverySource = ValidationTrialRecoverySource.ExistingStrategyLabRun;
        return trial;
    }

    private static bool ParametersMatch(string parametersJson, IReadOnlyDictionary<string, string> expected)
    {
        try
        {
            var actual = JsonSerializer.Deserialize<Dictionary<string, string>>(parametersJson, JsonOptions)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in expected)
            {
                if (!actual.TryGetValue(key, out var actualValue)
                    || !string.Equals(actualValue, value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return ValidationLabService.ParameterFingerprint(actual)
                == ValidationLabService.ParameterFingerprint(expected);
        }
        catch
        {
            return false;
        }
    }

}
