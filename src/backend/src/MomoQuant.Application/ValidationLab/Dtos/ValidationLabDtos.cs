using MomoQuant.Application.StrategyLab.Dtos;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.ValidationLab.Dtos;

public sealed class CreateValidationExperimentRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public ValidationExperimentType ExperimentType { get; init; } =
        ValidationExperimentType.ValidateExistingFrozenConfiguration;
    public required string StrategyCode { get; init; }
    public string? StrategyVersion { get; init; }
    public long? SourceStrategyLabRunId { get; init; }
    public required long ExchangeId { get; init; }
    public required long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public required DateTime RequestedStartUtc { get; init; }
    public required DateTime RequestedEndUtc { get; init; }
    public decimal SplitRatio { get; init; } = 0.70m;
    public int RequiredWarmupCandles { get; init; } = 100;
    public Dictionary<string, string>? StrategyParameters { get; init; }
    public Dictionary<string, string>? ParameterSearchSpaceOverrides { get; init; }
    public StrategyLabObservationSettingsDto? ObservationSettings { get; init; }
    public ValidationQualificationProfileDto? QualificationProfile { get; init; }
    public ValidationPrimaryQualificationLayer PrimaryQualificationLayer { get; init; } =
        ValidationPrimaryQualificationLayer.RawStrategy;
    public decimal InitialBalance { get; init; } = 10000m;
    public decimal MakerFeeRate { get; init; } = 0.0002m;
    public decimal TakerFeeRate { get; init; } = 0.0004m;
    public decimal SlippagePercent { get; init; }
    public int MaximumTrials { get; init; } = 50;
    public int DeterministicSeed { get; init; } = 42;
    public bool AutoImportMissingCandles { get; init; } = true;
}

public sealed class UpdateValidationExperimentRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public DateTime? RequestedStartUtc { get; init; }
    public DateTime? RequestedEndUtc { get; init; }
    public decimal? SplitRatio { get; init; }
    public int? RequiredWarmupCandles { get; init; }
    public Dictionary<string, string>? StrategyParameters { get; init; }
    public Dictionary<string, string>? ParameterSearchSpaceOverrides { get; init; }
    public StrategyLabObservationSettingsDto? ObservationSettings { get; init; }
    public ValidationQualificationProfileDto? QualificationProfile { get; init; }
    public ValidationPrimaryQualificationLayer? PrimaryQualificationLayer { get; init; }
    public decimal? InitialBalance { get; init; }
    public decimal? MakerFeeRate { get; init; }
    public decimal? TakerFeeRate { get; init; }
    public decimal? SlippagePercent { get; init; }
    public int? MaximumTrials { get; init; }
    public int? DeterministicSeed { get; init; }
}

public sealed class ValidationQualificationProfileDto
{
    public string ProfileVersion { get; init; } = "StandardHoldoutQualification/v1";
    public ValidationPrimaryQualificationLayer PrimaryQualificationLayer { get; init; } =
        ValidationPrimaryQualificationLayer.RawStrategy;
    public int MinimumTrainingClosedTrades { get; init; } = 30;
    public int MinimumValidationClosedTrades { get; init; } = 15;
    public decimal MinimumTrainingProfitFactor { get; init; } = 1.10m;
    public decimal MinimumValidationProfitFactor { get; init; } = 1.05m;
    public decimal MinimumTrainingNetExpectancyR { get; init; }
    public decimal MinimumValidationNetExpectancyR { get; init; }
    public decimal MaximumTrainingDrawdownPercent { get; init; } = 25m;
    public decimal MaximumValidationDrawdownPercent { get; init; } = 25m;
    public decimal MinimumOpportunityRetentionPercent { get; init; } = 40m;
    public decimal MaximumAllowedExpectancyDegradation { get; init; } = 0.50m;
    public decimal MaximumSingleTradePnlContributionPercent { get; init; } = 40m;
    public bool RequirePositiveValidationNetPnl { get; init; } = true;
    public bool RequirePositiveValidationNetExpectancy { get; init; } = true;
    public bool RequireParameterStability { get; init; } = true;
    public ExpectancyMetricType? ExpectancyMetric { get; init; }
    public ProfitFactorMetricType? ProfitFactorMetric { get; init; }
    public ExpiredTradeMetricPolicy? ExpiredTradeMetricPolicy { get; init; }
}

public class ValidationExperimentDto
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public ValidationExperimentType ExperimentType { get; init; }
    public ValidationExperimentStatus Status { get; init; }
    public string StrategyCode { get; init; } = string.Empty;
    public string StrategyVersion { get; init; } = string.Empty;
    public long? SourceStrategyLabRunId { get; init; }
    public long ExchangeId { get; init; }
    public string Exchange { get; init; } = string.Empty;
    public long SymbolId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string Timeframe { get; init; } = string.Empty;
    public DateTime RequestedStartUtc { get; init; }
    public DateTime RequestedEndUtc { get; init; }
    public decimal SplitRatio { get; init; }
    public string SplitAlgorithmVersion { get; init; } = string.Empty;
    public int TotalEligibleCandleCount { get; init; }
    public int TrainingCandleCount { get; init; }
    public int ValidationCandleCount { get; init; }
    public DateTime? TrainingStartUtc { get; init; }
    public DateTime? TrainingEndUtc { get; init; }
    public DateTime? ValidationStartUtc { get; init; }
    public DateTime? ValidationEndUtc { get; init; }
    public DateTime? SplitCandleOpenTimeUtc { get; init; }
    public int RequiredWarmupCandles { get; init; }
    public DateTime? TrainingWarmupStartUtc { get; init; }
    public DateTime? ValidationWarmupStartUtc { get; init; }
    public string CandleDataFingerprint { get; init; } = string.Empty;
    public ValidationRevealStatus ValidationRevealStatus { get; init; }
    public ValidationPrimaryQualificationLayer PrimaryQualificationLayer { get; init; }
    public string? PrimaryLayerWarning { get; init; }
    public DateTime? FrozenAtUtc { get; init; }
    public DateTime? ValidationRevealedAtUtc { get; init; }
    public StrategyRobustnessDecision? StrategyRobustnessDecision { get; init; }
    public string? PrimaryFailureReason { get; init; }
    public string? DecisionExplanation { get; init; }
    public int BoundaryCensoredCount { get; init; }
    public decimal InitialBalance { get; init; }
    public int MaximumTrials { get; init; }
    public int DeterministicSeed { get; init; }
    public string? ErrorMessage { get; init; }
    public string? CurrentStage { get; init; }
    public decimal PercentComplete { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }
    public long? TrainingStrategyLabRunId { get; init; }
    public long? ValidationStrategyLabRunId { get; init; }
    public string? FrozenParameterFingerprint { get; init; }
    public string ValidationMetricsVersion { get; init; } = "ValidationMetrics/v1.2";
    public CandidateReconciliationStatus? CandidateReconciliationStatus { get; init; }
    public ValidationLeakageAuditStatus? LeakageAuditStatus { get; init; }
    public ParameterStabilityApplicability? ParameterStabilityApplicability { get; init; }
    public SegmentDetectorContinuityMode SegmentDetectorContinuityMode { get; init; }
    public ExpectancyMetricType ExpectancyMetric { get; init; }
    public ProfitFactorMetricType ProfitFactorMetric { get; init; }
    public string? HoldoutExclusivityPolicyVersion { get; init; }
    public int CrossSegmentOverlapCount { get; init; }
    public string? MetricConsistencyStatus { get; init; }
    public ValidationExportVerificationStatus? ExportVerificationStatus { get; init; }
    public ValidationLaboratoryReadiness? ValidationLaboratoryReadinessStatus { get; init; }
    public bool IsCanonical { get; init; }
    public ValidationExperimentSupersessionStatus SupersessionStatus { get; init; }
    public long? SupersededByExperimentId { get; init; }
    public DateTime? SupersededAtUtc { get; init; }
    public string? SupersessionReason { get; init; }
    public ValidationSelectionIntegrityStatus SelectionIntegrityStatus { get; init; }
    public long? SelectedTrialId { get; init; }
    public int? SelectedTrialNumber { get; init; }
    public string? SelectedTrialParameterFingerprint { get; init; }
    public FrozenSnapshotValidationStatus FrozenSnapshotValidationStatus { get; init; }
    public string? SelectionIntegrityVersion { get; init; }
    public string? RiskBasisVersion { get; init; }
    public string? ParameterFingerprintVersion { get; init; }
    public string? FreezeSource { get; init; }
    public bool IsQualificationCapable { get; init; } = true;
    public string? TrialPopulationSummaryJson { get; init; }
    public string? CloseoutAuditJson { get; init; }
}

public sealed class ValidationExperimentDetailDto : ValidationExperimentDto
{
    public string CandleDataSnapshotJson { get; init; } = "{}";
    public string WarmupSnapshotJson { get; init; } = "{}";
    public string ParameterSearchSpaceSnapshotJson { get; init; } = "{}";
    public string OptimizationObjectiveSnapshotJson { get; init; } = "{}";
    public string? FrozenStrategyParameterSnapshotJson { get; init; }
    public string? FrozenStrategyFingerprint { get; init; }
    public string? FrozenConfidenceSnapshotJson { get; init; }
    public string? FrozenRiskSnapshotJson { get; init; }
    public string? FrozenCostModelSnapshotJson { get; init; }
    public string QualificationProfileSnapshotJson { get; init; } = "{}";
    public string? FailureReasonsJson { get; init; }
    public string? QualificationRuleResultsJson { get; init; }
    public string DiagnosticsJson { get; init; } = "[]";
    public string? OverlayResultsJson { get; init; }
    public string? ComparisonJson { get; init; }
    public string? RegimeComparisonJson { get; init; }
    public string? ParameterStabilityJson { get; init; }
    public string? CandidateReconciliationJson { get; init; }
    public string? LeakageAuditJson { get; init; }
    public string? HoldoutExclusivityJson { get; init; }
    public string? MetricConsistencyJson { get; init; }
    public string? ExportVerificationJson { get; init; }
    public string DraftConfigurationJson { get; init; } = "{}";
    public IReadOnlyList<ValidationSegmentResultDto>? SegmentResults { get; init; }
    public HoldoutReuseWarningDto? HoldoutReuseWarning { get; init; }
}

public sealed class ValidationSegmentResultDto
{
    public long Id { get; init; }
    public ValidationSegmentType SegmentType { get; init; }
    public ValidationLayerType LayerType { get; init; }
    public long? StrategyLabRunId { get; init; }
    public string MetricsJson { get; init; } = "{}";
    public int CandleCount { get; init; }
    public int CandidateCount { get; init; }
    public int ClosedTradeCount { get; init; }
    public decimal? NetExpectancyR { get; init; }
    public decimal? ProfitFactor { get; init; }
    public decimal? NetPnl { get; init; }
    public decimal? NetReturnPercent { get; init; }
    public decimal? MaximumDrawdownPercent { get; init; }
    public decimal? TransactionCosts { get; init; }
    public int BoundaryCensoredCount { get; init; }
    public string ResultFingerprint { get; init; } = string.Empty;
    public string ResultCalculationVersion { get; init; } = "ValidationMetrics/v1.2";
    public decimal? GrossExpectancyR { get; init; }
    public decimal? GrossProfitFactor { get; init; }
    public decimal? NetProfitFactor { get; init; }
    public decimal? GrossAverageR { get; init; }
    public decimal? NetAverageR { get; init; }
    public decimal? GrossPnl { get; init; }
    public int PersistedCandidateRowCount { get; init; }
    public int MetricIncludedCandidateCount { get; init; }
    public int MetricExcludedCandidateCount { get; init; }
    public int CrossSegmentOverlapCount { get; init; }
    public decimal? GrossProfit { get; init; }
    public decimal? GrossLoss { get; init; }
    public decimal? NetProfit { get; init; }
    public decimal? NetLoss { get; init; }

    /// <summary>
    /// Included trades carrying non-blocking warnings (ValidationMetrics/v1.3.1).
    /// Distinct from metric exclusion population counts.
    /// </summary>
    public int MetricWarningBearingIncludedTradeCount { get; init; }

    /// <summary>Distinct warning codes on included trades (never exclusion reasons).</summary>
    public IReadOnlyList<string>? MetricWarningCodes { get; init; }

    /// <summary>Optional ValidationMetricPopulation/v1 (ValidationMetrics/v1.3.2+).</summary>
    public ValidationMetricPopulationSummary? Population { get; init; }

    public ValidationMetricApplicability? MonetaryPnlApplicability { get; init; }
    public ValidationMetricApplicability? GrossProfitFactorApplicability { get; init; }
    public ValidationMetricApplicability? NetProfitFactorApplicability { get; init; }
    public ValidationMetricApplicability? GrossExpectancyApplicability { get; init; }
    public ValidationMetricApplicability? NetExpectancyApplicability { get; init; }
    public ValidationRiskBasisValidationStatus? RiskBasisValidationStatus { get; init; }

    public int? CandidatePopulationCount { get; init; }
    public int? BoundaryEligibleCandidateCount { get; init; }
    public int? PathInputPopulationCount { get; init; }
    public int? IncludedPathInputCount { get; init; }
    public int? ExcludedPathInputCount { get; init; }
    public int? ClosedOutcomePopulationCount { get; init; }
    public int? MonetaryPnlPopulationCount { get; init; }
    public int? GrossRPopulationCount { get; init; }
    public int? NetRPopulationCount { get; init; }
    public int? WinnerPopulationCount { get; init; }
    public int? LoserPopulationCount { get; init; }
    public int? NeutralPopulationCount { get; init; }
    public string? PopulationContractVersion { get; init; }
    public IReadOnlyDictionary<string, int>? ExclusionCountsByReason { get; init; }
    public IReadOnlyDictionary<string, int>? WarningCountsByCode { get; init; }
}

public sealed class ValidationParameterTrialDto
{
    public long Id { get; init; }
    public int TrialNumber { get; init; }
    public string ParameterSnapshotJson { get; init; } = "{}";
    public string ParameterFingerprint { get; init; } = string.Empty;
    public ValidationTrialStatus Status { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public int RawCandidateCount { get; init; }
    public int ClosedTradeCount { get; init; }
    public int WinnerCount { get; init; }
    public int LoserCount { get; init; }
    public int ExpiredCount { get; init; }
    public decimal? NetExpectancyR { get; init; }
    public decimal? GrossPnl { get; init; }
    public decimal? NetPnl { get; init; }
    public decimal? ProfitFactor { get; init; }
    public decimal? MaximumDrawdownPercent { get; init; }
    public decimal? FeeImpactPercent { get; init; }
    public decimal? TrainingScore { get; init; }
    public string GuardrailDecision { get; init; } = string.Empty;
    public string? GuardrailFailureReasonsJson { get; init; }
    public int? Rank { get; init; }
    public long? StrategyLabRunId { get; init; }
    public string? ErrorMessage { get; init; }
    public ValidationTrialRecoverySource RecoverySource { get; init; }
}

public sealed class HoldoutReuseWarningDto
{
    public IReadOnlyList<long> PriorExperimentIds { get; init; } = [];
    public decimal OverlapPercent { get; init; }
    public int RevealCount { get; init; }
    public DateTime? FirstRevealedAtUtc { get; init; }
    public string ContaminationRisk { get; init; } = "Low";
    public bool RepeatedHoldoutExposure { get; init; }
}

public sealed class ValidationCandidateQuery
{
    public ValidationSegmentClassification? Segment { get; init; }
    public ValidationLayerType? Layer { get; init; }
    /// <summary>
    /// Optional metric population filter, e.g. CrossSegmentOverlapExcludedFromValidation.
    /// Segment=Invalid also selects cross-segment overlap (audit-only) validation candidates.
    /// </summary>
    public string? MetricClassification { get; init; }
    public bool? CrossSegmentOverlapOnly { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public sealed class ValidationSelectionIntegrityReportDto
{
    public long ExperimentId { get; init; }
    public ValidationSelectionIntegrityStatus Status { get; init; }
    public long? SelectedTrialId { get; init; }
    public int? SelectedTrialNumber { get; init; }
    public string? SelectedParameterFingerprint { get; init; }
    public string? FrozenParameterFingerprint { get; init; }
    public FrozenSnapshotValidationStatus SnapshotValidationStatus { get; init; }
    public bool FingerprintsMatch { get; init; }
    public bool IsEligibleForSelection { get; init; }
    public IReadOnlyList<string> Violations { get; init; } = [];
    public TrainingSelectionPopulationSummary? Population { get; init; }
}

public sealed class ValidationMetricBasisAuditSegmentDto
{
    public ValidationSegmentType SegmentType { get; init; }
    public ValidationLayerType LayerType { get; init; }
    public int IncludedTradeCount { get; init; }
    public int ExcludedTradeCount { get; init; }
    public ValidationMetricApplicability NetExpectancyApplicability { get; init; }
    public IReadOnlyList<string> ExclusionReasons { get; init; } = [];
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
    public decimal? GrossExpectancyR { get; init; }
    public decimal? NetExpectancyR { get; init; }
    public string MetricsVersion { get; init; } = string.Empty;
    public string RiskBasisVersion { get; init; } = string.Empty;
}

public sealed class ValidationMetricBasisAuditReportDto
{
    public long ExperimentId { get; init; }
    public string ValidationMetricsVersion { get; init; } = string.Empty;
    public string RiskBasisVersion { get; init; } = string.Empty;
    public IReadOnlyList<ValidationMetricBasisAuditSegmentDto> Segments { get; init; } = [];
}

public sealed class RecalculateValidationMetricsRequest
{
    public string TargetMetricsVersion { get; init; } = "ValidationMetrics/v1.3";
    public string TargetRiskBasisVersion { get; init; } = "ValidationRiskBasis/v1";
    public string Reason { get; init; } = string.Empty;
    public bool PreserveOriginal { get; init; } = true;
}

public sealed class RecalculateValidationMetricsResultDto
{
    public long ExperimentId { get; init; }
    public string TargetMetricsVersion { get; init; } = string.Empty;
    public string TargetRiskBasisVersion { get; init; } = string.Empty;
    public DateTime RecalculatedAtUtc { get; init; }
    public bool PreserveOriginal { get; init; }
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<ValidationMetricBasisAuditSegmentDto> RecalculatedSegments { get; init; } = [];
}
