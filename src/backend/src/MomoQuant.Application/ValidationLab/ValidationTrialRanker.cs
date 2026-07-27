using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Ranks trials using training-only fields. Validation metrics must never affect ranking.
/// For ValidationMetrics/v1.3.2 experiments (Milestone 23.0D), ranking reads only the fields
/// persisted from the trial metric snapshot and requires snapshot rank eligibility — trials are
/// never re-scored from StrategyLab summaries.
/// Deterministic tie-break: TrainingScore desc → NetExpectancyR desc → ProfitFactor desc →
/// MaximumDrawdownPercent asc → ClosedTradeCount desc → ParameterFingerprint ordinal asc →
/// TrialNumber asc. Null metrics always order after evaluated metrics.
/// </summary>
public static class ValidationTrialRanker
{
    public static IReadOnlyList<ValidationParameterTrial> OrderForRanking(
        IEnumerable<ValidationParameterTrial> trials,
        bool requireSnapshotEligibility = false)
    {
        var eligible = trials
            .Where(t => ValidationAuthoritativeAuditQualificationEvaluator.MeetsCachedAuditEligibilityFields(t));

        if (requireSnapshotEligibility)
        {
            eligible = eligible.Where(IsSnapshotRankEligible);
        }

        return eligible
            .OrderByDescending(t => t.TrainingScore ?? decimal.MinValue)
            .ThenByDescending(t => t.NetExpectancyR ?? decimal.MinValue)
            .ThenByDescending(t => t.ProfitFactor ?? decimal.MinValue)
            .ThenBy(t => t.MaximumDrawdownPercent ?? decimal.MaxValue)
            .ThenByDescending(t => t.ClosedTradeCount)
            .ThenBy(t => t.ParameterFingerprint, StringComparer.Ordinal)
            .ThenBy(t => t.TrialNumber)
            .ToList();
    }

    /// <summary>Snapshot-eligible: calculator marked the trial Eligible and persisted a fingerprint.</summary>
    public static bool IsSnapshotRankEligible(ValidationParameterTrial trial) =>
        trial.TrialRankEligibility == ValidationTrialRankEligibility.Eligible
        && !string.IsNullOrWhiteSpace(trial.TrialMetricFingerprint);

    public static void AssignRanks(
        IList<ValidationParameterTrial> trials,
        bool requireSnapshotEligibility = false)
    {
        foreach (var trial in trials)
        {
            trial.Rank = null;
            if (!ValidationAuthoritativeAuditQualificationEvaluator.MeetsCachedAuditEligibilityFields(trial))
            {
                if (ValidationAuthoritativeAuditQualificationEvaluator.IsGuardrailPassedCompleted(trial)
                    || trial.TrialRankEligibility == ValidationTrialRankEligibility.Eligible)
                {
                    if (trial.TrialRankEligibility == ValidationTrialRankEligibility.Eligible)
                    {
                        trial.TrialRankEligibility = ValidationTrialRankEligibility.Ineligible;
                    }

                    ValidationTrainingFailurePersistence.AppendRankIneligibleReasons(
                        trial,
                        [
                            ValidationAuthoritativeAuditQualificationEvaluator.RankIneligibleReasonCode,
                            trial.AuthoritativeAuditExecutionId is null
                                ? ValidationAuditCompletenessCode.HistoricalNotEvaluated.ToString()
                                : trial.AuditCompletionStatus.ToString()
                        ]);
                }
            }
        }

        var ordered = OrderForRanking(trials, requireSnapshotEligibility);
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Rank = i + 1;
        }
    }

    public static ValidationParameterTrial? SelectWinner(
        IEnumerable<ValidationParameterTrial> trials,
        bool requireSnapshotEligibility = false) =>
        OrderForRanking(trials, requireSnapshotEligibility).FirstOrDefault();
}
