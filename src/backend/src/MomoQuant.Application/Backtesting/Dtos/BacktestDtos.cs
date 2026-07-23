using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Backtesting.Dtos;

public sealed class RunBacktestRequest
{
    public required string Name { get; init; }
    public long ExchangeId { get; init; }
    public required IReadOnlyList<long> SymbolIds { get; init; }
    public required IReadOnlyList<string> Timeframes { get; init; }
    public required DateTime FromUtc { get; init; }
    public required DateTime ToUtc { get; init; }
    public string? FromDate { get; init; }
    public string? ToDate { get; init; }
    public bool AutoImportMissingCandles { get; init; } = true;
    public decimal InitialBalance { get; init; }
    public long RiskProfileId { get; init; }
    public required IReadOnlyList<long> StrategyIds { get; init; }
    public string ExecutionMode { get; init; } = "MarketFill";
    public decimal MakerFeeRate { get; init; } = 0.0002m;
    public decimal TakerFeeRate { get; init; } = 0.0005m;
    public int OrderExpiryCandles { get; init; } = 3;
    public bool UseAiScoring { get; init; }
    public bool StrictAiRequired { get; init; }
    public decimal MinConfidenceScore { get; init; } = 80m;
    public decimal SlippagePercent { get; init; }
    public string EvaluationMode { get; init; } = nameof(BenchmarkEvaluationMode.FullValidation);
    public bool EnableShadowTradeAnalysis { get; init; } = true;
    public string SameCandleExitPolicy { get; init; } = "ConservativeStopFirst";
    public bool RunAnyway { get; init; }
    public long? BenchmarkRunId { get; init; }
    public long? BenchmarkRunItemId { get; init; }
    public string? BenchmarkStrategyCode { get; init; }
    public string? BenchmarkSymbol { get; init; }
    public string? BenchmarkTimeframe { get; init; }
    public long? RequestedByUserId { get; init; }
}

public sealed class RunBacktestResponse
{
    public long BacktestRunId { get; init; }
    public required string Status { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public required BacktestSummaryDto Summary { get; init; }
}

public sealed class BacktestRunDto
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public required string Status { get; init; }
    public long ExchangeId { get; init; }
    public long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public decimal InitialBalance { get; init; }
    public decimal? FinalBalance { get; init; }
    public long RiskProfileId { get; init; }
    public required string ExecutionMode { get; init; }
    public bool UseAiScoring { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class BacktestSummaryDto
{
    public decimal InitialBalance { get; init; }
    public decimal FinalBalance { get; init; }
    public decimal NetPnl { get; init; }
    public decimal NetPnlPercent { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public decimal WinRatePercent { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal AverageWin { get; init; }
    public decimal AverageLoss { get; init; }
    public decimal LargestLoss { get; init; }
    public decimal AverageRewardRisk { get; init; }
    public decimal TotalFees { get; init; }
    public int TotalSignals { get; init; }
    public int ApprovedSignals { get; init; }
    public int FilledOrders { get; init; }
    public int MissedOrders { get; init; }
    public int RejectedSignals { get; init; }
    public int ConfidenceRejectedSignals { get; init; }
    public int RiskRejectedSignals { get; init; }
    public int RejectedByBothSignals { get; init; }
}

public sealed class BacktestResultDto
{
    public long BacktestRunId { get; init; }
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
    public decimal TotalSlippage { get; init; }
    public int TotalSignals { get; init; }
    public int ApprovedSignals { get; init; }
    public int RejectedSignals { get; init; }
    public int MissedOrders { get; init; }
    public int FilledOrders { get; init; }
    public int CancelledOrders { get; init; }
}

public sealed class BacktestTradeDto
{
    public long Id { get; init; }
    public long SymbolId { get; init; }
    public long? StrategyId { get; init; }
    public required string Direction { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal? ExitPrice { get; init; }
    public decimal Quantity { get; init; }
    public decimal StopLoss { get; init; }
    public decimal TakeProfit { get; init; }
    public required string Status { get; init; }
    public decimal NetPnl { get; init; }
    public decimal Fees { get; init; }
    public string? CloseReason { get; init; }
    public DateTime OpenedAtUtc { get; init; }
    public DateTime? ClosedAtUtc { get; init; }
}

public sealed class BacktestOrderDto
{
    public long Id { get; init; }
    public long SymbolId { get; init; }
    public required string Mode { get; init; }
    public required string Side { get; init; }
    public required string OrderType { get; init; }
    public decimal Price { get; init; }
    public decimal Quantity { get; init; }
    public required string Status { get; init; }
    public bool IsPostOnly { get; init; }
    public DateTime RequestedAtUtc { get; init; }
    public DateTime? FilledAtUtc { get; init; }
}

public sealed class BacktestMissedOrderDto
{
    public long Id { get; init; }
    public long SymbolId { get; init; }
    public long SignalId { get; init; }
    public decimal RequestedPrice { get; init; }
    public required string Reason { get; init; }
    public DateTime ExpiredAtUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class BacktestEquityPointDto
{
    public long BacktestRunId { get; init; }
    public DateTime TimestampUtc { get; init; }
    public decimal Balance { get; init; }
    public decimal Equity { get; init; }
    public decimal Drawdown { get; init; }
    public decimal DrawdownPercent { get; init; }
    public int OpenPositionCount { get; init; }
}

public sealed class BacktestStrategyBreakdownDto
{
    public required string StrategyCode { get; init; }
    public int TotalSignals { get; init; }
    public int ApprovedSignals { get; init; }
    public int RejectedSignals { get; init; }
    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public decimal NetPnl { get; init; }
    public decimal WinRatePercent { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public decimal AverageConfidenceScore { get; init; }
}

public sealed class BacktestSymbolBreakdownDto
{
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public decimal NetPnl { get; init; }
    public decimal WinRatePercent { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public decimal TotalFees { get; init; }
    public int MissedOrders { get; init; }
}
