using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Indicators.Calculators;

public static class AtrCalculator
{
    public const int DefaultPeriod = 14;

    public sealed class State
    {
        public decimal? PreviousClose { get; set; }
        public decimal? AverageTrueRange { get; set; }
        public int ProcessedTrueRanges { get; set; }
        public readonly List<decimal> InitialTrueRanges = [];
    }

    public static decimal CalculateTrueRange(Candle candle, decimal? previousClose)
    {
        if (previousClose is null)
        {
            return candle.High - candle.Low;
        }

        var highLow = candle.High - candle.Low;
        var highClose = Math.Abs(candle.High - previousClose.Value);
        var lowClose = Math.Abs(candle.Low - previousClose.Value);
        return Math.Max(highLow, Math.Max(highClose, lowClose));
    }

    public static decimal? CalculateNext(Candle candle, State state, int period = DefaultPeriod)
    {
        var trueRange = CalculateTrueRange(candle, state.PreviousClose);
        state.PreviousClose = candle.Close;
        state.ProcessedTrueRanges++;

        if (state.ProcessedTrueRanges < period)
        {
            state.InitialTrueRanges.Add(trueRange);
            if (state.ProcessedTrueRanges < period)
            {
                return null;
            }

            state.AverageTrueRange = state.InitialTrueRanges.Average();
            return state.AverageTrueRange;
        }

        state.AverageTrueRange = ((state.AverageTrueRange ?? 0m) * (period - 1) + trueRange) / period;
        return state.AverageTrueRange;
    }
}
