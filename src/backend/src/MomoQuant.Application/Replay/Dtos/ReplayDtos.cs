using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Replay.Dtos;

public sealed class CreateReplaySessionRequest
{
    public required string Name { get; init; }
    public long ExchangeId { get; init; }
    public long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
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
    public string Speed { get; init; } = "ManualStep";
}

public sealed class UpdateReplaySpeedRequest
{
    public required string Speed { get; init; }
}

public sealed class ReplaySessionDto
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public required string Status { get; init; }
    public long ExchangeId { get; init; }
    public long SymbolId { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public decimal InitialBalance { get; init; }
    public decimal CurrentBalance { get; init; }
    public decimal CurrentEquity { get; init; }
    public long RiskProfileId { get; init; }
    public required string ExecutionMode { get; init; }
    public bool UseAiScoring { get; init; }
    public required string Speed { get; init; }
    public int CurrentFrameIndex { get; init; }
    public long? CurrentCandleId { get; init; }
    public int TotalFrames { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? PausedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }
}

public sealed class ReplayControlResponse
{
    public long ReplaySessionId { get; init; }
    public required string Status { get; init; }
    public int CurrentFrameIndex { get; init; }
    public ReplayFrameDto? CurrentFrame { get; init; }
}

public sealed class ReplayFrameDto
{
    public long ReplaySessionId { get; init; }
    public int FrameIndex { get; init; }
    public DateTime TimestampUtc { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public required ReplayCandleDto Candle { get; init; }
    public ReplayIndicatorSnapshotDto? IndicatorSnapshot { get; init; }
    public required string MarketRegime { get; init; }
    public required IReadOnlyList<ReplayStrategyResultDto> StrategyResults { get; init; }
    public ReplayAiDecisionDto? AiDecision { get; init; }
    public ReplayRiskDecisionDto? RiskDecision { get; init; }
    public ReplayOrderDto? SimulatedOrder { get; init; }
    public ReplayFillDto? SimulatedFill { get; init; }
    public ReplayPositionDto? OpenPosition { get; init; }
    public ReplayTradeDto? ClosedTrade { get; init; }
    public ReplayMissedOrderDto? MissedOrder { get; init; }
    public decimal Balance { get; init; }
    public decimal Equity { get; init; }
    public decimal Drawdown { get; init; }
    public decimal DrawdownPercent { get; init; }
    public required string HumanReadableExplanation { get; init; }
}

public sealed class ReplayCandleDto
{
    public long Id { get; init; }
    public DateTime OpenTimeUtc { get; init; }
    public DateTime CloseTimeUtc { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
}

public sealed class ReplayIndicatorSnapshotDto
{
    public long Id { get; init; }
    public long CandleId { get; init; }
    public decimal? Ema20 { get; init; }
    public decimal? Ema50 { get; init; }
    public decimal? Ema200 { get; init; }
    public decimal? Vwap { get; init; }
    public decimal? Rsi14 { get; init; }
    public decimal? Atr14 { get; init; }
    public decimal? VolumeSma20 { get; init; }
    public decimal? SwingHigh { get; init; }
    public decimal? SwingLow { get; init; }
    public required string MarketStructure { get; init; }
}

public sealed class ReplayStrategyResultDto
{
    public required string StrategyCode { get; init; }
    public required string StrategyName { get; init; }
    public bool Evaluated { get; init; } = true;
    public bool Skipped { get; init; }
    public string? SkipReason { get; init; }
    public required string SignalType { get; init; }
    public required string Direction { get; init; }
    public decimal Strength { get; init; }
    public decimal ConfidenceContribution { get; init; }
    public decimal? EntryPrice { get; init; }
    public decimal? SuggestedStopLoss { get; init; }
    public decimal? SuggestedTakeProfit { get; init; }
    public decimal? StopLoss { get; init; }
    public decimal? TakeProfit { get; init; }
    public required string Reason { get; init; }
    public string? Regime { get; init; }
    public string? Timeframe { get; init; }
    public bool IsValid { get; init; }
}

public sealed class ReplayAiDecisionDto
{
    public long Id { get; init; }
    public required string MarketRegime { get; init; }
    public decimal ConfidenceScore { get; init; }
    public required string Classification { get; init; }
    public bool TradeAllowed { get; init; }
    public required string Summary { get; init; }
    public required string Explanation { get; init; }
}

public sealed class ReplayRiskDecisionDto
{
    public long Id { get; init; }
    public required string Decision { get; init; }
    public required string Reason { get; init; }
    public string? RejectedRuleKey { get; init; }
    public decimal? PositionSize { get; init; }
    public decimal? StopLoss { get; init; }
    public decimal? TakeProfit { get; init; }
}

public sealed class ReplayOrderDto
{
    public long Id { get; init; }
    public required string Mode { get; init; }
    public required string Side { get; init; }
    public required string OrderType { get; init; }
    public required string Status { get; init; }
    public decimal Price { get; init; }
    public decimal Quantity { get; init; }
    public bool IsPostOnly { get; init; }
}

public sealed class ReplayFillDto
{
    public long Id { get; init; }
    public decimal FillPrice { get; init; }
    public decimal FillQuantity { get; init; }
    public decimal Fee { get; init; }
    public required string LiquidityType { get; init; }
    public DateTime FilledAtUtc { get; init; }
}

public sealed class ReplayPositionDto
{
    public required string Direction { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal Quantity { get; init; }
    public decimal StopLoss { get; init; }
    public decimal TakeProfit { get; init; }
    public decimal UnrealizedPnl { get; init; }
    public required string StrategyCode { get; init; }
}

public sealed class ReplayTradeDto
{
    public long Id { get; init; }
    public required string Direction { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal? ExitPrice { get; init; }
    public decimal Quantity { get; init; }
    public required string Status { get; init; }
    public string? CloseReason { get; init; }
    public decimal NetPnl { get; init; }
    public decimal Fees { get; init; }
}

public sealed class ReplayMissedOrderDto
{
    public long Id { get; init; }
    public decimal RequestedPrice { get; init; }
    public required string Reason { get; init; }
    public DateTime ExpiredAtUtc { get; init; }
}

public sealed class ReplaySignalDto
{
    public long Id { get; init; }
    public required string StrategyCode { get; init; }
    public required string SignalType { get; init; }
    public required string Direction { get; init; }
    public decimal Strength { get; init; }
    public required string Reason { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}
