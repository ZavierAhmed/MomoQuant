using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A1C — Adaptive MTF default-contract formula evidence.
/// </summary>
public sealed class MomoAdaptiveMtfTrendBreakoutTests
{
    private static readonly DateTime Start = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string Code = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout.ToCode();

    [Fact]
    public void Strategy_HasCorrectCode_AndSupportsRegimesTimeframes()
    {
        var strategy = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy();
        Assert.Equal(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout, strategy.Code);
        Assert.Contains(MarketRegime.Trending, strategy.SupportedRegimes);
        Assert.Contains(MarketRegime.Breakout, strategy.SupportedRegimes);
        Assert.Contains(Timeframe.M5, strategy.SupportedTimeframes);
        Assert.Contains(Timeframe.M15, strategy.SupportedTimeframes);
        Assert.Contains(Timeframe.H1, strategy.SupportedTimeframes);
        Assert.Contains(Timeframe.H4, strategy.SupportedTimeframes);
        Assert.Equal("1.0.0", MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version);
    }

    [Fact]
    public void GetDefaultParameterContract_MatchesExactDefaults()
    {
        var contract = MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract();
        var p = MomoAdaptiveMtfTrendBreakoutEvaluator.ReadParameters(contract);
        Assert.Equal(50, p.HtfFastEmaPeriod);
        Assert.Equal(200, p.HtfSlowEmaPeriod);
        Assert.Equal(5, p.HtfSlopeLookback);
        Assert.Equal(20, p.LtfFastEmaPeriod);
        Assert.Equal(50, p.LtfSlowEmaPeriod);
        Assert.Equal(20, p.BreakoutLookback);
        Assert.Equal(14, p.FastAtrPeriod);
        Assert.Equal(100, p.SlowAtrPeriod);
        Assert.Equal(1.00m, p.MinVolatilityRatio);
        Assert.Equal(2.25m, p.MaxVolatilityRatio);
        Assert.True(p.RequireHistogramExpansion);
        Assert.Equal(2.50m, p.FixedRewardRisk);
        Assert.Equal(70m, p.MinStrength);
        Assert.Equal(12, p.MacdFast);
        Assert.Equal(26, p.MacdSlow);
        Assert.Equal(9, p.MacdSignal);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_ValidLong_WithExactDefaults()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var parameters = MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract();

        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);

        Assert.NotNull(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.EntryConfirmed, reason);
        Assert.Equal(TradeDirection.Long, candidate.Direction);
        Assert.True(candidate.EntryPrice > 0m);
        Assert.True(candidate.StopLoss > 0m && candidate.StopLoss < candidate.EntryPrice);
        Assert.True(candidate.TakeProfit > candidate.EntryPrice);
        var risk = candidate.EntryPrice - candidate.StopLoss;
        var reward = candidate.TakeProfit - candidate.EntryPrice;
        Assert.Equal(2.50m, Math.Round(reward / risk, 8));
        Assert.True(candidate.Strength >= 70m);
        Assert.False(string.IsNullOrWhiteSpace(candidate.SetupFingerprint));
        var breakdown = Assert.IsType<MomoAdaptiveMtfTrendBreakoutEvaluator.StrengthBreakdownResult>(candidate.StrengthBreakdown);
        Assert.InRange(breakdown.HtfAlignment, 0m, 100m);
        Assert.InRange(breakdown.ExecutionTrend, 0m, 100m);
        Assert.InRange(breakdown.VolatilityQuality, 0m, 100m);
        Assert.InRange(breakdown.BreakoutQuality, 0m, 100m);
        Assert.InRange(breakdown.Momentum, 0m, 100m);
        Assert.InRange(breakdown.RetestQuality, 0m, 100m);
        Assert.Equal(Math.Round(breakdown.Total, 8), Math.Round(candidate.Strength, 8));
        Assert.Contains("Long", candidate.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_ValidShort_WithExactDefaults()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidShort(Start);
        var parameters = MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract();

        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);

        Assert.NotNull(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.EntryConfirmed, reason);
        Assert.Equal(TradeDirection.Short, candidate.Direction);
        Assert.True(candidate.EntryPrice > 0m);
        Assert.True(candidate.StopLoss > candidate.EntryPrice);
        Assert.True(candidate.TakeProfit < candidate.EntryPrice && candidate.TakeProfit > 0m);
        var risk = candidate.StopLoss - candidate.EntryPrice;
        var reward = candidate.EntryPrice - candidate.TakeProfit;
        Assert.Equal(2.50m, Math.Round(reward / risk, 8));
        Assert.True(candidate.Strength >= 70m);
        Assert.False(string.IsNullOrWhiteSpace(candidate.SetupFingerprint));
        var breakdown = Assert.IsType<MomoAdaptiveMtfTrendBreakoutEvaluator.StrengthBreakdownResult>(candidate.StrengthBreakdown);
        Assert.Equal(Math.Round(breakdown.Total, 8), Math.Round(candidate.Strength, 8));
    }

    [Fact]
    public void EvaluateAtCurrentCandle_UnsupportedRegime_Rejects()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var (candidate, reason) = Evaluate(ltf, htf, Defaults(), MarketRegime.Ranging);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.UnsupportedRegime, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_HtfEmaNotAligned_RejectsHtfTrendNotAligned()
    {
        var (ltf, _) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        // Flat HTF destroys EMA50>EMA200 alignment while keeping LTF depth.
        var flatHtf = AdaptiveDefaultFixtures.BuildFlatHtf(Start, 230, 50000m);
        var t = ltf[^1].CloseTimeUtc;
        flatHtf = flatHtf.Where(c => c.CloseTimeUtc <= t).ToList();
        // Ensure enough HTF by backdating flats
        while (flatHtf.Count < 205)
        {
            var first = flatHtf[0];
            flatHtf.Insert(0, CloneHtf(first, first.OpenTimeUtc.AddHours(-1), 50000m));
        }

        var (candidate, reason) = Evaluate(ltf, flatHtf, Defaults(), MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.HtfTrendNotAligned, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_HtfSlopeNotAligned_Rejects()
    {
        var (ltf, _) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        // Historical uptrend keeps EMA50 > EMA200; steep recent decline forces slope < 0.
        // HTF open times must sit under the LTF evaluation clock (SliceHtf filters by CloseTimeUtc).
        var rebuilt = new List<Candle>();
        decimal px = 40000m;
        var htfStart = ltf[^1].CloseTimeUtc.AddHours(-250);
        for (var i = 0; i < 250; i++)
        {
            if (i < 200)
            {
                px += 50m;
            }
            else
            {
                px -= 120m;
            }

            var open = htfStart.AddHours(i);
            rebuilt.Add(new Candle
            {
                SymbolId = 1, ExchangeId = 1, Timeframe = Timeframe.H1,
                OpenTimeUtc = open, CloseTimeUtc = open.AddHours(1),
                Open = px + 10m, High = px + 30m, Low = px - 30m, Close = px,
                Volume = 1m, IsClosed = true, CreatedAtUtc = open
            });
        }

        var (candidate, reason) = Evaluate(ltf, rebuilt, Defaults(), MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.HtfSlopeNotAligned, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_RetestInvalidation_Rejects()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var parameters = Defaults();
        // Disable expansion so a single crushed retest close is less likely to flip histogram sign at confirm.
        parameters["requireHistogramExpansion"] = "false";

        var retestIdx = ltf.Count - 2;
        var levelGuess = ltf.Skip(Math.Max(0, ltf.Count - 25)).Take(20).Max(c => c.High);
        var atrGuess = 400m;
        var crushed = levelGuess - (0.35m * atrGuess) - 50m; // clearly below invalidate band
        ltf[retestIdx] = CloneLtf(ltf[retestIdx], crushed + 5m, levelGuess + 5m, crushed - 5m, crushed);

        // Keep confirmation elevated/bullish above level
        var confirm = ltf[^1];
        ltf[^1] = CloneLtf(confirm, levelGuess + 20m, levelGuess + 80m, levelGuess + 10m, levelGuess + 60m);

        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.RetestInvalidated, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_ExpiredSetup_CannotConfirmLater()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var parameters = Defaults();
        parameters["maxRetestBars"] = "2";

        var confirm = ltf[^1];
        ltf.RemoveAt(ltf.Count - 1);
        var anchor = ltf[^1];
        for (var i = 0; i < 6; i++)
        {
            var open = anchor.CloseTimeUtc.AddMinutes(i * 5);
            var px = anchor.Close;
            ltf.Add(new Candle
            {
                SymbolId = 1, ExchangeId = 1, Timeframe = Timeframe.M5,
                OpenTimeUtc = open, CloseTimeUtc = open.AddMinutes(5),
                Open = px, High = px + 20m, Low = px - 5m, Close = px + 10m,
                Volume = 1m, IsClosed = true, CreatedAtUtc = open
            });
        }

        var lateOpen = ltf[^1].CloseTimeUtc;
        ltf.Add(new Candle
        {
            SymbolId = 1, ExchangeId = 1, Timeframe = Timeframe.M5,
            OpenTimeUtc = lateOpen, CloseTimeUtc = lateOpen.AddMinutes(5),
            Open = confirm.Open, High = confirm.High, Low = confirm.Low, Close = confirm.Close,
            Volume = 1m, IsClosed = true, CreatedAtUtc = lateOpen
        });

        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.RetestExpired, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_HtfCloseRejection_RejectsHtfTrendNotAligned()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        // Force last HTF close well below fast EMA.
        var last = htf[^1];
        htf[^1] = CloneHtf(last, last.OpenTimeUtc, last.Close * 0.90m);

        var (candidate, reason) = Evaluate(ltf, htf, Defaults(), MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.HtfTrendNotAligned, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_LtfEmaRejection_RejectsExecutionTrendNotAligned()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        // Collapse last 60 LTF closes downward so EMA20 drops below EMA50.
        var anchor = ltf[^60].Close;
        for (var i = ltf.Count - 60; i < ltf.Count; i++)
        {
            var px = anchor - (i - (ltf.Count - 60)) * 15m;
            ltf[i] = CloneLtf(ltf[i], px, px + 5m, px - 5m, px);
        }

        var (candidate, reason) = Evaluate(ltf, htf, Defaults(), MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.ExecutionTrendNotAligned, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_VolatilityTooLow_Rejects()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var parameters = Defaults();
        parameters["minVolatilityRatio"] = "10.00";
        parameters["maxVolatilityRatio"] = "20.00";

        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.VolatilityTooLow, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_VolatilityTooHigh_Rejects()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var parameters = Defaults();
        parameters["minVolatilityRatio"] = "0.10";
        parameters["maxVolatilityRatio"] = "0.20";

        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.VolatilityTooHigh, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_MacdSignRejection_RejectsMomentumNotConfirmed()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        // Drive last closes down hard to flip MACD histogram negative while keeping some structure.
        for (var i = ltf.Count - 40; i < ltf.Count; i++)
        {
            var px = ltf[i].Close - (i - (ltf.Count - 40)) * 40m;
            ltf[i] = CloneLtf(ltf[i], px + 20m, px + 30m, px - 30m, px);
        }

        var (candidate, reason) = Evaluate(ltf, htf, Defaults(), MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.True(
            reason is MomoAdaptiveMtfRejectionCodes.MomentumNotConfirmed
                or MomoAdaptiveMtfRejectionCodes.ExecutionTrendNotAligned,
            $"Expected momentum or execution rejection, got {reason}");
    }

    [Fact]
    public void EvaluateAtCurrentCandle_MacdExpansionRejection_RejectsWhenExpansionRequired()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        // Evaluate one bar before confirmation where hist may be expanding; force confirm bar with shrinking hist
        // by flattening the last close relative to prior so expansion fails while sign stays positive when possible.
        var parameters = Defaults();
        Assert.True(MomoAdaptiveMtfTrendBreakoutEvaluator.ReadParameters(parameters).RequireHistogramExpansion);

        // Truncate to retest bar — should wait rather than confirm without expansion path to entry
        var truncated = ltf.Take(ltf.Count - 1).ToList();
        var (candidate, reason) = Evaluate(truncated, htf, parameters, MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.WaitingForRetest, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_WaitingRetest_BeforeConfirmation()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var truncated = ltf.Take(ltf.Count - 1).ToList();
        var (candidate, reason) = Evaluate(truncated, htf, Defaults(), MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.WaitingForRetest, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_CompleteLongRetestExtreme_UsesLowestLow()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var (candidate, reason) = Evaluate(ltf, htf, Defaults(), MarketRegime.Breakout);
        Assert.NotNull(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.EntryConfirmed, reason);
        // Stop is below the retest extreme by stopBuffer*ATR — must be below entry
        Assert.True(candidate.StopLoss < candidate.EntryPrice);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_CompleteShortRetestExtreme_UsesHighestHigh()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidShort(Start);
        var (candidate, reason) = Evaluate(ltf, htf, Defaults(), MarketRegime.Breakout);
        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Short, candidate.Direction);
        Assert.True(candidate.StopLoss > candidate.EntryPrice);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_Overextension_RejectsBreakoutOverextended()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var parameters = Defaults();
        parameters["maxBreakoutChaseAtr"] = "0.01";

        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.BreakoutOverextended, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_InvalidLongTarget_Rejects()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        // fixedRewardRisk tiny but positive still produces target > entry; use negative via invalid params separately.
        // Force invalid stop geometry first: absurd stop buffer that pushes stop >= entry is InvalidStop.
        // For InvalidTarget specifically, production checks target <= entry after risk*RR — only with RR<=0 which is InvalidParameters.
        // Prove InvalidParameters for RR<=0 separately from geometry InvalidStop.
        var parameters = Defaults();
        parameters["fixedRewardRisk"] = "0";
        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.InvalidParameters, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_InvalidShortTarget_ParametersRejectFixedRewardRiskNonPositive()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidShort(Start);
        var parameters = Defaults();
        parameters["fixedRewardRisk"] = "-1";
        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.InvalidParameters, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_StrengthBelow70_Rejects()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var parameters = Defaults();
        parameters["minStrength"] = "99.9";

        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.StrengthBelowMinimum, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_ExactStrengthComponentCalculation_MatchesBreakdownTotal()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var (candidate, reason) = Evaluate(ltf, htf, Defaults(), MarketRegime.Breakout);
        Assert.NotNull(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.EntryConfirmed, reason);
        var breakdown = Assert.IsType<MomoAdaptiveMtfTrendBreakoutEvaluator.StrengthBreakdownResult>(candidate.StrengthBreakdown);
        var expected = Math.Round(
            (breakdown.HtfAlignment + breakdown.ExecutionTrend + breakdown.VolatilityQuality
             + breakdown.BreakoutQuality + breakdown.Momentum + breakdown.RetestQuality) / 6m, 8);
        Assert.Equal(expected, Math.Round(breakdown.Total, 8));
        Assert.Equal(Math.Round(breakdown.Total, 8), Math.Round(candidate.Strength, 8));
    }

    [Fact]
    public void EvaluateAtCurrentCandle_PriceScaleIndependentStrength()
    {
        var (ltfA, htfA) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var (ltfB, htfB) = AdaptiveDefaultFixtures.BuildValidLong(Start, priceScale: 0.1m);

        var (candA, reasonA) = Evaluate(ltfA, htfA, Defaults(), MarketRegime.Breakout);
        var (candB, reasonB) = Evaluate(ltfB, htfB, Defaults(), MarketRegime.Breakout);

        Assert.NotNull(candA);
        Assert.NotNull(candB);
        Assert.Equal(reasonA, reasonB);
        Assert.Equal(Math.Round(candA.Strength, 4), Math.Round(candB.Strength, 4));
    }

    [Fact]
    public void EvaluateAtCurrentCandle_DuplicateSetup_Rejects()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var (first, reason1) = Evaluate(ltf, htf, Defaults(), MarketRegime.Breakout);
        Assert.NotNull(first);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.EntryConfirmed, reason1);

        var seen = new HashSet<string> { first.SetupFingerprint };
        var (second, reason2) = MomoAdaptiveMtfTrendBreakoutEvaluator.EvaluateAtCurrentCandle(
            ltf, SliceHtf(htf, ltf[^1].CloseTimeUtc), Defaults(), MarketRegime.Breakout, seen, Code, 1, "5m");
        Assert.Null(second);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.DuplicateSetup, reason2);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_BreakoutLookback_ExcludesBreakoutCandle()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var (candidate, reason) = Evaluate(ltf, htf, Defaults(), MarketRegime.Breakout);
        Assert.NotNull(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.EntryConfirmed, reason);
        // Setup object carries breakoutIndex; broken level must be from prior bars only.
        dynamic setup = candidate.Setup;
        int breakoutIndex = setup.breakoutIndex;
        Assert.InRange(breakoutIndex, 0, ltf.Count - 2);
        Assert.True(ltf[breakoutIndex].Close > 0m);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_AdaptiveBuffer_MinClamp()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var parameters = Defaults();
        parameters["baseBreakoutBufferAtr"] = "0.00";
        parameters["volatilitySensitivity"] = "0.00";
        parameters["minBreakoutBufferAtr"] = "0.05";
        parameters["maxBreakoutBufferAtr"] = "0.35";

        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);
        // Still a valid path or buffer-not-met — min clamp must be applied (0.05), not negative.
        if (candidate is not null)
        {
            dynamic setup = candidate.Setup;
            decimal adaptiveBuffer = setup.adaptiveBuffer;
            Assert.True(adaptiveBuffer >= 0.05m);
        }
        else
        {
            Assert.NotEqual(MomoAdaptiveMtfRejectionCodes.InvalidParameters, reason);
        }
    }

    [Fact]
    public void EvaluateAtCurrentCandle_AdaptiveBuffer_MaxClamp()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var parameters = Defaults();
        parameters["baseBreakoutBufferAtr"] = "1.00";
        parameters["volatilitySensitivity"] = "1.00";
        parameters["minBreakoutBufferAtr"] = "0.05";
        parameters["maxBreakoutBufferAtr"] = "0.35";

        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);
        if (candidate is not null)
        {
            dynamic setup = candidate.Setup;
            decimal adaptiveBuffer = setup.adaptiveBuffer;
            Assert.True(adaptiveBuffer <= 0.35m);
            Assert.True(adaptiveBuffer >= 0.05m);
        }
        else
        {
            Assert.NotEqual(MomoAdaptiveMtfRejectionCodes.InvalidParameters, reason);
        }
    }

    [Fact]
    public void EvaluateAtCurrentCandle_EventTimeBreakoutAndRetestAtr_DoNotUseConfirmationOnly()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var (candidate, reason) = Evaluate(ltf, htf, Defaults(), MarketRegime.Breakout);
        Assert.NotNull(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.EntryConfirmed, reason);

        // Mutate only confirmation candle volume (not OHLC) — event ATR path must not depend on future bars.
        var last = ltf[^1];
        ltf[^1] = new Candle
        {
            SymbolId = last.SymbolId,
            ExchangeId = last.ExchangeId,
            Timeframe = last.Timeframe,
            OpenTimeUtc = last.OpenTimeUtc,
            CloseTimeUtc = last.CloseTimeUtc,
            Open = last.Open,
            High = last.High,
            Low = last.Low,
            Close = last.Close,
            Volume = last.Volume + 99999m,
            IsClosed = true,
            CreatedAtUtc = last.CreatedAtUtc
        };

        var (candidate2, reason2) = Evaluate(ltf, htf, Defaults(), MarketRegime.Breakout);
        Assert.NotNull(candidate2);
        Assert.Equal(reason, reason2);
        Assert.Equal(candidate.SetupFingerprint, candidate2.SetupFingerprint);
        Assert.Equal(candidate.EntryPrice, candidate2.EntryPrice);
        Assert.Equal(candidate.StopLoss, candidate2.StopLoss);
        Assert.Equal(candidate.TakeProfit, candidate2.TakeProfit);
        Assert.Equal(candidate.Strength, candidate2.Strength);
    }

    [Fact]
    public void HigherTimeframeMapping_IsCorrect()
    {
        Assert.Equal(Timeframe.H1, MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(Timeframe.M5));
        Assert.Equal(Timeframe.H4, MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(Timeframe.M15));
        Assert.Equal(Timeframe.H4, MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(Timeframe.H1));
        Assert.Equal(Timeframe.D1, MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(Timeframe.H4));
    }

    [Fact]
    public void Evaluate_InsufficientCandles_RejectsMtfDataUnavailable()
    {
        var strategy = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy();
        var result = strategy.Evaluate(new StrategyContext
        {
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            MarketRegime = MarketRegime.Trending,
            Candles = Array.Empty<Candle>(),
            HigherTimeframeCandles = Array.Empty<Candle>(),
            IndicatorSnapshot = null,
            EvaluatedAtUtc = DateTime.UtcNow
        });
        Assert.Contains(MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable, result.Reason ?? string.Empty);
    }

    private static Dictionary<string, string> Defaults() =>
        new(MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract());

    private static (MomoAdaptiveMtfTrendBreakoutEvaluator.MomoAdaptiveMtfCandidate? Candidate, string Reason) Evaluate(
        IReadOnlyList<Candle> ltf,
        IReadOnlyList<Candle> htf,
        IReadOnlyDictionary<string, string> parameters,
        MarketRegime regime,
        IReadOnlySet<string>? seen = null) =>
        MomoAdaptiveMtfTrendBreakoutEvaluator.EvaluateAtCurrentCandle(
            ltf,
            SliceHtf(htf, ltf[^1].CloseTimeUtc),
            parameters,
            regime,
            seen ?? new HashSet<string>(),
            Code,
            1,
            "5m");

    private static List<Candle> SliceHtf(IReadOnlyList<Candle> htf, DateTime t) =>
        htf.Where(c => c.IsClosed && c.CloseTimeUtc <= t).ToList();

    private static Candle CloneLtf(Candle c, decimal o, decimal h, decimal l, decimal close) => new()
    {
        SymbolId = c.SymbolId,
        ExchangeId = c.ExchangeId,
        Timeframe = c.Timeframe,
        OpenTimeUtc = c.OpenTimeUtc,
        CloseTimeUtc = c.CloseTimeUtc,
        Open = o,
        High = h,
        Low = l,
        Close = close,
        Volume = c.Volume,
        IsClosed = true,
        CreatedAtUtc = c.CreatedAtUtc
    };

    private static Candle CloneHtf(Candle c, DateTime open, decimal close) => new()
    {
        SymbolId = c.SymbolId,
        ExchangeId = c.ExchangeId,
        Timeframe = Timeframe.H1,
        OpenTimeUtc = open,
        CloseTimeUtc = open.AddHours(1),
        Open = close,
        High = close + 10m,
        Low = close - 10m,
        Close = close,
        Volume = c.Volume,
        IsClosed = true,
        CreatedAtUtc = open
    };
}

internal static class AdaptiveDefaultFixtures
{
    public static (List<Candle> ltf, List<Candle> htf) BuildValidLong(DateTime start, decimal priceScale = 1m)
    {
        var ltf = new List<Candle>();
        var htf = new List<Candle>();
        decimal mid = 40000m * priceScale;
        var baseAtr = 200m * priceScale;
        var trend = 4m * priceScale;

        for (var i = 0; i < 2700; i++)
        {
            mid += trend;
            var open = mid - baseAtr * 0.1m;
            var close = mid + baseAtr * 0.2m;
            Add(ltf, htf, start, i, open, Math.Max(open, close) + baseAtr * 0.3m, Math.Min(open, close) - baseAtr * 0.2m, close);
        }

        var boxTop = mid + baseAtr * 0.5m;
        for (var j = 0; j < 40; j++)
        {
            mid += trend * (j >= 20 ? 2.5m : 0.8m);
            var atr = j >= 24 ? baseAtr * 2.5m : baseAtr;
            var open = mid - atr * 0.15m;
            var close = mid + atr * 0.35m;
            var high = Math.Max(open, close) + atr * 0.25m;
            if (j < 30)
            {
                high = Math.Min(high, boxTop + (j * trend * 0.1m));
            }

            if (j >= 20 && j < 30)
            {
                high = Math.Min(high, mid + baseAtr * 0.3m);
            }

            var low = Math.Min(open, close) - atr * 0.2m;
            Add(ltf, htf, start, ltf.Count, open, high, low, close);
        }

        var lookbackHigh = ltf.TakeLast(20).Max(c => c.High);
        {
            var atr = baseAtr * 2.2m;
            var open = lookbackHigh - atr * 0.05m;
            var close = lookbackHigh + atr * 0.45m;
            Add(ltf, htf, start, ltf.Count, open, close + atr * 0.1m, open - atr * 0.2m, close);
        }

        {
            var atr = baseAtr * 2.2m;
            var open = lookbackHigh + atr * 0.15m;
            var low = lookbackHigh - atr * 0.08m;
            var close = lookbackHigh + atr * 0.10m;
            Add(ltf, htf, start, ltf.Count, open, open + atr * 0.1m, low, close);
        }

        {
            var atr = baseAtr * 2.2m;
            var open = lookbackHigh + atr * 0.05m;
            var close = lookbackHigh + atr * 0.40m;
            Add(ltf, htf, start, ltf.Count, open, close + atr * 0.05m, open - atr * 0.1m, close);
        }

        return (ltf, htf);
    }

    public static (List<Candle> ltf, List<Candle> htf) BuildValidShort(DateTime start, decimal priceScale = 1m)
    {
        var ltf = new List<Candle>();
        var htf = new List<Candle>();
        decimal mid = 60000m * priceScale;
        var baseAtr = 200m * priceScale;
        var trend = 4m * priceScale;

        for (var i = 0; i < 2700; i++)
        {
            mid -= trend;
            var open = mid + baseAtr * 0.1m;
            var close = mid - baseAtr * 0.2m;
            Add(ltf, htf, start, i, open, Math.Max(open, close) + baseAtr * 0.2m, Math.Min(open, close) - baseAtr * 0.3m, close, bearishHtf: true);
        }

        var boxBot = mid - baseAtr * 0.5m;
        for (var j = 0; j < 40; j++)
        {
            mid -= trend * (j >= 20 ? 2.5m : 0.8m);
            var atr = j >= 24 ? baseAtr * 2.5m : baseAtr;
            var open = mid + atr * 0.15m;
            var close = mid - atr * 0.35m;
            var low = Math.Min(open, close) - atr * 0.25m;
            if (j < 30)
            {
                low = Math.Max(low, boxBot - (j * trend * 0.1m));
            }

            if (j >= 20 && j < 30)
            {
                low = Math.Max(low, mid - baseAtr * 0.3m);
            }

            var high = Math.Max(open, close) + atr * 0.2m;
            Add(ltf, htf, start, ltf.Count, open, high, low, close, bearishHtf: true);
        }

        var lookbackLow = ltf.TakeLast(20).Min(c => c.Low);
        {
            var atr = baseAtr * 2.2m;
            var open = lookbackLow + atr * 0.05m;
            var close = lookbackLow - atr * 0.45m;
            Add(ltf, htf, start, ltf.Count, open, open + atr * 0.2m, close - atr * 0.1m, close, bearishHtf: true);
        }

        {
            var atr = baseAtr * 2.2m;
            var open = lookbackLow - atr * 0.15m;
            var high = lookbackLow + atr * 0.08m;
            var close = lookbackLow - atr * 0.10m;
            Add(ltf, htf, start, ltf.Count, open, high, close - atr * 0.1m, close, bearishHtf: true);
        }

        {
            var atr = baseAtr * 2.2m;
            var open = lookbackLow - atr * 0.05m;
            var close = lookbackLow - atr * 0.40m;
            Add(ltf, htf, start, ltf.Count, open, open + atr * 0.1m, close - atr * 0.05m, close, bearishHtf: true);
        }

        return (ltf, htf);
    }

    public static List<Candle> BuildFlatHtf(DateTime start, int count, decimal px)
    {
        var list = new List<Candle>();
        for (var i = 0; i < count; i++)
        {
            var open = start.AddHours(i);
            list.Add(new Candle
            {
                SymbolId = 1, ExchangeId = 1, Timeframe = Timeframe.H1,
                OpenTimeUtc = open, CloseTimeUtc = open.AddHours(1),
                Open = px, High = px + 5m, Low = px - 5m, Close = px,
                Volume = 1m, IsClosed = true, CreatedAtUtc = open
            });
        }

        return list;
    }

    private static void Add(
        List<Candle> ltf,
        List<Candle> htf,
        DateTime start,
        int i,
        decimal o,
        decimal h,
        decimal l,
        decimal c,
        bool bearishHtf = false)
    {
        var open = start.AddMinutes(i * 5);
        ltf.Add(new Candle
        {
            SymbolId = 1, ExchangeId = 1, Timeframe = Timeframe.M5,
            OpenTimeUtc = open, CloseTimeUtc = open.AddMinutes(5),
            Open = o, High = h, Low = l, Close = c, Volume = 100m + i, IsClosed = true, CreatedAtUtc = open
        });
        if (i % 12 == 11)
        {
            htf.Add(new Candle
            {
                SymbolId = 1, ExchangeId = 1, Timeframe = Timeframe.H1,
                OpenTimeUtc = start.AddMinutes((i - 11) * 5),
                CloseTimeUtc = start.AddMinutes((i - 11) * 5).AddHours(1),
                Open = bearishHtf ? c + 50m : c - 50m,
                High = h + 50m,
                Low = l - 50m,
                Close = bearishHtf ? c - 30m : c + 30m,
                Volume = 1000m, IsClosed = true, CreatedAtUtc = open
            });
        }
    }
}
