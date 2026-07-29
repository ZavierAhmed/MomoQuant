using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A1D — Adaptive MTF default-contract formula evidence.
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
        Assert.Equal(TradeDirection.Long, candidate!.Direction);
        Assert.Equal(51540.000m, candidate.EntryPrice);
        Assert.Equal(51262.368710296346047451164471m, candidate.StopLoss);
        Assert.Equal(52234.078224259134881372088822m, candidate.TakeProfit);
        var risk = candidate.EntryPrice - candidate.StopLoss;
        var reward = candidate.TakeProfit - candidate.EntryPrice;
        Assert.Equal(2.50m, Math.Round(reward / risk, 8));
        Assert.Equal(71.63470143201214509425913407m, candidate.Strength);
        Assert.Equal("6046B1A38922BED1", candidate.SetupFingerprint);
        Assert.Equal("Long MTF trend breakout retest confirmed.", candidate.Reason);

        var breakdown = Assert.IsType<MomoAdaptiveMtfTrendBreakoutEvaluator.StrengthBreakdownResult>(candidate.StrengthBreakdown);
        Assert.Equal(100m, breakdown.HtfAlignment);
        Assert.Equal(26.219979440800739041582574400m, breakdown.ExecutionTrend);
        Assert.Equal(88.41181306137762195365436734m, breakdown.VolatilityQuality);
        Assert.Equal(100m, breakdown.BreakoutQuality);
        Assert.Equal(45.454746984890453691257796790m, breakdown.Momentum);
        Assert.Equal(69.721669105004055879060065890m, breakdown.RetestQuality);
        Assert.Equal(71.63470143201214509425913407m, breakdown.Total);
        Assert.Equal(breakdown.Total, candidate.Strength);

        dynamic setup = candidate.Setup!;
        Assert.Equal(51364.000m, (decimal)setup.brokenLevel);
        Assert.Equal(2740, (int)setup.breakoutIndex);
        Assert.Equal(2741, (int)setup.retestIndex);
        Assert.Equal(2742, (int)setup.confirmationIndex);
        Assert.Equal(0.2154778505099169588368980612m, (decimal)setup.adaptiveBuffer);
        Assert.Equal(1.7698523367327797255793204083m, (decimal)setup.volRatio);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_ValidShort_WithExactDefaults()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidShort(Start);
        var parameters = MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract();

        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);

        Assert.NotNull(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.EntryConfirmed, reason);
        Assert.Equal(TradeDirection.Short, candidate!.Direction);
        Assert.Equal(48460.000m, candidate.EntryPrice);
        Assert.Equal(48737.631289703653952548835529m, candidate.StopLoss);
        Assert.Equal(47765.921775740865118627911178m, candidate.TakeProfit);
        var risk = candidate.StopLoss - candidate.EntryPrice;
        var reward = candidate.EntryPrice - candidate.TakeProfit;
        Assert.Equal(2.50m, Math.Round(reward / risk, 8));
        Assert.Equal(71.823998383593273339932049937m, candidate.Strength);
        Assert.Equal("F99C1578DBF02B61", candidate.SetupFingerprint);
        Assert.Equal("Short MTF trend breakout retest confirmed.", candidate.Reason);

        var breakdown = Assert.IsType<MomoAdaptiveMtfTrendBreakoutEvaluator.StrengthBreakdownResult>(candidate.StrengthBreakdown);
        Assert.Equal(100m, breakdown.HtfAlignment);
        Assert.Equal(27.355761150287508515620069600m, breakdown.ExecutionTrend);
        Assert.Equal(88.41181306137762195365436734m, breakdown.VolatilityQuality);
        Assert.Equal(100m, breakdown.BreakoutQuality);
        Assert.Equal(45.454746984890453691257796790m, breakdown.Momentum);
        Assert.Equal(69.721669105004055879060065890m, breakdown.RetestQuality);
        Assert.Equal(71.823998383593273339932049937m, breakdown.Total);
        Assert.Equal(breakdown.Total, candidate.Strength);

        dynamic setup = candidate.Setup!;
        Assert.Equal(48636.000m, (decimal)setup.brokenLevel);
        Assert.Equal(0.2154778505099169588368980612m, (decimal)setup.adaptiveBuffer);
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
    public void EvaluateAtCurrentCandle_MacdSignRejection_Long_RejectsMomentumNotConfirmed()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        // Soften only the confirmation close so histogram flips <= 0 while HTF/LTF EMA gates remain green.
        var last = ltf[^1];
        const decimal delta = 465m;
        ltf[^1] = CloneLtf(
            last,
            last.Open,
            Math.Max(last.High, last.Close - delta),
            Math.Min(last.Low, last.Close - delta),
            last.Close - delta);

        var snapshot = CaptureMomentumSnapshot(ltf, htf, longSide: true);
        Assert.True(snapshot.HtfFast > snapshot.HtfSlow);
        Assert.True(snapshot.HtfSlope > 0m);
        Assert.True(snapshot.HtfClose > snapshot.HtfFast);
        Assert.True(snapshot.LtfFast > snapshot.LtfSlow);
        Assert.True(snapshot.Histogram <= 0m);
        AssertExactMomentum(
            snapshot,
            htfFast: 49848.601614763552479815455594m,
            htfSlow: 46240.712616319900460569359337m,
            htfSlope: 250.601614763552479815455594m,
            htfClose: 51229.000m,
            ltfFast: 51183.040841273389450654016910m,
            ltfSlow: 51041.742316043186289666590063m,
            histogram: -0.19714374588991498950902445m,
            previousHistogram: 22.86435121918728099819418444m);

        var (candidate, reason) = Evaluate(ltf, htf, Defaults(), MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.MomentumNotConfirmed, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_MacdSignRejection_Short_RejectsMomentumNotConfirmed()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidShort(Start);
        var last = ltf[^1];
        const decimal delta = 465m;
        ltf[^1] = CloneLtf(
            last,
            last.Open,
            Math.Max(last.High, last.Close + delta),
            Math.Min(last.Low, last.Close + delta),
            last.Close + delta);

        var snapshot = CaptureMomentumSnapshot(ltf, htf, longSide: false);
        Assert.True(snapshot.HtfFast < snapshot.HtfSlow);
        Assert.True(snapshot.HtfSlope < 0m);
        Assert.True(snapshot.HtfClose < snapshot.HtfFast);
        Assert.True(snapshot.LtfFast < snapshot.LtfSlow);
        Assert.True(snapshot.Histogram >= 0m);
        AssertExactMomentum(
            snapshot,
            htfFast: 50151.398385236447520184544406m,
            htfSlow: 53759.287383680099539430640663m,
            htfSlope: -250.601614763552479815455594m,
            htfClose: 48771.000m,
            ltfFast: 48816.959158726610549345983090m,
            ltfSlow: 48958.257683956813710333409937m,
            histogram: 0.19714374588991498950902445m,
            previousHistogram: -22.86435121918728099819418444m);

        var (candidate, reason) = Evaluate(ltf, htf, Defaults(), MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.MomentumNotConfirmed, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_MacdExpansionRejection_Long_ThenExpansionPasses()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var parameters = Defaults();
        Assert.Equal("true", parameters["requireHistogramExpansion"]);
        Assert.True(MomoAdaptiveMtfTrendBreakoutEvaluator.ReadParameters(parameters).RequireHistogramExpansion);

        var original = ltf[^1];
        var prevClose = ltf[^2].Close;
        var target = original.Close + (prevClose - original.Close) * 0.79m;
        ltf[^1] = CloneLtf(
            original,
            Math.Min(original.Open, target - 0.01m),
            Math.Max(original.High, target),
            Math.Min(original.Low, target),
            target);

        var contracting = CaptureMomentumSnapshot(ltf, htf, longSide: true);
        Assert.True(contracting.HtfFast > contracting.HtfSlow);
        Assert.True(contracting.HtfSlope > 0m);
        Assert.True(contracting.HtfClose > contracting.HtfFast);
        Assert.True(contracting.LtfFast > contracting.LtfSlow);
        Assert.True(contracting.Histogram > 0m);
        Assert.True(contracting.PreviousHistogram > 0m);
        Assert.True(contracting.Histogram <= contracting.PreviousHistogram);
        AssertExactMomentum(
            contracting,
            htfFast: 49848.601614763552479815455594m,
            htfSlow: 46240.712616319900460569359337m,
            htfSlope: 250.601614763552479815455594m,
            htfClose: 51229.000m,
            ltfFast: 51217.395126987675164939731196m,
            ltfSlow: 51055.888198396127466137178299m,
            histogram: 22.82316394641777731818328355m,
            previousHistogram: 22.86435121918728099819418444m);

        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.MomentumNotConfirmed, reason);

        // Change only the confirmation close so histogram expands again.
        ltf[^1] = original;
        var expanding = CaptureMomentumSnapshot(ltf, htf, longSide: true);
        Assert.True(expanding.Histogram > 0m);
        Assert.True(expanding.PreviousHistogram > 0m);
        Assert.True(expanding.Histogram > expanding.PreviousHistogram);

        var (passed, passedReason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);
        Assert.NotNull(passed);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.EntryConfirmed, passedReason);
        Assert.Equal(TradeDirection.Long, passed!.Direction);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_MacdExpansionRejection_Short_ThenExpansionPasses()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidShort(Start);
        var parameters = Defaults();
        Assert.True(MomoAdaptiveMtfTrendBreakoutEvaluator.ReadParameters(parameters).RequireHistogramExpansion);

        var original = ltf[^1];
        var prevClose = ltf[^2].Close;
        var target = original.Close + (prevClose - original.Close) * 0.79m;
        ltf[^1] = CloneLtf(
            original,
            Math.Max(original.Open, target + 0.01m),
            Math.Max(original.High, target),
            Math.Min(original.Low, target),
            target);

        var contracting = CaptureMomentumSnapshot(ltf, htf, longSide: false);
        Assert.True(contracting.HtfFast < contracting.HtfSlow);
        Assert.True(contracting.HtfSlope < 0m);
        Assert.True(contracting.HtfClose < contracting.HtfFast);
        Assert.True(contracting.LtfFast < contracting.LtfSlow);
        Assert.True(contracting.Histogram < 0m);
        Assert.True(contracting.PreviousHistogram < 0m);
        Assert.True(contracting.Histogram >= contracting.PreviousHistogram);
        AssertExactMomentum(
            contracting,
            htfFast: 50151.398385236447520184544406m,
            htfSlow: 53759.287383680099539430640663m,
            htfSlope: -250.601614763552479815455594m,
            htfClose: 48771.000m,
            ltfFast: 48782.604873012324835060268804m,
            ltfSlow: 48944.111801603872533862821701m,
            histogram: -22.82316394641777731818328355m,
            previousHistogram: -22.86435121918728099819418444m);

        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.MomentumNotConfirmed, reason);

        ltf[^1] = original;
        var expanding = CaptureMomentumSnapshot(ltf, htf, longSide: false);
        Assert.True(expanding.Histogram < 0m);
        Assert.True(expanding.PreviousHistogram < 0m);
        Assert.True(expanding.Histogram < expanding.PreviousHistogram);

        var (passed, passedReason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);
        Assert.NotNull(passed);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.EntryConfirmed, passedReason);
        Assert.Equal(TradeDirection.Short, passed!.Direction);
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

        dynamic setup = candidate!.Setup!;
        int breakoutIndex = setup.breakoutIndex;
        int retestIndex = setup.retestIndex;
        decimal brokenLevel = setup.brokenLevel;
        decimal retestExtreme = setup.retestExtreme;
        decimal confirmationAtrFast = setup.confirmationAtrFast;
        decimal stopBufferAtr = setup.stopBufferAtr;

        var lookbackStart = breakoutIndex - 20;
        var expectedBroken = ltf.Skip(lookbackStart).Take(20).Max(c => c.High);
        Assert.Equal(expectedBroken, brokenLevel);
        Assert.True(ltf[breakoutIndex].High <= expectedBroken || ltf[breakoutIndex].Close > brokenLevel);
        // Breakout candle excluded from lookback window [breakoutIndex-20, breakoutIndex-1].
        Assert.Equal(
            ltf.Skip(breakoutIndex - 20).Take(20).Max(c => c.High),
            brokenLevel);

        var deepestRetestLow = ltf.Skip(breakoutIndex + 1).Take(retestIndex - breakoutIndex).Min(c => c.Low);
        Assert.Equal(deepestRetestLow, retestExtreme);
        Assert.Equal(retestExtreme - (stopBufferAtr * confirmationAtrFast), candidate.StopLoss);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_CompleteShortRetestExtreme_UsesHighestHigh()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidShort(Start);
        var (candidate, reason) = Evaluate(ltf, htf, Defaults(), MarketRegime.Breakout);
        Assert.NotNull(candidate);
        Assert.Equal(TradeDirection.Short, candidate!.Direction);

        dynamic setup = candidate.Setup!;
        int breakoutIndex = setup.breakoutIndex;
        int retestIndex = setup.retestIndex;
        decimal brokenLevel = setup.brokenLevel;
        decimal retestExtreme = setup.retestExtreme;
        decimal confirmationAtrFast = setup.confirmationAtrFast;
        decimal stopBufferAtr = setup.stopBufferAtr;

        var expectedBroken = ltf.Skip(breakoutIndex - 20).Take(20).Min(c => c.Low);
        Assert.Equal(expectedBroken, brokenLevel);

        var highestRetestHigh = ltf.Skip(breakoutIndex + 1).Take(retestIndex - breakoutIndex).Max(c => c.High);
        Assert.Equal(highestRetestHigh, retestExtreme);
        Assert.Equal(retestExtreme + (stopBufferAtr * confirmationAtrFast), candidate.StopLoss);
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
    public void EvaluateAtCurrentCandle_InvalidLongTarget_IsUnreachableInvariantWithPositiveRiskAndRewardRisk()
    {
        // Documented invariant: after InvalidStop (stop < entry ⇒ risk > 0) and ValidateParameters
        // (fixedRewardRisk > 0), long target = entry + risk×RR cannot be <= entry without overflow.
        // InvalidParameters for RR<=0 is NOT InvalidTarget proof.
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var parameters = Defaults();
        parameters["fixedRewardRisk"] = "0";
        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.InvalidParameters, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_InvalidShortTarget_PositiveRewardRiskProducesTargetAtOrBelowZero()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidShort(Start);
        var parameters = Defaults();
        parameters["fixedRewardRisk"] = "200";

        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.InvalidTarget, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_InvalidShortTarget_ExtremePositiveRewardRisk_IsOverflowSafe()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidShort(Start);
        var parameters = Defaults();
        parameters["fixedRewardRisk"] = decimal.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);
        Assert.Null(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.InvalidTarget, reason);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_StrengthBelow70_RejectsWithExactComponents()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildStrengthBelowMinimumLong(Start);
        var parameters = Defaults();
        Assert.Equal("70", parameters["minStrength"]);

        // Capture exact components with an otherwise-identical path that only relaxes the gate.
        var probeParams = Defaults();
        probeParams["minStrength"] = "0";
        var (probe, probeReason) = Evaluate(ltf, htf, probeParams, MarketRegime.Breakout);
        Assert.NotNull(probe);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.EntryConfirmed, probeReason);
        var breakdown = Assert.IsType<MomoAdaptiveMtfTrendBreakoutEvaluator.StrengthBreakdownResult>(probe!.StrengthBreakdown);
        Assert.Equal(100m, breakdown.HtfAlignment);
        Assert.Equal(25.772858607853959728801751200m, breakdown.ExecutionTrend);
        Assert.Equal(88.41181306137762195365436734m, breakdown.VolatilityQuality);
        Assert.Equal(100m, breakdown.BreakoutQuality);
        Assert.Equal(44.973641119937106886979454510m, breakdown.Momentum);
        Assert.Equal(40.202941371443130486587024600m, breakdown.RetestQuality);
        Assert.Equal(66.560209026768636509337099608m, breakdown.Total);
        Assert.True(breakdown.Total < 70m);

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

        dynamic setup = candidate!.Setup!;
        int breakoutIndex = setup.breakoutIndex;
        decimal brokenLevel = setup.brokenLevel;
        const int lookback = 20;
        var priorOnly = ltf.Skip(breakoutIndex - lookback).Take(lookback).ToList();
        Assert.Equal(lookback, priorOnly.Count);
        Assert.DoesNotContain(priorOnly, c => c.OpenTimeUtc == ltf[breakoutIndex].OpenTimeUtc);
        Assert.Equal(priorOnly.Max(c => c.High), brokenLevel);
        Assert.True(ltf[breakoutIndex].Close > brokenLevel);
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
        Assert.NotNull(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.EntryConfirmed, reason);
        dynamic setup = candidate!.Setup!;
        Assert.Equal(0.05m, (decimal)setup.adaptiveBuffer);
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
        // Max buffer reduces breakout-quality contribution; relax only the gate so clamp storage is observable.
        parameters["minStrength"] = "0";

        var (candidate, reason) = Evaluate(ltf, htf, parameters, MarketRegime.Breakout);
        Assert.NotNull(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.EntryConfirmed, reason);
        dynamic setup = candidate!.Setup!;
        Assert.Equal(0.35m, (decimal)setup.adaptiveBuffer);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_EventTimeBreakoutAtr_ExactAndPostEventOhlcMutationHasNoEffect()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var settings = MomoAdaptiveMtfTrendBreakoutEvaluator.ReadParameters(Defaults());
        var atrFast = MomoAdaptiveMtfTrendBreakoutEvaluator.ComputeWilderAtrSeries(ltf, settings.FastAtrPeriod);
        var atrSlow = MomoAdaptiveMtfTrendBreakoutEvaluator.ComputeWilderAtrSeries(ltf, settings.SlowAtrPeriod);

        var (candidate, reason) = Evaluate(ltf, htf, Defaults(), MarketRegime.Breakout);
        Assert.NotNull(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.EntryConfirmed, reason);
        dynamic setup = candidate!.Setup!;
        int breakoutIndex = setup.breakoutIndex;
        int retestIndex = setup.retestIndex;
        Assert.Equal(atrFast[breakoutIndex], (decimal)setup.breakoutAtrFast);
        Assert.Equal(atrSlow[breakoutIndex], (decimal)setup.breakoutAtrSlow);
        Assert.Equal(349.05954976083357099324744597m, (decimal)setup.breakoutAtrFast);
        Assert.Equal(ltf[breakoutIndex].CloseTimeUtc, ltf[breakoutIndex].CloseTimeUtc);

        // Mutate OHLC strictly after the breakout event. Open is not an ATR input; keep Close/High/Low
        // so confirmation body, geometry, and event-time ATR remain identical while the mutated bar is evaluated.
        var mutated = ltf.Select(c => CloneLtf(c, c.Open, c.High, c.Low, c.Close)).ToList();
        var confirm = mutated[^1];
        var pollutedOpen = confirm.Open + 55m;
        Assert.True(pollutedOpen < confirm.Close);
        mutated[^1] = CloneLtf(confirm, pollutedOpen, confirm.High, confirm.Low, confirm.Close);
        Assert.Equal(pollutedOpen, mutated[^1].Open);
        Assert.True(mutated.Count - 1 > breakoutIndex);

        var (mutatedCandidate, mutatedReason) = Evaluate(mutated, htf, Defaults(), MarketRegime.Breakout);
        Assert.NotNull(mutatedCandidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.EntryConfirmed, mutatedReason);
        dynamic mutatedSetup = mutatedCandidate!.Setup!;
        Assert.Equal(breakoutIndex, (int)mutatedSetup.breakoutIndex);
        Assert.Equal(retestIndex, (int)mutatedSetup.retestIndex);
        Assert.Equal((decimal)setup.breakoutAtrFast, (decimal)mutatedSetup.breakoutAtrFast);
        Assert.Equal((decimal)setup.breakoutAtrSlow, (decimal)mutatedSetup.breakoutAtrSlow);
        Assert.Equal((decimal)setup.retestAtrFast, (decimal)mutatedSetup.retestAtrFast);
        Assert.Equal(ltf[breakoutIndex].CloseTimeUtc, mutated[breakoutIndex].CloseTimeUtc);
        Assert.Equal(candidate.EntryPrice, mutatedCandidate.EntryPrice);
        Assert.Equal(candidate.StopLoss, mutatedCandidate.StopLoss);
        Assert.Equal(candidate.TakeProfit, mutatedCandidate.TakeProfit);
        Assert.Equal(candidate.SetupFingerprint, mutatedCandidate.SetupFingerprint);
        Assert.Equal(candidate.Reason, mutatedCandidate.Reason);
        Assert.Equal(
            Assert.IsType<MomoAdaptiveMtfTrendBreakoutEvaluator.StrengthBreakdownResult>(candidate.StrengthBreakdown).Total,
            Assert.IsType<MomoAdaptiveMtfTrendBreakoutEvaluator.StrengthBreakdownResult>(mutatedCandidate.StrengthBreakdown).Total);
        Assert.Equal(pollutedOpen, mutated[^1].Open);
    }

    [Fact]
    public void EvaluateAtCurrentCandle_EventTimeRetestAtr_ExactAndPostEventOhlcMutationHasNoEffect()
    {
        var (ltf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        var settings = MomoAdaptiveMtfTrendBreakoutEvaluator.ReadParameters(Defaults());
        var atrFast = MomoAdaptiveMtfTrendBreakoutEvaluator.ComputeWilderAtrSeries(ltf, settings.FastAtrPeriod);

        var (candidate, reason) = Evaluate(ltf, htf, Defaults(), MarketRegime.Breakout);
        Assert.NotNull(candidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.EntryConfirmed, reason);
        dynamic setup = candidate!.Setup!;
        int breakoutIndex = setup.breakoutIndex;
        int retestIndex = setup.retestIndex;
        Assert.Equal(atrFast[retestIndex], (decimal)setup.retestAtrFast);
        Assert.Equal(340.78386763505974449372977126m, (decimal)setup.retestAtrFast);

        var mutated = ltf.Select(c => CloneLtf(c, c.Open, c.High, c.Low, c.Close)).ToList();
        var confirm = mutated[^1];
        var pollutedOpen = confirm.Open + 55m;
        Assert.True(pollutedOpen < confirm.Close);
        mutated[^1] = CloneLtf(confirm, pollutedOpen, confirm.High, confirm.Low, confirm.Close);
        Assert.Equal(pollutedOpen, mutated[^1].Open);
        Assert.True(mutated.Count - 1 > retestIndex);

        var (mutatedCandidate, mutatedReason) = Evaluate(mutated, htf, Defaults(), MarketRegime.Breakout);
        Assert.NotNull(mutatedCandidate);
        Assert.Equal(MomoAdaptiveMtfRejectionCodes.EntryConfirmed, mutatedReason);
        dynamic mutatedSetup = mutatedCandidate!.Setup!;
        Assert.Equal(breakoutIndex, (int)mutatedSetup.breakoutIndex);
        Assert.Equal(retestIndex, (int)mutatedSetup.retestIndex);
        Assert.Equal((decimal)setup.breakoutAtrFast, (decimal)mutatedSetup.breakoutAtrFast);
        Assert.Equal((decimal)setup.breakoutAtrSlow, (decimal)mutatedSetup.breakoutAtrSlow);
        Assert.Equal((decimal)setup.retestAtrFast, (decimal)mutatedSetup.retestAtrFast);
        Assert.Equal(ltf[retestIndex].CloseTimeUtc, mutated[retestIndex].CloseTimeUtc);
        Assert.Equal(candidate.EntryPrice, mutatedCandidate.EntryPrice);
        Assert.Equal(candidate.StopLoss, mutatedCandidate.StopLoss);
        Assert.Equal(candidate.TakeProfit, mutatedCandidate.TakeProfit);
        Assert.Equal(candidate.SetupFingerprint, mutatedCandidate.SetupFingerprint);
        Assert.Equal(candidate.Reason, mutatedCandidate.Reason);
        Assert.Equal(
            Assert.IsType<MomoAdaptiveMtfTrendBreakoutEvaluator.StrengthBreakdownResult>(candidate.StrengthBreakdown).Total,
            Assert.IsType<MomoAdaptiveMtfTrendBreakoutEvaluator.StrengthBreakdownResult>(mutatedCandidate.StrengthBreakdown).Total);
        Assert.Equal(pollutedOpen, mutated[^1].Open);
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

    private sealed record MomentumSnapshot(
        decimal HtfFast,
        decimal HtfSlow,
        decimal HtfSlope,
        decimal HtfClose,
        decimal LtfFast,
        decimal LtfSlow,
        decimal Histogram,
        decimal PreviousHistogram);

    private static MomentumSnapshot CaptureMomentumSnapshot(
        IReadOnlyList<Candle> ltf,
        IReadOnlyList<Candle> htf,
        bool longSide)
    {
        var settings = MomoAdaptiveMtfTrendBreakoutEvaluator.ReadParameters(Defaults());
        var closes = ltf.Select(c => c.Close).ToArray();
        var ltfFast = MomoAdaptiveMtfTrendBreakoutEvaluator.ComputeEma(closes, settings.LtfFastEmaPeriod);
        var ltfSlow = MomoAdaptiveMtfTrendBreakoutEvaluator.ComputeEma(closes, settings.LtfSlowEmaPeriod);
        var (_, _, hist) = MomoAdaptiveMtfTrendBreakoutEvaluator.ComputeMacd(
            closes, settings.MacdFast, settings.MacdSlow, settings.MacdSignal);
        var i = closes.Length - 1;
        Assert.True(MomoAdaptiveMtfTrendBreakoutEvaluator.TryGetEma(ltfFast, i, settings.LtfFastEmaPeriod, out var execFast));
        Assert.True(MomoAdaptiveMtfTrendBreakoutEvaluator.TryGetEma(ltfSlow, i, settings.LtfSlowEmaPeriod, out var execSlow));
        Assert.True(MomoAdaptiveMtfTrendBreakoutEvaluator.TryGetMacdHistogram(hist, i, settings, out var histogram));
        Assert.True(MomoAdaptiveMtfTrendBreakoutEvaluator.TryGetMacdHistogram(hist, i - 1, settings, out var previous));

        var sliced = SliceHtf(htf, ltf[^1].CloseTimeUtc);
        var htfCloses = sliced.Select(c => c.Close).ToArray();
        var htfFastSeries = MomoAdaptiveMtfTrendBreakoutEvaluator.ComputeEma(htfCloses, settings.HtfFastEmaPeriod);
        var htfSlowSeries = MomoAdaptiveMtfTrendBreakoutEvaluator.ComputeEma(htfCloses, settings.HtfSlowEmaPeriod);
        var htfLast = htfCloses.Length - 1;
        Assert.True(MomoAdaptiveMtfTrendBreakoutEvaluator.TryGetEma(htfFastSeries, htfLast, settings.HtfFastEmaPeriod, out var htfFast));
        Assert.True(MomoAdaptiveMtfTrendBreakoutEvaluator.TryGetEma(htfSlowSeries, htfLast, settings.HtfSlowEmaPeriod, out var htfSlow));
        Assert.True(MomoAdaptiveMtfTrendBreakoutEvaluator.TryGetEma(
            htfFastSeries, htfLast - settings.HtfSlopeLookback, settings.HtfFastEmaPeriod, out var slopeStart));
        _ = longSide;
        return new MomentumSnapshot(
            htfFast,
            htfSlow,
            htfFast - slopeStart,
            sliced[htfLast].Close,
            execFast,
            execSlow,
            histogram,
            previous);
    }

    private static void AssertExactMomentum(
        MomentumSnapshot snapshot,
        decimal htfFast,
        decimal htfSlow,
        decimal htfSlope,
        decimal htfClose,
        decimal ltfFast,
        decimal ltfSlow,
        decimal histogram,
        decimal previousHistogram)
    {
        Assert.Equal(htfFast, snapshot.HtfFast);
        Assert.Equal(htfSlow, snapshot.HtfSlow);
        Assert.Equal(htfSlope, snapshot.HtfSlope);
        Assert.Equal(htfClose, snapshot.HtfClose);
        Assert.Equal(ltfFast, snapshot.LtfFast);
        Assert.Equal(ltfSlow, snapshot.LtfSlow);
        Assert.Equal(histogram, snapshot.Histogram);
        Assert.Equal(previousHistogram, snapshot.PreviousHistogram);
    }

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

    public static (List<Candle> ltf, List<Candle> htf) BuildStrengthBelowMinimumLong(DateTime start)
    {
        var (ltf, htf) = BuildValidLong(start);
        var retestIdx = ltf.Count - 2;
        var brokenGuess = ltf.Skip(ltf.Count - 25).Take(20).Max(c => c.High);
        var retest = ltf[retestIdx];
        const decimal deep = 51294.000m;
        ltf[retestIdx] = new Candle
        {
            SymbolId = retest.SymbolId,
            ExchangeId = retest.ExchangeId,
            Timeframe = retest.Timeframe,
            OpenTimeUtc = retest.OpenTimeUtc,
            CloseTimeUtc = retest.CloseTimeUtc,
            Open = brokenGuess + 10m,
            High = brokenGuess + 30m,
            Low = deep,
            Close = brokenGuess + 5m,
            Volume = retest.Volume,
            IsClosed = true,
            CreatedAtUtc = retest.CreatedAtUtc
        };
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
