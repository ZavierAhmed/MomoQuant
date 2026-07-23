using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.PaperTrading.Dtos;

public sealed class CreatePaperAccountRequest
{
    public required string Name { get; init; }
    public decimal InitialBalance { get; init; }
    public required string Currency { get; init; }
}

public sealed class UpdatePaperAccountRequest
{
    public required string Name { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class PaperAccountDto
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public decimal InitialBalance { get; init; }
    public decimal CurrentBalance { get; init; }
    public decimal CurrentEquity { get; init; }
    public required string Currency { get; init; }
    public decimal TotalRealizedPnl { get; init; }
    public decimal TotalUnrealizedPnl { get; init; }
    public decimal TotalFees { get; init; }
    public decimal MaxDrawdown { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }
}

public sealed class PaperAccountSnapshotDto
{
    public long Id { get; init; }
    public long PaperAccountId { get; init; }
    public long? PaperSessionId { get; init; }
    public DateTime TimestampUtc { get; init; }
    public decimal Balance { get; init; }
    public decimal Equity { get; init; }
    public decimal RealizedPnl { get; init; }
    public decimal UnrealizedPnl { get; init; }
    public decimal TotalFees { get; init; }
    public decimal Drawdown { get; init; }
    public decimal DrawdownPercent { get; init; }
    public int OpenPositionCount { get; init; }
}

public sealed class CreatePaperSessionRequest
{
    public required string Name { get; init; }
    public long PaperAccountId { get; init; }
    public long ExchangeId { get; init; }
    public required IReadOnlyList<long> SymbolIds { get; init; }
    public required IReadOnlyList<string> Timeframes { get; init; }
    public string Mode { get; init; } = "HistoricalPaper";
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
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
    public bool AllowAbnormalMarketPaperTrading { get; init; }
    public long? ParameterSetId { get; init; }
}

public sealed class PaperSessionDto
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public long PaperAccountId { get; init; }
    public required string Status { get; init; }
    public required string Mode { get; init; }
    public long ExchangeId { get; init; }
    public long RiskProfileId { get; init; }
    public required string ExecutionMode { get; init; }
    public bool UseAiScoring { get; init; }
    public decimal MinConfidenceScore { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public DateTime? CurrentCandleTimeUtc { get; init; }
    public int CurrentCandleIndex { get; init; }
    public int TotalCandles { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? PausedAtUtc { get; init; }
    public DateTime? StoppedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }
}

public sealed class PaperSessionStatusDto
{
    public long SessionId { get; init; }
    public long PaperSessionId { get; init; }
    public required string Status { get; init; }
    public required string Mode { get; init; }
    public int CurrentCandleIndex { get; init; }
    public int ProcessedCandles { get; init; }
    public int? TotalCandles { get; init; }
    public decimal? ProgressPercent { get; init; }
    public string? ProgressLabel { get; init; }
    public DateTime? CurrentCandleTimeUtc { get; init; }
    public decimal CurrentBalance { get; init; }
    public decimal CurrentEquity { get; init; }
    public int OpenPositionCount { get; init; }
    public int OrdersCount { get; init; }
    public int TradesCount { get; init; }
    public int MissedOrdersCount { get; init; }
    public DateTime? LastUpdatedAtUtc { get; init; }
    public bool? Connected { get; init; }
    public DateTime? LastLiveUpdateUtc { get; init; }
    public DateTime? LastClosedCandleUtc { get; init; }
    public DateTime? LastProcessedCandleUtc { get; init; }
    public decimal? LatestPrice { get; init; }
    public IReadOnlyList<string>? SubscribedSymbols { get; init; }
    public IReadOnlyList<string>? SubscribedTimeframes { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed class PaperSessionControlResponse
{
    public long PaperSessionId { get; init; }
    public required string Status { get; init; }
    public int CurrentCandleIndex { get; init; }
    public int TotalCandles { get; init; }
    public DateTime? CurrentCandleTimeUtc { get; init; }
}

public sealed class PaperOrderDto
{
    public long Id { get; init; }
    public long SymbolId { get; init; }
    public required string Mode { get; init; }
    public required string Side { get; init; }
    public required string OrderType { get; init; }
    public decimal Price { get; init; }
    public decimal Quantity { get; init; }
    public required string Status { get; init; }
    public DateTime RequestedAtUtc { get; init; }
    public DateTime? FilledAtUtc { get; init; }
}

public sealed class PaperFillDto
{
    public long Id { get; init; }
    public long OrderId { get; init; }
    public decimal FillPrice { get; init; }
    public decimal FillQuantity { get; init; }
    public decimal Fee { get; init; }
    public required string LiquidityType { get; init; }
    public DateTime FilledAtUtc { get; init; }
}

public sealed class PaperPositionDto
{
    public long Id { get; init; }
    public long SymbolId { get; init; }
    public required string Direction { get; init; }
    public decimal Quantity { get; init; }
    public decimal AverageEntryPrice { get; init; }
    public decimal MarkPrice { get; init; }
    public decimal UnrealizedPnl { get; init; }
    public required string Status { get; init; }
    public DateTime OpenedAtUtc { get; init; }
}

public sealed class PaperTradeDto
{
    public long Id { get; init; }
    public long SymbolId { get; init; }
    public required string Direction { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal? ExitPrice { get; init; }
    public decimal Quantity { get; init; }
    public required string Status { get; init; }
    public decimal NetPnl { get; init; }
    public decimal Fees { get; init; }
    public DateTime OpenedAtUtc { get; init; }
    public DateTime? ClosedAtUtc { get; init; }
}

public sealed class PaperMissedOrderDto
{
    public long Id { get; init; }
    public long SymbolId { get; init; }
    public decimal RequestedPrice { get; init; }
    public required string Reason { get; init; }
    public DateTime ExpiredAtUtc { get; init; }
}

public sealed class PaperEquityPointDto
{
    public DateTime TimestampUtc { get; init; }
    public decimal Balance { get; init; }
    public decimal Equity { get; init; }
    public decimal Drawdown { get; init; }
    public decimal DrawdownPercent { get; init; }
    public int OpenPositionCount { get; init; }
}

public sealed class PaperSignalDto
{
    public long Id { get; init; }
    public long StrategyId { get; init; }
    public long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public required string SignalType { get; init; }
    public required string Direction { get; init; }
    public decimal Strength { get; init; }
    public string? Reason { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class PaperRiskDecisionDto
{
    public long Id { get; init; }
    public long SymbolId { get; init; }
    public required string Decision { get; init; }
    public string? Reason { get; init; }
    public string? RejectedRuleKey { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class PaperAiDecisionDto
{
    public long Id { get; init; }
    public long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public required string MarketRegime { get; init; }
    public decimal ConfidenceScore { get; init; }
    public bool TradeAllowed { get; init; }
    public string? Summary { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class PaperSessionLiveStatusDto
{
    public long SessionId { get; init; }
    public required string Status { get; init; }
    public required string Mode { get; init; }
    public bool Connected { get; init; }
    public string? ProgressLabel { get; init; }
    public int ProcessedCandles { get; init; }
    public int? TotalCandles { get; init; }
    public decimal? ProgressPercent { get; init; }
    public DateTime? CurrentCandleTimeUtc { get; init; }
    public DateTime? LastLiveUpdateUtc { get; init; }
    public DateTime? LastClosedCandleUtc { get; init; }
    public DateTime? LastProcessedCandleUtc { get; init; }
    public decimal? LatestPrice { get; init; }
    public required IReadOnlyList<PaperSymbolLiveStatusDto> SymbolStatuses { get; init; }
    public decimal CurrentBalance { get; init; }
    public decimal CurrentEquity { get; init; }
    public int OpenPositionCount { get; init; }
    public int OrdersCount { get; init; }
    public int TradesCount { get; init; }
    public int MissedOrdersCount { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed class PaperSymbolLiveStatusDto
{
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public DateTime? LastLiveUpdateUtc { get; init; }
    public DateTime? LastClosedCandleUtc { get; init; }
    public DateTime? LastProcessedCandleUtc { get; init; }
    public decimal? LatestPrice { get; init; }
    public DateTime? CurrentCandleOpenTimeUtc { get; init; }
    public DateTime? CurrentCandleCloseTimeUtc { get; init; }
    public bool IsSubscribed { get; init; }
    public string? StreamName { get; init; }
    public string? StreamWarning { get; init; }
}
