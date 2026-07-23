using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public sealed class TrialSelectionAuditResult
{
    public long? SelectedTrialId { get; init; }
    public int? SelectedTrialNumber { get; init; }
    public string? ParameterFingerprint { get; init; }
    public ValidationTrialStatus? TrialStatus { get; init; }
    public string? GuardrailDecision { get; init; }
    public bool IsEligibleForSelection { get; init; }
    public int? TrainingRank { get; init; }
    public decimal? TrainingScore { get; init; }
    public IReadOnlyList<string> FailedGuardrails { get; init; } = [];
    public string? FrozenParameterFingerprint { get; init; }
    public bool FrozenFingerprintMatchesSelected { get; init; }
    public ValidationSelectionIntegrityStatus IntegrityStatus { get; init; }
    public string? PolicyVersion { get; init; }
    public string Explanation { get; init; } = string.Empty;
    public StrategyRobustnessDecision? DerivedIntegrityVerdict { get; init; }
}

public sealed class TrialPopulationSummary
{
    public int RequestedTrialCount { get; init; }
    public int GeneratedTrialCount { get; init; }
    public int UniqueParameterFingerprints { get; init; }
    public int TerminalTrialCount { get; init; }
    public int CompletedEligibleCount { get; init; }
    public int GuardrailRejectedCount { get; init; }
    public int FailedCount { get; init; }
    public int InterruptedCount { get; init; }
    public int PendingCount { get; init; }
    public int RunningCount { get; init; }
    public int RankedCount { get; init; }
    public int DuplicateFingerprintCount { get; init; }
    public int RecoveredFromStrategyLabRunCount { get; init; }
}

public interface IValidationTrialSelectionAuditor
{
    TrialSelectionAuditResult AuditSelection(
        ValidationExperiment experiment,
        IReadOnlyList<ValidationParameterTrial> trials);

    TrialPopulationSummary SummarizePopulation(
        ValidationExperiment experiment,
        IReadOnlyList<ValidationParameterTrial> trials);
}

public sealed class ValidationTrialSelectionAuditor : IValidationTrialSelectionAuditor
{
    public TrialSelectionAuditResult AuditSelection(
        ValidationExperiment experiment,
        IReadOnlyList<ValidationParameterTrial> trials)
    {
        var ranked = ValidationTrialRanker.OrderForRanking(trials).ToList();
        var winner = ranked.FirstOrDefault();
        ValidationParameterTrial? selected = null;

        if (!string.IsNullOrWhiteSpace(experiment.FrozenParameterFingerprint))
        {
            selected = trials.FirstOrDefault(t =>
                string.Equals(t.ParameterFingerprint, experiment.FrozenParameterFingerprint, StringComparison.Ordinal));
        }

        if (selected is null && experiment.TrainingStrategyLabRunId is long runId)
        {
            selected = trials.FirstOrDefault(t => t.StrategyLabRunId == runId);
        }

        selected ??= winner ?? trials.OrderBy(t => t.TrialNumber).FirstOrDefault();

        var failedGuardrails = ParseGuardrailFailures(selected?.GuardrailFailureReasonsJson);
        var guardrailPassed = string.Equals(selected?.GuardrailDecision, "Passed", StringComparison.OrdinalIgnoreCase);
        var eligible = guardrailPassed && selected?.Status == ValidationTrialStatus.Completed;

        var frozenMatch = selected is not null
            && !string.IsNullOrWhiteSpace(experiment.FrozenParameterFingerprint)
            && string.Equals(selected.ParameterFingerprint, experiment.FrozenParameterFingerprint, StringComparison.Ordinal);

        ValidationSelectionIntegrityStatus status;
        StrategyRobustnessDecision? derived = null;
        string explanation;

        if (experiment.ExperimentType == ValidationExperimentType.ValidateExistingFrozenConfiguration)
        {
            status = ValidationSelectionIntegrityStatus.NotEvaluated;
            explanation = "Single-configuration validation — training trial selection not applicable.";
        }
        else if (ranked.Count == 0 && selected is not null && !guardrailPassed)
        {
            status = ValidationSelectionIntegrityStatus.InvalidSelectedTrial;
            derived = StrategyRobustnessDecision.FailedNoTrainingTrialPassedGuardrails;
            explanation =
                "All trials failed guardrails but holdout was executed without an explicit fallback selection policy.";
        }
        else if (winner is not null && guardrailPassed)
        {
            status = ValidationSelectionIntegrityStatus.Valid;
            explanation = "Guardrail-passed trial selected under default TrainingTrialSelection policy.";
        }
        else if (ranked.Count == 0)
        {
            status = ValidationSelectionIntegrityStatus.NoEligibleTrial;
            derived = StrategyRobustnessDecision.FailedNoTrainingTrialPassedGuardrails;
            explanation = "No trials passed training guardrails.";
        }
        else
        {
            status = ValidationSelectionIntegrityStatus.InvalidSelectedTrial;
            derived = StrategyRobustnessDecision.FailedNoTrainingTrialPassedGuardrails;
            explanation = "Frozen/executed configuration does not match a guardrail-passed ranked trial.";
        }

        return new TrialSelectionAuditResult
        {
            SelectedTrialId = selected?.Id,
            SelectedTrialNumber = selected?.TrialNumber,
            ParameterFingerprint = selected?.ParameterFingerprint,
            TrialStatus = selected?.Status,
            GuardrailDecision = selected?.GuardrailDecision,
            IsEligibleForSelection = eligible,
            TrainingRank = selected?.Rank,
            TrainingScore = selected?.TrainingScore,
            FailedGuardrails = failedGuardrails,
            FrozenParameterFingerprint = experiment.FrozenParameterFingerprint,
            FrozenFingerprintMatchesSelected = frozenMatch,
            IntegrityStatus = status,
            PolicyVersion = "TrainingTrialSelection/Default/v1 (GuardrailPassedRequired)",
            Explanation = explanation,
            DerivedIntegrityVerdict = derived
        };
    }

    public TrialPopulationSummary SummarizePopulation(
        ValidationExperiment experiment,
        IReadOnlyList<ValidationParameterTrial> trials)
    {
        var fingerprints = trials.Select(t => t.ParameterFingerprint).ToList();
        var unique = fingerprints.Distinct(StringComparer.Ordinal).Count();
        var dupes = fingerprints.Count - unique;
        var terminal = trials.Count(t =>
            t.Status is ValidationTrialStatus.Completed
                or ValidationTrialStatus.GuardrailRejected
                or ValidationTrialStatus.Failed
                or ValidationTrialStatus.Interrupted);
        var eligible = trials.Count(t =>
            string.Equals(t.GuardrailDecision, "Passed", StringComparison.OrdinalIgnoreCase));

        return new TrialPopulationSummary
        {
            RequestedTrialCount = experiment.MaximumTrials,
            GeneratedTrialCount = trials.Count,
            UniqueParameterFingerprints = unique,
            TerminalTrialCount = terminal,
            CompletedEligibleCount = eligible,
            GuardrailRejectedCount = trials.Count(t => t.Status == ValidationTrialStatus.GuardrailRejected),
            FailedCount = trials.Count(t => t.Status == ValidationTrialStatus.Failed),
            InterruptedCount = trials.Count(t => t.Status == ValidationTrialStatus.Interrupted),
            PendingCount = trials.Count(t => t.Status == ValidationTrialStatus.Pending),
            RunningCount = trials.Count(t => t.Status == ValidationTrialStatus.Running),
            RankedCount = trials.Count(t => t.Rank.HasValue),
            DuplicateFingerprintCount = dupes,
            RecoveredFromStrategyLabRunCount = trials.Count(t =>
                t.RecoverySource == ValidationTrialRecoverySource.ExistingStrategyLabRun)
        };
    }

    private static IReadOnlyList<string> ParseGuardrailFailures(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [json];
        }
    }
}
