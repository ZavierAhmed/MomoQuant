namespace MomoQuant.Domain.Risk;

using MomoQuant.Domain.Enums;

public sealed class RiskContext
{
    public long? TradingSessionId { get; init; }
    public required long SymbolId { get; init; }
    public string? Symbol { get; init; }
    public required TradeDirection Direction { get; init; }
    public required decimal EntryPrice { get; init; }
    public decimal? SuggestedStopLoss { get; init; }
    public decimal? SuggestedTakeProfit { get; init; }
    public required decimal ConfidenceScore { get; init; }
    public decimal RawConfidenceScore { get; init; }
    public decimal EffectiveMinConfidenceScore { get; init; }
    public string? ConfidenceSource { get; init; }
    public string? StrategyCode { get; init; }
    public long? SignalId { get; init; }
    public long? AiDecisionId { get; init; }
    public required decimal AccountBalance { get; init; }
    public required decimal DailyPnl { get; init; }
    public required decimal WeeklyPnl { get; init; }
    public required int OpenPositionCount { get; init; }
    public required decimal OpenSymbolExposure { get; init; }
    public required decimal TotalExposure { get; init; }
    public required int ConsecutiveLosses { get; init; }
    public decimal? SpreadPercent { get; init; }
    public decimal? AtrPercent { get; init; }
    public MarketRegime? MarketRegime { get; init; }
    public required bool EmergencyStopEnabled { get; init; }
    public required IReadOnlyList<RiskRule> Rules { get; init; }
    public required DateTime EvaluationTimeUtc { get; init; }
}
