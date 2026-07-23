using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Indicators.Calculators;

public static class VolumeSmaCalculator
{
    public const int DefaultPeriod = 20;

    public static decimal? Calculate(IReadOnlyList<Candle> candles, int index, int period = DefaultPeriod)
    {
        if (index + 1 < period)
        {
            return null;
        }

        decimal sum = 0m;
        for (var i = index - period + 1; i <= index; i++)
        {
            sum += candles[i].Volume;
        }

        return sum / period;
    }
}
