using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Domain.ValidationLab;

public class ValidationExperiment : Entity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ValidationExperimentType ExperimentType { get; set; }
    public ValidationExperimentStatus Status { get; set; } = ValidationExperimentStatus.Draft;
    public string StrategyCode { get; set; } = string.Empty;
    public string StrategyVersion { get; set; } = "1.0.0";
    public long? SourceStrategyLabRunId { get; set; }
    public long ExchangeId { get; set; }
    public string Exchange { get; set; } = string.Empty;
    public long SymbolId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public DateTime RequestedStartUtc { get; set; }
    public DateTime RequestedEndUtc { get; set; }
    public decimal SplitRatio { get; set; } = 0.70m;
    public string SplitAlgorithmVersion { get; set; } = "ChronologicalHoldout/v1";
    public int TotalEligibleCandleCount { get; set; }
    public int TrainingCandleCount { get; set; }
    public int ValidationCandleCount { get; set; }
    public DateTime? TrainingStartUtc { get; set; }
    public DateTime? TrainingEndUtc { get; set; }
    public DateTime? ValidationStartUtc { get; set; }
    public DateTime? ValidationEndUtc { get; set; }
    public DateTime? SplitCandleOpenTimeUtc { get; set; }
    public int RequiredWarmupCandles { get; set; } = 100;
    public DateTime? TrainingWarmupStartUtc { get; set; }
    public DateTime? ValidationWarmupStartUtc { get; set; }
    public string WarmupAlgorithmVersion { get; set; } = "StrategyLabWarmup/v1";
    public string CandleDataSnapshotJson { get; set; } = "{}";
    public string CandleDataFingerprint { get; set; } = string.Empty;
    public string WarmupSnapshotJson { get; set; } = "{}";
    public string ParameterSearchSpaceSnapshotJson { get; set; } = "{}";
    public string OptimizationObjectiveSnapshotJson { get; set; } = "{}";
    public string? FrozenStrategyParameterSnapshotJson { get; set; }
    public string? FrozenParameterFingerprint { get; set; }
    public string? FrozenStrategyFingerprint { get; set; }
    public string? FrozenConfidenceSnapshotJson { get; set; }
    public string? FrozenRiskSnapshotJson { get; set; }
    public string? FrozenCostModelSnapshotJson { get; set; }
    public string QualificationProfileSnapshotJson { get; set; } = "{}";
    public string DraftConfigurationJson { get; set; } = "{}";
    public ValidationPrimaryQualificationLayer PrimaryQualificationLayer { get; set; } =
        ValidationPrimaryQualificationLayer.RawStrategy;
    public ValidationRevealStatus ValidationRevealStatus { get; set; } = ValidationRevealStatus.Hidden;
    public DateTime? FrozenAtUtc { get; set; }
    public DateTime? ValidationRevealedAtUtc { get; set; }
    public string? ValidationRevealedBy { get; set; }
    public StrategyRobustnessDecision? StrategyRobustnessDecision { get; set; }
    public string? PrimaryFailureReason { get; set; }
    public string? FailureReasonsJson { get; set; }
    public string? QualificationRuleResultsJson { get; set; }
    public string? DecisionExplanation { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string DiagnosticsJson { get; set; } = "[]";
    public string OverlayResultsJson { get; set; } = "{}";
    public string ComparisonJson { get; set; } = "{}";
    public string RegimeComparisonJson { get; set; } = "{}";
    public string ParameterStabilityJson { get; set; } = "{}";
    public long? TrainingStrategyLabRunId { get; set; }
    public long? ValidationStrategyLabRunId { get; set; }
    public int BoundaryCensoredCount { get; set; }
    public decimal InitialBalance { get; set; } = 10000m;
    public int MaximumTrials { get; set; } = 50;
    public int DeterministicSeed { get; set; } = 42;
    public string? ErrorMessage { get; set; }
    public string? CurrentStage { get; set; }
    public decimal PercentComplete { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    // Milestone 22.1 integrity / metric contract fields
    public string ValidationMetricsVersion { get; set; } = "ValidationMetrics/v1.2";
    public string? CandidateReconciliationJson { get; set; }
    public CandidateReconciliationStatus? CandidateReconciliationStatus { get; set; }
    public string? LeakageAuditJson { get; set; }
    public ValidationLeakageAuditStatus? LeakageAuditStatus { get; set; }
    public ParameterStabilityApplicability? ParameterStabilityApplicability { get; set; }
    public SegmentDetectorContinuityMode SegmentDetectorContinuityMode { get; set; } =
        SegmentDetectorContinuityMode.FreshSessionWithWarmup;
    public ExpectancyMetricType ExpectancyMetric { get; set; } = ExpectancyMetricType.NetExpectancyR;
    public ProfitFactorMetricType ProfitFactorMetric { get; set; } = ProfitFactorMetricType.NetProfitFactor;

    // Milestone 22.2 holdout exclusivity / export readiness
    public string HoldoutExclusivityPolicyVersion { get; set; } = "ValidationHoldoutExclusivity/v1";
    public string? HoldoutExclusivityJson { get; set; }
    public string? MetricConsistencyJson { get; set; }
    public string? MetricConsistencyStatus { get; set; }
    public string? ExportVerificationJson { get; set; }
    public ValidationExportVerificationStatus? ExportVerificationStatus { get; set; }
    public ValidationLaboratoryReadiness? ValidationLaboratoryReadinessStatus { get; set; }
    public int CrossSegmentOverlapCount { get; set; }

    // Milestone 22.3 closeout / lineage
    public bool IsCanonical { get; set; }
    public ValidationExperimentSupersessionStatus SupersessionStatus { get; set; }
    public long? SupersededByExperimentId { get; set; }
    public DateTime? SupersededAtUtc { get; set; }
    public string? SupersessionReason { get; set; }
    public long? SelectedTrialId { get; set; }
    public int? SelectedTrialNumber { get; set; }
    public string? SelectedTrialParameterSnapshotJson { get; set; }
    public string? SelectedTrialParameterFingerprint { get; set; }
    public ValidationSelectionIntegrityStatus SelectionIntegrityStatus { get; set; }
    public FrozenSnapshotValidationStatus FrozenSnapshotValidationStatus { get; set; }
    public string SelectionIntegrityVersion { get; set; } = "ValidationSelectionIntegrity/v1";
    public string RiskBasisVersion { get; set; } = "ValidationRiskBasis/v1";
    public string ParameterFingerprintVersion { get; set; } = "ValidationParameterFingerprint/v1";
    public string? FreezeSource { get; set; }
    public bool AllowInfrastructureOnlyRejectedTrialFallback { get; set; }
    public string? FallbackSelectionPolicyVersion { get; set; }
    public string? FallbackSelectionReason { get; set; }
    public bool IsQualificationCapable { get; set; } = true;
    public string? CloseoutAuditJson { get; set; }
    public string? TrialPopulationSummaryJson { get; set; }
}