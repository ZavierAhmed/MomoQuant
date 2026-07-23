namespace MomoQuant.Application.Reports.Dtos;

public sealed class ReportQuery
{
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public long? SymbolId { get; init; }
    public long? StrategyId { get; init; }
    public string? Timeframe { get; init; }
    public string? Mode { get; init; }
    public string? MarketRegime { get; init; }
    public int Limit { get; init; } = 100;
}

public sealed class OverviewReportDto
{
    public int TotalBacktests { get; init; }
    public int TotalPaperSessions { get; init; }
    public int TotalTrades { get; init; }
    public int TotalOrders { get; init; }
    public int TotalSignals { get; init; }
    public int TotalRiskDecisions { get; init; }
    public int TotalAiDecisions { get; init; }
    public int TotalMissedOrders { get; init; }
    public string? BestStrategy { get; init; }
    public string? WorstStrategy { get; init; }
    public string? BestSymbol { get; init; }
    public string? WorstSymbol { get; init; }
    public decimal TotalNetPnl { get; init; }
    public decimal TotalFees { get; init; }
    public decimal AverageWinRate { get; init; }
    public decimal AverageProfitFactor { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public DateTime GeneratedAtUtc { get; init; }
}

public sealed class BacktestReportDto
{
    public long BacktestRunId { get; init; }
    public required string Name { get; init; }
    public required string Status { get; init; }
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public decimal InitialBalance { get; init; }
    public decimal FinalBalance { get; init; }
    public decimal NetPnl { get; init; }
    public decimal NetPnlPercent { get; init; }
    public decimal GrossProfit { get; init; }
    public decimal GrossLoss { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdown { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public int BreakEvenTrades { get; init; }
    public decimal WinRatePercent { get; init; }
    public decimal AverageWin { get; init; }
    public decimal AverageLoss { get; init; }
    public decimal LargestWin { get; init; }
    public decimal LargestLoss { get; init; }
    public decimal AverageRewardRisk { get; init; }
    public decimal TotalFees { get; init; }
    public int TotalSignals { get; init; }
    public int ApprovedSignals { get; init; }
    public int RejectedSignals { get; init; }
    public int MissedOrders { get; init; }
    public int FilledOrders { get; init; }
    public int CancelledOrders { get; init; }
}

public sealed class PaperSessionReportDto
{
    public long PaperSessionId { get; init; }
    public long PaperAccountId { get; init; }
    public required string Name { get; init; }
    public required string Status { get; init; }
    public required string Mode { get; init; }
    public decimal InitialBalance { get; init; }
    public decimal CurrentBalance { get; init; }
    public decimal CurrentEquity { get; init; }
    public decimal RealizedPnl { get; init; }
    public decimal UnrealizedPnl { get; init; }
    public decimal TotalFees { get; init; }
    public decimal MaxDrawdown { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public decimal WinRatePercent { get; init; }
    public decimal ProfitFactor { get; init; }
    public int TotalOrders { get; init; }
    public int FilledOrders { get; init; }
    public int MissedOrders { get; init; }
    public int RejectedSignals { get; init; }
    public DateTime GeneratedAtUtc { get; init; }
}

public sealed class StrategyPerformanceReportDto
{
    public long StrategyId { get; init; }
    public required string StrategyCode { get; init; }
    public required string StrategyName { get; init; }
    public required string Mode { get; init; }
    public int TotalSignals { get; init; }
    public int EntrySignals { get; init; }
    public int NoTradeSignals { get; init; }
    public int ApprovedSignals { get; init; }
    public int RejectedSignals { get; init; }
    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public decimal WinRatePercent { get; init; }
    public decimal NetPnl { get; init; }
    public decimal GrossProfit { get; init; }
    public decimal GrossLoss { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal AveragePnl { get; init; }
    public decimal AverageConfidenceScore { get; init; }
    public decimal AverageRewardRisk { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public decimal TotalFees { get; init; }
    public int MissedOrders { get; init; }
}

public sealed class SymbolPerformanceReportDto
{
    public long SymbolId { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public required string Mode { get; init; }
    public int TotalSignals { get; init; }
    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public decimal WinRatePercent { get; init; }
    public decimal NetPnl { get; init; }
    public decimal GrossProfit { get; init; }
    public decimal GrossLoss { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal AveragePnl { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public decimal TotalFees { get; init; }
    public int MissedOrders { get; init; }
    public string? BestStrategy { get; init; }
    public string? WorstStrategy { get; init; }
}

public sealed class MarketRegimeReportDto
{
    public required string MarketRegime { get; init; }
    public int TotalSignals { get; init; }
    public int ApprovedSignals { get; init; }
    public int RejectedSignals { get; init; }
    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public decimal WinRatePercent { get; init; }
    public decimal NetPnl { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal AverageConfidenceScore { get; init; }
    public decimal AverageRiskRejectionRate { get; init; }
}

public sealed class RiskRejectionReportDto
{
    public int TotalRiskDecisions { get; init; }
    public int ApprovedCount { get; init; }
    public int RejectedCount { get; init; }
    public int AdjustedCount { get; init; }
    public int EmergencyBlockedCount { get; init; }
    public decimal RejectionRatePercent { get; init; }
    public required IReadOnlyList<RiskRuleRejectionSummaryDto> TopRejectedRules { get; init; }
    public required IReadOnlyList<RiskRejectionDetailDto> RejectionDetails { get; init; }
}

public sealed class RiskRuleRejectionSummaryDto
{
    public required string RuleKey { get; init; }
    public int Count { get; init; }
    public decimal Percentage { get; init; }
}

public sealed class RiskRejectionDetailDto
{
    public DateTime TimestampUtc { get; init; }
    public required string Mode { get; init; }
    public required string Symbol { get; init; }
    public string? StrategyCode { get; init; }
    public string? RejectedRuleKey { get; init; }
    public required string Reason { get; init; }
    public decimal? ConfidenceScore { get; init; }
}

public sealed class AiDecisionReportDto
{
    public int TotalAiDecisions { get; init; }
    public decimal AverageConfidenceScore { get; init; }
    public required IReadOnlyDictionary<string, int> ConfidenceBreakdown { get; init; }
    public required IReadOnlyDictionary<string, int> RegimeBreakdown { get; init; }
    public int AnomalyCount { get; init; }
    public required IReadOnlyDictionary<string, int> AnomalySeverityBreakdown { get; init; }
    public required IReadOnlyList<StrategyConfidenceSummaryDto> AverageConfidenceByStrategy { get; init; }
    public required IReadOnlyList<SymbolConfidenceSummaryDto> AverageConfidenceBySymbol { get; init; }
    public int HighConfidenceLosses { get; init; }
    public required IReadOnlyList<MarketRegimeReportDto> MarketRegimePerformance { get; init; }
}

public sealed class StrategyConfidenceSummaryDto
{
    public required string StrategyCode { get; init; }
    public decimal AverageConfidenceScore { get; init; }
}

public sealed class SymbolConfidenceSummaryDto
{
    public required string Symbol { get; init; }
    public decimal AverageConfidenceScore { get; init; }
}

public sealed class MissedOrderReportDto
{
    public int TotalMissedOrders { get; init; }
    public required IReadOnlyList<MissedOrderGroupDto> MissedByStrategy { get; init; }
    public required IReadOnlyList<MissedOrderGroupDto> MissedBySymbol { get; init; }
    public required IReadOnlyList<MissedOrderGroupDto> MissedByTimeframe { get; init; }
    public required IReadOnlyList<MissedOrderGroupDto> MissedByExecutionMode { get; init; }
    public decimal AverageMissDistance { get; init; }
    public decimal? EstimatedMissedPnl { get; init; }
    public required IReadOnlyList<MissedOrderDetailDto> MissedOrderDetails { get; init; }
}

public sealed class MissedOrderGroupDto
{
    public required string Key { get; init; }
    public int Count { get; init; }
}

public sealed class MissedOrderDetailDto
{
    public long Id { get; init; }
    public required string Symbol { get; init; }
    public string? StrategyCode { get; init; }
    public required string Timeframe { get; init; }
    public required string ExecutionMode { get; init; }
    public decimal RequestedPrice { get; init; }
    public required string Reason { get; init; }
    public DateTime ExpiredAtUtc { get; init; }
}

public sealed class EquityCurvePointDto
{
    public DateTime TimestampUtc { get; init; }
    public decimal Balance { get; init; }
    public decimal Equity { get; init; }
    public decimal Drawdown { get; init; }
    public decimal DrawdownPercent { get; init; }
    public int OpenPositionCount { get; init; }
}

public sealed class DrawdownReportDto
{
    public decimal MaxDrawdown { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public DateTime? DrawdownStartUtc { get; init; }
    public DateTime? DrawdownEndUtc { get; init; }
    public int? RecoveryTimeCandles { get; init; }
    public required IReadOnlyList<EquityCurvePointDto> DrawdownSeries { get; init; }
}

public sealed class ExecutionReportDto
{
    public required string Mode { get; init; }
    public int TotalOrders { get; init; }
    public int FilledOrders { get; init; }
    public int CancelledOrders { get; init; }
    public int MissedOrders { get; init; }
    public decimal FillRatePercent { get; init; }
    public decimal AverageFees { get; init; }
    public decimal TotalFees { get; init; }
    public int MakerOrders { get; init; }
    public int TakerOrders { get; init; }
    public decimal MakerFillRatePercent { get; init; }
    public decimal MissedMakerRatePercent { get; init; }
}
