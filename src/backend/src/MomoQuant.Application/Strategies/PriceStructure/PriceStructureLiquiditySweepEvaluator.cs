using MomoQuant.Application.Strategies.PriceStructure.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.PriceStructure;

public static class PriceStructureLiquiditySweepEvaluator
{
    public const string StrategyVersion = "1.0.0";

    public static LiquiditySweepParameters ReadParameters(IReadOnlyDictionary<string, string> parameters) => new()
    {
        SwingLeftBars = StrategyParameterReader.GetInt(parameters, "swingLeftBars", 2),
        SwingRightBars = StrategyParameterReader.GetInt(parameters, "swingRightBars", 2),
        MaxLiquidityLevelAgeBars = StrategyParameterReader.GetInt(parameters, "maxLiquidityLevelAgeBars", 200),
        IncludeSingleSwingLevels = StrategyParameterReader.GetBool(parameters, "includeSingleSwingLevels", true),
        IncludeEqualHighLowLevels = StrategyParameterReader.GetBool(parameters, "includeEqualHighLowLevels", true),
        EqualLevelTolerancePercent = StrategyParameterReader.GetDecimal(parameters, "equalLevelTolerancePercent", 0.10m),
        MaxReclaimBars = StrategyParameterReader.GetInt(parameters, "maxReclaimBars", 1),
        RequireSameCandleReclaim = StrategyParameterReader.GetBool(parameters, "requireSameCandleReclaim", true),
        MinimumSweepDistancePercent = StrategyParameterReader.GetDecimal(parameters, "minimumSweepDistancePercent", 0m),
        MaximumSweepDistancePercent = StrategyParameterReader.GetDecimal(parameters, "maximumSweepDistancePercent", 0m) == 0m
            ? null
            : StrategyParameterReader.GetDecimal(parameters, "maximumSweepDistancePercent", 0m),
        ConfirmationMode = StrategyParameterReader.GetString(parameters, "confirmationMode", "ReclaimCloseOnly"),
        FixedRewardRisk = StrategyParameterReader.GetDecimal(parameters, "fixedRewardRisk", 2.0m),
        StopBufferPercent = StrategyParameterReader.GetDecimal(parameters, "stopBufferPercent", 0.05m)
    };

    public static (PriceStructureCandidateDto? Candidate, string Reason) EvaluateAtCurrentCandle(
        IReadOnlyList<Candle> candles,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlySet<string> seenFingerprints,
        string strategyCode,
        long symbolId,
        string timeframe,
        Action<PriceStructureLiquidityFunnelEvent>? funnelSink = null)
    {
        if (candles.Count < 10)
        {
            return (null, PriceStructureRejectionCodes.InsufficientData);
        }

        var settings = ReadParameters(parameters);
        var currentIndex = candles.Count - 1;
        var maxConfirmed = currentIndex - settings.SwingRightBars;
        if (maxConfirmed < settings.SwingLeftBars)
        {
            return (null, PriceStructureRejectionCodes.InsufficientData);
        }

        var swings = PriceStructureSwingDetector.DetectConfirmedSwings(
            candles,
            settings.SwingLeftBars,
            settings.SwingRightBars,
            true,
            maxConfirmed);

        if (swings.Count == 0)
        {
            return (null, PriceStructureRejectionCodes.NoLiquidityLevel);
        }

        var current = candles[currentIndex];
        var bestReason = PriceStructureRejectionCodes.NoSweep;

        foreach (var swing in swings.Where(s => currentIndex - s.Index <= settings.MaxLiquidityLevelAgeBars).OrderByDescending(s => s.Index))
        {
            if (!swing.IsHigh)
            {
                var (candidate, reason) = TryBuildBullishSweep(
                    candles, swing, currentIndex, current, settings, seenFingerprints, strategyCode, symbolId, timeframe, funnelSink);
                if (candidate is not null)
                {
                    return (candidate, candidate.Reason);
                }

                bestReason = reason;
            }
            else
            {
                var (candidate, reason) = TryBuildBearishSweep(
                    candles, swing, currentIndex, current, settings, seenFingerprints, strategyCode, symbolId, timeframe, funnelSink);
                if (candidate is not null)
                {
                    return (candidate, candidate.Reason);
                }

                bestReason = reason;
            }
        }

        return (null, bestReason);
    }

    private static (PriceStructureCandidateDto? Candidate, string Reason) TryBuildBullishSweep(
        IReadOnlyList<Candle> candles,
        ConfirmedSwing swing,
        int currentIndex,
        Candle current,
        LiquiditySweepParameters settings,
        IReadOnlySet<string> seenFingerprints,
        string strategyCode,
        long symbolId,
        string timeframe,
        Action<PriceStructureLiquidityFunnelEvent>? funnelSink)
    {
        Emit(funnelSink, PriceStructureLiquidityFunnelEventKind.SweepCheck, $"SC:{swing.Index}:{currentIndex}:L", TradeDirection.Long, swing, current, current.Low);

        if (current.Low >= swing.Price)
        {
            return (null, PriceStructureRejectionCodes.NoSweep);
        }

        if (!PassesSweepDistance(current.Low, swing.Price, settings))
        {
            return (null, PriceStructureRejectionCodes.NoSweep);
        }

        Emit(funnelSink, PriceStructureLiquidityFunnelEventKind.SweepDetected, $"SD:{swing.Index}:{currentIndex}:L", TradeDirection.Long, swing, current, current.Low);

        if (current.Close <= swing.Price)
        {
            Emit(funnelSink, PriceStructureLiquidityFunnelEventKind.SweepWithoutReclaim, $"SWR:{swing.Index}:{currentIndex}:L", TradeDirection.Long, swing, current, current.Close);
            return (null, PriceStructureRejectionCodes.SweepDidNotReclaim);
        }

        if (settings.RequireSameCandleReclaim && !IsSameCandleReclaimLong(current, swing.Price))
        {
            Emit(funnelSink, PriceStructureLiquidityFunnelEventKind.SweepWithoutReclaim, $"SWR2:{swing.Index}:{currentIndex}:L", TradeDirection.Long, swing, current, current.Close);
            return (null, PriceStructureRejectionCodes.SweepDidNotReclaim);
        }

        Emit(funnelSink, PriceStructureLiquidityFunnelEventKind.SameCandleReclaim, $"SCR:{swing.Index}:{currentIndex}:L", TradeDirection.Long, swing, current, current.Close);

        var entry = current.Close;
        var stop = current.Low * (1m - settings.StopBufferPercent / 100m);
        if (stop >= entry || stop <= 0)
        {
            return (null, PriceStructureRejectionCodes.InvalidStop);
        }

        var risk = entry - stop;
        var target = entry + risk * settings.FixedRewardRisk;
        var fingerprint = BuildFingerprint(strategyCode, symbolId, timeframe, TradeDirection.Long, swing, currentIndex, candles);
        if (seenFingerprints.Contains(fingerprint))
        {
            return (null, PriceStructureRejectionCodes.DuplicateSetup);
        }

        return (new PriceStructureCandidateDto
        {
            Direction = TradeDirection.Long,
            EntryPrice = entry,
            StopLoss = stop,
            Target1 = target,
            RewardRisk = settings.FixedRewardRisk,
            Reason = "Bullish sell-side liquidity sweep and reclaim.",
            SetupFingerprint = fingerprint,
            Structure = new PriceStructureSetupDto
            {
                SetupType = "LiquiditySweepReclaim",
                Direction = "Long",
                BrokenOrSweptLevel = swing.Price,
                SwingTimeUtc = swing.OpenTimeUtc,
                BreakoutOrSweepTimeUtc = current.OpenTimeUtc,
                RetestOrReclaimTimeUtc = current.OpenTimeUtc,
                ConfirmationTimeUtc = current.OpenTimeUtc,
                SwingIndex = swing.Index,
                BreakoutIndex = currentIndex,
                RetestIndex = currentIndex,
                ConfirmationIndex = currentIndex
            }
        }, "Bullish sell-side liquidity sweep and reclaim.");
    }

    private static (PriceStructureCandidateDto? Candidate, string Reason) TryBuildBearishSweep(
        IReadOnlyList<Candle> candles,
        ConfirmedSwing swing,
        int currentIndex,
        Candle current,
        LiquiditySweepParameters settings,
        IReadOnlySet<string> seenFingerprints,
        string strategyCode,
        long symbolId,
        string timeframe,
        Action<PriceStructureLiquidityFunnelEvent>? funnelSink)
    {
        Emit(funnelSink, PriceStructureLiquidityFunnelEventKind.SweepCheck, $"SC:{swing.Index}:{currentIndex}:H", TradeDirection.Short, swing, current, current.High);

        if (current.High <= swing.Price)
        {
            return (null, PriceStructureRejectionCodes.NoSweep);
        }

        if (!PassesSweepDistance(current.High, swing.Price, settings))
        {
            return (null, PriceStructureRejectionCodes.NoSweep);
        }

        Emit(funnelSink, PriceStructureLiquidityFunnelEventKind.SweepDetected, $"SD:{swing.Index}:{currentIndex}:H", TradeDirection.Short, swing, current, current.High);

        if (current.Close >= swing.Price)
        {
            Emit(funnelSink, PriceStructureLiquidityFunnelEventKind.SweepWithoutReclaim, $"SWR:{swing.Index}:{currentIndex}:H", TradeDirection.Short, swing, current, current.Close);
            return (null, PriceStructureRejectionCodes.SweepDidNotReclaim);
        }

        if (settings.RequireSameCandleReclaim && !IsSameCandleReclaimShort(current, swing.Price))
        {
            Emit(funnelSink, PriceStructureLiquidityFunnelEventKind.SweepWithoutReclaim, $"SWR2:{swing.Index}:{currentIndex}:H", TradeDirection.Short, swing, current, current.Close);
            return (null, PriceStructureRejectionCodes.SweepDidNotReclaim);
        }

        Emit(funnelSink, PriceStructureLiquidityFunnelEventKind.SameCandleReclaim, $"SCR:{swing.Index}:{currentIndex}:H", TradeDirection.Short, swing, current, current.Close);

        var entry = current.Close;
        var stop = current.High * (1m + settings.StopBufferPercent / 100m);
        if (stop <= entry || stop <= 0)
        {
            return (null, PriceStructureRejectionCodes.InvalidStop);
        }

        var risk = stop - entry;
        var target = entry - risk * settings.FixedRewardRisk;
        var fingerprint = BuildFingerprint(strategyCode, symbolId, timeframe, TradeDirection.Short, swing, currentIndex, candles);
        if (seenFingerprints.Contains(fingerprint))
        {
            return (null, PriceStructureRejectionCodes.DuplicateSetup);
        }

        return (new PriceStructureCandidateDto
        {
            Direction = TradeDirection.Short,
            EntryPrice = entry,
            StopLoss = stop,
            Target1 = target,
            RewardRisk = settings.FixedRewardRisk,
            Reason = "Bearish buy-side liquidity sweep and reclaim.",
            SetupFingerprint = fingerprint,
            Structure = new PriceStructureSetupDto
            {
                SetupType = "LiquiditySweepReclaim",
                Direction = "Short",
                BrokenOrSweptLevel = swing.Price,
                SwingTimeUtc = swing.OpenTimeUtc,
                BreakoutOrSweepTimeUtc = current.OpenTimeUtc,
                RetestOrReclaimTimeUtc = current.OpenTimeUtc,
                ConfirmationTimeUtc = current.OpenTimeUtc,
                SwingIndex = swing.Index,
                BreakoutIndex = currentIndex,
                RetestIndex = currentIndex,
                ConfirmationIndex = currentIndex
            }
        }, "Bearish buy-side liquidity sweep and reclaim.");
    }

    private static bool IsSameCandleReclaimLong(Candle candle, decimal level) =>
        candle.Low < level && candle.Close > level;

    private static bool IsSameCandleReclaimShort(Candle candle, decimal level) =>
        candle.High > level && candle.Close < level;

    private static bool PassesSweepDistance(decimal sweepPrice, decimal level, LiquiditySweepParameters settings)
    {
        if (level <= 0)
        {
            return false;
        }

        var distancePercent = Math.Abs(level - sweepPrice) / level * 100m;
        if (settings.MinimumSweepDistancePercent > 0 && distancePercent < settings.MinimumSweepDistancePercent)
        {
            return false;
        }

        if (settings.MaximumSweepDistancePercent.HasValue && distancePercent > settings.MaximumSweepDistancePercent.Value)
        {
            return false;
        }

        return true;
    }

    public static string BuildFingerprint(
        string strategyCode,
        long symbolId,
        string timeframe,
        TradeDirection direction,
        ConfirmedSwing swing,
        int sweepIndex,
        IReadOnlyList<Candle> candles)
    {
        var swingLabel = swing.IsHigh ? "SWINGHIGH" : "SWINGLOW";
        var level = Math.Round(swing.Price, 8);
        var sweepTs = candles[sweepIndex].OpenTimeUtc.ToString("yyyyMMdd'T'HHmm");
        var raw = $"{strategyCode}|v{StrategyVersion}|{symbolId}|{timeframe}|{direction}|{swingLabel}_{level}|SWEEP_{sweepTs}";
        return SetupFingerprintHasher.Hash(raw);
    }

    private static void Emit(
        Action<PriceStructureLiquidityFunnelEvent>? sink,
        PriceStructureLiquidityFunnelEventKind kind,
        string key,
        TradeDirection direction,
        ConfirmedSwing swing,
        Candle eventCandle,
        decimal? eventPrice)
    {
        sink?.Invoke(new PriceStructureLiquidityFunnelEvent
        {
            Kind = kind,
            Key = key,
            Direction = direction,
            Level = swing.Price,
            LevelTimeUtc = swing.OpenTimeUtc,
            EventTimeUtc = eventCandle.OpenTimeUtc,
            EventPrice = eventPrice
        });
    }
}
