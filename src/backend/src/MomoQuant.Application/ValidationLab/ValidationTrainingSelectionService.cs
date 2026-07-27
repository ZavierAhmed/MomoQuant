using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public sealed class TrainingSelectionPopulationSummary
{
    public int RequestedTrialCount { get; init; }
    public int GeneratedTrialCount { get; init; }
    public int UniqueParameterFingerprintCount { get; init; }
    public int TerminalTrialCount { get; init; }
    public int GuardrailPassedTrialCount { get; init; }
    public int GuardrailRejectedTrialCount { get; init; }
    public int FailedTrialCount { get; init; }
    public int InterruptedTrialCount { get; init; }
    public int CancelledTrialCount { get; init; }
    public int EligibleTrialCount { get; init; }
    public int RankedTrialCount { get; init; }
    public int SelectedTrialCount { get; init; }
    public int DuplicateFingerprintCount { get; init; }
}

public sealed class TrainingSelectionFinalizeResult
{
    public bool Succeeded { get; init; }
    public ValidationParameterTrial? SelectedTrial { get; init; }
    public TrainingSelectionPopulationSummary Population { get; init; } = new();
    public ValidationSelectionIntegrityStatus IntegrityStatus { get; init; }
    public StrategyRobustnessDecision? FailureCode { get; init; }
    public string? FailureMessage { get; init; }
    public bool ShouldFailExperiment { get; init; }
    /// <summary>True when guardrail-passed trials exist but authoritative audit evidence is incomplete.</summary>
    public bool AuditEvidenceIncomplete { get; init; }
}

public interface IValidationTrainingSelectionService
{
    TrainingSelectionPopulationSummary SummarizePopulation(
        ValidationExperiment experiment,
        IReadOnlyList<ValidationParameterTrial> trials);

    TrainingSelectionFinalizeResult FinalizeTrainingSelection(
        ValidationExperiment experiment,
        IReadOnlyList<ValidationParameterTrial> trials);
}

public sealed class ValidationTrainingSelectionService : IValidationTrainingSelectionService
{
    public const string DefaultPolicyVersion = "TrainingTrialSelection/Default/v1 (GuardrailPassedRequired)";
    public const string ZeroEligibleMessage =
        "All completed training trials failed the frozen training guardrails. No configuration was eligible for selection, so holdout validation was not run.";
    public const string AuditIncompleteMessage =
        ValidationAuthoritativeAuditQualificationEvaluator.UserSafeIncompleteMessage;

    public TrainingSelectionPopulationSummary SummarizePopulation(
        ValidationExperiment experiment,
        IReadOnlyList<ValidationParameterTrial> trials)
    {
        var fingerprints = trials.Select(t => t.ParameterFingerprint).ToList();
        var unique = fingerprints.Distinct(StringComparer.Ordinal).Count();
        var terminal = trials.Count(t =>
            t.Status is ValidationTrialStatus.Completed
                or ValidationTrialStatus.GuardrailRejected
                or ValidationTrialStatus.Failed
                or ValidationTrialStatus.LeakageFailed
                or ValidationTrialStatus.AuditPersistenceFailed
                or ValidationTrialStatus.Interrupted);
        // v1.3.2 experiments additionally require snapshot rank eligibility (Milestone 23.0D).
        var useSnapshotEligibility =
            ValidationMetricsContract.IsPopulationPathMetricsVersion(experiment.ValidationMetricsVersion);
        var guardrailPassed = trials.Count(
            ValidationAuthoritativeAuditQualificationEvaluator.IsGuardrailPassedCompleted);
        var eligible = trials.Count(t =>
            ValidationAuthoritativeAuditQualificationEvaluator.MeetsCachedAuditEligibilityFields(t)
            && (!useSnapshotEligibility || ValidationTrialRanker.IsSnapshotRankEligible(t)));

        return new TrainingSelectionPopulationSummary
        {
            RequestedTrialCount = experiment.MaximumTrials,
            GeneratedTrialCount = trials.Count,
            UniqueParameterFingerprintCount = unique,
            TerminalTrialCount = terminal,
            GuardrailPassedTrialCount = guardrailPassed,
            GuardrailRejectedTrialCount = trials.Count(t => t.Status == ValidationTrialStatus.GuardrailRejected),
            FailedTrialCount = trials.Count(t =>
                t.Status is ValidationTrialStatus.Failed
                    or ValidationTrialStatus.LeakageFailed
                    or ValidationTrialStatus.AuditPersistenceFailed),
            InterruptedTrialCount = trials.Count(t => t.Status == ValidationTrialStatus.Interrupted),
            CancelledTrialCount = 0,
            EligibleTrialCount = eligible,
            RankedTrialCount = trials.Count(t => t.Rank.HasValue),
            SelectedTrialCount = experiment.SelectedTrialId.HasValue ? 1 : 0,
            DuplicateFingerprintCount = fingerprints.Count - unique
        };
    }

    public TrainingSelectionFinalizeResult FinalizeTrainingSelection(
        ValidationExperiment experiment,
        IReadOnlyList<ValidationParameterTrial> trials)
    {
        var population = SummarizePopulation(experiment, trials);

        if (experiment.ExperimentType == ValidationExperimentType.ValidateExistingFrozenConfiguration)
        {
            return new TrainingSelectionFinalizeResult
            {
                Succeeded = true,
                Population = population,
                IntegrityStatus = ValidationSelectionIntegrityStatus.NotEvaluated
            };
        }

        var trialList = trials.ToList();
        var useSnapshotEligibility =
            ValidationMetricsContract.IsPopulationPathMetricsVersion(experiment.ValidationMetricsVersion);
        ValidationTrialRanker.AssignRanks(trialList, useSnapshotEligibility);
        var winner = ValidationTrialRanker.SelectWinner(trialList, useSnapshotEligibility);

        if (population.EligibleTrialCount == 0)
        {
            if (experiment.AllowInfrastructureOnlyRejectedTrialFallback && trials.Count > 0)
            {
                var fallback = trials
                    .OrderByDescending(t => t.TrainingScore ?? decimal.MinValue)
                    .ThenBy(t => t.TrialNumber)
                    .First();
                return new TrainingSelectionFinalizeResult
                {
                    Succeeded = true,
                    SelectedTrial = fallback,
                    Population = CopyPopulation(population, selectedTrialCount: 1),
                    IntegrityStatus = ValidationSelectionIntegrityStatus.InfrastructureOnlyFallback,
                    FailureMessage = "Infrastructure-only fallback selection was used."
                };
            }

            // Audit incompleteness is distinct from snapshot-rank ineligibility (v1.3.2).
            var auditIncomplete = trialList.Any(t =>
                ValidationAuthoritativeAuditQualificationEvaluator.IsGuardrailPassedCompleted(t)
                && (t.AuthoritativeAuditExecutionId is null
                    || t.AuditCompletionStatus != ValidationAuditCompletionStatus.Complete));
            return new TrainingSelectionFinalizeResult
            {
                Succeeded = false,
                Population = population,
                IntegrityStatus = ValidationSelectionIntegrityStatus.FailedNoEligibleTrials,
                FailureCode = auditIncomplete
                    ? null
                    : StrategyRobustnessDecision.FailedNoTrainingTrialPassedGuardrails,
                FailureMessage = auditIncomplete ? AuditIncompleteMessage : ZeroEligibleMessage,
                ShouldFailExperiment = true,
                AuditEvidenceIncomplete = auditIncomplete
            };
        }

        if (winner is null)
        {
            var auditIncomplete = trialList.Any(t =>
                ValidationAuthoritativeAuditQualificationEvaluator.IsGuardrailPassedCompleted(t)
                && (t.AuthoritativeAuditExecutionId is null
                    || t.AuditCompletionStatus != ValidationAuditCompletionStatus.Complete));
            return new TrainingSelectionFinalizeResult
            {
                Succeeded = false,
                Population = population,
                IntegrityStatus = ValidationSelectionIntegrityStatus.FailedSelectedTrialMissing,
                FailureCode = auditIncomplete
                    ? null
                    : StrategyRobustnessDecision.FailedNoTrainingTrialPassedGuardrails,
                FailureMessage = auditIncomplete ? AuditIncompleteMessage : ZeroEligibleMessage,
                ShouldFailExperiment = true,
                AuditEvidenceIncomplete = auditIncomplete
            };
        }

        return new TrainingSelectionFinalizeResult
        {
            Succeeded = true,
            SelectedTrial = winner,
            Population = CopyPopulation(population, selectedTrialCount: 1, rankedTrialCount: population.EligibleTrialCount),
            IntegrityStatus = ValidationSelectionIntegrityStatus.Passed
        };
    }

    private static TrainingSelectionPopulationSummary CopyPopulation(
        TrainingSelectionPopulationSummary source,
        int? selectedTrialCount = null,
        int? rankedTrialCount = null) =>
        new()
        {
            RequestedTrialCount = source.RequestedTrialCount,
            GeneratedTrialCount = source.GeneratedTrialCount,
            UniqueParameterFingerprintCount = source.UniqueParameterFingerprintCount,
            TerminalTrialCount = source.TerminalTrialCount,
            GuardrailPassedTrialCount = source.GuardrailPassedTrialCount,
            GuardrailRejectedTrialCount = source.GuardrailRejectedTrialCount,
            FailedTrialCount = source.FailedTrialCount,
            InterruptedTrialCount = source.InterruptedTrialCount,
            CancelledTrialCount = source.CancelledTrialCount,
            EligibleTrialCount = source.EligibleTrialCount,
            RankedTrialCount = rankedTrialCount ?? source.RankedTrialCount,
            SelectedTrialCount = selectedTrialCount ?? source.SelectedTrialCount,
            DuplicateFingerprintCount = source.DuplicateFingerprintCount
        };
}
