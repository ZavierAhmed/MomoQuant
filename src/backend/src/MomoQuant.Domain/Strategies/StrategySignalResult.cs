namespace MomoQuant.Domain.Strategies;

using MomoQuant.Domain.Enums;

public sealed class StrategySignalResult
{
    public required SignalType SignalType { get; init; }
    public required TradeDirection Direction { get; init; }
    public required decimal Strength { get; init; }
    public required decimal ConfidenceContribution { get; init; }
    public decimal? EntryPrice { get; init; }
    public decimal? SuggestedStopLoss { get; init; }
    public decimal? SuggestedTakeProfit { get; init; }
    public required string Reason { get; init; }
    public string? RawDataJson { get; init; }
}
