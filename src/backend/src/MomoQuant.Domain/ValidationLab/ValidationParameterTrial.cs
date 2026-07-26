using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Domain.ValidationLab;

public class ValidationParameterTrial : Entity
{
    public long ValidationExperimentId { get; set; }
    public int TrialNumber { get; set; }
    public string ParameterSnapshotJson { get; set; } = "{}";
    public string ParameterFingerprint { get; set; } = string.Empty;
    public ValidationTrialStatus Status { get; set; } = ValidationTrialStatus.Pending;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int RawCandidateCount { get; set; }
    public int ClosedTradeCount { get; set; }
    public int WinnerCount { get; set; }
    public int LoserCount { get; set; }
    public int ExpiredCount { get; set; }
    public decimal? NetExpectancyR { get; set; }
    public decimal? GrossPnl { get; set; }
    public decimal? NetPnl { get; set; }
    public decimal? ProfitFactor { get; set; }
    public decimal? MaximumDrawdownPercent { get; set; }
    public decimal? FeeImpactPercent { get; set; }
    public decimal? TrainingScore { get; set; }
    public string GuardrailDecision { get; set; } = "NotEvaluated";
    public string? GuardrailFailureReasonsJson { get; set; }
    public int? Rank { get; set; }
    public string? DiagnosticWarningsJson { get; set; }
    public long? StrategyLabRunId { get; set; }
    public string? ErrorMessage { get; set; }
    public ValidationTrialRecoverySource RecoverySource { get; set; } = ValidationTrialRecoverySource.None;

    // Milestone 23.0D — persisted trial metric snapshot (ValidationMetrics/v1.3.2)
    public string? TrialMetricSnapshotJson { get; set; }

    /// <summary>SHA-256 (lowercase hex) of <see cref="TrialMetricSnapshotJson"/>.</summary>
    public string? TrialMetricFingerprint { get; set; }

    public string? TrialMetricsVersion { get; set; }
    public string? TrainingScoreVersion { get; set; }
    public string? GuardrailEvaluationJson { get; set; }
    public int? CandidatePopulationCount { get; set; }
    public int? BoundaryEligibleCandidateCount { get; set; }
    public int? IncludedPathInputCount { get; set; }
    public int? ExcludedPathInputCount { get; set; }
    public int? ClosedOutcomePopulationCount { get; set; }
    public int? MonetaryPnlPopulationCount { get; set; }
    public int? GrossRPopulationCount { get; set; }
    public int? NetRPopulationCount { get; set; }

    /// <summary>Aggregate risk-basis status over included path inputs only.</summary>
    public ValidationRiskBasisValidationStatus? IncludedPopulationRiskStatus { get; set; }

    /// <summary>Aggregate integrity status over all path inputs including excluded ones.</summary>
    public ValidationRiskBasisValidationStatus? CompletePathInputIntegrityStatus { get; set; }

    public ValidationTrialRankEligibility TrialRankEligibility { get; set; } =
        ValidationTrialRankEligibility.NotEvaluated;

    public string? RankIneligibleReasonsJson { get; set; }

    // Milestone 23.0E2C1 — authoritative durable audit-execution link
    /// <summary>Public identity of the current authoritative <c>ValidationAuditExecution</c>.</summary>
    public Guid? AuthoritativeAuditExecutionId { get; set; }

    public ValidationAuditCompletionStatus AuditCompletionStatus { get; set; } =
        ValidationAuditCompletionStatus.NotEvaluated;

    public int AuditAttemptNumber { get; set; }
}