using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Indicators.Calculators;

public static class DonchianCalculator
{
    public static (decimal? High, decimal? Low) Calculate(IReadOnlyList<Candle> candles, int index, int period = 20)
    {
        if (index < period)
        {
            return (null, null);
        }

        decimal? high = null;
        decimal? low = null;
        for (var i = index - period; i < index; i++)
        {
            high = high.HasValue ? Math.Max(high.Value, candles[i].High) : candles[i].High;
            low = low.HasValue ? Math.Min(low.Value, candles[i].Low) : candles[i].Low;
        }

        return (high, low);
    }
}
