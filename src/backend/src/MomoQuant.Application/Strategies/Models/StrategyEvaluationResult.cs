using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Strategies.Models;

public sealed class StrategyEvaluationResult
{
    public required string StrategyCode { get; init; }
    public required string StrategyName { get; init; }
    public required bool Evaluated { get; init; }
    public required bool Skipped { get; init; }
    public string? SkipReason { get; init; }
    public required SignalType SignalType { get; init; }
    public required TradeDirection Direction { get; init; }
    public required decimal Strength { get; init; }
    public required decimal ConfidenceContribution { get; init; }
    public decimal? EntryPrice { get; init; }
    public decimal? SuggestedStopLoss { get; init; }
    public decimal? SuggestedTakeProfit { get; init; }
    public decimal? StopLoss => SuggestedStopLoss;
    public decimal? TakeProfit => SuggestedTakeProfit;
    public required string Reason { get; init; }
    public string? Regime { get; init; }
    public string? Timeframe { get; init; }
    public string? RawDataJson { get; init; }
    public required bool IsValid { get; init; }
}
