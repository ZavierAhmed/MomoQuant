// RSI Primed [ChartPrime] — MOMO-native C# port
// Original Pine indicator: RSI Primed [ChartPrime]
// Copyright ChartPrime — Mozilla Public License 2.0
// This port approximates the published Pine behavior using Chebyshev Type I filtering,
// RSI OHLC, Heikin Ashi RSI candles, and adaptive MA length from dominant cycle estimation.

using MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.BbLiquiditySweep;

public interface IDominantCyclePeriodService
{
    int EstimatePeriod(IReadOnlyList<decimal> source, int minPeriod = 1, int maxPeriod = 2048);
    bool UsesFallback { get; }
}

public sealed class FixedFallbackDominantCyclePeriodService : IDominantCyclePeriodService
{
    private readonly int _fallbackPeriod;

    public FixedFallbackDominantCyclePeriodService(int fallbackPeriod = 24) => _fallbackPeriod = fallbackPeriod;

    public bool UsesFallback => true;

    public int EstimatePeriod(IReadOnlyList<decimal> source, int minPeriod = 1, int maxPeriod = 2048) =>
        Math.Clamp(_fallbackPeriod, minPeriod, maxPeriod);
}

public sealed class EhlersDominantCyclePeriodService : IDominantCyclePeriodService
{
    public bool UsesFallback => false;

    public int EstimatePeriod(IReadOnlyList<decimal> source, int minPeriod = 1, int maxPeriod = 2048)
    {
        if (source.Count < 20)
        {
            return Math.Clamp(24, minPeriod, maxPeriod);
        }

        var smooth = new List<decimal>(source.Count);
        decimal? prev = null;
        foreach (var value in source)
        {
            prev = prev is null ? value : prev * 0.33m + value * 0.67m;
            smooth.Add(prev.Value);
        }

        var detrender = new List<decimal>();
        for (var i = 6; i < smooth.Count; i++)
        {
            detrender.Add(smooth[i] - smooth[i - 6]);
        }

        if (detrender.Count < 10)
        {
            return Math.Clamp(24, minPeriod, maxPeriod);
        }

        var amplitudes = new List<decimal>();
        for (var lag = minPeriod; lag <= Math.Min(maxPeriod, detrender.Count / 2); lag++)
        {
            decimal sum = 0m;
            for (var i = lag; i < detrender.Count; i++)
            {
                sum += detrender[i] * detrender[i - lag];
            }

            amplitudes.Add(Math.Abs(sum));
        }

        if (amplitudes.Count == 0)
        {
            return Math.Clamp(24, minPeriod, maxPeriod);
        }

        var bestIndex = 0;
        for (var i = 1; i < amplitudes.Count; i++)
        {
            if (amplitudes[i] > amplitudes[bestIndex])
            {
                bestIndex = i;
            }
        }

        return Math.Clamp(minPeriod + bestIndex, minPeriod, maxPeriod);
    }
}

public sealed class RsiPrimedChartPrimeIndicator
{
    private readonly IDominantCyclePeriodService _dominantCycle;

    public RsiPrimedChartPrimeIndicator(IDominantCyclePeriodService? dominantCycle = null) =>
        _dominantCycle = dominantCycle ?? new FixedFallbackDominantCyclePeriodService();

    public IReadOnlyList<RsiPrimedResultDto> CalculateSeries(
        IReadOnlyList<Candle> candles,
        int length = 24,
        int smoothing = 3,
        bool useHeikinAshi = true,
        decimal overboughtLevel = 70m,
        decimal oversoldLevel = 30m,
        RsiPrimedSignalValueMode signalValueMode = RsiPrimedSignalValueMode.HaClose,
        bool enableAutoMa = true,
        int harmonic = 1)
    {
        if (candles.Count == 0)
        {
            return [];
        }

        var implementationMode = _dominantCycle.UsesFallback
            ? RsiPrimedImplementationMode.DominantCycleFallback
            : RsiPrimedImplementationMode.FullPort;

        var warnings = implementationMode == RsiPrimedImplementationMode.DominantCycleFallback
            ? new List<string> { "RSI Primed port with fixed dominant-cycle fallback." }
            : [];

        var opens = CalculateSmoothedRsiSeries(candles.Select(c => c.Open).ToList(), length, smoothing);
        var highs = CalculateSmoothedRsiSeries(candles.Select(c => c.High).ToList(), length, smoothing);
        var lows = CalculateSmoothedRsiSeries(candles.Select(c => c.Low).ToList(), length, smoothing);
        var closes = CalculateSmoothedRsiSeries(candles.Select(c => c.Close).ToList(), length, smoothing);

        var results = new List<RsiPrimedResultDto>(candles.Count);
        decimal? prevHaOpen = null;
        decimal? prevHaClose = null;

        for (var index = 0; index < candles.Count; index++)
        {
            var rsiOpen = opens[index];
            var rsiHigh = highs[index];
            var rsiLow = lows[index];
            var rsiClose = closes[index];

            decimal? haOpen = null;
            decimal? haHigh = null;
            decimal? haLow = null;
            decimal? haClose = null;
            decimal? ohlc4 = null;

            if (rsiOpen is not null && rsiHigh is not null && rsiLow is not null && rsiClose is not null)
            {
                if (useHeikinAshi)
                {
                    haClose = (rsiOpen.Value + rsiHigh.Value + rsiLow.Value + rsiClose.Value) / 4m;
                    haOpen = prevHaOpen is null && prevHaClose is null
                        ? (rsiOpen.Value + rsiClose.Value) / 2m
                        : (prevHaOpen!.Value + prevHaClose!.Value) / 2m;
                    haHigh = Math.Max(rsiHigh.Value, Math.Max(haOpen.Value, haClose.Value));
                    haLow = Math.Min(rsiLow.Value, Math.Min(haOpen.Value, haClose.Value));
                    prevHaOpen = haOpen;
                    prevHaClose = haClose;
                }
                else
                {
                    haOpen = rsiOpen;
                    haHigh = rsiHigh;
                    haLow = rsiLow;
                    haClose = rsiClose;
                }

                ohlc4 = (haClose!.Value + haOpen!.Value + haHigh!.Value + haLow!.Value) / 4m;
            }

            results.Add(new RsiPrimedResultDto
            {
                TimeUtc = candles[index].CloseTimeUtc,
                RsiOpen = rsiOpen,
                RsiHigh = rsiHigh,
                RsiLow = rsiLow,
                RsiClose = rsiClose,
                HaOpen = haOpen,
                HaHigh = haHigh,
                HaLow = haLow,
                HaClose = haClose,
                Ohlc4 = ohlc4,
                DominantCycleLength = 0,
                IsOversold = false,
                IsOverbought = false,
                ImplementationMode = implementationMode,
                Warnings = warnings
            });
        }

        var ohlc4Series = results.Select(item => item.Ohlc4 ?? 50m).ToList();
        var maLength = enableAutoMa
            ? _dominantCycle.EstimatePeriod(ohlc4Series, 1, 2048) / Math.Max(harmonic, 1)
            : length;
        maLength = Math.Max(3, maLength);

        var adaptiveSeries = ChebyshevSeries(ohlc4Series, maLength, 0.05m);
        for (var index = 0; index < results.Count; index++)
        {
            var item = results[index];
            var signalValue = ResolveSignalValue(item, signalValueMode);
            var adaptiveMa = index < adaptiveSeries.Count ? adaptiveSeries[index] : null;
            results[index] = new RsiPrimedResultDto
            {
                TimeUtc = item.TimeUtc,
                RsiOpen = item.RsiOpen,
                RsiHigh = item.RsiHigh,
                RsiLow = item.RsiLow,
                RsiClose = item.RsiClose,
                HaOpen = item.HaOpen,
                HaHigh = item.HaHigh,
                HaLow = item.HaLow,
                HaClose = item.HaClose,
                Ohlc4 = item.Ohlc4,
                AdaptiveMa = adaptiveMa,
                DominantCycleLength = maLength,
                SignalValue = signalValue,
                IsOversold = signalValue <= oversoldLevel,
                IsOverbought = signalValue >= overboughtLevel,
                BullishPattern = DetectBullishPattern(results, index),
                BearishPattern = DetectBearishPattern(results, index),
                ImplementationMode = item.ImplementationMode,
                Warnings = item.Warnings
            };
        }

        return results;
    }

    public static decimal? ChebyshevI(decimal source, decimal? previous, int length, decimal ripple)
    {
        if (length <= 0)
        {
            return source;
        }

        if (previous is null)
        {
            return source;
        }

        // Stable Chebyshev Type I approximation for the MOMO port (avoids decimal overflow from hyperbolic transforms).
        var alpha = 2m / (length + 1m);
        var rippleDamping = 1m - Math.Clamp(ripple, 0m, 0.5m);
        var adjustedAlpha = alpha * rippleDamping;
        return adjustedAlpha * source + (1m - adjustedAlpha) * previous.Value;
    }

    internal static List<decimal?> CalculateSmoothedRsiSeries(IReadOnlyList<decimal> source, int length, int smoothing)
    {
        var rawRsi = new List<decimal?>(source.Count);
        var state = new RsiState();
        foreach (var value in source)
        {
            rawRsi.Add(CalculateRsi(value, state, length));
        }

        var smoothed = new List<decimal?>(source.Count);
        decimal? chebyPrev = null;
        foreach (var value in rawRsi)
        {
            if (value is null)
            {
                smoothed.Add(null);
                chebyPrev = null;
                continue;
            }

            chebyPrev = ChebyshevI(value.Value, chebyPrev, Math.Max(smoothing, 1), 0.05m);
            smoothed.Add(chebyPrev);
        }

        return smoothed;
    }

    internal static List<decimal?> ChebyshevSeries(IReadOnlyList<decimal> source, int length, decimal ripple)
    {
        var output = new List<decimal?>(source.Count);
        decimal? previous = null;
        foreach (var value in source)
        {
            previous = ChebyshevI(value, previous, length, ripple);
            output.Add(previous);
        }

        return output;
    }

    private static decimal? ResolveSignalValue(RsiPrimedResultDto item, RsiPrimedSignalValueMode mode) =>
        mode switch
        {
            RsiPrimedSignalValueMode.HaLowHigh when item.HaLow.HasValue && item.HaHigh.HasValue =>
                (item.HaLow.Value + item.HaHigh.Value) / 2m,
            RsiPrimedSignalValueMode.Ohlc4 => item.Ohlc4,
            _ => item.HaClose ?? item.RsiClose
        };

    private static decimal? CalculateRsi(decimal close, RsiState state, int length)
    {
        if (state.Previous is null)
        {
            state.Previous = close;
            return null;
        }

        var change = close - state.Previous.Value;
        state.Previous = close;
        var gain = change > 0m ? change : 0m;
        var loss = change < 0m ? -change : 0m;
        state.Count++;

        if (state.Count < length)
        {
            state.AvgGain += gain;
            state.AvgLoss += loss;
            if (state.Count < length)
            {
                return null;
            }

            state.AvgGain /= length;
            state.AvgLoss /= length;
        }
        else
        {
            state.AvgGain = ((state.AvgGain * (length - 1)) + gain) / length;
            state.AvgLoss = ((state.AvgLoss * (length - 1)) + loss) / length;
        }

        if (state.AvgLoss == 0m)
        {
            return state.AvgGain > 0m ? 100m : 0m;
        }

        var rs = state.AvgGain / state.AvgLoss;
        return Math.Clamp(100m - (100m / (1m + rs)), 0m, 100m);
    }

    private static string? DetectBullishPattern(IReadOnlyList<RsiPrimedResultDto> series, int index)
    {
        if (index < 2)
        {
            return null;
        }

        var c0 = series[index - 2].HaClose;
        var c1 = series[index - 1].HaClose;
        var c2 = series[index].HaClose;
        if (c0 is null || c1 is null || c2 is null)
        {
            return null;
        }

        if (c1 < c0 && c2 > c1 && c2 > ((c0 + c1) / 2m))
        {
            return "MorningStarBullish";
        }

        if (c2 > c1 && c1 < c0)
        {
            return "EngulfingBullish";
        }

        return null;
    }

    private static string? DetectBearishPattern(IReadOnlyList<RsiPrimedResultDto> series, int index)
    {
        if (index < 2)
        {
            return null;
        }

        var c0 = series[index - 2].HaClose;
        var c1 = series[index - 1].HaClose;
        var c2 = series[index].HaClose;
        if (c0 is null || c1 is null || c2 is null)
        {
            return null;
        }

        if (c1 > c0 && c2 < c1 && c2 < ((c0 + c1) / 2m))
        {
            return "EveningStarBearish";
        }

        if (c2 < c1 && c1 > c0)
        {
            return "EngulfingBearish";
        }

        return null;
    }

    private sealed class RsiState
    {
        public decimal? Previous { get; set; }
        public decimal AvgGain { get; set; }
        public decimal AvgLoss { get; set; }
        public int Count { get; set; }
    }
}
