namespace MomoQuant.Domain.Strategies;

using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;

public sealed class StrategyContext
{
    public long? TradingSessionId { get; init; }
    public long? ExchangeId { get; init; }
    public required long SymbolId { get; init; }
    public string? Symbol { get; init; }
    public required Timeframe Timeframe { get; init; }
    public required Timeframe HigherTimeframe { get; init; }
    public required MarketRegime MarketRegime { get; init; }
    public required IReadOnlyList<Candle> Candles { get; init; }
    public required IndicatorSnapshot? IndicatorSnapshot { get; init; }
    public IReadOnlyList<IndicatorSnapshot> RecentIndicatorSnapshots { get; init; } = [];
    public IReadOnlyDictionary<string, string> StrategyParameters { get; init; }
        = new Dictionary<string, string>();
    public required DateTime EvaluatedAtUtc { get; init; }

    /// <summary>Absolute index of the evaluated candle in the full loaded dataset.</summary>
    public int? CurrentCandleIndex { get; init; }

    public Candle? CurrentCandle => Candles.Count > 0 ? Candles[^1] : null;
}
