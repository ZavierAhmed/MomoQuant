namespace MomoQuant.Application.Indicators.Calculators;

public static class EmaCalculator
{
    public static decimal CalculateMultiplier(int period) =>
        2m / (period + 1);

    public static decimal? CalculateInitial(IReadOnlyList<decimal> closes, int period)
    {
        if (closes.Count < period)
        {
            return null;
        }

        var sum = closes.Take(period).Sum();
        return sum / period;
    }

    public static decimal CalculateNext(decimal previousEma, decimal close, int period) =>
        ((close - previousEma) * CalculateMultiplier(period)) + previousEma;
}
