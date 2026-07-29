using System.Text.Json;
using MomoQuant.Application.Common;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.PriceStructure;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.MomoAdaptive;

public static class MomoAdaptiveMtfTrendBreakoutEvaluator
{
    public const string StrategyVersion = "1.0.0";

    public sealed class MomoAdaptiveMtfParameters
    {
        public int HtfFastEmaPeriod { get; init; } = 50;
        public int HtfSlowEmaPeriod { get; init; } = 200;
        public int HtfSlopeLookback { get; init; } = 5;
        public int LtfFastEmaPeriod { get; init; } = 20;
        public int LtfSlowEmaPeriod { get; init; } = 50;
        public int BreakoutLookback { get; init; } = 20;
        public int FastAtrPeriod { get; init; } = 14;
        public int SlowAtrPeriod { get; init; } = 100;
        public decimal MinVolatilityRatio { get; init; } = 1.00m;
        public decimal MaxVolatilityRatio { get; init; } = 2.25m;
        public decimal BaseBreakoutBufferAtr { get; init; } = 0.10m;
        public decimal VolatilitySensitivity { get; init; } = 0.15m;
        public decimal MinBreakoutBufferAtr { get; init; } = 0.05m;
        public decimal MaxBreakoutBufferAtr { get; init; } = 0.35m;
        public int MacdFast { get; init; } = 12;
        public int MacdSlow { get; init; } = 26;
        public int MacdSignal { get; init; } = 9;
        public bool RequireHistogramExpansion { get; init; } = true;
        public int MaxRetestBars { get; init; } = 8;
        public decimal RetestToleranceAtr { get; init; } = 0.35m;
        public decimal MaxBreakoutChaseAtr { get; init; } = 1.00m;
        public decimal StopBufferAtr { get; init; } = 0.20m;
        public decimal FixedRewardRisk { get; init; } = 2.50m;
        public decimal MinStrength { get; init; } = 70m;
    }

    public sealed class MomoAdaptiveMtfCandidate
    {
        public required TradeDirection Direction { get; init; }
        public required decimal EntryPrice { get; init; }
        public required decimal StopLoss { get; init; }
        public required decimal TakeProfit { get; init; }
        public required decimal Strength { get; init; }
        public required string Reason { get; init; }
        public required string SetupFingerprint { get; init; }
        public required object StrengthBreakdown { get; init; }
        public required object Setup { get; init; }
    }

    public static MomoAdaptiveMtfParameters ReadParameters(IReadOnlyDictionary<string, string> parameters) => new()
    {
        HtfFastEmaPeriod = StrategyParameterReader.GetInt(parameters, "htfFastEmaPeriod", 50),
        HtfSlowEmaPeriod = StrategyParameterReader.GetInt(parameters, "htfSlowEmaPeriod", 200),
        HtfSlopeLookback = StrategyParameterReader.GetInt(parameters, "htfSlopeLookback", 5),
        LtfFastEmaPeriod = StrategyParameterReader.GetInt(parameters, "ltfFastEmaPeriod", 20),
        LtfSlowEmaPeriod = StrategyParameterReader.GetInt(parameters, "ltfSlowEmaPeriod", 50),
        BreakoutLookback = StrategyParameterReader.GetInt(parameters, "breakoutLookback", 20),
        FastAtrPeriod = StrategyParameterReader.GetInt(parameters, "fastAtrPeriod", 14),
        SlowAtrPeriod = StrategyParameterReader.GetInt(parameters, "slowAtrPeriod", 100),
        MinVolatilityRatio = StrategyParameterReader.GetDecimal(parameters, "minVolatilityRatio", 1.00m),
        MaxVolatilityRatio = StrategyParameterReader.GetDecimal(parameters, "maxVolatilityRatio", 2.25m),
        BaseBreakoutBufferAtr = StrategyParameterReader.GetDecimal(parameters, "baseBreakoutBufferAtr", 0.10m),
        VolatilitySensitivity = StrategyParameterReader.GetDecimal(parameters, "volatilitySensitivity", 0.15m),
        MinBreakoutBufferAtr = StrategyParameterReader.GetDecimal(parameters, "minBreakoutBufferAtr", 0.05m),
        MaxBreakoutBufferAtr = StrategyParameterReader.GetDecimal(parameters, "maxBreakoutBufferAtr", 0.35m),
        MacdFast = StrategyParameterReader.GetInt(parameters, "macdFast", 12),
        MacdSlow = StrategyParameterReader.GetInt(parameters, "macdSlow", 26),
        MacdSignal = StrategyParameterReader.GetInt(parameters, "macdSignal", 9),
        RequireHistogramExpansion = StrategyParameterReader.GetBool(parameters, "requireHistogramExpansion", true),
        MaxRetestBars = StrategyParameterReader.GetInt(parameters, "maxRetestBars", 8),
        RetestToleranceAtr = StrategyParameterReader.GetDecimal(parameters, "retestToleranceAtr", 0.35m),
        MaxBreakoutChaseAtr = StrategyParameterReader.GetDecimal(parameters, "maxBreakoutChaseAtr", 1.00m),
        StopBufferAtr = StrategyParameterReader.GetDecimal(parameters, "stopBufferAtr", 0.20m),
        FixedRewardRisk = StrategyParameterReader.GetDecimal(parameters, "fixedRewardRisk", 2.50m),
        MinStrength = StrategyParameterReader.GetDecimal(parameters, "minStrength", 70m)
    };

    public static Timeframe ResolveHigherTimeframe(Timeframe timeframe) => timeframe switch
    {
        Timeframe.M5 => Timeframe.H1,
        Timeframe.M15 => Timeframe.H4,
        Timeframe.H1 => Timeframe.H4,
        Timeframe.H4 => Timeframe.D1,
        _ => throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, "Unsupported execution timeframe for HTF mapping.")
    };

    public static (MomoAdaptiveMtfCandidate? Candidate, string Reason) EvaluateAtCurrentCandle(
        IReadOnlyList<Candle> candles,
        IReadOnlyList<Candle> higherTimeframeCandles,
        IReadOnlyDictionary<string, string> parameters,
        MarketRegime marketRegime,
        IReadOnlySet<string> seenFingerprints,
        string strategyCode,
        long symbolId,
        string timeframe)
    {
        var settings = ReadParameters(parameters);

        var validationResult = ValidateParameters(settings);
        if (validationResult != null)
        {
            return (null, validationResult);
        }

        if (marketRegime != MarketRegime.Trending && marketRegime != MarketRegime.Breakout)
        {
            return (null, MomoAdaptiveMtfRejectionCodes.UnsupportedRegime);
        }

        var minLtfBars = ComputeMinLtfBars(settings);
        if (candles.Count < minLtfBars)
        {
            return (null, MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable);
        }

        var minHtfBars = settings.HtfSlowEmaPeriod + settings.HtfSlopeLookback;
        if (higherTimeframeCandles.Count < minHtfBars)
        {
            return (null, MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable);
        }

        var currentIndex = candles.Count - 1;
        var ltfCloses = candles.Select(c => c.Close).ToArray();
        var ltfEmaFast = ComputeEma(ltfCloses, settings.LtfFastEmaPeriod);
        var ltfEmaSlow = ComputeEma(ltfCloses, settings.LtfSlowEmaPeriod);
        var ltfAtrFast = ComputeWilderAtr(candles, settings.FastAtrPeriod);
        var ltfAtrSlow = ComputeWilderAtr(candles, settings.SlowAtrPeriod);
        var (_, _, macdHistogram) = ComputeMacd(ltfCloses, settings.MacdFast, settings.MacdSlow, settings.MacdSignal);

        if (!TryGetEma(ltfEmaFast, currentIndex, settings.LtfFastEmaPeriod, out var execEmaFast) ||
            !TryGetEma(ltfEmaSlow, currentIndex, settings.LtfSlowEmaPeriod, out var execEmaSlow) ||
            !TryGetAtr(ltfAtrFast, currentIndex, settings.FastAtrPeriod, out var confirmationAtrFast) ||
            !TryGetAtr(ltfAtrSlow, currentIndex, settings.SlowAtrPeriod, out var confirmationAtrSlow) ||
            !TryGetMacdHistogram(macdHistogram, currentIndex, settings, out var histogram) ||
            !TryGetMacdHistogram(macdHistogram, currentIndex - 1, settings, out var previousHistogram))
        {
            return (null, MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable);
        }

        if (confirmationAtrSlow <= 0m || confirmationAtrFast <= 0m)
        {
            return (null, MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable);
        }

        var htfCloses = higherTimeframeCandles.Select(c => c.Close).ToArray();
        var htfEmaFast = ComputeEma(htfCloses, settings.HtfFastEmaPeriod);
        var htfEmaSlow = ComputeEma(htfCloses, settings.HtfSlowEmaPeriod);
        var htfLastIndex = higherTimeframeCandles.Count - 1;
        var htfSlopeIndex = htfLastIndex - settings.HtfSlopeLookback;

        if (!TryGetEma(htfEmaFast, htfLastIndex, settings.HtfFastEmaPeriod, out var htfFast) ||
            !TryGetEma(htfEmaSlow, htfLastIndex, settings.HtfSlowEmaPeriod, out var htfSlow) ||
            !TryGetEma(htfEmaFast, htfSlopeIndex, settings.HtfFastEmaPeriod, out var htfFastSlopeStart))
        {
            return (null, MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable);
        }

        var htfClose = higherTimeframeCandles[htfLastIndex].Close;
        var htfSlope = htfFast - htfFastSlopeStart;

        var longHtfAligned = htfFast > htfSlow;
        var shortHtfAligned = htfFast < htfSlow;

        if (!longHtfAligned && !shortHtfAligned)
        {
            return (null, MomoAdaptiveMtfRejectionCodes.HtfTrendNotAligned);
        }

        string bestReason = MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable;

        if (longHtfAligned)
        {
            var (candidate, reason) = TryBuildLongCandidate(
                candles,
                currentIndex,
                settings,
                seenFingerprints,
                strategyCode,
                symbolId,
                timeframe,
                htfFast,
                htfSlow,
                htfSlope,
                htfClose,
                execEmaFast,
                execEmaSlow,
                confirmationAtrFast,
                histogram,
                previousHistogram,
                ltfAtrFast,
                ltfAtrSlow);
            if (candidate is not null)
            {
                return (candidate, MomoAdaptiveMtfRejectionCodes.EntryConfirmed);
            }

            bestReason = PickCloserReason(bestReason, reason);
        }

        if (shortHtfAligned)
        {
            var (candidate, reason) = TryBuildShortCandidate(
                candles,
                currentIndex,
                settings,
                seenFingerprints,
                strategyCode,
                symbolId,
                timeframe,
                htfFast,
                htfSlow,
                htfSlope,
                htfClose,
                execEmaFast,
                execEmaSlow,
                confirmationAtrFast,
                histogram,
                previousHistogram,
                ltfAtrFast,
                ltfAtrSlow);
            if (candidate is not null)
            {
                return (candidate, MomoAdaptiveMtfRejectionCodes.EntryConfirmed);
            }

            bestReason = PickCloserReason(bestReason, reason);
        }

        return (null, bestReason);
    }

    public static IReadOnlyDictionary<string, string> GetDefaultParameterContract()
    {
        var defaults = new MomoAdaptiveMtfParameters();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return new Dictionary<string, string>
        {
            ["htfFastEmaPeriod"] = defaults.HtfFastEmaPeriod.ToString(inv),
            ["htfSlowEmaPeriod"] = defaults.HtfSlowEmaPeriod.ToString(inv),
            ["htfSlopeLookback"] = defaults.HtfSlopeLookback.ToString(inv),
            ["ltfFastEmaPeriod"] = defaults.LtfFastEmaPeriod.ToString(inv),
            ["ltfSlowEmaPeriod"] = defaults.LtfSlowEmaPeriod.ToString(inv),
            ["breakoutLookback"] = defaults.BreakoutLookback.ToString(inv),
            ["fastAtrPeriod"] = defaults.FastAtrPeriod.ToString(inv),
            ["slowAtrPeriod"] = defaults.SlowAtrPeriod.ToString(inv),
            ["minVolatilityRatio"] = defaults.MinVolatilityRatio.ToString("0.00", inv),
            ["maxVolatilityRatio"] = defaults.MaxVolatilityRatio.ToString("0.00", inv),
            ["baseBreakoutBufferAtr"] = defaults.BaseBreakoutBufferAtr.ToString("0.00", inv),
            ["volatilitySensitivity"] = defaults.VolatilitySensitivity.ToString("0.00", inv),
            ["minBreakoutBufferAtr"] = defaults.MinBreakoutBufferAtr.ToString("0.00", inv),
            ["maxBreakoutBufferAtr"] = defaults.MaxBreakoutBufferAtr.ToString("0.00", inv),
            ["macdFast"] = defaults.MacdFast.ToString(inv),
            ["macdSlow"] = defaults.MacdSlow.ToString(inv),
            ["macdSignal"] = defaults.MacdSignal.ToString(inv),
            ["requireHistogramExpansion"] = defaults.RequireHistogramExpansion ? "true" : "false",
            ["maxRetestBars"] = defaults.MaxRetestBars.ToString(inv),
            ["retestToleranceAtr"] = defaults.RetestToleranceAtr.ToString("0.00", inv),
            ["maxBreakoutChaseAtr"] = defaults.MaxBreakoutChaseAtr.ToString("0.00", inv),
            ["stopBufferAtr"] = defaults.StopBufferAtr.ToString("0.00", inv),
            ["fixedRewardRisk"] = defaults.FixedRewardRisk.ToString("0.00", inv),
            ["minStrength"] = defaults.MinStrength.ToString(inv)
        };
    }

    public static int ComputeWarmupBars(MomoAdaptiveMtfParameters settings) =>
        ComputeMinLtfBars(settings);

    private static string? ValidateParameters(MomoAdaptiveMtfParameters settings)
    {
        if (settings.HtfFastEmaPeriod <= 0 || settings.HtfSlowEmaPeriod <= 0 ||
            settings.HtfSlopeLookback <= 0 || settings.LtfFastEmaPeriod <= 0 ||
            settings.LtfSlowEmaPeriod <= 0 || settings.BreakoutLookback <= 0 ||
            settings.FastAtrPeriod <= 0 || settings.SlowAtrPeriod <= 0 ||
            settings.MacdFast <= 0 || settings.MacdSlow <= 0 || settings.MacdSignal <= 0 ||
            settings.MaxRetestBars <= 0)
        {
            return MomoAdaptiveMtfRejectionCodes.InvalidParameters;
        }

        if (settings.HtfFastEmaPeriod >= settings.HtfSlowEmaPeriod)
        {
            return MomoAdaptiveMtfRejectionCodes.InvalidParameters;
        }

        if (settings.LtfFastEmaPeriod >= settings.LtfSlowEmaPeriod)
        {
            return MomoAdaptiveMtfRejectionCodes.InvalidParameters;
        }

        if (settings.FastAtrPeriod >= settings.SlowAtrPeriod)
        {
            return MomoAdaptiveMtfRejectionCodes.InvalidParameters;
        }

        if (settings.MacdFast >= settings.MacdSlow)
        {
            return MomoAdaptiveMtfRejectionCodes.InvalidParameters;
        }

        if (settings.MinVolatilityRatio > settings.MaxVolatilityRatio)
        {
            return MomoAdaptiveMtfRejectionCodes.InvalidParameters;
        }

        if (settings.MinBreakoutBufferAtr > settings.MaxBreakoutBufferAtr)
        {
            return MomoAdaptiveMtfRejectionCodes.InvalidParameters;
        }

        if (settings.RetestToleranceAtr < 0m || settings.MaxBreakoutChaseAtr < 0m ||
            settings.StopBufferAtr < 0m || settings.BaseBreakoutBufferAtr < 0m ||
            settings.VolatilitySensitivity < 0m || settings.MinBreakoutBufferAtr < 0m ||
            settings.MaxBreakoutBufferAtr < 0m)
        {
            return MomoAdaptiveMtfRejectionCodes.InvalidParameters;
        }

        if (settings.FixedRewardRisk <= 0m)
        {
            return MomoAdaptiveMtfRejectionCodes.InvalidParameters;
        }

        if (settings.MinStrength < 0m || settings.MinStrength > 100m)
        {
            return MomoAdaptiveMtfRejectionCodes.InvalidParameters;
        }

        return null;
    }

    private static (MomoAdaptiveMtfCandidate? Candidate, string Reason) TryBuildLongCandidate(
        IReadOnlyList<Candle> candles,
        int currentIndex,
        MomoAdaptiveMtfParameters settings,
        IReadOnlySet<string> seenFingerprints,
        string strategyCode,
        long symbolId,
        string timeframe,
        decimal htfFast,
        decimal htfSlow,
        decimal htfSlope,
        decimal htfClose,
        decimal execEmaFast,
        decimal execEmaSlow,
        decimal confirmationAtrFast,
        decimal histogram,
        decimal previousHistogram,
        decimal[] ltfAtrFast,
        decimal[] ltfAtrSlow)
    {
        if (htfFast <= htfSlow)
        {
            return (null, MomoAdaptiveMtfRejectionCodes.HtfTrendNotAligned);
        }

        if (htfSlope <= 0m)
        {
            return (null, MomoAdaptiveMtfRejectionCodes.HtfSlopeNotAligned);
        }

        if (htfClose <= htfFast)
        {
            return (null, MomoAdaptiveMtfRejectionCodes.HtfTrendNotAligned);
        }

        if (execEmaFast <= execEmaSlow)
        {
            return (null, MomoAdaptiveMtfRejectionCodes.ExecutionTrendNotAligned);
        }

        if (histogram <= 0m || (settings.RequireHistogramExpansion && histogram <= previousHistogram))
        {
            return (null, MomoAdaptiveMtfRejectionCodes.MomentumNotConfirmed);
        }

        var minBreakoutIndex = Math.Max(settings.BreakoutLookback, settings.SlowAtrPeriod);
        var bestReason = MomoAdaptiveMtfRejectionCodes.NoBreakout;

        for (var breakoutIndex = currentIndex - 1; breakoutIndex >= minBreakoutIndex; breakoutIndex--)
        {
            if (!TryGetAtr(ltfAtrFast, breakoutIndex, settings.FastAtrPeriod, out var breakoutAtrFast) ||
                !TryGetAtr(ltfAtrSlow, breakoutIndex, settings.SlowAtrPeriod, out var breakoutAtrSlow))
            {
                continue;
            }

            if (breakoutAtrSlow <= 0m)
            {
                continue;
            }

            var volRatio = breakoutAtrFast / breakoutAtrSlow;

            if (volRatio < settings.MinVolatilityRatio)
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.VolatilityTooLow);
                continue;
            }

            if (volRatio > settings.MaxVolatilityRatio)
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.VolatilityTooHigh);
                continue;
            }

            var adaptiveBuffer = ComputeAdaptiveBuffer(settings, volRatio);

            var brokenLevel = ComputeRangeHigh(candles, breakoutIndex - settings.BreakoutLookback, breakoutIndex - 1);
            if (!brokenLevel.HasValue)
            {
                continue;
            }

            var breakoutCandle = candles[breakoutIndex];
            if (breakoutCandle.Close <= brokenLevel.Value)
            {
                continue;
            }

            var breakoutDistance = breakoutCandle.Close - brokenLevel.Value;
            var requiredDistance = adaptiveBuffer * breakoutAtrFast;
            if (breakoutDistance < requiredDistance)
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.BreakoutBufferNotMet);
                continue;
            }

            var retestSearchEnd = Math.Min(currentIndex, breakoutIndex + settings.MaxRetestBars);
            int? retestIndex = null;
            var retestTouched = false;
            var retestInvalidated = false;
            decimal retestLow = decimal.MaxValue;

            for (var i = breakoutIndex + 1; i <= retestSearchEnd; i++)
            {
                if (!TryGetAtr(ltfAtrFast, i, settings.FastAtrPeriod, out var retestAtr))
                {
                    continue;
                }

                var candle = candles[i];
                if (IsRetestInvalidatedLong(candle, brokenLevel.Value, settings.RetestToleranceAtr, retestAtr))
                {
                    bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.RetestInvalidated);
                    retestInvalidated = true;
                    retestIndex = null;
                    break;
                }

                if (!retestTouched
                    && IsLongRetestTouch(candle, brokenLevel.Value, settings.RetestToleranceAtr, retestAtr))
                {
                    retestTouched = true;
                    retestIndex = i;
                }

                if (retestTouched)
                {
                    retestLow = Math.Min(retestLow, candle.Low);
                }
            }

            if (retestInvalidated)
            {
                continue;
            }

            if (retestIndex is null)
            {
                if (currentIndex > breakoutIndex + settings.MaxRetestBars)
                {
                    bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.RetestExpired);
                }
                else
                {
                    bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.WaitingForRetest);
                }

                continue;
            }

            if (!TryGetAtr(ltfAtrFast, retestIndex.Value, settings.FastAtrPeriod, out var retestEventAtrFast))
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable);
                continue;
            }

            if (currentIndex > breakoutIndex + settings.MaxRetestBars)
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.RetestExpired);
                continue;
            }

            if (!IsLongConfirmation(candles[currentIndex], brokenLevel.Value))
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.WaitingForRetest);
                continue;
            }

            var entry = candles[currentIndex].Close;
            if (entry - brokenLevel.Value > settings.MaxBreakoutChaseAtr * confirmationAtrFast)
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.BreakoutOverextended);
                continue;
            }

            var stop = retestLow - (settings.StopBufferAtr * confirmationAtrFast);
            if (stop >= entry || stop <= 0m || entry <= 0m)
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.InvalidStop);
                continue;
            }

            var risk = entry - stop;
            // Invariant: with validated FixedRewardRisk > 0 and InvalidStop ensuring risk > 0,
            // long target = entry + risk*RR is always > entry when the multiply does not overflow.
            if (!TryComputeLongTarget(entry, risk, settings.FixedRewardRisk, out var target))
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.InvalidTarget);
                continue;
            }

            var fingerprint = BuildFingerprint(
                strategyCode,
                symbolId,
                timeframe,
                TradeDirection.Long,
                brokenLevel.Value,
                breakoutIndex,
                retestIndex.Value,
                candles);

            if (seenFingerprints.Contains(fingerprint))
            {
                return (null, MomoAdaptiveMtfRejectionCodes.DuplicateSetup);
            }

            var strengthBreakdown = BuildStrengthBreakdown(
                TradeDirection.Long,
                htfFast,
                htfSlow,
                htfSlope,
                htfClose,
                execEmaFast,
                execEmaSlow,
                volRatio,
                settings,
                breakoutDistance,
                requiredDistance,
                histogram,
                previousHistogram,
                brokenLevel.Value,
                retestLow,
                confirmationAtrFast);

            var rawStrength = strengthBreakdown.Total;
            if (rawStrength < settings.MinStrength)
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.StrengthBelowMinimum);
                continue;
            }

            var strength = ConfidenceScoreNormalizer.Normalize(rawStrength);

            return (new MomoAdaptiveMtfCandidate
            {
                Direction = TradeDirection.Long,
                EntryPrice = entry,
                StopLoss = stop,
                TakeProfit = target,
                Strength = strength,
                Reason = "Long MTF trend breakout retest confirmed.",
                SetupFingerprint = fingerprint,
                StrengthBreakdown = strengthBreakdown,
                Setup = new
                {
                    setupType = "MtfTrendBreakoutRetest",
                    direction = "Long",
                    brokenLevel = brokenLevel.Value,
                    breakoutTimeUtc = candles[breakoutIndex].OpenTimeUtc,
                    retestTimeUtc = candles[retestIndex.Value].OpenTimeUtc,
                    confirmationTimeUtc = candles[currentIndex].OpenTimeUtc,
                    breakoutIndex,
                    retestIndex,
                    confirmationIndex = currentIndex,
                    adaptiveBuffer,
                    volRatio,
                    breakoutAtrFast,
                    breakoutAtrSlow,
                    retestAtrFast = retestEventAtrFast,
                    confirmationAtrFast,
                    retestExtreme = retestLow,
                    stopBufferAtr = settings.StopBufferAtr
                }
            }, MomoAdaptiveMtfRejectionCodes.EntryConfirmed);
        }

        return (null, bestReason);
    }

    private static (MomoAdaptiveMtfCandidate? Candidate, string Reason) TryBuildShortCandidate(
        IReadOnlyList<Candle> candles,
        int currentIndex,
        MomoAdaptiveMtfParameters settings,
        IReadOnlySet<string> seenFingerprints,
        string strategyCode,
        long symbolId,
        string timeframe,
        decimal htfFast,
        decimal htfSlow,
        decimal htfSlope,
        decimal htfClose,
        decimal execEmaFast,
        decimal execEmaSlow,
        decimal confirmationAtrFast,
        decimal histogram,
        decimal previousHistogram,
        decimal[] ltfAtrFast,
        decimal[] ltfAtrSlow)
    {
        if (htfFast >= htfSlow)
        {
            return (null, MomoAdaptiveMtfRejectionCodes.HtfTrendNotAligned);
        }

        if (htfSlope >= 0m)
        {
            return (null, MomoAdaptiveMtfRejectionCodes.HtfSlopeNotAligned);
        }

        if (htfClose >= htfFast)
        {
            return (null, MomoAdaptiveMtfRejectionCodes.HtfTrendNotAligned);
        }

        if (execEmaFast >= execEmaSlow)
        {
            return (null, MomoAdaptiveMtfRejectionCodes.ExecutionTrendNotAligned);
        }

        if (histogram >= 0m || (settings.RequireHistogramExpansion && histogram >= previousHistogram))
        {
            return (null, MomoAdaptiveMtfRejectionCodes.MomentumNotConfirmed);
        }

        var minBreakoutIndex = Math.Max(settings.BreakoutLookback, settings.SlowAtrPeriod);
        var bestReason = MomoAdaptiveMtfRejectionCodes.NoBreakout;

        for (var breakoutIndex = currentIndex - 1; breakoutIndex >= minBreakoutIndex; breakoutIndex--)
        {
            if (!TryGetAtr(ltfAtrFast, breakoutIndex, settings.FastAtrPeriod, out var breakoutAtrFast) ||
                !TryGetAtr(ltfAtrSlow, breakoutIndex, settings.SlowAtrPeriod, out var breakoutAtrSlow))
            {
                continue;
            }

            if (breakoutAtrSlow <= 0m)
            {
                continue;
            }

            var volRatio = breakoutAtrFast / breakoutAtrSlow;

            if (volRatio < settings.MinVolatilityRatio)
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.VolatilityTooLow);
                continue;
            }

            if (volRatio > settings.MaxVolatilityRatio)
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.VolatilityTooHigh);
                continue;
            }

            var adaptiveBuffer = ComputeAdaptiveBuffer(settings, volRatio);

            var brokenLevel = ComputeRangeLow(candles, breakoutIndex - settings.BreakoutLookback, breakoutIndex - 1);
            if (!brokenLevel.HasValue)
            {
                continue;
            }

            var breakoutCandle = candles[breakoutIndex];
            if (breakoutCandle.Close >= brokenLevel.Value)
            {
                continue;
            }

            var breakoutDistance = brokenLevel.Value - breakoutCandle.Close;
            var requiredDistance = adaptiveBuffer * breakoutAtrFast;
            if (breakoutDistance < requiredDistance)
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.BreakoutBufferNotMet);
                continue;
            }

            var retestSearchEnd = Math.Min(currentIndex, breakoutIndex + settings.MaxRetestBars);
            int? retestIndex = null;
            var retestTouched = false;
            var retestInvalidated = false;
            decimal retestHigh = decimal.MinValue;

            for (var i = breakoutIndex + 1; i <= retestSearchEnd; i++)
            {
                if (!TryGetAtr(ltfAtrFast, i, settings.FastAtrPeriod, out var retestAtr))
                {
                    continue;
                }

                var candle = candles[i];
                if (IsRetestInvalidatedShort(candle, brokenLevel.Value, settings.RetestToleranceAtr, retestAtr))
                {
                    bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.RetestInvalidated);
                    retestInvalidated = true;
                    retestIndex = null;
                    break;
                }

                if (!retestTouched
                    && IsShortRetestTouch(candle, brokenLevel.Value, settings.RetestToleranceAtr, retestAtr))
                {
                    retestTouched = true;
                    retestIndex = i;
                }

                if (retestTouched)
                {
                    retestHigh = Math.Max(retestHigh, candle.High);
                }
            }

            if (retestInvalidated)
            {
                continue;
            }

            if (retestIndex is null)
            {
                if (currentIndex > breakoutIndex + settings.MaxRetestBars)
                {
                    bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.RetestExpired);
                }
                else
                {
                    bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.WaitingForRetest);
                }

                continue;
            }

            if (!TryGetAtr(ltfAtrFast, retestIndex.Value, settings.FastAtrPeriod, out var retestEventAtrFast))
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable);
                continue;
            }

            if (currentIndex > breakoutIndex + settings.MaxRetestBars)
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.RetestExpired);
                continue;
            }

            if (!IsShortConfirmation(candles[currentIndex], brokenLevel.Value))
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.WaitingForRetest);
                continue;
            }

            var entry = candles[currentIndex].Close;
            if (brokenLevel.Value - entry > settings.MaxBreakoutChaseAtr * confirmationAtrFast)
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.BreakoutOverextended);
                continue;
            }

            var stop = retestHigh + (settings.StopBufferAtr * confirmationAtrFast);
            if (stop <= entry || stop <= 0m || entry <= 0m)
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.InvalidStop);
                continue;
            }

            var risk = stop - entry;
            if (!TryComputeShortTarget(entry, risk, settings.FixedRewardRisk, out var target))
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.InvalidTarget);
                continue;
            }

            var fingerprint = BuildFingerprint(
                strategyCode,
                symbolId,
                timeframe,
                TradeDirection.Short,
                brokenLevel.Value,
                breakoutIndex,
                retestIndex.Value,
                candles);

            if (seenFingerprints.Contains(fingerprint))
            {
                return (null, MomoAdaptiveMtfRejectionCodes.DuplicateSetup);
            }

            var strengthBreakdown = BuildStrengthBreakdown(
                TradeDirection.Short,
                htfFast,
                htfSlow,
                htfSlope,
                htfClose,
                execEmaFast,
                execEmaSlow,
                volRatio,
                settings,
                breakoutDistance,
                requiredDistance,
                histogram,
                previousHistogram,
                brokenLevel.Value,
                retestHigh,
                confirmationAtrFast);

            var rawStrength = strengthBreakdown.Total;
            if (rawStrength < settings.MinStrength)
            {
                bestReason = PickCloserReason(bestReason, MomoAdaptiveMtfRejectionCodes.StrengthBelowMinimum);
                continue;
            }

            var strength = ConfidenceScoreNormalizer.Normalize(rawStrength);

            return (new MomoAdaptiveMtfCandidate
            {
                Direction = TradeDirection.Short,
                EntryPrice = entry,
                StopLoss = stop,
                TakeProfit = target,
                Strength = strength,
                Reason = "Short MTF trend breakout retest confirmed.",
                SetupFingerprint = fingerprint,
                StrengthBreakdown = strengthBreakdown,
                Setup = new
                {
                    setupType = "MtfTrendBreakoutRetest",
                    direction = "Short",
                    brokenLevel = brokenLevel.Value,
                    breakoutTimeUtc = candles[breakoutIndex].OpenTimeUtc,
                    retestTimeUtc = candles[retestIndex.Value].OpenTimeUtc,
                    confirmationTimeUtc = candles[currentIndex].OpenTimeUtc,
                    breakoutIndex,
                    retestIndex,
                    confirmationIndex = currentIndex,
                    adaptiveBuffer,
                    volRatio,
                    breakoutAtrFast,
                    breakoutAtrSlow,
                    retestAtrFast = retestEventAtrFast,
                    confirmationAtrFast,
                    retestExtreme = retestHigh,
                    stopBufferAtr = settings.StopBufferAtr
                }
            }, MomoAdaptiveMtfRejectionCodes.EntryConfirmed);
        }

        return (null, bestReason);
    }

    public sealed class StrengthBreakdownResult
    {
        public required decimal HtfAlignment { get; init; }
        public required decimal ExecutionTrend { get; init; }
        public required decimal VolatilityQuality { get; init; }
        public required decimal BreakoutQuality { get; init; }
        public required decimal Momentum { get; init; }
        public required decimal RetestQuality { get; init; }
        public decimal Total =>
            Clamp(
                (HtfAlignment + ExecutionTrend + VolatilityQuality + BreakoutQuality + Momentum + RetestQuality) / 6m,
                0m,
                100m);
    }

    private static StrengthBreakdownResult BuildStrengthBreakdown(
        TradeDirection direction,
        decimal htfFast,
        decimal htfSlow,
        decimal htfSlope,
        decimal htfClose,
        decimal execEmaFast,
        decimal execEmaSlow,
        decimal volRatio,
        MomoAdaptiveMtfParameters settings,
        decimal breakoutDistance,
        decimal requiredDistance,
        decimal histogram,
        decimal previousHistogram,
        decimal brokenLevel,
        decimal retestExtreme,
        decimal atrFast)
    {
        var htfSpread = htfSlow != 0m ? Math.Abs(htfFast - htfSlow) / Math.Abs(htfSlow) * 100m : 0m;
        var htfSlopeScore = htfFast != 0m ? Math.Abs(htfSlope) / Math.Abs(htfFast) * 100m : 0m;
        var htfCloseDistance = htfFast != 0m ? Math.Abs(htfClose - htfFast) / Math.Abs(htfFast) * 100m : 0m;
        // Calibration (23.1A1C): percent-of-price EMA/slope/close distances are typically 0.2–3%.
        // Prior ×2/×3/×8 left realistic aligned trends well below minStrength=70; scale so a clear
        // multi-percent HTF alignment and ~0.5%+ LTF EMA spread can contribute meaningfully without a floor.
        var htfAlignment = Clamp((htfSpread * 25m) + (htfSlopeScore * 40m) + (htfCloseDistance * 20m), 0m, 100m);

        var execSpread = execEmaSlow != 0m ? Math.Abs(execEmaFast - execEmaSlow) / Math.Abs(execEmaSlow) * 100m : 0m;
        var executionTrend = Clamp(execSpread * 80m, 0m, 100m);

        var midVol = (settings.MinVolatilityRatio + settings.MaxVolatilityRatio) / 2m;
        var volSpan = settings.MaxVolatilityRatio - settings.MinVolatilityRatio;
        var volDistance = volSpan > 0m ? Math.Abs(volRatio - midVol) / volSpan : 0m;
        var volatilityQuality = Clamp((1m - volDistance) * 100m, 0m, 100m);

        var breakoutQuality = requiredDistance > 0m
            ? Clamp((breakoutDistance / requiredDistance) * 50m, 0m, 100m)
            : 0m;

        var histDelta = Math.Abs(histogram - previousHistogram);
        var histMagnitude = Math.Abs(histogram);
        var histMagnitudeNormalized = atrFast > 0m ? (histMagnitude / atrFast) : 0m;
        var histDeltaNormalized = atrFast > 0m ? (histDelta / atrFast) : 0m;
        // MACD histogram is typically a small fraction of ATR; prior ×25/×30 under-scored momentum.
        var momentum = Clamp((histMagnitudeNormalized * 400m) + (histDeltaNormalized * 500m), 0m, 100m);

        var retestDistance = Math.Abs(retestExtreme - brokenLevel);
        var tolerance = settings.RetestToleranceAtr * atrFast;
        var retestQuality = tolerance > 0m
            ? Clamp((1m - (retestDistance / tolerance)) * 100m, 0m, 100m)
            : 0m;

        return new StrengthBreakdownResult
        {
            HtfAlignment = htfAlignment,
            ExecutionTrend = executionTrend,
            VolatilityQuality = volatilityQuality,
            BreakoutQuality = breakoutQuality,
            Momentum = momentum,
            RetestQuality = retestQuality
        };
    }

    private static decimal ComputeAdaptiveBuffer(MomoAdaptiveMtfParameters settings, decimal volRatio)
    {
        var raw = settings.BaseBreakoutBufferAtr + ((volRatio - 1m) * settings.VolatilitySensitivity);
        return Clamp(raw, settings.MinBreakoutBufferAtr, settings.MaxBreakoutBufferAtr);
    }

    /// <summary>
    /// Long target is entry + risk×RR. With validated positive risk and RR the geometric
    /// target &lt;= entry branch is unreachable; overflow maps to InvalidTarget.
    /// </summary>
    private static bool TryComputeLongTarget(decimal entry, decimal risk, decimal rewardRisk, out decimal target)
    {
        target = 0m;
        if (risk <= 0m || rewardRisk <= 0m)
        {
            return false;
        }

        try
        {
            var reward = risk * rewardRisk;
            target = entry + reward;
            // Geometric invariant under positive risk/RR: target > entry. Overflow-safe path only.
            return target > entry;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// Short target is entry − risk×RR. Large positive RR can yield target &lt;= 0 (InvalidTarget).
    /// Extreme values must not throw — return false for a stable diagnostic.
    /// </summary>
    private static bool TryComputeShortTarget(decimal entry, decimal risk, decimal rewardRisk, out decimal target)
    {
        target = 0m;
        if (risk <= 0m || rewardRisk <= 0m || entry <= 0m)
        {
            return false;
        }

        try
        {
            var reward = risk * rewardRisk;
            if (reward >= entry)
            {
                return false;
            }

            target = entry - reward;
            return target > 0m && target < entry;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// Public Wilder ATR series for formula proofs (event-time breakout/retest ATR).
    /// </summary>
    public static decimal[] ComputeWilderAtrSeries(IReadOnlyList<Candle> candles, int period) =>
        ComputeWilderAtr(candles, period);

    private static int ComputeMinLtfBars(MomoAdaptiveMtfParameters settings) =>
        Math.Max(settings.SlowAtrPeriod, settings.LtfSlowEmaPeriod)
        + settings.BreakoutLookback
        + settings.MaxRetestBars
        + settings.MacdSlow
        + settings.MacdSignal
        + 2;

    private static bool IsLongRetestTouch(Candle candle, decimal level, decimal toleranceAtr, decimal atr) =>
        candle.Low <= level + (toleranceAtr * atr) && candle.Low >= level - (toleranceAtr * atr);

    private static bool IsShortRetestTouch(Candle candle, decimal level, decimal toleranceAtr, decimal atr) =>
        candle.High >= level - (toleranceAtr * atr) && candle.High <= level + (toleranceAtr * atr);

    private static bool IsRetestInvalidatedLong(Candle candle, decimal level, decimal toleranceAtr, decimal atr) =>
        candle.Close < level - (toleranceAtr * atr);

    private static bool IsRetestInvalidatedShort(Candle candle, decimal level, decimal toleranceAtr, decimal atr) =>
        candle.Close > level + (toleranceAtr * atr);

    private static bool IsLongConfirmation(Candle candle, decimal level) =>
        candle.Close > level && StrategyCandleHelper.IsBullish(candle);

    private static bool IsShortConfirmation(Candle candle, decimal level) =>
        candle.Close < level && StrategyCandleHelper.IsBearish(candle);

    public static string BuildFingerprint(
        string strategyCode,
        long symbolId,
        string timeframe,
        TradeDirection direction,
        decimal brokenLevel,
        int breakoutIndex,
        int retestIndex,
        IReadOnlyList<Candle> candles)
    {
        var level = Math.Round(brokenLevel, 8);
        var breakTs = candles[breakoutIndex].OpenTimeUtc.ToString("yyyyMMdd'T'HHmm");
        var retestTs = candles[retestIndex].OpenTimeUtc.ToString("yyyyMMdd'T'HHmm");
        var raw = $"{strategyCode}|v{StrategyVersion}|{symbolId}|{timeframe}|{direction}|LEVEL_{level}|BREAK_{breakTs}|RETEST_{retestTs}";
        return SetupFingerprintHasher.Hash(raw);
    }

    private static decimal? ComputeRangeHigh(IReadOnlyList<Candle> candles, int startIndex, int endIndex)
    {
        if (startIndex < 0 || endIndex < startIndex)
        {
            return null;
        }

        decimal? high = null;
        for (var i = startIndex; i <= endIndex; i++)
        {
            high = high.HasValue ? Math.Max(high.Value, candles[i].High) : candles[i].High;
        }

        return high;
    }

    private static decimal? ComputeRangeLow(IReadOnlyList<Candle> candles, int startIndex, int endIndex)
    {
        if (startIndex < 0 || endIndex < startIndex)
        {
            return null;
        }

        decimal? low = null;
        for (var i = startIndex; i <= endIndex; i++)
        {
            low = low.HasValue ? Math.Min(low.Value, candles[i].Low) : candles[i].Low;
        }

        return low;
    }

    internal static decimal[] ComputeEma(IReadOnlyList<decimal> values, int period)
    {
        var result = new decimal[values.Count];
        if (values.Count < period || period <= 0)
        {
            return result;
        }

        decimal sum = 0m;
        for (var i = 0; i < period; i++)
        {
            sum += values[i];
        }

        var multiplier = 2m / (period + 1m);
        result[period - 1] = sum / period;
        for (var i = period; i < values.Count; i++)
        {
            result[i] = ((values[i] - result[i - 1]) * multiplier) + result[i - 1];
        }

        return result;
    }

    private static decimal[] ComputeWilderAtr(IReadOnlyList<Candle> candles, int period)
    {
        var result = new decimal[candles.Count];
        if (candles.Count <= period || period <= 0)
        {
            return result;
        }

        decimal sum = 0m;
        for (var i = 1; i <= period; i++)
        {
            sum += TrueRange(candles, i);
        }

        result[period] = sum / period;
        for (var i = period + 1; i < candles.Count; i++)
        {
            result[i] = ((result[i - 1] * (period - 1)) + TrueRange(candles, i)) / period;
        }

        return result;
    }

    private static decimal TrueRange(IReadOnlyList<Candle> candles, int index)
    {
        var candle = candles[index];
        var previousClose = candles[index - 1].Close;
        return Math.Max(candle.High - candle.Low, Math.Max(Math.Abs(candle.High - previousClose), Math.Abs(candle.Low - previousClose)));
    }

    internal static (decimal[] MacdLine, decimal[] SignalLine, decimal[] Histogram) ComputeMacd(
        IReadOnlyList<decimal> closes,
        int fastPeriod,
        int slowPeriod,
        int signalPeriod)
    {
        var macdLine = new decimal[closes.Count];
        var signalLine = new decimal[closes.Count];
        var histogram = new decimal[closes.Count];

        if (closes.Count < slowPeriod + signalPeriod)
        {
            return (macdLine, signalLine, histogram);
        }

        var fastEma = ComputeEma(closes, fastPeriod);
        var slowEma = ComputeEma(closes, slowPeriod);
        for (var i = 0; i < closes.Count; i++)
        {
            if (i >= slowPeriod - 1 && fastEma[i] != 0m && slowEma[i] != 0m)
            {
                macdLine[i] = fastEma[i] - slowEma[i];
            }
        }

        var macdValues = new List<decimal>();
        var macdStartIndex = slowPeriod - 1;
        for (var i = macdStartIndex; i < closes.Count; i++)
        {
            macdValues.Add(macdLine[i]);
        }

        var signalValues = ComputeEma(macdValues, signalPeriod);
        for (var i = 0; i < signalValues.Length; i++)
        {
            var targetIndex = macdStartIndex + i;
            signalLine[targetIndex] = signalValues[i];
            histogram[targetIndex] = macdLine[targetIndex] - signalLine[targetIndex];
        }

        return (macdLine, signalLine, histogram);
    }

    internal static bool TryGetEma(decimal[] values, int index, int period, out decimal value)
    {
        if (index < period - 1 || index >= values.Length)
        {
            value = 0m;
            return false;
        }

        value = values[index];
        return true;
    }

    private static bool TryGetAtr(decimal[] values, int index, int period, out decimal value)
    {
        if (index < period || index >= values.Length)
        {
            value = 0m;
            return false;
        }

        value = values[index];
        return true;
    }

    internal static bool TryGetMacdHistogram(
        decimal[] histogram,
        int index,
        MomoAdaptiveMtfParameters settings,
        out decimal value)
    {
        var minIndex = settings.MacdSlow + settings.MacdSignal - 2;
        if (index < minIndex || index >= histogram.Length)
        {
            value = 0m;
            return false;
        }

        value = histogram[index];
        return true;
    }

    private static decimal Clamp(decimal value, decimal min, decimal max) =>
        Math.Min(max, Math.Max(min, value));

    private static string PickCloserReason(string current, string candidate)
    {
        var priority = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [MomoAdaptiveMtfRejectionCodes.EntryConfirmed] = 0,
            [MomoAdaptiveMtfRejectionCodes.DuplicateSetup] = 1,
            [MomoAdaptiveMtfRejectionCodes.InvalidStop] = 2,
            [MomoAdaptiveMtfRejectionCodes.InvalidTarget] = 3,
            [MomoAdaptiveMtfRejectionCodes.BreakoutOverextended] = 4,
            [MomoAdaptiveMtfRejectionCodes.StrengthBelowMinimum] = 5,
            [MomoAdaptiveMtfRejectionCodes.RetestInvalidated] = 6,
            [MomoAdaptiveMtfRejectionCodes.WaitingForRetest] = 7,
            [MomoAdaptiveMtfRejectionCodes.RetestExpired] = 8,
            [MomoAdaptiveMtfRejectionCodes.BreakoutBufferNotMet] = 9,
            [MomoAdaptiveMtfRejectionCodes.VolatilityTooLow] = 10,
            [MomoAdaptiveMtfRejectionCodes.VolatilityTooHigh] = 11,
            [MomoAdaptiveMtfRejectionCodes.NoBreakout] = 12,
            [MomoAdaptiveMtfRejectionCodes.MomentumNotConfirmed] = 13,
            [MomoAdaptiveMtfRejectionCodes.ExecutionTrendNotAligned] = 14,
            [MomoAdaptiveMtfRejectionCodes.HtfSlopeNotAligned] = 15,
            [MomoAdaptiveMtfRejectionCodes.HtfTrendNotAligned] = 16,
            [MomoAdaptiveMtfRejectionCodes.UnsupportedRegime] = 17,
            [MomoAdaptiveMtfRejectionCodes.InvalidParameters] = 18,
            [MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable] = 19
        };

        if (string.IsNullOrEmpty(current))
        {
            return candidate;
        }

        priority.TryGetValue(current, out var currentRank);
        priority.TryGetValue(candidate, out var candidateRank);
        return candidateRank < currentRank ? candidate : current;
    }
}
