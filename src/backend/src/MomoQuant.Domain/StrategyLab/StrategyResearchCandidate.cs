using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Domain.StrategyLab;

public class StrategyResearchCandidate : Entity
{
    public long StrategyLabRunId { get; set; }
    public string StrategyCode { get; set; } = string.Empty;
    public string StrategyVersion { get; set; } = "1.0.0";
    public long ExchangeId { get; set; }
    public long SymbolId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public TradeDirection Direction { get; set; }
    public DateTime SetupDetectedAtUtc { get; set; }
    public DateTime ProposedEntryTimeUtc { get; set; }
    public decimal ProposedEntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target1 { get; set; }
    public decimal? Target2 { get; set; }
    public decimal RewardRisk { get; set; }
    public StrategyResearchCandidateStatus CandidateStatus { get; set; }
    public string StrategyReason { get; set; } = string.Empty;
    public string SetupFingerprint { get; set; } = string.Empty;
    public string ParametersJson { get; set; } = "{}";
    public string StructureJson { get; set; } = "{}";

    public decimal? ConfidenceScore { get; set; }
    public decimal? ConfidenceThreshold { get; set; }
    public decimal? ConfidenceMargin { get; set; }
    public ResearchConfidenceDecision? ConfidenceDecision { get; set; }
    public string? ConfidenceReason { get; set; }
    public string? ConfidenceModelVersion { get; set; }
    public string? ConfidenceComponentsJson { get; set; }
    public DateTime? ConfidenceEvaluatedAtUtc { get; set; }

    // Compatibility / legacy risk fields (RiskDecision = FinancialRiskDecision)
    public ResearchRiskDecision? RiskDecision { get; set; }
    public string? RiskReason { get; set; }
    public decimal? RiskScore { get; set; }
    public decimal? RiskThreshold { get; set; }
    public decimal? RiskMargin { get; set; }
    public decimal? RiskPerTradePercent { get; set; }
    public decimal? ProposedPositionSize { get; set; }
    public decimal? ProposedLeverage { get; set; }
    public decimal? StopDistancePercent { get; set; }
    public decimal? CurrentExposurePercent { get; set; }
    public decimal? DailyLossUsagePercent { get; set; }
    public decimal? CurrentDrawdownPercent { get; set; }
    public long? RiskProfileId { get; set; }
    public string? RiskProfileVersion { get; set; }
    public string? RiskRejectedRuleKey { get; set; }
    public DateTime? RiskEvaluatedAtUtc { get; set; }

    // Risk Observation / v2 + v2.1 futures semantics fields
    public decimal? CandidateRiskScore { get; set; }
    public decimal? PortfolioRiskScore { get; set; }
    public PortfolioRiskAssessmentStatus? PortfolioRiskAssessmentStatus { get; set; }
    public string? RiskModelVersion { get; set; }
    public string? RiskAssessmentVersion { get; set; }
    public string? RiskComponentsJson { get; set; }
    public string? RiskRuleResultsJson { get; set; }
    public string? RiskFailedRuleKeysJson { get; set; }
    public string? RiskWarningRuleKeysJson { get; set; }
    public decimal? RiskAmount { get; set; }
    public decimal? RiskAtStopPercent { get; set; }
    public decimal? PositionNotional { get; set; }
    public decimal? PositionExposurePercent { get; set; }
    public decimal? NotionalExposurePercent { get; set; }
    public decimal? MarginUsagePercent { get; set; }
    public decimal? MinimumRequiredLeverage { get; set; }
    public decimal? AssessmentLeverage { get; set; }
    public decimal? PreferredLeverage { get; set; }
    public decimal? MaxLeverage { get; set; }
    public decimal? InitialMarginRequired { get; set; }
    public decimal? EstimatedRoundTripFees { get; set; }
    public decimal? FeeToTargetPercent { get; set; }
    public string? PositionSizingUnavailableReason { get; set; }
    public decimal? ConcurrentRiskPercent { get; set; }
    public decimal? CurrentNotionalExposurePercent { get; set; }
    public decimal? CurrentMarginUsagePercent { get; set; }
    public int? ConcurrentPositionCount { get; set; }
    public ResearchRiskScoreDecision? RiskScoreDecision { get; set; }
    public ResearchHardRuleComplianceDecision? HardRuleComplianceDecision { get; set; }
    public ResearchRiskPolicyEligibilityDecision? RiskPolicyEligibilityDecision { get; set; }
    public string? RiskPolicyReason { get; set; }
    public string? RiskPolicyFailedRuleKeysJson { get; set; }
    public decimal? RiskPolicyMinimumConfidence { get; set; }
    public string? FinalPipelineRejectionSourcesJson { get; set; }
    public string? RiskProfileName { get; set; }
    public string? RiskProfileSource { get; set; }
    public string? RiskProfileSnapshotId { get; set; }
    public DrawdownCalculationMode? DrawdownCalculationMode { get; set; }

    // IndependentPaths/v1 — path-specific assessments (generic risk fields = Risk-Only)
    public GenericRiskFieldSource? GenericRiskFieldSource { get; set; }
    public string? RiskPathAssessmentVersion { get; set; }
    public ResearchRiskDecision? RiskOnlyFinancialRiskDecision { get; set; }
    public ShadowEntryDecision? RiskOnlyEntryDecision { get; set; }
    public string? RiskOnlyRejectionSourcesJson { get; set; }
    public string? RiskOnlyAssessmentJson { get; set; }
    public decimal? RiskOnlyCurrentDrawdownPercent { get; set; }
    public decimal? RiskOnlyDailyLossUsagePercent { get; set; }
    public decimal? RiskOnlyCurrentMarginUsagePercent { get; set; }
    public decimal? RiskOnlyConcurrentRiskPercent { get; set; }
    public int? RiskOnlyOpenPositionCount { get; set; }
    public ResearchRiskDecision? FullPipelineFinancialRiskDecision { get; set; }
    public ShadowEntryDecision? FullPipelineEntryDecision { get; set; }
    public string? FullPipelineRejectionSourcesJson { get; set; }
    public string? FullPipelineAssessmentJson { get; set; }
    public decimal? FullPipelineCurrentDrawdownPercent { get; set; }
    public decimal? FullPipelineDailyLossUsagePercent { get; set; }
    public decimal? FullPipelineCurrentMarginUsagePercent { get; set; }
    public decimal? FullPipelineConcurrentRiskPercent { get; set; }
    public int? FullPipelineOpenPositionCount { get; set; }

    public ResearchFinalPipelineDecision? FinalPipelineDecision { get; set; }
    public RawOutcomeStatus RawOutcomeStatus { get; set; } = RawOutcomeStatus.Pending;
    public DateTime? RawExitTimeUtc { get; set; }
    public decimal? RawExitPrice { get; set; }
    public string? RawExitReason { get; set; }
    public ResearchExitOutcome ExitOutcome { get; set; } = ResearchExitOutcome.NotSet;
    public ResearchNetResult NetResult { get; set; } = ResearchNetResult.Unknown;
    public decimal? RawGrossPnl { get; set; }
    public decimal? RawNetPnl { get; set; }
    public decimal? RawPnlPercent { get; set; }
    public decimal? RawRMultiple { get; set; }
    public decimal? Mfe { get; set; }
    public decimal? Mae { get; set; }
    public int? DurationBars { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
