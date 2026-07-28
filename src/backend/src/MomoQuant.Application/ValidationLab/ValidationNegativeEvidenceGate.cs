using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Milestone 23.0E2C3A — shared production path for security-negative access evidence.
/// Positive proof remains scoped to authoritative verifier-complete executions only;
/// negative proof from any attempt is never discarded.
/// </summary>
public static class ValidationNegativeEvidenceGate
{
    public static IReadOnlyList<ValidationCandleAccessAudit> Scan(
        IEnumerable<ValidationCandleAccessAudit> allAudits) =>
        ValidationLeakageEvidenceSelector.CollectNegativeBlockingEvidence(allAudits);

    public static bool HasBlockingEvidence(IEnumerable<ValidationCandleAccessAudit> allAudits) =>
        Scan(allAudits).Count > 0;

    public static ValidationTrainingFailureRecord CreateCanonicalBoundaryRecord(
        ValidationTrainingFailurePhase phase = ValidationTrainingFailurePhase.TrialBody) =>
        new()
        {
            Code = ValidationTrainingFailureCodes.ValidationDataLeakage,
            Category = ValidationTrainingFailureCategory.Boundary,
            Precedence = ValidationTrainingFailurePrecedence.Boundary,
            Phase = phase,
            UserSafeMessage = ValidationTrainingFailureHandler.UserSafeLeakageMessage,
            OccurredAtUtc = DateTime.UtcNow,
            IsQualificationBlocking = true
        };

    public static ValidationTrainingFailureAggregate BuildBoundaryAggregate(
        ValidationExperiment experiment,
        ValidationTrainingFailurePhase phase = ValidationTrainingFailurePhase.TrialBody)
    {
        var aggregate = ValidationTrainingFailurePersistence.MergeExisting(experiment.FailureReasonsJson);
        aggregate.Observe(CreateCanonicalBoundaryRecord(phase));
        return aggregate;
    }

    public static void InvalidateTentativeSelection(ValidationExperiment experiment)
    {
        experiment.SelectedTrialId = null;
        experiment.SelectedTrialNumber = null;
        experiment.SelectedTrialParameterSnapshotJson = null;
        experiment.SelectedTrialParameterFingerprint = null;
        experiment.SelectedMetricFingerprint = null;
        experiment.TrainingStrategyLabRunId = null;
        experiment.ValidationStrategyLabRunId = null;
        experiment.FrozenStrategyParameterSnapshotJson = null;
        experiment.FrozenParameterFingerprint = null;
        experiment.FrozenAtUtc = null;
    }

    public static void ApplyBoundaryBlock(
        ValidationExperiment experiment,
        ValidationTrainingFailureAggregate aggregate,
        bool invalidateTentativeSelection)
    {
        if (invalidateTentativeSelection)
        {
            InvalidateTentativeSelection(experiment);
        }

        experiment.LeakageAuditStatus = ValidationLeakageAuditStatus.Failed;
        experiment.IsQualificationCapable = false;
        ValidationTrainingFailurePersistence.ApplyToExperiment(experiment, aggregate);
    }

    public static void UpdateLeakageAuditJsonFromNegativeRows(
        ValidationExperiment experiment,
        IReadOnlyList<ValidationCandleAccessAudit> negativeRows,
        IValidationLeakageAuditor leakageAuditor,
        string optimizerFingerprint)
    {
        if (experiment.ValidationStartUtc is null
            || experiment.TrainingStartUtc is null
            || experiment.TrainingEndUtc is null)
        {
            return;
        }

        var negative = leakageAuditor.EvaluateFromAccessEvidence(
            negativeRows,
            experiment.ValidationStartUtc.Value,
            experiment.TrainingStartUtc.Value,
            experiment.TrainingEndUtc.Value,
            optimizerFingerprint);

        if (negative.Status != ValidationLeakageAuditStatus.Failed)
        {
            negative = new ValidationLeakageAuditReport
            {
                Status = ValidationLeakageAuditStatus.Failed,
                MaximumTimestampAccessedByOptimizer = negative.MaximumTimestampAccessedByOptimizer,
                ValidationStartUtc = negative.ValidationStartUtc,
                TrainingStartUtc = negative.TrainingStartUtc,
                TrainingEndUtc = negative.TrainingEndUtc,
                OptimizerInputFingerprint = optimizerFingerprint,
                TrialAccesses = negative.TrialAccesses,
                Reason =
                    "ValidationDataLeakageDetected: denied or boundary-violation access evidence remains qualification-blocking.",
                BlocksFreezeOrPassed = true,
                AccessEvidenceCount = negativeRows.Count,
                DeniedAccessCount = negativeRows.Count(a => a.WasDenied)
            };
        }

        experiment.LeakageAuditJson = leakageAuditor.Serialize(negative);
        experiment.LeakageAuditStatus = ValidationLeakageAuditStatus.Failed;
    }
}
