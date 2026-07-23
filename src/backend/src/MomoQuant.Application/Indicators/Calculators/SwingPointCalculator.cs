using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Indicators.Calculators;

public static class SwingPointCalculator
{
    public const int DefaultLookback = 2;

    public static decimal? DetectSwingHigh(IReadOnlyList<Candle> candles, int index, int lookback = DefaultLookback)
    {
        if (!HasEnoughNeighbors(candles.Count, index, lookback))
        {
            return null;
        }

        var candidate = candles[index].High;
        for (var offset = 1; offset <= lookback; offset++)
        {
            if (candidate <= candles[index - offset].High || candidate <= candles[index + offset].High)
            {
                return null;
            }
        }

        return candidate;
    }

    public static decimal? DetectSwingLow(IReadOnlyList<Candle> candles, int index, int lookback = DefaultLookback)
    {
        if (!HasEnoughNeighbors(candles.Count, index, lookback))
        {
            return null;
        }

        var candidate = candles[index].Low;
        for (var offset = 1; offset <= lookback; offset++)
        {
            if (candidate >= candles[index - offset].Low || candidate >= candles[index + offset].Low)
            {
                return null;
            }
        }

        return candidate;
    }

    private static bool HasEnoughNeighbors(int count, int index, int lookback) =>
        index >= lookback && index + lookback < count;
}
