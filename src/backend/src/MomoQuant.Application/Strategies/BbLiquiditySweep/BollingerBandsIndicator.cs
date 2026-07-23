using MomoQuant.Application.Indicators.Calculators;
using MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.BbLiquiditySweep;

public static class BollingerBandsIndicator
{
    public static BollingerBandsValueDto? CalculateAt(
        IReadOnlyList<Candle> candles,
        int index,
        int period = 20,
        decimal stdDev = 2.0m)
    {
        if (index < 0 || index >= candles.Count)
        {
            return null;
        }

        var (middle, upper, lower, bandwidth) = BollingerCalculator.Calculate(candles, index, period, stdDev);
        if (middle is null || upper is null || lower is null)
        {
            return null;
        }

        var close = candles[index].Close;
        var bandRange = upper.Value - lower.Value;
        var percentB = bandRange == 0m ? 0.5m : (close - lower.Value) / bandRange;

        return new BollingerBandsValueDto
        {
            TimeUtc = candles[index].CloseTimeUtc,
            Middle = middle.Value,
            Upper = upper.Value,
            Lower = lower.Value,
            Bandwidth = bandwidth ?? 0m,
            PercentB = percentB
        };
    }

    public static IReadOnlyList<BollingerBandsValueDto> CalculateSeries(
        IReadOnlyList<Candle> candles,
        int period = 20,
        decimal stdDev = 2.0m)
    {
        var results = new List<BollingerBandsValueDto>(candles.Count);
        for (var index = 0; index < candles.Count; index++)
        {
            var value = CalculateAt(candles, index, period, stdDev);
            if (value is not null)
            {
                results.Add(value);
            }
        }

        return results;
    }
}
