using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.Implementations;

internal static class StrategyCandleHelper
{
    public static decimal WickPercent(Candle candle, bool lowerWick)
    {
        var range = candle.High - candle.Low;
        if (range <= 0m)
        {
            return 0m;
        }

        var wick = lowerWick
            ? Math.Min(candle.Open, candle.Close) - candle.Low
            : candle.High - Math.Max(candle.Open, candle.Close);
        return wick / range * 100m;
    }

    public static bool IsBullish(Candle candle) => candle.Close > candle.Open;

    public static bool IsBearish(Candle candle) => candle.Close < candle.Open;

    public static decimal? RangeHigh(IReadOnlyList<Candle> candles, int lookback)
    {
        if (candles.Count == 0)
        {
            return null;
        }

        var start = Math.Max(0, candles.Count - lookback - 1);
        decimal? high = null;
        for (var i = start; i < candles.Count - 1; i++)
        {
            high = high.HasValue ? Math.Max(high.Value, candles[i].High) : candles[i].High;
        }

        return high;
    }

    public static decimal? RangeLow(IReadOnlyList<Candle> candles, int lookback)
    {
        if (candles.Count == 0)
        {
            return null;
        }

        var start = Math.Max(0, candles.Count - lookback - 1);
        decimal? low = null;
        for (var i = start; i < candles.Count - 1; i++)
        {
            low = low.HasValue ? Math.Min(low.Value, candles[i].Low) : candles[i].Low;
        }

        return low;
    }
}
