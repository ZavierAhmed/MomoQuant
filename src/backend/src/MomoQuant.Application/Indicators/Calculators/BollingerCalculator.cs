using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Indicators.Calculators;

public static class BollingerCalculator
{
    public static (decimal? Middle, decimal? Upper, decimal? Lower, decimal? Bandwidth) Calculate(
        IReadOnlyList<Candle> candles,
        int index,
        int period = 20,
        decimal stdDevMultiplier = 2m)
    {
        if (index < period - 1)
        {
            return (null, null, null, null);
        }

        decimal sum = 0m;
        for (var i = index - period + 1; i <= index; i++)
        {
            sum += candles[i].Close;
        }

        var middle = sum / period;
        decimal varianceSum = 0m;
        for (var i = index - period + 1; i <= index; i++)
        {
            var diff = candles[i].Close - middle;
            varianceSum += diff * diff;
        }

        var stdDev = (decimal)Math.Sqrt((double)(varianceSum / period));
        var upper = middle + (stdDevMultiplier * stdDev);
        var lower = middle - (stdDevMultiplier * stdDev);
        var bandwidth = middle == 0m ? 0m : (upper - lower) / middle * 100m;

        return (middle, upper, lower, bandwidth);
    }
}
