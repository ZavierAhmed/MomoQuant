using MomoQuant.Application.Indicators.Calculators;
using MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.BbLiquiditySweep;

public sealed class MomoLiquidityLineEngine : IExternalLiquidityLineEngine
{
    public IReadOnlyList<LiquidityLevelDto> CalculateLiquidityLevels(
        IReadOnlyList<Candle> candles,
        string timeframe,
        BbLiquiditySweepParameters? parameters = null,
        int smoothingA = 7,
        int smoothingB = 3) =>
        DetectLevels(candles, timeframe, parameters, smoothingA, smoothingB);

    public IReadOnlyList<LiquidityLevelDto> CalculateLtfLiquidityLevels(
        IReadOnlyList<Candle> candles,
        string timeframe,
        BbLiquiditySweepParameters? parameters = null,
        int smoothingA = 5,
        int smoothingB = 1) =>
        DetectLevels(candles, timeframe, parameters, smoothingA, smoothingB);

    public ExternalLiquidityEngineInfoDto GetImplementationInfo() => new()
    {
        ExternalIndicatorName = "#itsimpossible",
        SourceCodeAvailable = false,
        ImplementationMode = "MOMO_APPROXIMATION",
        ApproximationReason = "Exact #itsimpossible Pine source was not provided.",
        CompatibleSettings =
        [
            "v1.2 5m reference mode",
            "LTF 1m smoothing 5-1 mode",
            "BalancedResearch tunable swing/tolerance defaults"
        ]
    };

    private static List<LiquidityLevelDto> DetectLevels(
        IReadOnlyList<Candle> candles,
        string timeframe,
        BbLiquiditySweepParameters? parameters,
        int smoothingA,
        int smoothingB)
    {
        if (candles.Count == 0)
        {
            return [];
        }

        var settings = parameters ?? BbLiquiditySweepParameters.ApplyStrictnessProfile(BbStrategyStrictnessProfile.BalancedResearch);
        var swingLeft = settings.SwingLeft;
        var swingRight = settings.SwingRight;
        var equalToleranceAtrMultiplier = settings.EqualHighLowToleranceAtrMultiplier;
        var minTouches = settings.IncludeSingleSwingLevels ? Math.Max(1, settings.MinTouches) : Math.Max(2, settings.MinTouches);

        var atrValues = BuildAtrSeries(candles, 14);
        var levelSeriesHigh = settings.UseBodiesForLevels && !settings.UseWicksForLevels
            ? candles.Select(c => Math.Max(c.Open, c.Close)).ToList()
            : candles.Select(c => c.High).ToList();
        var levelSeriesLow = settings.UseBodiesForLevels && !settings.UseWicksForLevels
            ? candles.Select(c => Math.Min(c.Open, c.Close)).ToList()
            : candles.Select(c => c.Low).ToList();

        var smoothedHighs = SmoothSeries(levelSeriesHigh, smoothingA, smoothingB);
        var smoothedLows = SmoothSeries(levelSeriesLow, smoothingA, smoothingB);
        var levels = new List<LiquidityLevelDto>();

        for (var index = swingLeft; index < candles.Count - swingRight; index++)
        {
            var swingHigh = SwingPointCalculator.DetectSwingHigh(candles, index, swingLeft);
            if (swingHigh.HasValue)
            {
                var atr = atrValues[index] ?? 0m;
                var percentTolerance = swingHigh.Value * (settings.EqualHighLowTolerancePercent / 100m);
                var tolerance = Math.Max(atr * equalToleranceAtrMultiplier, percentTolerance);
                tolerance = Math.Max(tolerance, swingHigh.Value * 0.0001m);
                var touchCount = settings.RequireEqualHighLow
                    ? CountEqualTouches(smoothedHighs, index, swingHigh.Value, tolerance)
                    : 1;
                if (touchCount >= minTouches)
                {
                    levels.Add(CreateLevel(
                        candles[index],
                        timeframe,
                        LiquidityDirection.BuySideLiquidity,
                        swingHigh.Value,
                        touchCount,
                        smoothedHighs[index]));
                }
            }

            var swingLow = SwingPointCalculator.DetectSwingLow(candles, index, swingLeft);
            if (swingLow.HasValue)
            {
                var atr = atrValues[index] ?? 0m;
                var percentTolerance = swingLow.Value * (settings.EqualHighLowTolerancePercent / 100m);
                var tolerance = Math.Max(atr * equalToleranceAtrMultiplier, percentTolerance);
                tolerance = Math.Max(tolerance, swingLow.Value * 0.0001m);
                var touchCount = settings.RequireEqualHighLow
                    ? CountEqualTouches(smoothedLows, index, swingLow.Value, tolerance)
                    : 1;
                if (touchCount >= minTouches)
                {
                    levels.Add(CreateLevel(
                        candles[index],
                        timeframe,
                        LiquidityDirection.SellSideLiquidity,
                        swingLow.Value,
                        touchCount,
                        smoothedLows[index]));
                }
            }
        }

        return MergeLevels(levels, candles, settings);
    }

    private static List<LiquidityLevelDto> MergeLevels(
        IReadOnlyList<LiquidityLevelDto> levels,
        IReadOnlyList<Candle> candles,
        BbLiquiditySweepParameters settings)
    {
        if (levels.Count == 0)
        {
            return [];
        }

        var atrValues = BuildAtrSeries(candles, 14);
        var lastAtr = atrValues.LastOrDefault(value => value.HasValue) ?? 0m;
        var mergeTolerance = Math.Max(lastAtr * settings.LevelMergeToleranceAtrMultiplier, 0.0001m);
        var maxAge = settings.MaxLevelAgeCandles <= 0
            ? int.MaxValue
            : settings.MaxLevelAgeCandles;
        var lastCloseTime = candles[^1].CloseTimeUtc;

        return levels
            .Where(level => (lastCloseTime - level.CreatedAtUtc).TotalMinutes <= maxAge * 3)
            .GroupBy(level => level.Direction)
            .SelectMany(group => group
                .OrderBy(level => level.CreatedAtUtc)
                .Aggregate(new List<LiquidityLevelDto>(), (merged, level) =>
                {
                    var existing = merged.FirstOrDefault(item => Math.Abs(item.Price - level.Price) <= mergeTolerance && item.Direction == level.Direction);
                    if (existing is null)
                    {
                        merged.Add(level);
                    }
                    else if (level.StrengthScore > existing.StrengthScore)
                    {
                        merged.Remove(existing);
                        merged.Add(level);
                    }

                    return merged;
                }))
            .OrderBy(level => level.CreatedAtUtc)
            .ToList();
    }

    private static LiquidityLevelDto CreateLevel(
        Candle sourceCandle,
        string timeframe,
        LiquidityDirection direction,
        decimal price,
        int touchCount,
        decimal smoothedValue)
    {
        var strength = Math.Min(100m, 40m + (touchCount * 10m) + Math.Abs(price - smoothedValue) / Math.Max(price, 1m) * 1000m);
        return new LiquidityLevelDto
        {
            Id = $"{timeframe}:{direction}:{sourceCandle.CloseTimeUtc:O}:{price}",
            Timeframe = timeframe,
            Direction = direction,
            Price = price,
            CreatedAtUtc = sourceCandle.CloseTimeUtc,
            LastTouchedAtUtc = sourceCandle.CloseTimeUtc,
            SourceSwingCandleTimeUtc = sourceCandle.CloseTimeUtc,
            StrengthScore = strength,
            TouchCount = touchCount,
            ImplementationMode = "MOMO_APPROXIMATION",
            SourceIndicatorName = "#itsimpossible"
        };
    }

    private static int CountEqualTouches(IReadOnlyList<decimal> series, int centerIndex, decimal price, decimal tolerance)
    {
        var touches = 0;
        var start = Math.Max(0, centerIndex - 20);
        var end = Math.Min(series.Count - 1, centerIndex + 5);
        for (var index = start; index <= end; index++)
        {
            if (Math.Abs(series[index] - price) <= tolerance)
            {
                touches++;
            }
        }

        return Math.Max(1, touches);
    }

    private static List<decimal> SmoothSeries(IReadOnlyList<decimal> values, int smoothingA, int smoothingB)
    {
        if (values.Count == 0)
        {
            return [];
        }

        var alphaA = 2m / (smoothingA + 1m);
        var alphaB = 2m / (Math.Max(smoothingB, 1) + 1m);
        var firstPass = new List<decimal>(values.Count);
        decimal? emaA = null;
        foreach (var value in values)
        {
            emaA = emaA is null ? value : (alphaA * value) + ((1m - alphaA) * emaA.Value);
            firstPass.Add(emaA.Value);
        }

        var secondPass = new List<decimal>(values.Count);
        decimal? emaB = null;
        foreach (var value in firstPass)
        {
            emaB = emaB is null ? value : (alphaB * value) + ((1m - alphaB) * emaB.Value);
            secondPass.Add(emaB.Value);
        }

        return secondPass;
    }

    private static List<decimal?> BuildAtrSeries(IReadOnlyList<Candle> candles, int period)
    {
        var atrSeries = new List<decimal?>(candles.Count);
        decimal? atr = null;
        for (var index = 0; index < candles.Count; index++)
        {
            if (index == 0)
            {
                atrSeries.Add(null);
                continue;
            }

            var current = candles[index];
            var previous = candles[index - 1];
            var trueRange = Math.Max(current.High - current.Low,
                Math.Max(Math.Abs(current.High - previous.Close), Math.Abs(current.Low - previous.Close)));

            if (atr is null || index < period)
            {
                atr = trueRange;
            }
            else
            {
                atr = ((atr.Value * (period - 1)) + trueRange) / period;
            }

            atrSeries.Add(index >= period ? atr : null);
        }

        return atrSeries;
    }
}
