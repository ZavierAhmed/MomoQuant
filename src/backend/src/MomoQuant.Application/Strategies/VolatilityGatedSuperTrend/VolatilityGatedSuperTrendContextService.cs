using MomoQuant.Application.Indicators.Calculators;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.VolatilityGatedSuperTrend;

public sealed class VolatilityGatedSuperTrendCandleData
{
    public decimal? SuperTrendLine { get; init; }
    public int TrendDirection { get; init; }
    public bool TrendFlip { get; init; }
    public decimal? FastAtr { get; init; }
    public decimal? SlowAtr { get; init; }
    public decimal? VolatilityRatio { get; init; }
    public decimal? MacdLine { get; init; }
    public decimal? MacdSignal { get; init; }
    public decimal? MacdHistogram { get; init; }
    public decimal? AtrForStops { get; init; }
    public decimal? RecentSwingLow { get; init; }
    public decimal? RecentSwingHigh { get; init; }
}

public interface IVolatilityGatedSuperTrendContextService
{
    void Precompute(long symbolId, IReadOnlyList<Candle> candles, VolatilityGatedSuperTrendParameters parameters);
    VolatilityGatedSuperTrendCandleData? GetCandleData(long symbolId, int index);
    int GetWarmupCandles(VolatilityGatedSuperTrendParameters parameters);
}

public sealed class VolatilityGatedSuperTrendContextService : IVolatilityGatedSuperTrendContextService
{
    private readonly Dictionary<long, List<VolatilityGatedSuperTrendCandleData>> _cache = new();

    public int GetWarmupCandles(VolatilityGatedSuperTrendParameters parameters) =>
        Math.Max(300, Math.Max(parameters.SlowAtrPeriod, parameters.MacdSlow + parameters.MacdSignal) + 50);

    public void Precompute(long symbolId, IReadOnlyList<Candle> candles, VolatilityGatedSuperTrendParameters parameters)
    {
        var data = new List<VolatilityGatedSuperTrendCandleData>(candles.Count);
        var stAtrState = new AtrCalculator.State();
        var fastAtrState = new AtrCalculator.State();
        var slowAtrState = new AtrCalculator.State();
        var stState = new SupertrendCalculator.State();
        var macdState = ConfigurableMacdCalculator.CreateState(parameters.MacdFast, parameters.MacdSlow, parameters.MacdSignal);

        var recentSwingLows = new List<(int Index, decimal Price)>();
        var recentSwingHighs = new List<(int Index, decimal Price)>();
        var previousDirection = 0;

        for (var i = 0; i < candles.Count; i++)
        {
            var candle = candles[i];
            var stAtr = AtrCalculator.CalculateNext(candle, stAtrState, parameters.AtrPeriod);
            var fastAtr = AtrCalculator.CalculateNext(candle, fastAtrState, parameters.FastAtrPeriod);
            var slowAtr = AtrCalculator.CalculateNext(candle, slowAtrState, parameters.SlowAtrPeriod);
            var (stLine, direction) = SupertrendCalculator.CalculateNext(candle, stAtr, stState, parameters.SuperTrendMultiplier);
            var (macdLine, macdSignal, macdHistogram) = ConfigurableMacdCalculator.CalculateNext(candle.Close, macdState);

            var swingLow = SwingPointCalculator.DetectSwingLow(candles, i);
            var swingHigh = SwingPointCalculator.DetectSwingHigh(candles, i);
            if (swingLow.HasValue) recentSwingLows.Add((i, swingLow.Value));
            if (swingHigh.HasValue) recentSwingHighs.Add((i, swingHigh.Value));
            recentSwingLows.RemoveAll(x => i - x.Index > 50);
            recentSwingHighs.RemoveAll(x => i - x.Index > 50);

            var trendFlip = previousDirection != 0 && direction != 0 && direction != previousDirection;
            previousDirection = direction;

            decimal? volRatio = null;
            if (fastAtr.HasValue && slowAtr.HasValue && slowAtr.Value > 0m)
            {
                volRatio = fastAtr.Value / slowAtr.Value;
            }

            data.Add(new VolatilityGatedSuperTrendCandleData
            {
                SuperTrendLine = stLine,
                TrendDirection = direction,
                TrendFlip = trendFlip,
                FastAtr = fastAtr,
                SlowAtr = slowAtr,
                VolatilityRatio = volRatio,
                MacdLine = macdLine,
                MacdSignal = macdSignal,
                MacdHistogram = macdHistogram,
                AtrForStops = stAtr ?? fastAtr,
                RecentSwingLow = recentSwingLows.Count > 0 ? recentSwingLows[^1].Price : null,
                RecentSwingHigh = recentSwingHighs.Count > 0 ? recentSwingHighs[^1].Price : null
            });
        }

        _cache[symbolId] = data;
    }

    public VolatilityGatedSuperTrendCandleData? GetCandleData(long symbolId, int index)
    {
        if (!_cache.TryGetValue(symbolId, out var data) || index < 0 || index >= data.Count)
        {
            return null;
        }

        return data[index];
    }
}

internal static class ConfigurableMacdCalculator
{
    public sealed class State
    {
        public required MacdCalculator.EmaState Fast { get; init; }
        public required MacdCalculator.EmaState Slow { get; init; }
        public required MacdCalculator.EmaState Signal { get; init; }
        public decimal? MacdLine { get; set; }
    }

    public static State CreateState(int fastPeriod, int slowPeriod, int signalPeriod) => new()
    {
        Fast = new MacdCalculator.EmaState(fastPeriod),
        Slow = new MacdCalculator.EmaState(slowPeriod),
        Signal = new MacdCalculator.EmaState(signalPeriod)
    };

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
}
