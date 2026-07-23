using MomoQuant.Application.Strategies.PriceStructure.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.PriceStructure;

public static class PriceStructureBreakoutRetestEvaluator
{
    public const string StrategyVersion = "1.0.0";

    public static BreakoutRetestParameters ReadParameters(IReadOnlyDictionary<string, string> parameters) => new()
    {
        SwingLeftBars = StrategyParameterReader.GetInt(parameters, "swingLeftBars", 2),
        SwingRightBars = StrategyParameterReader.GetInt(parameters, "swingRightBars", 2),
        MinSwingDistanceBars = StrategyParameterReader.GetInt(parameters, "minSwingDistanceBars", 3),
        UseWicksForSwing = StrategyParameterReader.GetBool(parameters, "useWicksForSwing", true),
        MinBreakoutClosePercent = StrategyParameterReader.GetDecimal(parameters, "minBreakoutClosePercent", 0m),
        BreakoutMustCloseBeyondLevel = StrategyParameterReader.GetBool(parameters, "breakoutMustCloseBeyondLevel", true),
        MaxRetestBars = StrategyParameterReader.GetInt(parameters, "maxRetestBars", 20),
        RetestTolerancePercent = StrategyParameterReader.GetDecimal(parameters, "retestTolerancePercent", 0.15m),
        RetestToleranceMode = StrategyParameterReader.GetString(parameters, "retestToleranceMode", "Percent"),
        AllowWickThroughLevel = StrategyParameterReader.GetBool(parameters, "allowWickThroughLevel", true),
        MaxRetestPenetrationPercent = StrategyParameterReader.GetDecimal(parameters, "maxRetestPenetrationPercent", 0.30m),
        ConfirmationMode = StrategyParameterReader.GetString(parameters, "confirmationMode", "BullishReactionClose"),
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
        Action<PriceStructureBreakoutFunnelEvent>? funnelSink = null)
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
            settings.UseWicksForSwing,
            maxConfirmed);

        PriceStructureCandidateDto? bestCandidate = null;
        var bestReason = PriceStructureRejectionCodes.NoConfirmedSwing;

        foreach (var swing in swings.OrderByDescending(s => s.Index))
        {
            if (swing.IsHigh)
            {
                var (candidate, reason) = TryBuildBullishCandidate(
                    candles, swing, currentIndex, settings, seenFingerprints, strategyCode, symbolId, timeframe, funnelSink);
                if (candidate is not null)
                {
                    return (candidate, candidate.Reason);
                }

                if (string.Equals(reason, PriceStructureRejectionCodes.DuplicateSetup, StringComparison.Ordinal))
                {
                    return (null, reason);
                }

                bestReason = PickCloserReason(bestReason, reason);
            }
            else
            {
                var (candidate, reason) = TryBuildBearishCandidate(
                    candles, swing, currentIndex, settings, seenFingerprints, strategyCode, symbolId, timeframe, funnelSink);
                if (candidate is not null)
                {
                    return (candidate, candidate.Reason);
                }

                if (string.Equals(reason, PriceStructureRejectionCodes.DuplicateSetup, StringComparison.Ordinal))
                {
                    return (null, reason);
                }

                bestReason = PickCloserReason(bestReason, reason);
            }
        }

        return (null, bestReason);
    }

    private static (PriceStructureCandidateDto? Candidate, string Reason) TryBuildBullishCandidate(
        IReadOnlyList<Candle> candles,
        ConfirmedSwing swing,
        int currentIndex,
        BreakoutRetestParameters settings,
        IReadOnlySet<string> seenFingerprints,
        string strategyCode,
        long symbolId,
        string timeframe,
        Action<PriceStructureBreakoutFunnelEvent>? funnelSink)
    {
        var confirmDelay = settings.SwingRightBars;
        var minBreakoutIndex = swing.Index + confirmDelay + settings.MinSwingDistanceBars;
        if (minBreakoutIndex >= currentIndex)
        {
            return (null, PriceStructureRejectionCodes.NoBreakout);
        }

        Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.BreakoutCheck, $"BCHK:{swing.Index}:L", TradeDirection.Long, swing, null, null);

        int? breakoutIndex = null;
        for (var i = minBreakoutIndex; i < currentIndex; i++)
        {
            var candle = candles[i];
            if (settings.BreakoutMustCloseBeyondLevel && candle.Close <= swing.Price && candle.High > swing.Price)
            {
                Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.BreakoutRejectedNoClose, $"BNC:{swing.Index}:{i}", TradeDirection.Long, swing, candle, candle.Close);
            }

            if (!IsBullishBreakout(candle, swing.Price, settings))
            {
                continue;
            }

            breakoutIndex = i;
            break;
        }

        if (breakoutIndex is null)
        {
            return (null, PriceStructureRejectionCodes.NoBreakout);
        }

        var breakoutCandle = candles[breakoutIndex.Value];
        Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.BreakoutDetected, $"BO:{swing.Index}:{breakoutIndex}", TradeDirection.Long, swing, breakoutCandle, breakoutCandle.Close);

        var retestSearchEnd = Math.Min(currentIndex, breakoutIndex.Value + settings.MaxRetestBars);
        int? retestIndex = null;
        decimal retestLow = decimal.MaxValue;

        for (var i = breakoutIndex.Value + 1; i <= retestSearchEnd; i++)
        {
            var candle = candles[i];
            Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.RetestCheck, $"RC:{swing.Index}:{breakoutIndex}:{i}", TradeDirection.Long, swing, candle, candle.Low);
            if (IsRetestInvalidatedLong(candle, swing.Price, settings))
            {
                Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.InvalidatedRetest, $"RI:{swing.Index}:{i}", TradeDirection.Long, swing, candle, candle.Close);
                return (null, PriceStructureRejectionCodes.RetestInvalidated);
            }

            if (IsBullishRetestTouch(candle, swing.Price, settings))
            {
                retestIndex = i;
                retestLow = candle.Low;
            }
        }

        if (retestIndex is null)
        {
            if (currentIndex > breakoutIndex.Value + settings.MaxRetestBars)
            {
                Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.ExpiredRetest, $"RE:{swing.Index}:{breakoutIndex}", TradeDirection.Long, swing, breakoutCandle, null);
                return (null, PriceStructureRejectionCodes.RetestExpired);
            }

            return (null, PriceStructureRejectionCodes.WaitingForRetest);
        }

        var retestCandle = candles[retestIndex.Value];
        Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.ValidRetest, $"RV:{swing.Index}:{breakoutIndex}:{retestIndex}", TradeDirection.Long, swing, retestCandle, retestCandle.Low);
        Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.ConfirmationCheck, $"CC:{swing.Index}:{retestIndex}:{currentIndex}", TradeDirection.Long, swing, candles[currentIndex], candles[currentIndex].Close);

        if (!IsBullishConfirmation(candles, retestIndex.Value, currentIndex, swing.Price, settings))
        {
            Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.ConfirmationFailed, $"CF:{swing.Index}:{retestIndex}:{currentIndex}", TradeDirection.Long, swing, candles[currentIndex], candles[currentIndex].Close);
            return (null, PriceStructureRejectionCodes.NoConfirmation);
        }

        Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.ConfirmationPassed, $"CP:{swing.Index}:{retestIndex}:{currentIndex}", TradeDirection.Long, swing, candles[currentIndex], candles[currentIndex].Close);

        var entryCandle = candles[currentIndex];
        var entry = entryCandle.Close;
        var stop = retestLow * (1m - settings.StopBufferPercent / 100m);
        if (stop >= entry || entry <= 0 || stop <= 0)
        {
            return (null, PriceStructureRejectionCodes.InvalidStop);
        }

        var risk = entry - stop;
        var target = entry + (risk * settings.FixedRewardRisk);
        var fingerprint = BuildFingerprint(
            strategyCode,
            symbolId,
            timeframe,
            TradeDirection.Long,
            swing,
            breakoutIndex.Value,
            retestIndex.Value,
            candles);

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
            Reason = "Bullish breakout retest confirmed.",
            SetupFingerprint = fingerprint,
            Structure = new PriceStructureSetupDto
            {
                SetupType = "BreakoutRetest",
                Direction = "Long",
                BrokenOrSweptLevel = swing.Price,
                SwingTimeUtc = swing.OpenTimeUtc,
                BreakoutOrSweepTimeUtc = candles[breakoutIndex.Value].OpenTimeUtc,
                RetestOrReclaimTimeUtc = candles[retestIndex.Value].OpenTimeUtc,
                ConfirmationTimeUtc = entryCandle.OpenTimeUtc,
                SwingIndex = swing.Index,
                BreakoutIndex = breakoutIndex,
                RetestIndex = retestIndex,
                ConfirmationIndex = currentIndex
            }
        }, "Bullish breakout retest confirmed.");
    }

    private static (PriceStructureCandidateDto? Candidate, string Reason) TryBuildBearishCandidate(
        IReadOnlyList<Candle> candles,
        ConfirmedSwing swing,
        int currentIndex,
        BreakoutRetestParameters settings,
        IReadOnlySet<string> seenFingerprints,
        string strategyCode,
        long symbolId,
        string timeframe,
        Action<PriceStructureBreakoutFunnelEvent>? funnelSink)
    {
        var confirmDelay = settings.SwingRightBars;
        var minBreakoutIndex = swing.Index + confirmDelay + settings.MinSwingDistanceBars;
        if (minBreakoutIndex >= currentIndex)
        {
            return (null, PriceStructureRejectionCodes.NoBreakout);
        }

        Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.BreakoutCheck, $"BCHK:{swing.Index}:H", TradeDirection.Short, swing, null, null);

        int? breakoutIndex = null;
        for (var i = minBreakoutIndex; i < currentIndex; i++)
        {
            var candle = candles[i];
            if (settings.BreakoutMustCloseBeyondLevel && candle.Close >= swing.Price && candle.Low < swing.Price)
            {
                Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.BreakoutRejectedNoClose, $"BNC:{swing.Index}:{i}", TradeDirection.Short, swing, candle, candle.Close);
            }

            if (!IsBearishBreakout(candle, swing.Price, settings))
            {
                continue;
            }

            breakoutIndex = i;
            break;
        }

        if (breakoutIndex is null)
        {
            return (null, PriceStructureRejectionCodes.NoBreakout);
        }

        var breakoutCandle = candles[breakoutIndex.Value];
        Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.BreakoutDetected, $"BO:{swing.Index}:{breakoutIndex}", TradeDirection.Short, swing, breakoutCandle, breakoutCandle.Close);

        var retestSearchEnd = Math.Min(currentIndex, breakoutIndex.Value + settings.MaxRetestBars);
        int? retestIndex = null;
        decimal retestHigh = decimal.MinValue;

        for (var i = breakoutIndex.Value + 1; i <= retestSearchEnd; i++)
        {
            var candle = candles[i];
            Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.RetestCheck, $"RC:{swing.Index}:{breakoutIndex}:{i}", TradeDirection.Short, swing, candle, candle.High);
            if (IsRetestInvalidatedShort(candle, swing.Price, settings))
            {
                Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.InvalidatedRetest, $"RI:{swing.Index}:{i}", TradeDirection.Short, swing, candle, candle.Close);
                return (null, PriceStructureRejectionCodes.RetestInvalidated);
            }

            if (IsBearishRetestTouch(candle, swing.Price, settings))
            {
                retestIndex = i;
                retestHigh = candle.High;
            }
        }

        if (retestIndex is null)
        {
            if (currentIndex > breakoutIndex.Value + settings.MaxRetestBars)
            {
                Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.ExpiredRetest, $"RE:{swing.Index}:{breakoutIndex}", TradeDirection.Short, swing, breakoutCandle, null);
                return (null, PriceStructureRejectionCodes.RetestExpired);
            }

            return (null, PriceStructureRejectionCodes.WaitingForRetest);
        }

        var retestCandle = candles[retestIndex.Value];
        Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.ValidRetest, $"RV:{swing.Index}:{breakoutIndex}:{retestIndex}", TradeDirection.Short, swing, retestCandle, retestCandle.High);
        Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.ConfirmationCheck, $"CC:{swing.Index}:{retestIndex}:{currentIndex}", TradeDirection.Short, swing, candles[currentIndex], candles[currentIndex].Close);

        if (!IsBearishConfirmation(candles, retestIndex.Value, currentIndex, swing.Price, settings))
        {
            Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.ConfirmationFailed, $"CF:{swing.Index}:{retestIndex}:{currentIndex}", TradeDirection.Short, swing, candles[currentIndex], candles[currentIndex].Close);
            return (null, PriceStructureRejectionCodes.NoConfirmation);
        }

        Emit(funnelSink, PriceStructureBreakoutFunnelEventKind.ConfirmationPassed, $"CP:{swing.Index}:{retestIndex}:{currentIndex}", TradeDirection.Short, swing, candles[currentIndex], candles[currentIndex].Close);

        var entryCandle = candles[currentIndex];
        var entry = entryCandle.Close;
        var stop = retestHigh * (1m + settings.StopBufferPercent / 100m);
        if (stop <= entry || entry <= 0 || stop <= 0)
        {
            return (null, PriceStructureRejectionCodes.InvalidStop);
        }

        var risk = stop - entry;
        var target = entry - (risk * settings.FixedRewardRisk);
        var fingerprint = BuildFingerprint(
            strategyCode,
            symbolId,
            timeframe,
            TradeDirection.Short,
            swing,
            breakoutIndex.Value,
            retestIndex.Value,
            candles);

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
            Reason = "Bearish breakout retest confirmed.",
            SetupFingerprint = fingerprint,
            Structure = new PriceStructureSetupDto
            {
                SetupType = "BreakoutRetest",
                Direction = "Short",
                BrokenOrSweptLevel = swing.Price,
                SwingTimeUtc = swing.OpenTimeUtc,
                BreakoutOrSweepTimeUtc = candles[breakoutIndex.Value].OpenTimeUtc,
                RetestOrReclaimTimeUtc = candles[retestIndex.Value].OpenTimeUtc,
                ConfirmationTimeUtc = entryCandle.OpenTimeUtc,
                SwingIndex = swing.Index,
                BreakoutIndex = breakoutIndex,
                RetestIndex = retestIndex,
                ConfirmationIndex = currentIndex
            }
        }, "Bearish breakout retest confirmed.");
    }

    private static bool IsBullishBreakout(Candle candle, decimal level, BreakoutRetestParameters settings)
    {
        if (settings.BreakoutMustCloseBeyondLevel && candle.Close <= level)
        {
            return false;
        }

        if (!settings.BreakoutMustCloseBeyondLevel && candle.High <= level)
        {
            return false;
        }

        if (settings.MinBreakoutClosePercent > 0 && level > 0)
        {
            var distance = (candle.Close - level) / level * 100m;
            if (distance < settings.MinBreakoutClosePercent)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBearishBreakout(Candle candle, decimal level, BreakoutRetestParameters settings)
    {
        if (settings.BreakoutMustCloseBeyondLevel && candle.Close >= level)
        {
            return false;
        }

        if (!settings.BreakoutMustCloseBeyondLevel && candle.Low >= level)
        {
            return false;
        }

        if (settings.MinBreakoutClosePercent > 0 && level > 0)
        {
            var distance = (level - candle.Close) / level * 100m;
            if (distance < settings.MinBreakoutClosePercent)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBullishRetestTouch(Candle candle, decimal level, BreakoutRetestParameters settings)
    {
        var tolerance = level * settings.RetestTolerancePercent / 100m;
        var upper = level + tolerance;
        var lower = settings.AllowWickThroughLevel ? level - tolerance : level;
        return candle.Low <= upper && candle.Low >= lower - tolerance;
    }

    private static bool IsBearishRetestTouch(Candle candle, decimal level, BreakoutRetestParameters settings)
    {
        var tolerance = level * settings.RetestTolerancePercent / 100m;
        var lower = level - tolerance;
        var upper = settings.AllowWickThroughLevel ? level + tolerance : level;
        return candle.High >= lower && candle.High <= upper + tolerance;
    }

    private static bool IsRetestInvalidatedLong(Candle candle, decimal level, BreakoutRetestParameters settings)
    {
        var maxPen = level * settings.MaxRetestPenetrationPercent / 100m;
        return candle.Close < level - maxPen;
    }

    private static bool IsRetestInvalidatedShort(Candle candle, decimal level, BreakoutRetestParameters settings)
    {
        var maxPen = level * settings.MaxRetestPenetrationPercent / 100m;
        return candle.Close > level + maxPen;
    }

    private static bool IsBullishConfirmation(
        IReadOnlyList<Candle> candles,
        int retestIndex,
        int currentIndex,
        decimal level,
        BreakoutRetestParameters settings)
    {
        if (currentIndex < retestIndex)
        {
            return false;
        }

        var mode = settings.ConfirmationMode;
        if (string.Equals(mode, "NoConfirmation", StringComparison.OrdinalIgnoreCase))
        {
            return currentIndex == retestIndex;
        }

        var confirmCandle = candles[currentIndex];
        if (string.Equals(mode, "BullishEngulfing", StringComparison.OrdinalIgnoreCase) && currentIndex > 0)
        {
            var prev = candles[currentIndex - 1];
            return confirmCandle.Close > level
                   && confirmCandle.Close > confirmCandle.Open
                   && confirmCandle.Close >= prev.Open
                   && confirmCandle.Open <= prev.Close;
        }

        if (string.Equals(mode, "CloseAbovePreviousHigh", StringComparison.OrdinalIgnoreCase) && currentIndex > 0)
        {
            var prev = candles[currentIndex - 1];
            return confirmCandle.Close > level && confirmCandle.Close > prev.High;
        }

        return confirmCandle.Close > level && confirmCandle.Close > confirmCandle.Open;
    }

    private static bool IsBearishConfirmation(
        IReadOnlyList<Candle> candles,
        int retestIndex,
        int currentIndex,
        decimal level,
        BreakoutRetestParameters settings)
    {
        if (currentIndex < retestIndex)
        {
            return false;
        }

        var mode = settings.ConfirmationMode;
        if (string.Equals(mode, "NoConfirmation", StringComparison.OrdinalIgnoreCase))
        {
            return currentIndex == retestIndex;
        }

        var confirmCandle = candles[currentIndex];
        if (string.Equals(mode, "BullishEngulfing", StringComparison.OrdinalIgnoreCase) && currentIndex > 0)
        {
            var prev = candles[currentIndex - 1];
            return confirmCandle.Close < level
                   && confirmCandle.Close < confirmCandle.Open
                   && confirmCandle.Close <= prev.Open
                   && confirmCandle.Open >= prev.Close;
        }

        if (string.Equals(mode, "CloseAbovePreviousHigh", StringComparison.OrdinalIgnoreCase) && currentIndex > 0)
        {
            var prev = candles[currentIndex - 1];
            return confirmCandle.Close < level && confirmCandle.Close < prev.Low;
        }

        return confirmCandle.Close < level && confirmCandle.Close < confirmCandle.Open;
    }

    public static string BuildFingerprint(
        string strategyCode,
        long symbolId,
        string timeframe,
        TradeDirection direction,
        ConfirmedSwing swing,
        int breakoutIndex,
        int retestIndex,
        IReadOnlyList<Candle> candles)
    {
        var swingLabel = swing.IsHigh ? "SWINGHIGH" : "SWINGLOW";
        var level = Math.Round(swing.Price, 8);
        var breakTs = candles[breakoutIndex].OpenTimeUtc.ToString("yyyyMMdd'T'HHmm");
        var retestTs = candles[retestIndex].OpenTimeUtc.ToString("yyyyMMdd'T'HHmm");
        var raw = $"{strategyCode}|v{StrategyVersion}|{symbolId}|{timeframe}|{direction}|{swingLabel}_{level}|BREAK_{breakTs}|RETEST_{retestTs}";
        return SetupFingerprintHasher.Hash(raw);
    }

    private static string PickCloserReason(string current, string candidate)
    {
        var priority = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [PriceStructureRejectionCodes.NoConfirmation] = 1,
            [PriceStructureRejectionCodes.WaitingForRetest] = 2,
            [PriceStructureRejectionCodes.RetestExpired] = 3,
            [PriceStructureRejectionCodes.RetestInvalidated] = 4,
            [PriceStructureRejectionCodes.NoBreakout] = 5,
            [PriceStructureRejectionCodes.NoConfirmedSwing] = 6
        };

        if (!priority.TryGetValue(current, out var currentRank))
        {
            currentRank = 99;
        }

        if (!priority.TryGetValue(candidate, out var candidateRank))
        {
            candidateRank = 99;
        }

        return candidateRank < currentRank ? candidate : current;
    }

    private static void Emit(
        Action<PriceStructureBreakoutFunnelEvent>? sink,
        PriceStructureBreakoutFunnelEventKind kind,
        string key,
        TradeDirection direction,
        ConfirmedSwing swing,
        Candle? eventCandle,
        decimal? eventPrice)
    {
        sink?.Invoke(new PriceStructureBreakoutFunnelEvent
        {
            Kind = kind,
            Key = key,
            Direction = direction,
            Level = swing.Price,
            LevelTimeUtc = swing.OpenTimeUtc,
            EventTimeUtc = eventCandle?.OpenTimeUtc,
            EventPrice = eventPrice
        });
    }
}
