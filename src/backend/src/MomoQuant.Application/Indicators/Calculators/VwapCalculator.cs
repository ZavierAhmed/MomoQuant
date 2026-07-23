using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Indicators.Calculators;

public static class VwapCalculator
{
    public static decimal? CalculateCumulative(IReadOnlyList<Candle> candles, int index)
    {
        if (index < 0 || index >= candles.Count)
        {
            return null;
        }

        decimal cumulativeTypicalVolume = 0m;
        decimal cumulativeVolume = 0m;

        for (var i = 0; i <= index; i++)
        {
            var candle = candles[i];
            if (candle.Volume <= 0m)
            {
                continue;
            }

            var typicalPrice = (candle.High + candle.Low + candle.Close) / 3m;
            cumulativeTypicalVolume += typicalPrice * candle.Volume;
            cumulativeVolume += candle.Volume;
        }

        if (cumulativeVolume <= 0m)
        {
            return null;
        }

        return cumulativeTypicalVolume / cumulativeVolume;
    }
}
