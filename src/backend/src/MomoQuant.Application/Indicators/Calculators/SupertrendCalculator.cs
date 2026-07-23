using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Indicators.Calculators;

public static class SupertrendCalculator
{
    public sealed class State
    {
        public decimal? Supertrend { get; set; }
        public int Direction { get; set; }
        public decimal? FinalUpperBand { get; set; }
        public decimal? FinalLowerBand { get; set; }
    }

    public static (decimal? Value, int Direction) CalculateNext(Candle candle, decimal? atr, State state, decimal multiplier = 3m)
    {
        if (!atr.HasValue || atr.Value <= 0m)
        {
            return (null, 0);
        }

        var hl2 = (candle.High + candle.Low) / 2m;
        var basicUpper = hl2 + (multiplier * atr.Value);
        var basicLower = hl2 - (multiplier * atr.Value);

        if (!state.FinalUpperBand.HasValue || !state.FinalLowerBand.HasValue)
        {
            state.FinalUpperBand = basicUpper;
            state.FinalLowerBand = basicLower;
            state.Supertrend = basicLower;
            state.Direction = 1;
            return (state.Supertrend, state.Direction);
        }

        var finalUpper = basicUpper < state.FinalUpperBand.Value || candle.Close > state.FinalUpperBand.Value
            ? basicUpper
            : state.FinalUpperBand.Value;
        var finalLower = basicLower > state.FinalLowerBand.Value || candle.Close < state.FinalLowerBand.Value
            ? basicLower
            : state.FinalLowerBand.Value;

        state.FinalUpperBand = finalUpper;
        state.FinalLowerBand = finalLower;

        if (state.Supertrend == state.FinalUpperBand)
        {
            state.Supertrend = candle.Close <= finalUpper ? finalUpper : finalLower;
            state.Direction = candle.Close <= finalUpper ? -1 : 1;
        }
        else
        {
            state.Supertrend = candle.Close >= finalLower ? finalLower : finalUpper;
            state.Direction = candle.Close >= finalLower ? 1 : -1;
        }

        return (state.Supertrend, state.Direction);
    }
}
