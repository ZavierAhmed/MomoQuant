using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.StrategyLab.Dtos;

public sealed class StrategyOpportunityMetricsDto
{
    public int Evaluations { get; init; }
    public int RawCandidates { get; init; }
    public decimal CandidatesPer1000Candles { get; init; }
    public decimal CandidatesPerDay { get; init; }
    public decimal CandidatesPer30Days { get; init; }
    public double? AverageBarsBetweenCandidates { get; init; }
    public int? MedianBarsBetweenCandidates { get; init; }
    public int? LongestGapBetweenCandidates { get; init; }
    public int LongCandidateCount { get; init; }
    public int ShortCandidateCount { get; init; }
}

public sealed class StrategyLabPerformanceSummaryDto
{
    public int RawCandidates { get; init; }
    public int RawClosedTrades { get; init; }
    public int Winners { get; init; }
    public int Losers { get; init; }
    public int Breakeven { get; init; }
    public decimal WinRate { get; init; }

    /// <summary>Sum of independently simulated candidate net PnL (setup research, not portfolio).</summary>
    public decimal NetPnl { get; init; }
    public string NetPnlLabel { get; init; } = "Independent Setup PnL";

    /// <summary>(IndependentSetupNetPnl / InitialBalance) * 100 — illustrative scale only, not portfolio return.</summary>
    public decimal PnlPercent { get; init; }
    public string PnlPercentLabel { get; init; } = "Independent Setup PnL % of Initial Balance";

    public decimal ProfitFactor { get; init; }
    public decimal Expectancy { get; init; }
    public decimal AverageR { get; init; }

    /// <summary>Drawdown of independently summed candidate PnL sequence — not capital-aware portfolio DD.</summary>
    public decimal MaxDrawdownPercent { get; init; }
    public string MaxDrawdownLabel { get; init; } = "Independent Setup Sequence Drawdown %";

    public bool PortfolioMetricsAvailable { get; init; }
    public string PortfolioMetricsNote { get; init; } =
        "Portfolio metrics unavailable. Candidates are simulated independently; overlapping capital is not modeled.";

    public decimal InitialBalance { get; init; }
    public decimal GrossWinnerPnl { get; init; }
    public decimal GrossLoserPnl { get; init; }
    public IReadOnlyList<string> MetricWarnings { get; init; } = [];
    public required StrategyOpportunityMetricsDto Opportunity { get; init; }
    public StrategyEvidenceQuality EvidenceQuality { get; init; }
    public string EvidenceQualityLabel { get; init; } = string.Empty;
}

public sealed class CandidateFunnelDto
{
    public int CandlesLoaded { get; init; }
    public int WarmupCandlesLoaded { get; init; }
    public int TestRangeCandles { get; init; }
    public int EligibleEvaluationCandles { get; init; }
    public int CandlesEvaluated { get; init; }
    public int ConfirmedSwingHighs { get; init; }
    public int ConfirmedSwingLows { get; init; }
    public int ConfirmedSwings => ConfirmedSwingHighs + ConfirmedSwingLows;
    public int BullishBreakoutChecks { get; init; }
    public int BearishBreakoutChecks { get; init; }
    public int BreakoutChecks => BullishBreakoutChecks + BearishBreakoutChecks;
    public int BullishBreakoutsDetected { get; init; }
    public int BearishBreakoutsDetected { get; init; }
    public int BreakoutsDetected => BullishBreakoutsDetected + BearishBreakoutsDetected;
    public int RetestChecks { get; init; }
    public int ValidRetests { get; init; }
    public int RetestsDetected => ValidRetests;
    public int ConfirmationChecks { get; init; }
    public int ConfirmationsPassed { get; init; }
    public int ActiveBuySideLiquidityLevels { get; init; }
    public int ActiveSellSideLiquidityLevels { get; init; }
    public int LiquidityLevelsCreated => ConfirmedSwings;
    public int LiquidityLevels => ActiveBuySideLiquidityLevels + ActiveSellSideLiquidityLevels;
    public int BuySideSweepChecks { get; init; }
    public int SellSideSweepChecks { get; init; }
    public int SweepChecks => BuySideSweepChecks + SellSideSweepChecks;
    public int BuySideSweepsDetected { get; init; }
    public int SellSideSweepsDetected { get; init; }
    public int SweepsDetected => BuySideSweepsDetected + SellSideSweepsDetected;
    public int SameCandleReclaims { get; init; }
    public int DelayedReclaims { get; init; }
    public int ReclaimsDetected => SameCandleReclaims + DelayedReclaims;
    public int CandidatesDetectedInMemory { get; init; }
    public int CandidatesRejectedAsDuplicate { get; init; }
    public int CandidatesSimulationInvalid { get; init; }
    public int CandidatesPersisted { get; init; }
    public int RawCandidates { get; init; }
    public int SimulationValidCandidates { get; init; }
    public int RawSimulatedTrades { get; init; }
    public int ClosedRawTrades { get; init; }
    public int ConfidenceApproved { get; init; }
    public int ConfidenceRejected { get; init; }
    public int RiskApproved { get; init; }
    public int RiskRejected { get; init; }
    public int FullPipelineApproved { get; init; }
    public string? PrimaryBlocker { get; init; }
    public string? PrimaryBlockerDetails { get; init; }
    public string? SuggestedNextAction { get; init; }
    public string ZeroCandidateClassification { get; init; } = "Unknown";
    public string StrategyFamily { get; init; } = string.Empty;
}

public sealed class CoverageDiagnosticsDto
{
    public DateTime? CoverageCheckStartedAtUtc { get; init; }
    public DateTime? RequestedFromUtc { get; init; }
    public DateTime? RequestedToUtc { get; init; }
    public string? RequestedTimeframe { get; init; }
    public int ExistingCandleCount { get; init; }
    public int MissingCandleCountEstimate { get; init; }
    public bool AutoImportAttempted { get; init; }
    public DateTime? ImportStartedAtUtc { get; init; }
    public DateTime? ImportCompletedAtUtc { get; init; }
    public int ImportedCandleCount { get; init; }
    public string? ImportError { get; init; }
    public string FinalCoverageStatus { get; init; } = "Unknown";
    public IReadOnlyList<CoverageMissingRangeDto> MissingRanges { get; init; } = [];
}

public sealed class CoverageMissingRangeDto
{
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public int EstimatedMissingCandles { get; init; }
}

public sealed class ZeroCandidateExplanationDto
{
    public string Classification { get; init; } = "Unknown";
    public string? PrimaryBlocker { get; init; }
    public string? Details { get; init; }
    public string? SuggestedNextAction { get; init; }
}

public sealed class DiagnosticEventDto
{
    public string Stage { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal Level { get; init; }
    public DateTime? LevelTimestampUtc { get; init; }
    public DateTime? EventTimestampUtc { get; init; }
    public DateTime? SecondaryTimestampUtc { get; init; }
    public decimal? EventPrice { get; init; }
    public string Outcome { get; init; } = string.Empty;
    public string? Reason { get; init; }
}

public sealed class GatedSubsetDto
{
    public int CandidateCount { get; init; }
    public int ClosedTradeCount { get; init; }
    public decimal NetPnl { get; init; }
    public decimal ProfitFactor { get; init; }
    public int Winners { get; init; }
    public int Losers { get; init; }
    public decimal WinRate { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public decimal? AverageConfidence { get; init; }
    public decimal? AverageRiskScore { get; init; }
}

public sealed class RawVsGatedComparisonDto
{
    public required GatedSubsetDto Raw { get; init; }
    public required GatedSubsetDto ConfidenceApproved { get; init; }
    public required GatedSubsetDto ConfidenceRejected { get; init; }
    public required GatedSubsetDto RiskApproved { get; init; }
    public required GatedSubsetDto RiskRejected { get; init; }
    public required GatedSubsetDto FullPipeline { get; init; }
    public IReadOnlyList<string> Interpretations { get; init; } = [];
}

public sealed class StrategyLabRunDto
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string StrategyCode { get; init; } = string.Empty;
    public string StrategyVersion { get; init; } = "1.0.0";
    public long ExchangeId { get; init; }
    public long SymbolId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string Timeframe { get; init; } = string.Empty;
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public StrategyLabExecutionMode ExecutionMode { get; init; }
    public StrategyLabRunStatus Status { get; init; }
    public string ExperimentFingerprint { get; init; } = string.Empty;
    public string? CurrentStage { get; init; }
    public decimal PercentComplete { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public string? ErrorMessage { get; init; }
    public string ParametersJson { get; init; } = "{}";
    public string StrategyFeatureFlagsJson { get; init; } = "{}";
    public StrategyLabObservationSettingsDto? ObservationSettings { get; init; }
    public decimal InitialBalance { get; init; }
    public long? RiskProfileId { get; init; }
    public string? CurrentStrategyVersion { get; init; }
    public bool StrategyVersionChanged { get; init; }
}

public sealed class StrategyResearchCandidateDto
{
    public long Id { get; init; }
    public DateTime SetupDetectedAtUtc { get; init; }
    public TradeDirection Direction { get; init; }
    public string SetupType { get; init; } = string.Empty;
    public decimal ProposedEntryPrice { get; init; }
    public decimal StopLoss { get; init; }
    public decimal Target1 { get; init; }
    public decimal RewardRisk { get; init; }
    public string StrategyReason { get; init; } = string.Empty;
    public RawOutcomeStatus RawOutcomeStatus { get; init; }
    public decimal? RawNetPnl { get; init; }
    public decimal? RawRMultiple { get; init; }
    public decimal? ConfidenceScore { get; init; }
    public decimal? ConfidenceThreshold { get; init; }
    public ResearchConfidenceDecision? ConfidenceDecision { get; init; }
    public decimal? ConfidenceMargin { get; init; }
    public string? ConfidenceReason { get; init; }
    public string? ConfidenceModelVersion { get; init; }
    public string? ConfidenceComponentsJson { get; init; }
    public DateTime? ConfidenceEvaluatedAtUtc { get; init; }
    public decimal? RiskScore { get; init; }
    public decimal? CandidateRiskScore { get; init; }
    public decimal? PortfolioRiskScore { get; init; }
    public PortfolioRiskAssessmentStatus? PortfolioRiskAssessmentStatus { get; init; }
    public decimal? RiskThreshold { get; init; }
    public ResearchRiskDecision? RiskDecision { get; init; }
    public decimal? RiskMargin { get; init; }
    public string? RiskReason { get; init; }
    public string? RiskModelVersion { get; init; }
    public string? RiskAssessmentVersion { get; init; }
    public string? RiskComponentsJson { get; init; }
    public string? RiskRuleResultsJson { get; init; }
    public string? RiskFailedRuleKeysJson { get; init; }
    public string? RiskWarningRuleKeysJson { get; init; }
    public decimal? RiskPerTradePercent { get; init; }
    public decimal? RiskAmount { get; init; }
    public decimal? RiskAtStopPercent { get; init; }
    public decimal? ProposedPositionSize { get; init; }
    public decimal? PositionNotional { get; init; }
    public decimal? ProposedLeverage { get; init; }
    public decimal? MinimumRequiredLeverage { get; init; }
    public decimal? AssessmentLeverage { get; init; }
    public decimal? PreferredLeverage { get; init; }
    public decimal? MaxLeverage { get; init; }
    public decimal? InitialMarginRequired { get; init; }
    public decimal? StopDistancePercent { get; init; }
    public decimal? PositionExposurePercent { get; init; }
    public decimal? NotionalExposurePercent { get; init; }
    public decimal? MarginUsagePercent { get; init; }
    public decimal? EstimatedRoundTripFees { get; init; }
    public decimal? FeeToTargetPercent { get; init; }
    public string? PositionSizingUnavailableReason { get; init; }
    public decimal? CurrentExposurePercent { get; init; }
    public decimal? CurrentNotionalExposurePercent { get; init; }
    public decimal? CurrentMarginUsagePercent { get; init; }
    public decimal? ConcurrentRiskPercent { get; init; }
    public decimal? DailyLossUsagePercent { get; init; }
    public decimal? CurrentDrawdownPercent { get; init; }
    public int? ConcurrentPositionCount { get; init; }
    public ResearchRiskScoreDecision? RiskScoreDecision { get; init; }
    public ResearchHardRuleComplianceDecision? HardRuleComplianceDecision { get; init; }
    public ResearchRiskPolicyEligibilityDecision? RiskPolicyEligibilityDecision { get; init; }
    public string? RiskPolicyReason { get; init; }
    public string? RiskPolicyFailedRuleKeysJson { get; init; }
    public decimal? RiskPolicyMinimumConfidence { get; init; }
    public string? FinalPipelineRejectionSourcesJson { get; init; }
    public long? RiskProfileId { get; init; }
    public string? RiskProfileVersion { get; init; }
    public string? RiskProfileName { get; init; }
    public string? RiskProfileSource { get; init; }
    public string? RiskProfileSnapshotId { get; init; }
    public string? RiskRejectedRuleKey { get; init; }
    public DateTime? RiskEvaluatedAtUtc { get; init; }
    public DrawdownCalculationMode? DrawdownCalculationMode { get; init; }
    public ResearchFinalPipelineDecision? FinalPipelineDecision { get; init; }
    public DateTime? RawExitTimeUtc { get; init; }
    public ResearchExitOutcome ExitOutcome { get; init; }
    public ResearchNetResult NetResult { get; init; }
    public decimal? Mfe { get; init; }
    public decimal? Mae { get; init; }
    public int? DurationBars { get; init; }
    public string StructureJson { get; init; } = "{}";

    /// <summary>Generic risk fields source for IndependentPaths/v1 (RiskOnly for new runs).</summary>
    public string? GenericRiskFieldSource { get; init; }
    public string? RiskPathAssessmentVersion { get; init; }
    public ResearchRiskDecision? RiskOnlyFinancialRiskDecision { get; init; }
    public ShadowEntryDecision? RiskOnlyEntryDecision { get; init; }
    public string? RiskOnlyRejectionSourcesJson { get; init; }
    public PathPortfolioAssessmentDto? RiskOnlyAssessment { get; init; }
    public decimal? RiskOnlyCurrentDrawdownPercent { get; init; }
    public decimal? RiskOnlyDailyLossUsagePercent { get; init; }
    public decimal? RiskOnlyCurrentMarginUsagePercent { get; init; }
    public decimal? RiskOnlyConcurrentRiskPercent { get; init; }
    public int? RiskOnlyOpenPositionCount { get; init; }
    public ResearchRiskDecision? FullPipelineFinancialRiskDecision { get; init; }
    public ShadowEntryDecision? FullPipelineEntryDecision { get; init; }
    public string? FullPipelineRejectionSourcesJson { get; init; }
    public PathPortfolioAssessmentDto? FullPipelineAssessment { get; init; }
    public decimal? FullPipelineCurrentDrawdownPercent { get; init; }
    public decimal? FullPipelineDailyLossUsagePercent { get; init; }
    public decimal? FullPipelineCurrentMarginUsagePercent { get; init; }
    public decimal? FullPipelineConcurrentRiskPercent { get; init; }
    public int? FullPipelineOpenPositionCount { get; init; }
}

public sealed class StrategyLabCandidateDetailDto
{
    public required StrategyResearchCandidateDto Candidate { get; init; }
    public PathPortfolioAssessmentDto? RiskOnlyAssessment { get; init; }
    public PathPortfolioAssessmentDto? FullPipelineAssessment { get; init; }
    public ResearchFinalPipelineDecision? FinalPipelineDecision { get; init; }
    public PortfolioPathCandidateComparisonDto? PathComparison { get; init; }
    public string PathAssessmentAvailability { get; init; } = "Available";
}

public sealed class PortfolioPathCandidateComparisonDto
{
    public bool FinancialRiskDecisionsDiffer { get; init; }
    public bool EntryDecisionsDiffer { get; init; }
    public decimal? DrawdownDifference { get; init; }
    public decimal? DailyLossDifference { get; init; }
    public decimal? BalanceDifference { get; init; }
    public IReadOnlyList<string> HighlightedDifferences { get; init; } = [];
}

public sealed class PortfolioPathComparisonDto
{
    public ShadowPortfolioSummaryDto? RiskOnlySummary { get; init; }
    public ShadowPortfolioSummaryDto? FullPipelineSummary { get; init; }
    public PortfolioPathDivergenceDto? DivergenceSummary { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
    public string RiskPathAssessmentVersion { get; init; } = IndependentPathsVersions.Current;
    public string PathAssessmentAvailability { get; init; } = "Available";
}

public sealed class StrategyLabRunDetailDto
{
    public required StrategyLabRunDto Run { get; init; }
    public required StrategyLabPerformanceSummaryDto Summary { get; init; }
    public required CandidateFunnelDto Funnel { get; init; }
    public RawVsGatedComparisonDto? GatedComparison { get; init; }
    public required IReadOnlyList<StrategyResearchCandidateDto> Candidates { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public CoverageDiagnosticsDto? CoverageDiagnostics { get; init; }
    public ZeroCandidateExplanationDto? ZeroCandidateExplanation { get; init; }
    public IReadOnlyList<DiagnosticEventDto> DiagnosticEvents { get; init; } = [];
    public IReadOnlyList<string> SampleFingerprints { get; init; } = [];
    public ShadowPortfolioSummaryDto? RiskOnlyShadowPortfolio { get; init; }
    public ShadowPortfolioSummaryDto? FullPipelineShadowPortfolio { get; init; }
    public PortfolioPathDivergenceDto? PortfolioPathDivergence { get; init; }
    public IReadOnlyList<string> PathDiagnostics { get; init; } = [];
    public string? RiskPathAssessmentVersion { get; init; }
    public ScoreDistributionDiagnosticsDto? PortfolioRiskScoreDiagnostics { get; init; }
    public string? DrawdownCalculationMode { get; init; }
}

public sealed class CreateStrategyLabRunRequest
{
    public string? Name { get; init; }
    public required string StrategyCode { get; init; }
    public required long ExchangeId { get; init; }
    public required long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public required DateTime FromUtc { get; init; }
    public required DateTime ToUtc { get; init; }
    public StrategyLabExecutionMode ExecutionMode { get; init; } = StrategyLabExecutionMode.RawStrategy;
    public Dictionary<string, string>? Parameters { get; init; }
    public decimal InitialBalance { get; init; } = 10000m;
    public long? RiskProfileId { get; init; }
    public decimal MakerFeeRate { get; init; } = 0.0002m;
    public decimal TakerFeeRate { get; init; } = 0.0004m;
    public decimal SlippagePercent { get; init; }
    public StrategyLabObservationSettingsDto? ObservationSettings { get; init; }
}

public sealed class StrategyHealthDto
{
    public string RegistrationStatus { get; init; } = "Healthy";
    public string CandleDataStatus { get; init; } = "Healthy";
    public int SyntheticTestsPassed { get; init; }
    public int SyntheticTestsTotal { get; init; }
    public int RecentEvaluations { get; init; }
    public int RecentRawCandidates { get; init; }
    public decimal CandidateRatePer1000Candles { get; init; }
    public int RawTrades { get; init; }
    public decimal? ConfidenceApprovalRate { get; init; }
    public decimal? RiskApprovalRate { get; init; }
    public int RecentStrategyLabRuns { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> ProblemCategories { get; init; } = [];
}

public sealed class StrategyLabStartupHealthDto
{
    public bool Healthy { get; init; }
    public bool StrategyLabRunTableAvailable { get; init; }
    public bool StrategyResearchCandidateTableAvailable { get; init; }
    public bool BreakoutRetestRegistered { get; init; }
    public bool LiquiditySweepRegistered { get; init; }
    public bool BreakoutRetestResolvable { get; init; }
    public bool LiquiditySweepResolvable { get; init; }
    public bool SyntheticTestsAvailable { get; init; }
    public IReadOnlyList<string> Issues { get; init; } = [];
    public string Status { get; init; } = "Healthy";
}

public sealed class SyntheticTestResultDto
{
    public required string ScenarioName { get; init; }
    public required string Description { get; init; }
    public bool Passed { get; init; }
    public int ExpectedCandidateCount { get; init; }
    public int ActualCandidateCount { get; init; }
    public TradeDirection? ExpectedDirection { get; init; }
    public TradeDirection? ActualDirection { get; init; }
    public string? ExpectedNoTradeReason { get; init; }
    public string? FailureDetails { get; init; }
}
