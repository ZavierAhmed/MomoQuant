using MomoQuant.Application.Indicators.Calculators;
using MomoQuant.Application.Strategies.PriceStructure.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.PriceStructure;

public static class PriceStructureBreakoutRetestEvaluator
{
    public const string StrategyVersion = "1.1.0";
    public const string StrategyVersionV10 = "1.0.0";

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
        RetestToleranceAtrMultiplier = StrategyParameterReader.GetDecimal(parameters, "retestToleranceAtrMultiplier", 0.25m),
        AllowWickThroughLevel = StrategyParameterReader.GetBool(parameters, "allowWickThroughLevel", true),
        MaxRetestPenetrationPercent = StrategyParameterReader.GetDecimal(parameters, "maxRetestPenetrationPercent", 0.30m),
        ConfirmationMode = StrategyParameterReader.GetString(parameters, "confirmationMode", "ReactionClose"),
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
        var validation = ValidateParameters(settings);
        if (validation is not null)
        {
            return (null, validation);
        }

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
        var retestTouched = false;
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

            var toleranceResult = TryComputeRetestTolerance(candles, i, swing.Price, settings);
            if (!toleranceResult.Succeeded)
            {
                return (null, toleranceResult.Reason);
            }

            if (!retestTouched
                && IsBullishRetestTouch(candle, swing.Price, toleranceResult.Tolerance, settings))
            {
                retestTouched = true;
                retestIndex = i;
            }

            if (retestTouched)
            {
                retestLow = Math.Min(retestLow, candle.Low);
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

        if (currentIndex > breakoutIndex.Value + settings.MaxRetestBars)
        {
            return (null, PriceStructureRejectionCodes.RetestExpired);
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
        if (risk <= 0m || target <= entry)
        {
            return (null, PriceStructureRejectionCodes.InvalidTarget);
        }

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
        var retestTouched = false;
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

            var toleranceResult = TryComputeRetestTolerance(candles, i, swing.Price, settings);
            if (!toleranceResult.Succeeded)
            {
                return (null, toleranceResult.Reason);
            }

            if (!retestTouched
                && IsBearishRetestTouch(candle, swing.Price, toleranceResult.Tolerance, settings))
            {
                retestTouched = true;
                retestIndex = i;
            }

            if (retestTouched)
            {
                retestHigh = Math.Max(retestHigh, candle.High);
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

        if (currentIndex > breakoutIndex.Value + settings.MaxRetestBars)
        {
            return (null, PriceStructureRejectionCodes.RetestExpired);
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
        if (risk <= 0m || target >= entry || target <= 0m)
        {
            return (null, PriceStructureRejectionCodes.InvalidTarget);
        }

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

    private static bool IsBullishRetestTouch(Candle candle, decimal level, decimal tolerance, BreakoutRetestParameters settings)
    {
        var upper = level + tolerance;
        var lower = settings.AllowWickThroughLevel ? level - tolerance : level;
        return candle.Low <= upper && candle.Low >= lower;
    }

    private static bool IsBearishRetestTouch(Candle candle, decimal level, decimal tolerance, BreakoutRetestParameters settings)
    {
        var lower = level - tolerance;
        var upper = settings.AllowWickThroughLevel ? level + tolerance : level;
        return candle.High >= lower && candle.High <= upper;
    }

    private sealed record RetestToleranceResult(bool Succeeded, decimal Tolerance, string Reason)
    {
        public static RetestToleranceResult Success(decimal tolerance) => new(true, tolerance, string.Empty);
        public static RetestToleranceResult Failure(string reason) => new(false, 0m, reason);
    }

    private static RetestToleranceResult TryComputeRetestTolerance(
        IReadOnlyList<Candle> candles,
        int candleIndex,
        decimal level,
        BreakoutRetestParameters settings)
    {
        if (string.Equals(settings.RetestToleranceMode, "Atr", StringComparison.OrdinalIgnoreCase))
        {
            var atr = ComputeAtr14AtIndex(candles, candleIndex);
            return atr is > 0m
                ? RetestToleranceResult.Success(atr.Value * settings.RetestToleranceAtrMultiplier)
                : RetestToleranceResult.Failure(PriceStructureRejectionCodes.InsufficientData);
        }

        return RetestToleranceResult.Success(level * settings.RetestTolerancePercent / 100m);
    }

    public static decimal? ComputeAtr14AtIndex(IReadOnlyList<Candle> candles, int index)
    {
        if (index < 0 || index >= candles.Count)
        {
            return null;
        }

        var state = new AtrCalculator.State();
        decimal? atr = null;
        for (var i = 0; i <= index; i++)
        {
            atr = AtrCalculator.CalculateNext(candles[i], state);
        }

        return atr;
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

        var mode = NormalizeConfirmationMode(settings.ConfirmationMode) ?? string.Empty;
        if (string.Equals(mode, "NoConfirmation", StringComparison.OrdinalIgnoreCase))
        {
            return currentIndex == retestIndex;
        }

        if (currentIndex <= retestIndex)
        {
            return false;
        }

        var confirmCandle = candles[currentIndex];
        if (string.Equals(mode, "Engulfing", StringComparison.OrdinalIgnoreCase) && currentIndex > 0)
        {
            var prev = candles[currentIndex - 1];
            return confirmCandle.Close > level
                   && confirmCandle.Close > confirmCandle.Open
                   && confirmCandle.Close >= prev.Open
                   && confirmCandle.Open <= prev.Close;
        }

        if (string.Equals(mode, "CloseBeyondPreviousExtreme", StringComparison.OrdinalIgnoreCase) && currentIndex > 0)
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

        var mode = NormalizeConfirmationMode(settings.ConfirmationMode) ?? string.Empty;
        if (string.Equals(mode, "NoConfirmation", StringComparison.OrdinalIgnoreCase))
        {
            return currentIndex == retestIndex;
        }

        if (currentIndex <= retestIndex)
        {
            return false;
        }

        var confirmCandle = candles[currentIndex];
        if (string.Equals(mode, "Engulfing", StringComparison.OrdinalIgnoreCase) && currentIndex > 0)
        {
            var prev = candles[currentIndex - 1];
            return confirmCandle.Close < level
                   && confirmCandle.Close < confirmCandle.Open
                   && confirmCandle.Close <= prev.Open
                   && confirmCandle.Open >= prev.Close;
        }

        if (string.Equals(mode, "CloseBeyondPreviousExtreme", StringComparison.OrdinalIgnoreCase) && currentIndex > 0)
        {
            var prev = candles[currentIndex - 1];
            return confirmCandle.Close < level && confirmCandle.Close < prev.Low;
        }

        return confirmCandle.Close < level && confirmCandle.Close < confirmCandle.Open;
    }

    internal static string? NormalizeConfirmationMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return null;
        }

        if (string.Equals(mode, "ReactionClose", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "BullishReactionClose", StringComparison.OrdinalIgnoreCase))
        {
            return "ReactionClose";
        }

        if (string.Equals(mode, "Engulfing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "BullishEngulfing", StringComparison.OrdinalIgnoreCase))
        {
            return "Engulfing";
        }

        if (string.Equals(mode, "CloseBeyondPreviousExtreme", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "CloseAbovePreviousHigh", StringComparison.OrdinalIgnoreCase))
        {
            return "CloseBeyondPreviousExtreme";
        }

        if (string.Equals(mode, "NoConfirmation", StringComparison.OrdinalIgnoreCase))
        {
            return "NoConfirmation";
        }

        return null;
    }

    private static string? ValidateParameters(BreakoutRetestParameters settings)
    {
        if (settings.SwingLeftBars <= 0
            || settings.SwingRightBars <= 0
            || settings.MinSwingDistanceBars < 0
            || settings.MaxRetestBars <= 0)
        {
            return PriceStructureRejectionCodes.InvalidParameters;
        }

        if (settings.MinBreakoutClosePercent < 0m
            || settings.RetestTolerancePercent < 0m
            || settings.RetestToleranceAtrMultiplier < 0m
            || settings.MaxRetestPenetrationPercent < 0m
            || settings.StopBufferPercent < 0m
            || settings.FixedRewardRisk <= 0m)
        {
            return PriceStructureRejectionCodes.InvalidParameters;
        }

        if (!string.Equals(settings.RetestToleranceMode, "Percent", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(settings.RetestToleranceMode, "Atr", StringComparison.OrdinalIgnoreCase))
        {
            return PriceStructureRejectionCodes.InvalidParameters;
        }

        return NormalizeConfirmationMode(settings.ConfirmationMode) is null
            ? PriceStructureRejectionCodes.InvalidParameters
            : null;
    }

    public static BreakoutRetestStrengthBreakdown ComputeStrengthBreakdown(
        IReadOnlyList<Candle> candles,
        PriceStructureCandidateDto candidate,
        BreakoutRetestParameters settings)
    {
        var structure = candidate.Structure;
        var level = structure.BrokenOrSweptLevel;
        var breakoutIdx = structure.BreakoutIndex;
        var retestIdx = structure.RetestIndex;
        var confirmIdx = structure.ConfirmationIndex ?? candles.Count - 1;
        var breakout = SafeCandle(candles, breakoutIdx);
        var retest = SafeCandle(candles, retestIdx);
        var confirm = SafeCandle(candles, confirmIdx);
        var prior = confirmIdx > 0 ? SafeCandle(candles, confirmIdx - 1) : null;

        var breakoutDistanceScore = ScoreBreakoutDistance(candidate.Direction, level, breakout, maxPoints: 25m);
        var retestQualityScore = ScoreRetestQuality(candidate.Direction, level, breakoutIdx, retestIdx, retest, maxPoints: 25m);
        var confirmationQualityScore = ScoreConfirmationQuality(
            candidate.Direction,
            settings.ConfirmationMode,
            confirm,
            prior,
            maxPoints: 25m);
        var rewardRiskValidityScore = ScoreRewardRiskValidity(
            candidate.EntryPrice,
            candidate.StopLoss,
            candidate.Target1,
            candidate.RewardRisk,
            settings.FixedRewardRisk,
            maxPoints: 25m);

        var total = Math.Clamp(
            Math.Round(
                breakoutDistanceScore + retestQualityScore + confirmationQualityScore + rewardRiskValidityScore,
                2),
            0m,
            100m);

        return new BreakoutRetestStrengthBreakdown(
            total,
            breakoutDistanceScore,
            retestQualityScore,
            confirmationQualityScore,
            rewardRiskValidityScore);
    }

    private static decimal ScoreBreakoutDistance(
        TradeDirection direction,
        decimal level,
        Candle? breakout,
        decimal maxPoints)
    {
        if (breakout is null || level <= 0)
        {
            return maxPoints * 0.25m;
        }

        var beyond = direction == TradeDirection.Long
            ? (breakout.Close - level) / level * 100m
            : (level - breakout.Close) / level * 100m;
        beyond = Math.Max(0m, beyond);

        var range = Math.Max(breakout.High - breakout.Low, 0.0000001m);
        var bodyRatio = Math.Abs(breakout.Close - breakout.Open) / range;
        var score = Clamp01(beyond / 0.35m) * (maxPoints * 0.55m) + Clamp01(bodyRatio) * (maxPoints * 0.45m);
        if (beyond <= 0.02m && bodyRatio < 0.35m)
        {
            score *= 0.55m;
        }

        return Math.Clamp(Math.Round(score, 2), 0m, maxPoints);
    }

    private static decimal ScoreRetestQuality(
        TradeDirection direction,
        decimal level,
        int? breakoutIdx,
        int? retestIdx,
        Candle? retest,
        decimal maxPoints)
    {
        if (retest is null || level <= 0)
        {
            return maxPoints * 0.25m;
        }

        var distancePct = Math.Abs(retest.Close - level) / level * 100m;
        var penetration = direction == TradeDirection.Long
            ? Math.Max(0m, (level - retest.Low) / level * 100m)
            : Math.Max(0m, (retest.High - level) / level * 100m);
        var bars = breakoutIdx.HasValue && retestIdx.HasValue
            ? Math.Max(0, retestIdx.Value - breakoutIdx.Value)
            : 10;

        var score = (1m - Clamp01(distancePct / 0.40m)) * (maxPoints * 0.40m)
                    + (1m - Clamp01(penetration / 0.50m)) * (maxPoints * 0.40m)
                    + (1m - Clamp01(bars / 20m)) * (maxPoints * 0.20m);
        return Math.Clamp(Math.Round(score, 2), 0m, maxPoints);
    }

    private static decimal ScoreConfirmationQuality(
        TradeDirection direction,
        string confirmationMode,
        Candle? confirm,
        Candle? prior,
        decimal maxPoints)
    {
        var mode = NormalizeConfirmationMode(confirmationMode) ?? string.Empty;
        if (string.Equals(mode, "NoConfirmation", StringComparison.OrdinalIgnoreCase))
        {
            return maxPoints * 0.50m;
        }

        if (confirm is null)
        {
            return maxPoints * 0.25m;
        }

        var range = Math.Max(confirm.High - confirm.Low, 0.0000001m);
        var bodyRatio = Math.Abs(confirm.Close - confirm.Open) / range;
        var bullish = confirm.Close > confirm.Open;
        var directionOk = direction == TradeDirection.Long ? bullish : !bullish;
        var closeLoc = direction == TradeDirection.Long
            ? (confirm.Close - confirm.Low) / range
            : (confirm.High - confirm.Close) / range;

        var score = (directionOk ? maxPoints * 0.24m : maxPoints * 0.04m)
                    + Clamp01(bodyRatio) * (maxPoints * 0.24m)
                    + Clamp01(closeLoc) * (maxPoints * 0.16m);

        if (string.Equals(mode, "Engulfing", StringComparison.OrdinalIgnoreCase) && prior is not null)
        {
            var engulfed = direction == TradeDirection.Long
                ? confirm.Close >= prior.Open && confirm.Open <= prior.Close
                : confirm.Close <= prior.Open && confirm.Open >= prior.Close;
            if (engulfed)
            {
                score += maxPoints * 0.12m;
            }
        }
        else if (string.Equals(mode, "CloseBeyondPreviousExtreme", StringComparison.OrdinalIgnoreCase) && prior is not null)
        {
            var beyondPrior = direction == TradeDirection.Long
                ? confirm.Close > prior.High
                : confirm.Close < prior.Low;
            if (beyondPrior)
            {
                score += maxPoints * 0.12m;
            }
        }

        return Math.Clamp(Math.Round(score, 2), 0m, maxPoints);
    }

    private static decimal ScoreRewardRiskValidity(
        decimal entry,
        decimal stop,
        decimal target,
        decimal rewardRisk,
        decimal fixedRewardRisk,
        decimal maxPoints)
    {
        if (entry <= 0 || stop <= 0 || target <= 0)
        {
            return 0m;
        }

        var risk = Math.Abs(entry - stop);
        if (risk <= 0)
        {
            return 0m;
        }

        var reward = Math.Abs(target - entry);
        var actualRr = reward / risk;
        var stopPct = risk / entry * 100m;

        var rrScore = actualRr >= fixedRewardRisk
            ? maxPoints * 0.55m
            : maxPoints * Clamp01(actualRr / Math.Max(fixedRewardRisk, 0.0001m)) * 0.55m;
        var stopScore = stopPct is >= 0.08m and <= 1.8m
            ? maxPoints * 0.30m
            : maxPoints * Math.Max(0.15m, 1m - Clamp01((Math.Abs(stopPct - 0.94m)) / 3m) * 0.55m) * 0.30m;
        var validityScore = rewardRisk >= fixedRewardRisk ? maxPoints * 0.15m : maxPoints * 0.05m;

        return Math.Clamp(Math.Round(rrScore + stopScore + validityScore, 2), 0m, maxPoints);
    }

    private static Candle? SafeCandle(IReadOnlyList<Candle> candles, int? index)
    {
        if (!index.HasValue || index.Value < 0 || index.Value >= candles.Count)
        {
            return null;
        }

        return candles[index.Value];
    }

    private static decimal Clamp01(decimal value) => Math.Clamp(value, 0m, 1m);

    public static string BuildFingerprint(
        string strategyCode,
        long symbolId,
        string timeframe,
        TradeDirection direction,
        ConfirmedSwing swing,
        int breakoutIndex,
        int retestIndex,
        IReadOnlyList<Candle> candles) =>
        BuildFingerprint(
            strategyCode,
            symbolId,
            timeframe,
            direction,
            swing,
            breakoutIndex,
            retestIndex,
            candles,
            StrategyVersion);

    public static string BuildFingerprint(
        string strategyCode,
        long symbolId,
        string timeframe,
        TradeDirection direction,
        ConfirmedSwing swing,
        int breakoutIndex,
        int retestIndex,
        IReadOnlyList<Candle> candles,
        string version)
    {
        var swingLabel = swing.IsHigh ? "SWINGHIGH" : "SWINGLOW";
        var level = Math.Round(swing.Price, 8);
        var breakTs = candles[breakoutIndex].OpenTimeUtc.ToString("yyyyMMdd'T'HHmm");
        var retestTs = candles[retestIndex].OpenTimeUtc.ToString("yyyyMMdd'T'HHmm");
        var raw = $"{strategyCode}|v{version}|{symbolId}|{timeframe}|{direction}|{swingLabel}_{level}|BREAK_{breakTs}|RETEST_{retestTs}";
        return SetupFingerprintHasher.Hash(raw);
    }

    private static string PickCloserReason(string current, string candidate)
    {
        var priority = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [PriceStructureRejectionCodes.InvalidStop] = 0,
            [PriceStructureRejectionCodes.InvalidTarget] = 0,
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
