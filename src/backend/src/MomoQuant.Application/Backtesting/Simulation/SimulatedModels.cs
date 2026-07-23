using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.Signals;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Application.Backtesting.Simulation;

public sealed class SimulatedPosition
{
    public required long SymbolId { get; init; }
    public required long StrategyId { get; init; }
    public required StrategyCode StrategyCode { get; init; }
    public required Timeframe Timeframe { get; init; }
    public required TradeDirection Direction { get; init; }
    public required decimal EntryPrice { get; init; }
    public required decimal Quantity { get; init; }
    public decimal StopLoss { get; set; }
    public required decimal TakeProfit { get; init; }
    public required decimal EntryFees { get; init; }
    public required DateTime OpenedAtUtc { get; init; }
    public required StrategySignal Signal { get; init; }
    public long? AiDecisionId { get; init; }
    public long? RiskDecisionId { get; init; }
    public required Order EntryOrder { get; init; }
    public required OrderFill EntryFill { get; init; }
    public required Trade Trade { get; init; }
    public decimal UnrealizedPnl { get; set; }
    public decimal? BreakevenTriggerPrice { get; set; }
    public bool BreakevenActivated { get; set; }
}

public sealed class PendingMarketFill
{
    public required long SymbolId { get; init; }
    public required long StrategyId { get; init; }
    public required StrategyCode StrategyCode { get; init; }
    public required Timeframe Timeframe { get; init; }
    public required TradeDirection Direction { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal StopLoss { get; init; }
    public required decimal TakeProfit { get; init; }
    public required decimal TakerFeeRate { get; init; }
    public required decimal SlippagePercent { get; init; }
    public required StrategySignal Signal { get; init; }
    public long? AiDecisionId { get; init; }
    public long? RiskDecisionId { get; init; }
    public required int FillAtCandleIndex { get; init; }
    public required DateTime RequestedAtUtc { get; init; }
    public decimal? BreakevenTriggerPrice { get; init; }
}

public sealed class PendingMakerOrder
{
    public required long SymbolId { get; init; }
    public required long StrategyId { get; init; }
    public required StrategyCode StrategyCode { get; init; }
    public required Timeframe Timeframe { get; init; }
    public required TradeDirection Direction { get; init; }
    public required decimal LimitPrice { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal StopLoss { get; init; }
    public required decimal TakeProfit { get; init; }
    public required decimal MakerFeeRate { get; init; }
    public required StrategySignal Signal { get; init; }
    public long? AiDecisionId { get; init; }
    public long? RiskDecisionId { get; init; }
    public required int PlacedAtCandleIndex { get; init; }
    public required int ExpiryCandleIndex { get; init; }
    public required DateTime RequestedAtUtc { get; init; }
    public required Order Order { get; init; }
    public required ExecutionMode ExecutionMode { get; init; }
    public decimal? BreakevenTriggerPrice { get; init; }
}

public sealed class SimulatedExecutionResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsMissedOrder { get; init; }
    public bool IsCancelled { get; init; }

    public static SimulatedExecutionResult Ok() => new() { Succeeded = true };

    public static SimulatedExecutionResult Missed(string message) =>
        new() { Succeeded = false, IsMissedOrder = true, ErrorMessage = message };

    public static SimulatedExecutionResult Cancelled(string message) =>
        new() { Succeeded = false, IsCancelled = true, ErrorMessage = message };
}

public sealed class MissedOrderResult
{
    public required MissedOrderReason Reason { get; init; }
    public required string Message { get; init; }
}
