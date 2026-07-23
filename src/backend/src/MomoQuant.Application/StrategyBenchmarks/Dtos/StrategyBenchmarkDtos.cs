using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.StrategyBenchmarks.Dtos;

public sealed class CreateStrategyBenchmarkRequest
{
    public string Name { get; init; } = "June 2026 Strategy Benchmark - BNB BTC ETH";
    public string ExchangeCode { get; init; } = "BINANCE_FUTURES";
    public IReadOnlyList<string> Symbols { get; init; } = ["BNBUSDT", "BTCUSDT", "ETHUSDT"];
    public IReadOnlyList<string> Timeframes { get; init; } = ["3m", "5m", "15m"];
    public string ExecutionTimeframeMode { get; init; } = "AutoSelectByStrategy";
    public string StrategyExecutionScope { get; init; } = "PreferredOnly";
    public IReadOnlyList<string>? ManualExecutionTimeframes { get; init; }
    public IReadOnlyList<long> StrategyIds { get; init; } = [];
    public DateOnly BenchmarkFromDate { get; init; } = new(2026, 6, 1);
    public DateOnly BenchmarkToDate { get; init; } = new(2026, 6, 30);
    public DateOnly WarmupFromDate { get; init; } = new(2026, 5, 25);
    public decimal InitialBalance { get; init; } = 10_000m;
    public long RiskProfileId { get; init; } = 1;
    public string ExecutionMode { get; init; } = "MarketFill";
    public decimal MakerFeeRate { get; init; } = 0.0002m;
    public decimal TakerFeeRate { get; init; } = 0.0005m;
    public int OrderExpiryCandles { get; init; } = 3;
    public bool UseAiScoring { get; init; }
    public decimal MinConfidenceScore { get; init; } = 40m;
    public string EvaluationMode { get; init; } = nameof(BenchmarkEvaluationMode.RawStrategyResearch);
    public bool EnableShadowTradeAnalysis { get; init; } = true;
    public string SameCandleExitPolicy { get; init; } = "ConservativeStopFirst";
    public bool IncludeDisabledStrategies { get; init; }
    public bool ImportMissingData { get; init; } = true;
    public bool RecalculateIndicators { get; init; } = true;
    public bool RunEachStrategyIndividually { get; init; } = true;
    public bool RunCombinedPortfolio { get; init; }
    public bool AllowLowCoverage { get; init; }
    public bool StopOnFirstFailure { get; init; }
}

public sealed class StrategyBenchmarkPreflightRequest
{
    public required string ExchangeCode { get; init; }
    public required IReadOnlyList<string> Symbols { get; init; }
    public required IReadOnlyList<long> StrategyIds { get; init; }
    public required DateOnly BenchmarkFromDate { get; init; }
    public required DateOnly BenchmarkToDate { get; init; }
    public required DateOnly WarmupFromDate { get; init; }
    public string ExecutionTimeframeMode { get; init; } = "AutoSelectByStrategy";
    public string StrategyExecutionScope { get; init; } = "PreferredOnly";
    public IReadOnlyList<string>? ManualExecutionTimeframes { get; init; }
}

public sealed class StrategyBenchmarkPreflightDto
{
    public required IReadOnlyList<string> SelectedSymbols { get; init; }
    public required IReadOnlyList<string> SelectedStrategies { get; init; }
    public required string ExecutionTimeframeMode { get; init; }
    public required string StrategyExecutionScope { get; init; }
    public required IReadOnlyList<StrategyBenchmarkResolvedExecutionRunDto> ResolvedExecutionRuns { get; init; }
    public required IReadOnlyList<StrategyBenchmarkPreflightTimeframeRequirementDto> RequiredImportTimeframes { get; init; }
    public required IReadOnlyList<StrategyBenchmarkPreflightTimeframeRequirementDto> RequiredIndicatorTimeframes { get; init; }
    public int EstimatedTotalRuns { get; init; }
    public int EstimatedCandleCount { get; init; }
    public required IReadOnlyList<string> MissingDataSummary { get; init; }
    public required IReadOnlyList<string> MissingIndicatorsSummary { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> BlockingIssues { get; init; }
}

public sealed class StrategyBenchmarkResolvedExecutionRunDto
{
    public long StrategyId { get; init; }
    public required string StrategyCode { get; init; }
    public required string StrategyName { get; init; }
    public required IReadOnlyList<string> ExecutionTimeframes { get; init; }
    public required IReadOnlyList<string> RequiredDataTimeframes { get; init; }
    public required IReadOnlyList<string> RequiredIndicatorTimeframes { get; init; }
}

public sealed class StrategyBenchmarkPreflightTimeframeRequirementDto
{
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public required string Reason { get; init; }
    public bool IsAnchorData { get; init; }
}

public sealed class StrategyBenchmarkRunDto
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public required string Status { get; init; }
    public long ExchangeId { get; init; }
    public required IReadOnlyList<string> Symbols { get; init; }
    public required IReadOnlyList<string> Timeframes { get; init; }
    public required IReadOnlyList<long> StrategyIds { get; init; }
    public DateTime BenchmarkFromUtc { get; init; }
    public DateTime BenchmarkToUtc { get; init; }
    public DateTime WarmupFromUtc { get; init; }
    public DateTime WarmupToUtc { get; init; }
    public decimal InitialBalance { get; init; }
    public long RiskProfileId { get; init; }
    public required string ExecutionMode { get; init; }
    public bool UseAiScoring { get; init; }
    public decimal MinConfidenceScore { get; init; }
    public string EvaluationMode { get; init; } = nameof(BenchmarkEvaluationMode.RawStrategyResearch);
    public bool IncludeDisabledStrategies { get; init; }
    public int TotalRuns { get; init; }
    public int CompletedRuns { get; init; }
    public decimal PercentComplete { get; init; }
    public string? CurrentStage { get; init; }
    public string? CurrentSymbol { get; init; }
    public string? CurrentTimeframe { get; init; }
    public string? CurrentStrategy { get; init; }
    public string? Message { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class StrategyBenchmarkProgressDto
{
    public long BenchmarkRunId { get; init; }
    public required string Status { get; init; }
    public string? CurrentStage { get; init; }
    public decimal PercentComplete { get; init; }
    public decimal DataPreparationPercent { get; init; }
    public decimal BacktestPercent { get; init; }
    public string? CurrentSymbol { get; init; }
    public string? CurrentTimeframe { get; init; }
    public string? CurrentStrategy { get; init; }
    public int CompletedRuns { get; init; }
    public int TotalRuns { get; init; }
    public int FailedRuns { get; init; }
    public int PendingRuns { get; init; }
    public string? Message { get; init; }
    public DateTime? LastHeartbeatAtUtc { get; init; }
    public DateTime? CurrentChunkFromUtc { get; init; }
    public DateTime? CurrentChunkToUtc { get; init; }
    public int CompletedImportChunks { get; init; }
    public int TotalImportChunks { get; init; }
    public int InsertedCandles { get; init; }
    public int SkippedDuplicateCandles { get; init; }
}

public sealed class StrategyBenchmarkRunItemDto
{
    public long Id { get; init; }
    public long BenchmarkRunId { get; init; }
    public long StrategyId { get; init; }
    public required string StrategyCode { get; init; }
    public required string StrategyName { get; init; }
    public long SymbolId { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public required string Status { get; init; }
    public long? BacktestRunId { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public DateTime? LastHeartbeatAtUtc { get; init; }
    public int? DurationSeconds { get; init; }
    public int? CandleCount { get; init; }
    public DateTime? LastProcessedCandleTimeUtc { get; init; }
    public int? LastProcessedCandleIndex { get; init; }
    public int? TotalCandles { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class StrategyBenchmarkDiagnosticsDto
{
    public long BenchmarkRunId { get; init; }
    public required string Status { get; init; }
    public string? CurrentStage { get; init; }
    public decimal PercentComplete { get; init; }
    public int CompletedRuns { get; init; }
    public int TotalRuns { get; init; }
    public int FailedRuns { get; init; }
    public int PendingRuns { get; init; }
    public StrategyBenchmarkRunItemDto? RunningItem { get; init; }
    public string? LastError { get; init; }
    public required IReadOnlyList<StrategyBenchmarkRunItemDto> RecentRunItems { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed class StrategyBenchmarkStrategyResultDto
{
    public int Rank { get; init; }
    public long StrategyId { get; init; }
    public required string StrategyCode { get; init; }
    public required string StrategyName { get; init; }
    public required string Grade { get; init; }
    public decimal Score { get; init; }
    public int TotalTrades { get; init; }
    public decimal NetPnl { get; init; }
    public decimal NetPnlPercent { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal WinRatePercent { get; init; }
    public decimal AverageConfidenceScore { get; init; }
    public string? BestSymbol { get; init; }
    public string? WorstSymbol { get; init; }
    public string? BestTimeframe { get; init; }
    public string? WorstTimeframe { get; init; }
    public string? ResultReason { get; init; }
    public int CandidateSignals { get; init; }
    public int ConfidenceRejected { get; init; }
    public int RiskRejections { get; init; }
    public decimal ShadowNetPnlPercent { get; init; }
    public decimal FalseRejectRatePercent { get; init; }
    public int NoTradeCount { get; init; }
    public string? TopNoTradeReason { get; init; }
    public required IReadOnlyList<string> Strengths { get; init; }
    public required IReadOnlyList<string> Weaknesses { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public string? PipelineSummary { get; init; }
    public int BbSweeps { get; init; }
    public int LiquiditySweeps { get; init; }
    public int CisdConfirmations { get; init; }
    public int RsiPassed { get; init; }
    public int TargetPassed3R { get; init; }
    public int FinalCandidates { get; init; }
}

public sealed class StrategyBenchmarkSymbolResultDto
{
    public required string Symbol { get; init; }
    public required string StrategyCode { get; init; }
    public required string StrategyName { get; init; }
    public required string Grade { get; init; }
    public decimal Score { get; init; }
    public int TotalTrades { get; init; }
    public decimal NetPnlPercent { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal WinRatePercent { get; init; }
}

public sealed class StrategyBenchmarkTimeframeResultDto
{
    public required string Timeframe { get; init; }
    public required string StrategyCode { get; init; }
    public required string StrategyName { get; init; }
    public required string Grade { get; init; }
    public decimal Score { get; init; }
    public int TotalTrades { get; init; }
    public decimal NetPnlPercent { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal WinRatePercent { get; init; }
}

public sealed class StrategyBenchmarkGradeDto
{
    public required string Grade { get; init; }
    public decimal Score { get; init; }
    public string? Label { get; init; }
    public required IReadOnlyList<string> Strengths { get; init; }
    public required IReadOnlyList<string> Weaknesses { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed class StrategyBenchmarkReportDto
{
    public required StrategyBenchmarkRunDto Run { get; init; }
    public required StrategyBenchmarkSummaryDto Summary { get; init; }
    public required StrategyBenchmarkPreparationDto DataPreparation { get; init; }
    public required IReadOnlyList<StrategyBenchmarkStrategyResultDto> StrategyRanking { get; init; }
    public required IReadOnlyList<StrategyBenchmarkResultMatrixDto> StrategyDetails { get; init; }
    public required IReadOnlyList<StrategyBenchmarkSymbolResultDto> SymbolResults { get; init; }
    public required IReadOnlyList<StrategyBenchmarkTimeframeResultDto> TimeframeResults { get; init; }
    public required IReadOnlyList<StrategyBenchmarkNoTradeAnalysisDto> NoTradeAnalysis { get; init; }
    public required IReadOnlyList<StrategyBenchmarkRiskRejectionDto> RiskRejections { get; init; }
    public required IReadOnlyList<StrategyBenchmarkPipelineFunnelDto> PipelineFunnel { get; init; }
    public required IReadOnlyList<CandidateTradeLedgerDto> CandidateTrades { get; init; }
    public required IReadOnlyList<ExecutedTradeLedgerDto> ExecutedTrades { get; init; }
    public required IReadOnlyList<CandidateTradeLedgerDto> RejectedCandidates { get; init; }
    public required IReadOnlyList<ShadowTradeLedgerDto> ShadowTrades { get; init; }
    public required IReadOnlyList<StrategyBenchmarkRejectionQualityDto> RejectionQuality { get; init; }
    public required RiskConfidenceCalibrationDto RiskConfidenceCalibration { get; init; }
    public required IReadOnlyList<string> DecisionRecommendations { get; init; }
    public DateTime GeneratedAtUtc { get; init; }
}

public sealed class StrategyBenchmarkNoTradeAnalysisDto
{
    public required string StrategyCode { get; init; }
    public required string StrategyName { get; init; }
    public required string Symbol { get; init; }
    public required string ExecutionTimeframe { get; init; }
    public int Evaluations { get; init; }
    public int NoTradeCount { get; init; }
    public int CandidateSignals { get; init; }
    public int Trades { get; init; }
    public string? TopNoTradeReason { get; init; }
    public int TopNoTradeReasonCount { get; init; }
    public int MissingDataCount { get; init; }
    public int MissingIndicatorsCount { get; init; }
    public int RiskRejections { get; init; }
    public string? TopRiskRejectionReason { get; init; }
    public string? ResultReason { get; init; }
    public required string Recommendation { get; init; }
    public required IReadOnlyList<StrategyFunnelStepDto> Funnel { get; init; }
    public required IReadOnlyList<string> TuningSuggestions { get; init; }
    public string? PipelineSummary { get; init; }
    public string? WhyZeroTradesAnalysis { get; init; }
    public IReadOnlyDictionary<string, int>? NoTradeReasonBreakdown { get; init; }
    public BbLiquiditySweepFunnelCountsDto? BbFunnelCounts { get; init; }
}

public sealed class BbLiquiditySweepFunnelCountsDto
{
    public int Evaluations { get; init; }
    public int CandlesInAllowedSession { get; init; }
    public int CandlesOutsideSession { get; init; }
    public int BollingerBandUpperWickBreaks { get; init; }
    public int BollingerBandLowerWickBreaks { get; init; }
    public int CandlesClosedBackInsideBb { get; init; }
    public int FiveMinuteLiquidityLevelsDetected { get; init; }
    public int OneMinuteLiquidityLevelsDetected { get; init; }
    public int BuySideLiquidityLevelsAvailable { get; init; }
    public int SellSideLiquidityLevelsAvailable { get; init; }
    public int BuySideLiquiditySweeps { get; init; }
    public int SellSideLiquiditySweeps { get; init; }
    public int CloseBackAcrossLiquidityLine { get; init; }
    public int CisdCandidates { get; init; }
    public int CisdConfirmed { get; init; }
    public int RsiPrimedEvaluations { get; init; }
    public int RsiPrimedPassed { get; init; }
    public int TargetPassed3R { get; init; }
    public int TargetPassedMinimumR { get; init; }
    public int FinalCandidateSignals { get; init; }
    public int TradesCreated { get; init; }
    public string? StrictnessProfile { get; init; }
    public bool DetectorCalibrationMode { get; init; }
}

public sealed class StrategyBenchmarkRiskRejectionDto
{
    public required string StrategyCode { get; init; }
    public required string StrategyName { get; init; }
    public required string Symbol { get; init; }
    public required string ExecutionTimeframe { get; init; }
    public int TotalCandidateSignals { get; init; }
    public int RiskRejections { get; init; }
    public string? TopRiskReason { get; init; }
    public decimal RejectionPercent { get; init; }
    public required string Recommendation { get; init; }
}

public sealed class StrategyFunnelStepDto
{
    public required string StepName { get; init; }
    public int PassedCount { get; init; }
    public int FailedCount { get; init; }
    public string? FailReason { get; init; }
}

public sealed class StrategyBenchmarkPipelineFunnelDto
{
    public required string StrategyCode { get; init; }
    public required string StrategyName { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public int Evaluations { get; init; }
    public int CandidateSignals { get; init; }
    public int ConfidenceApproved { get; init; }
    public int ConfidenceRejected { get; init; }
    public int RiskApproved { get; init; }
    public int RiskRejected { get; init; }
    public int ExecutedTrades { get; init; }
    public int ShadowTrades { get; init; }
    public decimal FinalNetPnl { get; init; }
    public decimal ShadowNetPnl { get; init; }
    public string? PipelineSummary { get; init; }
}

public sealed class CandidateTradeLedgerDto
{
    public DateTime SignalTimeUtc { get; init; }
    public required string StrategyCode { get; init; }
    public required string StrategyName { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public required string Direction { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal StopLoss { get; init; }
    public decimal TakeProfit { get; init; }
    public decimal CombinedConfidence { get; init; }
    public decimal RiskPercent { get; init; }
    public decimal Leverage { get; init; }
    public decimal MarginUsed { get; init; }
    public decimal NotionalValue { get; init; }
    public required string FinalDecision { get; init; }
    public required string FinalDecisionReason { get; init; }
}

public sealed class ExecutedTradeLedgerDto
{
    public DateTime EntryTimeUtc { get; init; }
    public DateTime? ExitTimeUtc { get; init; }
    public required string StrategyCode { get; init; }
    public required string Symbol { get; init; }
    public required string Direction { get; init; }
    public decimal Leverage { get; init; }
    public decimal MarginUsed { get; init; }
    public decimal NotionalValue { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal? ExitPrice { get; init; }
    public decimal StopLoss { get; init; }
    public decimal TakeProfit { get; init; }
    public decimal NetPnl { get; init; }
    public decimal NetPnlPercent { get; init; }
    public string? ExitReason { get; init; }
}

public sealed class ShadowTradeLedgerDto
{
    public DateTime SignalTimeUtc { get; init; }
    public required string StrategyCode { get; init; }
    public required string Symbol { get; init; }
    public required string Direction { get; init; }
    public required string RejectedBy { get; init; }
    public required string OutcomeClassification { get; init; }
    public string? ShadowExitReason { get; init; }
    public decimal ShadowNetPnl { get; init; }
    public decimal MaxFavorableExcursion { get; init; }
    public decimal MaxAdverseExcursion { get; init; }
    public int DurationCandles { get; init; }
}

public sealed class StrategyBenchmarkRejectionQualityDto
{
    public required string StrategyCode { get; init; }
    public required string StrategyName { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public int RejectedCandidateCount { get; init; }
    public int RejectedByConfidenceCount { get; init; }
    public int RejectedByRiskCount { get; init; }
    public int RejectedByBothCount { get; init; }
    public int ShadowTradesSimulated { get; init; }
    public int RejectedWouldHaveWon { get; init; }
    public int RejectedWouldHaveLost { get; init; }
    public int RejectedBreakEven { get; init; }
    public int RejectedNotEnoughData { get; init; }
    public decimal ShadowNetPnl { get; init; }
    public int ConfidenceFalseRejectCount { get; init; }
    public int RiskFalseRejectCount { get; init; }
    public int ConfidenceCorrectRejectCount { get; init; }
    public int RiskCorrectRejectCount { get; init; }
}

public sealed class RiskConfidenceCalibrationDto
{
    public decimal ConfidenceFalseRejectionRatePercent { get; init; }
    public decimal RiskFalseRejectionRatePercent { get; init; }
    public decimal ConfidenceCorrectRejectionRatePercent { get; init; }
    public decimal RiskCorrectRejectionRatePercent { get; init; }
    public decimal? ConfidenceThresholdRecommendation { get; init; }
    public required IReadOnlyList<string> RiskRuleRecommendations { get; init; }
    public required IReadOnlyList<string> StrategySpecificRecommendations { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> EvidenceSummary { get; init; }
}

public sealed class StrategyBenchmarkSummaryDto
{
    public int TotalBacktestRuns { get; init; }
    public int CompletedRuns { get; init; }
    public int FailedRuns { get; init; }
    public string? BestOverallStrategy { get; init; }
    public required IReadOnlyDictionary<string, string> BestStrategyBySymbol { get; init; }
    public required IReadOnlyDictionary<string, string> BestStrategyByTimeframe { get; init; }
    public required IReadOnlyList<string> StrategiesToRetune { get; init; }
    public required IReadOnlyList<string> StrategiesNeedingMoreData { get; init; }
}

public sealed class StrategyBenchmarkPreparationDto
{
    public required IReadOnlyList<StrategyBenchmarkImportSummaryDto> Imports { get; init; }
    public required IReadOnlyList<StrategyBenchmarkDataQualityDto> DataQuality { get; init; }
    public required IReadOnlyList<StrategyBenchmarkIndicatorSummaryDto> Indicators { get; init; }
}

public sealed class StrategyBenchmarkImportSummaryDto
{
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public DateTime RequestedFromUtc { get; init; }
    public DateTime RequestedToUtc { get; init; }
    public int ReceivedCandles { get; init; }
    public int InsertedCandles { get; init; }
    public int SkippedDuplicateCandles { get; init; }
    public int MissingCandles { get; init; }
    public decimal CoveragePercent { get; init; }
    public int TotalChunks { get; init; }
    public int FailedChunks { get; init; }
    public required IReadOnlyList<StrategyBenchmarkImportChunkSummaryDto> Chunks { get; init; }
}

public sealed class StrategyBenchmarkImportChunkSummaryDto
{
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public int ReceivedCandles { get; init; }
    public int InsertedCandles { get; init; }
    public int SkippedDuplicateCandles { get; init; }
    public required string Status { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class StrategyBenchmarkDataQualityDto
{
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public int TotalCandles { get; init; }
    public int ExpectedCandles { get; init; }
    public int MissingCandles { get; init; }
    public int DuplicateCandles { get; init; }
    public DateTime? FirstOpenTimeUtc { get; init; }
    public DateTime? LastOpenTimeUtc { get; init; }
    public decimal CoveragePercent { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed class StrategyBenchmarkIndicatorSummaryDto
{
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public int CandlesProcessed { get; init; }
    public int SnapshotsInserted { get; init; }
    public int SnapshotsUpdated { get; init; }
    public int MissingSnapshots { get; init; }
}

public sealed class StrategyBenchmarkResultMatrixDto
{
    public required string StrategyCode { get; init; }
    public required string StrategyName { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public required string Grade { get; init; }
    public decimal Score { get; init; }
    public int TotalTrades { get; init; }
    public decimal NetPnl { get; init; }
    public decimal NetPnlPercent { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal WinRatePercent { get; init; }
    public decimal AverageWin { get; init; }
    public decimal AverageLoss { get; init; }
    public decimal LargestLoss { get; init; }
    public decimal AverageRewardRisk { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}
