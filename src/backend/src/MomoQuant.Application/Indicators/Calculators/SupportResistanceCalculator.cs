using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Indicators.Calculators;

public static class SupportResistanceCalculator
{
    public static (decimal? Support, decimal? Resistance) Calculate(IReadOnlyList<Candle> candles, int index, int lookback = 50)
    {
        if (index < 2)
        {
            return (null, null);
        }

        var start = Math.Max(0, index - lookback);
        decimal? support = null;
        decimal? resistance = null;

        for (var i = start; i < index; i++)
        {
            support = support.HasValue ? Math.Min(support.Value, candles[i].Low) : candles[i].Low;
            resistance = resistance.HasValue ? Math.Max(resistance.Value, candles[i].High) : candles[i].High;
        }

        return (support, resistance);
    }
}
