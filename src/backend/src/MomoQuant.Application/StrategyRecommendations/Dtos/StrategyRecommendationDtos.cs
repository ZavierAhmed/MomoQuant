using MomoQuant.Application.MarketSituation.Dtos;

namespace MomoQuant.Application.StrategyRecommendations.Dtos;

public sealed class StrategyRecommendationResponseDto
{
    public long ExchangeId { get; init; }
    public required string ExchangeName { get; init; }
    public long SymbolId { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public required string Mode { get; init; }
    public required MarketSituationSummaryDto MarketSituation { get; init; }
    public required IReadOnlyList<StrategyRecommendationItemDto> RecommendedStrategies { get; init; }
    public required IReadOnlyList<long> SelectedByDefaultStrategyIds { get; init; }
    public DateTime GeneratedAtUtc { get; init; }
    public string? Warning { get; init; }
}

public sealed class MarketSituationSummaryDto
{
    public required string MarketRegime { get; init; }
    public required string TrendDirection { get; init; }
    public required string VolatilityState { get; init; }
    public required string MomentumState { get; init; }
}

public sealed class StrategyRecommendationItemDto
{
    public long StrategyId { get; init; }
    public required string StrategyCode { get; init; }
    public required string StrategyName { get; init; }
    public int SuitabilityScore { get; init; }
    public bool Recommended { get; init; }
    public bool IsEnabled { get; init; } = true;
    public required string Reason { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}
