using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.MarketSituation.Dtos;

public sealed class MarketSituationDto
{
    public long ExchangeId { get; init; }
    public required string ExchangeName { get; init; }
    public long SymbolId { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public DateTime DetectedAtUtc { get; init; }
    public decimal? LatestPrice { get; init; }
    public required string MarketRegime { get; init; }
    public required string TrendDirection { get; init; }
    public required string VolatilityState { get; init; }
    public required string MomentumState { get; init; }
    public required string VolumeState { get; init; }
    public required string RiskState { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> Signals { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required string DataSource { get; init; }
    public DateTime? LatestCandleTimeUtc { get; init; }
    public int CandleCountUsed { get; init; }
    public bool IndicatorsAvailable { get; init; }
}

public enum TrendDirection
{
    Bullish,
    Bearish,
    Neutral,
    Unknown
}

public enum VolatilityState
{
    Low,
    Normal,
    High,
    Extreme
}

public enum MomentumState
{
    Bullish,
    Bearish,
    Neutral,
    Overbought,
    Oversold
}

public enum VolumeState
{
    Low,
    Normal,
    High,
    Spike
}
