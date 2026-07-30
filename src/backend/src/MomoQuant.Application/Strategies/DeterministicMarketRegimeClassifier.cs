using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies;

/// <summary>
/// Shared non-AI market-regime classification used by Backtest and Strategy Laboratory.
/// Same candle + same indicator snapshot must yield the same regime in both paths.
/// </summary>
public static class DeterministicMarketRegimeClassifier
{
    public const string MappingContractVersion = "DeterministicRegime/v1";

    public static MarketRegime Classify(IndicatorSnapshot? snapshot, Candle candle)
    {
        if (snapshot is null
            || snapshot.Ema20 is null
            || snapshot.Ema50 is null
            || snapshot.Ema200 is null)
        {
            return MarketRegime.Unknown;
        }

        if (snapshot.Ema20 > snapshot.Ema50 && snapshot.Ema50 > snapshot.Ema200)
        {
            return MarketRegime.Trending;
        }

        if (snapshot.Ema20 < snapshot.Ema50 && snapshot.Ema50 < snapshot.Ema200)
        {
            return MarketRegime.Trending;
        }

        if (snapshot.Atr14 is not null && candle.Close > 0 && snapshot.Atr14.Value / candle.Close * 100m > 2m)
        {
            return MarketRegime.HighVolatility;
        }

        return MarketRegime.Ranging;
    }
}
