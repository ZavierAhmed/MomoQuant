namespace MomoQuant.Application.Indicators.Calculators;

public static class MacdCalculator
{
    public sealed class State
    {
        public EmaState Fast { get; } = new(12);
        public EmaState Slow { get; } = new(26);
        public EmaState Signal { get; } = new(9);
        public decimal? MacdLine { get; set; }
    }

    public static (decimal? Line, decimal? Signal, decimal? Histogram) CalculateNext(decimal close, State state)
    {
        var fast = state.Fast.Update(close);
        var slow = state.Slow.Update(close);
        if (!fast.HasValue || !slow.HasValue)
        {
            return (null, null, null);
        }

        state.MacdLine = fast.Value - slow.Value;
        var signal = state.Signal.Update(state.MacdLine.Value);
        if (!signal.HasValue)
        {
            return (state.MacdLine, null, null);
        }

        var histogram = state.MacdLine.Value - signal.Value;
        return (state.MacdLine, signal, histogram);
    }

    public sealed class EmaState(int period)
    {
        private readonly int _period = period;
        private decimal _sum;
        private int _count;
        public decimal? Value { get; private set; }

        public decimal? Update(decimal input)
        {
            if (Value is null)
            {
                _sum += input;
                _count++;
                if (_count < _period)
                {
                    return null;
                }

                Value = _sum / _period;
                return Value;
            }

            var multiplier = 2m / (_period + 1);
            Value = (input - Value.Value) * multiplier + Value.Value;
            return Value;
        }
    }
}
