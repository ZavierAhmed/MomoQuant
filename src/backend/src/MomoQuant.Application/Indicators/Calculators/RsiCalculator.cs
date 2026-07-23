namespace MomoQuant.Application.Indicators.Calculators;

public static class RsiCalculator
{
    public const int DefaultPeriod = 14;

    public sealed class State
    {
        public decimal? AverageGain { get; set; }
        public decimal? AverageLoss { get; set; }
        public decimal? PreviousClose { get; set; }
        public int ProcessedChanges { get; set; }
    }

    public static decimal? CalculateNext(decimal close, State state, int period = DefaultPeriod)
    {
        if (state.PreviousClose is null)
        {
            state.PreviousClose = close;
            return null;
        }

        var change = close - state.PreviousClose.Value;
        state.PreviousClose = close;

        var gain = change > 0m ? change : 0m;
        var loss = change < 0m ? -change : 0m;
        state.ProcessedChanges++;

        if (state.ProcessedChanges < period)
        {
            state.AverageGain = (state.AverageGain ?? 0m) + gain;
            state.AverageLoss = (state.AverageLoss ?? 0m) + loss;

            if (state.ProcessedChanges < period)
            {
                return null;
            }

            state.AverageGain /= period;
            state.AverageLoss /= period;
        }
        else
        {
            state.AverageGain = ((state.AverageGain ?? 0m) * (period - 1) + gain) / period;
            state.AverageLoss = ((state.AverageLoss ?? 0m) * (period - 1) + loss) / period;
        }

        if (state.AverageLoss == 0m)
        {
            return state.AverageGain > 0m ? 100m : 0m;
        }

        var relativeStrength = state.AverageGain!.Value / state.AverageLoss!.Value;
        var rsi = 100m - (100m / (1m + relativeStrength));
        return Math.Clamp(rsi, 0m, 100m);
    }
}
