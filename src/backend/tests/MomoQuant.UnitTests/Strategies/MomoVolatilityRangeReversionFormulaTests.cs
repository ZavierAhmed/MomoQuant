using System.Text.Json;
using MomoQuant.Application.Strategies.MomoRange;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A1D — Range Reversion default-contract formula evidence.
/// </summary>
public sealed class MomoVolatilityRangeReversionFormulaTests
{
    private const string Code = "MOMO_VOLATILITY_RANGE_REVERSION";

    [Fact]
    public void Defaults_MatchContract()
    {
        var p = MomoVolatilityRangeReversionParameters.Read(MomoVolatilityRangeReversionParameters.GetDefaultParameterContract());
        Assert.Equal(65m, p.MinStrength);
        Assert.Equal(1.25m, p.MinimumRewardRisk);
        Assert.Equal("RangeMidpoint", p.TargetMode);
        Assert.Equal(0.15m, p.BoundaryToleranceAtr);
        Assert.Equal(48, p.RangeLookback);
        Assert.Equal(3.0m, p.MinRangeWidthAtr);
        Assert.Equal(12.0m, p.MaxRangeWidthAtr);
        Assert.Equal(0.65m, p.MinVolatilityRatio);
        Assert.Equal(1.25m, p.MaxVolatilityRatio);
        Assert.Equal(0.50m, p.MaxEmaSeparationAtr);
        Assert.Equal(0.15m, p.MaxSlowEmaSlopeAtr);
    }

    [Fact]
    public void ValidLong_WithCompleteDefaults()
    {
        var candles = BuildValidLong();
        var (candidate, reason) = Eval(candles, Defaults());
        Assert.NotNull(candidate);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.EntryConfirmed, reason);
        Assert.Equal(TradeDirection.Long, candidate!.Direction);
        Assert.Equal(2868m, candidate.EntryPrice);
        Assert.Equal(2837.4362555235449367318813733m, candidate.StopLoss);
        Assert.Equal(3000m, candidate.TakeProfit);
        Assert.Equal(4.3188425456732526504674263312m, candidate.RewardRisk);
        Assert.Equal(68.035843253575279656322074267m, candidate.Strength);
        Assert.Equal("43E14ED345E566C3", candidate.SetupFingerprint);
        AssertExactLongGeometryAndStrength(candidate);
    }

    [Fact]
    public void ValidShort_WithCompleteDefaults()
    {
        var candles = BuildValidShort();
        var (candidate, reason) = Eval(candles, Defaults());
        Assert.NotNull(candidate);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.EntryConfirmed, reason);
        Assert.Equal(TradeDirection.Short, candidate!.Direction);
        Assert.Equal(3132m, candidate.EntryPrice);
        Assert.Equal(3162.5659501236556344586939822m, candidate.StopLoss);
        Assert.Equal(3000m, candidate.TakeProfit);
        Assert.Equal(4.3185308968309285994022367671m, candidate.RewardRisk);
        Assert.Equal(69.155342031802905333376497388m, candidate.Strength);
        Assert.Equal("033FADE1716FAF49", candidate.SetupFingerprint);
        AssertExactShortGeometryAndStrength(candidate);
    }

    [Fact]
    public void ExactLongGeometryAndStrengthBreakdown()
    {
        var (candidate, reason) = Eval(BuildValidLong(), Defaults());
        Assert.NotNull(candidate);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.EntryConfirmed, reason);
        AssertExactLongGeometryAndStrength(candidate!);
    }

    [Fact]
    public void ExactShortGeometryAndStrengthBreakdown()
    {
        var (candidate, reason) = Eval(BuildValidShort(), Defaults());
        Assert.NotNull(candidate);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.EntryConfirmed, reason);
        AssertExactShortGeometryAndStrength(candidate!);
    }

    private static void AssertExactLongGeometryAndStrength(MomoVolatilityRangeReversionCandidate candidate)
    {
        using var doc = JsonDocument.Parse(candidate.RawDataJson);
        var root = doc.RootElement;
        Assert.Equal(3150m, root.GetProperty("rangeHigh").GetDecimal());
        Assert.Equal(2850m, root.GetProperty("rangeLow").GetDecimal());
        Assert.Equal(3000m, root.GetProperty("rangeMidpoint").GetDecimal());
        Assert.Equal(42.254977905820253072474506656m, root.GetProperty("fastAtr").GetDecimal());
        Assert.Equal(41.757693421895668357208512272m, root.GetProperty("slowAtr").GetDecimal());
        Assert.Equal(1.0119088111237445250357590068m, root.GetProperty("volatilityRatio").GetDecimal());
        Assert.Equal(0.3055329783023949879111162213m, root.GetProperty("emaSeparationAtr").GetDecimal());
        Assert.Equal(-0.1320644848222473665370999215m, root.GetProperty("slowEmaSlopeAtr").GetDecimal());
        Assert.Equal(13.34637324329802989223565014m, root.GetProperty("rsi").GetDecimal());
        Assert.Equal(50.0m, root.GetProperty("wickPercent").GetDecimal());
        Assert.Equal(4.3188425456732526504674263312m, root.GetProperty("rewardRisk").GetDecimal());
        Assert.Equal(2868m, root.GetProperty("entry").GetDecimal());
        Assert.Equal(2837.4362555235449367318813733m, root.GetProperty("stop").GetDecimal());
        Assert.Equal(3000m, root.GetProperty("takeProfit").GetDecimal());
        Assert.Equal(2868m, candidate.EntryPrice);
        Assert.Equal(2837.4362555235449367318813733m, candidate.StopLoss);
        Assert.Equal(3000m, candidate.TakeProfit);

        var breakdown = root.GetProperty("strengthBreakdown");
        Assert.Equal(19.110567157773203009015303198m, breakdown.GetProperty("rangeQuality").GetDecimal());
        Assert.Equal(13.452279721906386874106024830m, breakdown.GetProperty("volatilityQuality").GetDecimal());
        Assert.Equal(12.373501003829697204436771348m, breakdown.GetProperty("rsiExtremity").GetDecimal());
        Assert.Equal(4.2857142857142857142857142855m, breakdown.GetProperty("wickQuality").GetDecimal());
        Assert.Equal(15m, breakdown.GetProperty("rewardRiskQuality").GetDecimal());
        Assert.Equal(3.8137810843517068544782606052m, breakdown.GetProperty("trendFlatness").GetDecimal());
        Assert.Equal(68.035843253575279656322074267m, breakdown.GetProperty("total").GetDecimal());
        Assert.Equal(68.035843253575279656322074267m, candidate.Strength);
        Assert.Equal("43E14ED345E566C3", candidate.SetupFingerprint);
    }

    private static void AssertExactShortGeometryAndStrength(MomoVolatilityRangeReversionCandidate candidate)
    {
        using var doc = JsonDocument.Parse(candidate.RawDataJson);
        var root = doc.RootElement;
        Assert.Equal(3150m, root.GetProperty("rangeHigh").GetDecimal());
        Assert.Equal(2850m, root.GetProperty("rangeLow").GetDecimal());
        Assert.Equal(3000m, root.GetProperty("rangeMidpoint").GetDecimal());
        Assert.Equal(42.263800494622537834775928682m, root.GetProperty("fastAtr").GetDecimal());
        Assert.Equal(41.757889451795668357208512272m, root.GetProperty("slowAtr").GetDecimal());
        Assert.Equal(1.0121153403457059620208324627m, root.GetProperty("volatilityRatio").GetDecimal());
        Assert.Equal(0.3048893274823407784093282618m, root.GetProperty("emaSeparationAtr").GetDecimal());
        Assert.Equal(0.1320393948889972179751438647m, root.GetProperty("slowEmaSlopeAtr").GetDecimal());
        Assert.Equal(88.60845770296578220299390318m, root.GetProperty("rsi").GetDecimal());
        Assert.Equal(50.0m, root.GetProperty("wickPercent").GetDecimal());
        Assert.Equal(4.3185308968309285994022367671m, root.GetProperty("rewardRisk").GetDecimal());
        Assert.Equal(3132m, root.GetProperty("entry").GetDecimal());
        Assert.Equal(3162.5659501236556344586939822m, root.GetProperty("stop").GetDecimal());
        Assert.Equal(3000m, root.GetProperty("takeProfit").GetDecimal());

        var breakdown = root.GetProperty("strengthBreakdown");
        Assert.Equal(19.107273652578651414226481444m, breakdown.GetProperty("rangeQuality").GetDecimal());
        Assert.Equal(13.447116491357350949479188432m, breakdown.GetProperty("volatilityQuality").GetDecimal());
        Assert.Equal(13.490547258837589830282230388m, breakdown.GetProperty("rsiExtremity").GetDecimal());
        Assert.Equal(4.2857142857142857142857142855m, breakdown.GetProperty("wickQuality").GetDecimal());
        Assert.Equal(15m, breakdown.GetProperty("rewardRiskQuality").GetDecimal());
        Assert.Equal(3.8246903433150274251028828380m, breakdown.GetProperty("trendFlatness").GetDecimal());
        Assert.Equal(69.155342031802905333376497388m, breakdown.GetProperty("total").GetDecimal());
        Assert.Equal(69.155342031802905333376497388m, candidate.Strength);
        Assert.Equal("033FADE1716FAF49", candidate.SetupFingerprint);
    }

    [Fact]
    public void InsideReclaim_LongRequiresCloseAboveRangeLow()
    {
        var candles = BuildValidLong();
        var last = candles[^1];
        candles[^1] = CloneCandle(last, close: 2849m, high: Math.Max(last.High, 2849m), low: last.Low, open: last.Open);
        var (c, reason) = Eval(candles, Defaults());
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.CloseDidNotReclaim, reason);
    }

    [Fact]
    public void OutsideClose_Rejected()
    {
        // Use short fixture so previous closes sit near the upper boundary; a small outside close
        // rejects reclaim without a multi-hundred ATR gap from a lower-boundary long probe.
        var candles = BuildValidShort();
        var last = candles[^1];
        candles[^1] = CloneCandle(last, close: 3150.5m, high: Math.Max(last.High, 3150.5m), low: last.Low, open: last.Open);
        var (c, reason) = Eval(candles, Defaults());
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.CloseDidNotReclaim, reason);
    }

    [Fact]
    public void PenetrationExactlyInsideTolerance_Accepted()
    {
        const decimal rangeLow = 2850m;
        const decimal boundaryToleranceAtr = 0.15m;

        var (candles, probe, atr, tolerance) = BuildLongAtBoundaryProbe(offsetFromBoundary: 0m);
        Assert.Equal(boundaryToleranceAtr * atr, tolerance);
        Assert.True(Math.Abs(probe - (rangeLow - tolerance)) < 0.0000001m);
        Assert.Equal(probe, candles[^1].Low);

        var (c, reason) = Eval(candles, Defaults());
        Assert.NotNull(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.EntryConfirmed, reason);
        using var doc = JsonDocument.Parse(c!.RawDataJson);
        Assert.Equal(atr, doc.RootElement.GetProperty("fastAtr").GetDecimal());
        Assert.True(Math.Abs(probe - doc.RootElement.GetProperty("acceptedLowerBoundaryProbe").GetDecimal()) < 0.0000001m);
    }

    [Fact]
    public void PenetrationJustOutsideTolerance_Rejected()
    {
        const decimal rangeLow = 2850m;
        const decimal boundaryToleranceAtr = 0.15m;
        const decimal epsilon = 0.01m;

        var (candles, probe, atr, tolerance) = BuildLongAtBoundaryProbe(offsetFromBoundary: -epsilon);
        Assert.Equal(boundaryToleranceAtr * atr, tolerance);
        Assert.True(Math.Abs(probe - (rangeLow - tolerance - epsilon)) < 0.0000001m);
        Assert.True(probe < rangeLow - tolerance);
        Assert.True(Math.Abs(epsilon - ((rangeLow - tolerance) - probe)) < 0.0000001m);
        Assert.Equal(probe, candles[^1].Low);

        var (c, reason) = Eval(candles, Defaults());
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.BoundaryPenetrationExceeded, reason);
    }

    /// <summary>
    /// Builds a long fixture whose final probe low sits at rangeLow − (0.15×ATR) + offsetFromBoundary,
    /// where ATR is taken from the final mutated candle series via the production ATR calculator.
    /// Fails if the probe does not converge.
    /// </summary>
    private static (List<Candle> Candles, decimal Probe, decimal Atr, decimal Tolerance) BuildLongAtBoundaryProbe(
        decimal offsetFromBoundary)
    {
        const decimal rangeLow = 2850m;
        const decimal boundaryToleranceAtr = 0.15m;
        const decimal convergenceTol = 0.00000001m;

        var candles = BuildValidLong();
        decimal probe = decimal.MinValue;
        decimal atr = 0m;
        decimal tolerance = 0m;
        for (var i = 0; i < 32; i++)
        {
            atr = ComputeProductionAtrAtLast(candles, period: 14);
            Assert.True(atr > 0m);
            tolerance = boundaryToleranceAtr * atr;
            var nextProbe = rangeLow - tolerance + offsetFromBoundary;
            var last = candles[^1];
            candles[^1] = CloneCandle(last, close: last.Close, high: last.High, low: nextProbe, open: last.Open);

            if (i > 0 && Math.Abs(nextProbe - probe) <= convergenceTol)
            {
                atr = ComputeProductionAtrAtLast(candles, period: 14);
                tolerance = boundaryToleranceAtr * atr;
                probe = rangeLow - tolerance + offsetFromBoundary;
                last = candles[^1];
                candles[^1] = CloneCandle(last, close: last.Close, high: last.High, low: probe, open: last.Open);
                atr = ComputeProductionAtrAtLast(candles, period: 14);
                tolerance = boundaryToleranceAtr * atr;
                var expected = rangeLow - tolerance + offsetFromBoundary;
                Assert.True(
                    Math.Abs(probe - expected) <= 0.0000001m
                    || Math.Abs(candles[^1].Low - expected) <= 0.0000001m,
                    $"probe={probe} low={candles[^1].Low} expected={expected} atr={atr} tol={tolerance}");
                // Return the stored wick and the ATR/tolerance of that final series.
                probe = candles[^1].Low;
                return (candles, probe, atr, tolerance);
            }

            probe = nextProbe;
        }

        Assert.Fail("Boundary probe builder did not converge within 32 iterations.");
        return (candles, probe, atr, tolerance);
    }

    private static decimal ComputeProductionAtrAtLast(IReadOnlyList<Candle> candles, int period)
    {
        var state = new MomoQuant.Application.Indicators.Calculators.AtrCalculator.State();
        decimal? latest = null;
        for (var i = 0; i < candles.Count; i++)
        {
            latest = MomoQuant.Application.Indicators.Calculators.AtrCalculator.CalculateNext(candles[i], state, period);
        }

        Assert.True(latest is > 0m);
        return latest!.Value;
    }

    [Fact]
    public void ZeroBoundaryTolerance_InvalidParameters()
    {
        var p = Defaults();
        p["boundaryToleranceAtr"] = "0";
        var (c, reason) = Eval(BuildValidLong(), p);
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.InvalidParameters, reason);
    }

    [Fact]
    public void SingleOutsideCloseFollowedByReclaim_RemainsEligible()
    {
        var candles = BuildSingleOutsideCloseReclaimEligibleLong();
        var p = Defaults();
        Assert.Equal("65", p["minStrength"]);
        var (c, reason) = Eval(candles, p);
        Assert.True(c is not null, $"expected candidate under minStrength=65, got {reason}");
        Assert.Equal(MomoVolatilityRangeRejectionCodes.EntryConfirmed, reason);
        Assert.True(c!.Strength >= 65m);
    }

    /// <summary>
    /// Single outside-close in the expansion window, then reclaim — eligible under complete defaults
    /// including minStrength=65 (baseline lows raised so the outside is mild and RSI stays oversold).
    /// </summary>
    internal static List<Candle> BuildSingleOutsideCloseReclaimEligibleLong()
    {
        var candles = BuildValidLong();
        var currentIndex = candles.Count - 1;
        var baselineEnd = currentIndex - 5;
        var baselineStart = baselineEnd - 48;
        const decimal floor = 2867.5m;
        for (var i = baselineStart; i < baselineEnd; i++)
        {
            var x = candles[i];
            if (x.Low < floor)
            {
                var newLow = floor;
                var newHigh = Math.Max(x.High, newLow + 1m);
                var newOpen = Math.Min(newHigh, Math.Max(newLow, x.Open));
                var newClose = Math.Min(newHigh, Math.Max(newLow, x.Close));
                candles[i] = CloneCandle(x, close: newClose, high: newHigh, low: newLow, open: newOpen);
            }
        }

        var deepIdx = baselineEnd + 1;
        var deep = candles[deepIdx];
        candles[deepIdx] = CloneCandle(deep, close: 2868m, high: Math.Max(deep.High, 3000m), low: 2850m, open: 2869m);

        var priorLow = decimal.MaxValue;
        for (var i = baselineStart; i < baselineEnd; i++)
        {
            priorLow = Math.Min(priorLow, candles[i].Low);
        }

        var ox = priorLow - 0.1m;
        var bar = candles[baselineEnd];
        candles[baselineEnd] = CloneCandle(bar, close: ox, high: ox + 3m, low: ox, open: ox + 1m);
        return candles;
    }

    [Fact]
    public void SameTime_FutureCandleMutation_HasNoEffect()
    {
        var clean = BuildValidLong();
        for (var i = 0; i < clean.Count; i++)
        {
            clean[i].Id = i + 1;
        }

        var evaluationIndex = clean.Count - 1;
        var evaluationCandle = clean[evaluationIndex];
        var evaluationTimeUtc = evaluationCandle.CloseTimeUtc;

        var pollutedSource = clean.Select(CloneCandleExact).ToList();
        for (var i = 0; i < pollutedSource.Count; i++)
        {
            pollutedSource[i].Id = clean[i].Id;
        }

        var last = pollutedSource[^1];
        pollutedSource.Add(new Candle
        {
            Id = 90_001,
            SymbolId = last.SymbolId,
            ExchangeId = last.ExchangeId,
            Timeframe = last.Timeframe,
            OpenTimeUtc = evaluationTimeUtc,
            CloseTimeUtc = evaluationTimeUtc.AddMinutes(5),
            Open = last.Close + 500m,
            High = last.High + 800m,
            Low = last.Low + 400m,
            Close = last.Close + 700m,
            Volume = last.Volume,
            IsClosed = false,
            CreatedAtUtc = evaluationTimeUtc
        });
        pollutedSource.Add(new Candle
        {
            Id = 90_002,
            SymbolId = last.SymbolId,
            ExchangeId = last.ExchangeId,
            Timeframe = last.Timeframe,
            OpenTimeUtc = evaluationTimeUtc.AddMinutes(5),
            CloseTimeUtc = evaluationTimeUtc.AddMinutes(10),
            Open = 1m,
            High = 2m,
            Low = 0.5m,
            Close = 1.5m,
            Volume = 1m,
            IsClosed = true,
            CreatedAtUtc = evaluationTimeUtc.AddMinutes(5)
        });

        // Production path: evaluate fixed index T from clean vs polluted source (prefix through T only).
        var cleanThroughT = clean.Take(evaluationIndex + 1).ToList();
        var pollutedThroughT = pollutedSource.Where(c => c.CloseTimeUtc <= evaluationTimeUtc && c.IsClosed).ToList();
        Assert.Equal(cleanThroughT.Count, pollutedThroughT.Count);
        Assert.Equal(cleanThroughT[^1].Id, pollutedThroughT[^1].Id);
        Assert.Equal(cleanThroughT[^1].CloseTimeUtc, pollutedThroughT[^1].CloseTimeUtc);

        var (first, reason1) = Eval(cleanThroughT, Defaults());
        var (second, reason2) = Eval(pollutedThroughT, Defaults());
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(reason1, reason2);
        Assert.Equal(first!.Direction, second!.Direction);
        Assert.Equal(first.EntryPrice, second.EntryPrice);
        Assert.Equal(first.StopLoss, second.StopLoss);
        Assert.Equal(first.TakeProfit, second.TakeProfit);
        Assert.Equal(first.Strength, second.Strength);
        Assert.Equal(first.SetupFingerprint, second.SetupFingerprint);
        Assert.Equal(first.RawDataJson, second.RawDataJson);
        Assert.True(pollutedSource.Count > clean.Count);
    }

    [Fact]
    public void TwoConsecutiveExpansionCloses_Rejected()
    {
        var candles = BuildValidLong();
        for (var i = 6; i >= 5; i--)
        {
            var idx = candles.Count - i;
            var bar = candles[idx];
            candles[idx] = CloneCandle(bar, close: 2849.5m, high: Math.Max(bar.High, 2860m), low: 2849.5m, open: 2855m);
        }

        var last = candles[^1];
        candles[^1] = CloneCandle(last, close: last.Close, high: last.High, low: 2847.5m, open: last.Open);
        var (c, reason) = Eval(candles, Defaults());
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.TrendFilterFailed, reason);
    }

    [Fact]
    public void EmaSeparation_Rejected()
    {
        var p = Defaults();
        p["maxEmaSeparationAtr"] = "0.01";
        var (c, reason) = Eval(BuildValidLong(), p);
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.TrendFilterFailed, reason);
    }

    [Fact]
    public void EmaSlope_Rejected()
    {
        var p = Defaults();
        p["maxSlowEmaSlopeAtr"] = "0.01";
        var (c, reason) = Eval(BuildValidLong(), p);
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.TrendFilterFailed, reason);
    }

    [Fact]
    public void RangeTooNarrow_Rejected()
    {
        var p = Defaults();
        p["minRangeWidthAtr"] = "50";
        p["maxRangeWidthAtr"] = "100";
        var (c, reason) = Eval(BuildValidLong(), p);
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.RangeTooNarrow, reason);
    }

    [Fact]
    public void RangeTooWide_Rejected()
    {
        var p = Defaults();
        p["minRangeWidthAtr"] = "0.5";
        p["maxRangeWidthAtr"] = "1.0";
        var (c, reason) = Eval(BuildValidLong(), p);
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.RangeTooWide, reason);
    }

    [Fact]
    public void VolatilityTooLow_Rejected()
    {
        var p = Defaults();
        p["minVolatilityRatio"] = "10";
        p["maxVolatilityRatio"] = "20";
        var (c, reason) = Eval(BuildValidLong(), p);
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.VolatilityTooLow, reason);
    }

    [Fact]
    public void VolatilityTooHigh_Rejected()
    {
        var p = Defaults();
        p["minVolatilityRatio"] = "0.01";
        p["maxVolatilityRatio"] = "0.02";
        var (c, reason) = Eval(BuildValidLong(), p);
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.VolatilityTooHigh, reason);
    }

    [Fact]
    public void Rsi_Rejected()
    {
        var p = Defaults();
        p["rsiOversold"] = "5";
        var (c, reason) = Eval(BuildValidLong(), p);
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.RsiNotExtreme, reason);
    }

    [Fact]
    public void Wick_Rejected()
    {
        var p = Defaults();
        p["minimumWickPercent"] = "99";
        var (c, reason) = Eval(BuildValidLong(), p);
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.WickConfirmationMissing, reason);
    }

    [Fact]
    public void MidpointRewardRisk_Rejected()
    {
        var p = Defaults();
        p["minimumRewardRisk"] = "100";
        var (c, reason) = Eval(BuildValidLong(), p);
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.RewardRiskInsufficient, reason);
    }

    [Fact]
    public void InvalidLongGeometry_Rejected()
    {
        var p = Defaults();
        p["stopBufferAtr"] = "100";
        var (c, reason) = Eval(BuildValidLong(), p);
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.InvalidStop, reason);
    }

    [Fact]
    public void InvalidShortGeometry_Rejected()
    {
        // Midpoint reclaim with a dominant upper wick → takeProfit >= entry → InvalidStop.
        var candles = BuildValidShort();
        var last = candles[^1];
        candles[^1] = CloneCandle(last, close: 3000m, high: 3152m, low: 2999m, open: 3001m);
        var p = Defaults();
        p["rsiOversold"] = "1";
        p["rsiOverbought"] = "5";
        var (c, reason) = Eval(candles, p);
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.InvalidStop, reason);
    }

    [Theory]
    [InlineData("minRangeWidthAtr", "0")]
    [InlineData("maxRangeWidthAtr", "0")]
    [InlineData("minVolatilityRatio", "0")]
    [InlineData("maxVolatilityRatio", "0")]
    [InlineData("boundaryToleranceAtr", "-1")]
    [InlineData("rangeLookback", "0")]
    [InlineData("fastEmaPeriod", "0")]
    [InlineData("slowEmaPeriod", "0")]
    [InlineData("fastAtrPeriod", "0")]
    [InlineData("slowAtrPeriod", "0")]
    [InlineData("rsiPeriod", "0")]
    [InlineData("slopeLookback", "0")]
    [InlineData("minimumRewardRisk", "0")]
    [InlineData("minimumWickPercent", "-1")]
    [InlineData("minimumWickPercent", "101")]
    [InlineData("stopBufferAtr", "-0.01")]
    [InlineData("minStrength", "-1")]
    [InlineData("minStrength", "101")]
    [InlineData("targetMode", "Nope")]
    [InlineData("rsiOversold", "80")]
    public void InvalidParameterFamily_Rejected(string key, string value)
    {
        var p = Defaults();
        p[key] = value;
        if (key == "rsiOversold")
        {
            p["rsiOverbought"] = "70";
        }

        var (c, reason) = Eval(BuildValidLong(), p);
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.InvalidParameters, reason);
    }

    [Fact]
    public void MinGreaterThanMaxWidth_InvalidParameters()
    {
        var p = Defaults();
        p["minRangeWidthAtr"] = "10";
        p["maxRangeWidthAtr"] = "3";
        var (c, reason) = Eval(BuildValidLong(), p);
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.InvalidParameters, reason);
    }

    [Fact]
    public void MinGreaterThanMaxVolatility_InvalidParameters()
    {
        var p = Defaults();
        p["minVolatilityRatio"] = "2";
        p["maxVolatilityRatio"] = "1";
        var (c, reason) = Eval(BuildValidLong(), p);
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.InvalidParameters, reason);
    }

    [Fact]
    public void StrengthBelow65_Rejected()
    {
        var p = Defaults();
        p["minStrength"] = "99.9";
        var (c, reason) = Eval(BuildValidLong(), p);
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.StrengthBelowMinimum, reason);
    }

    [Fact]
    public void DuplicateSetup_Rejected()
    {
        var candles = BuildValidLong();
        var (first, r1) = Eval(candles, Defaults());
        Assert.NotNull(first);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.EntryConfirmed, r1);
        var (second, r2) = MomoVolatilityRangeReversionEvaluator.EvaluateAtCurrentCandle(
            candles, Defaults(), new HashSet<string> { first!.SetupFingerprint }, Code, 1, "5m");
        Assert.Null(second);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.DuplicateSetup, r2);
    }

    private static Dictionary<string, string> Defaults() =>
        new(MomoVolatilityRangeReversionParameters.GetDefaultParameterContract());

    private static (MomoVolatilityRangeReversionCandidate? Candidate, string Reason) Eval(
        IReadOnlyList<Candle> candles,
        IReadOnlyDictionary<string, string> parameters) =>
        MomoVolatilityRangeReversionEvaluator.EvaluateAtCurrentCandle(
            candles, parameters, new HashSet<string>(), Code, 1, "5m");

    /// <summary>
    /// Locked default-valid long: ATR warmup → migrate → lookback extremes → RSI tip → compact probe.
    /// Params: halfWidth=150, barTr=40, entryAboveLow=18, migrate=220, tip=14, flat=2, warmup=400.
    /// </summary>
    internal static List<Candle> BuildValidLong() =>
        BuildLongMigrated(150m, 40m, 18m, 220, 14, 2, 2m, 400);

    /// <summary>Mirror of <see cref="BuildValidLong"/> for short.</summary>
    internal static List<Candle> BuildValidShort() =>
        BuildShortMigrated(150m, 40m, 18m, 220, 14, 2, 2m, 400);

    internal static List<Candle> BuildLongMigrated(
        decimal halfWidth,
        decimal barTr,
        decimal entryAboveLow,
        int migrateBars,
        int tipBars,
        int flatAfter,
        decimal probeBeyond,
        int warmup)
    {
        var start = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        const decimal mid = 3000m;
        var half = barTr / 2m;
        var rangeLow = mid - halfWidth;
        var rangeHigh = mid + halfWidth;
        var entry = rangeLow + entryAboveLow;
        var candles = new List<Candle>();

        void Add(decimal o, decimal h, decimal l, decimal c)
        {
            var i = candles.Count;
            candles.Add(new Candle
            {
                SymbolId = 1, ExchangeId = 1, Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = o, High = h, Low = l, Close = c,
                Volume = 100m + i, IsClosed = true, CreatedAtUtc = start
            });
        }

        void Bar(decimal center, decimal? floor = null, decimal? ceiling = null)
        {
            var c = center + ((candles.Count % 2 == 0) ? 0.5m : -0.5m);
            var lo = c - half;
            var hi = c + half;
            if (floor is not null)
            {
                lo = Math.Max(lo, floor.Value);
            }

            if (ceiling is not null)
            {
                hi = Math.Min(hi, ceiling.Value);
            }

            if (hi <= lo)
            {
                hi = lo + 1m;
            }

            Add(c + 0.5m, hi, lo, c - 0.5m);
        }

        for (var i = 0; i < warmup; i++)
        {
            Bar(mid);
        }

        var preTip = entry + Math.Max(28m, halfWidth * 0.22m);
        var migrateStep = (mid - preTip) / migrateBars;
        var px = mid;
        for (var i = 0; i < migrateBars; i++)
        {
            px -= migrateStep;
            Bar(px);
        }

        Add(preTip + 0.5m, rangeHigh, Math.Max(rangeLow + 1m, preTip - half), preTip - 0.5m);
        for (var i = 0; i < 4; i++)
        {
            Bar(preTip, floor: rangeLow + 1m, ceiling: rangeHigh - 1m);
        }

        Add(preTip - 0.5m, Math.Min(rangeHigh - 1m, preTip + half), rangeLow, preTip + 0.5m);
        for (var i = 0; i < 4; i++)
        {
            Bar(preTip, floor: rangeLow + 1m, ceiling: rangeHigh - 1m);
        }

        var tipStep = (preTip - entry) / Math.Max(1, tipBars);
        px = preTip;
        for (var i = 0; i < tipBars; i++)
        {
            px -= tipStep;
            Bar(px, floor: rangeLow + 1m, ceiling: rangeHigh - 1m);
        }

        for (var i = 0; i < flatAfter; i++)
        {
            Bar(entry, floor: rangeLow + 1m, ceiling: rangeHigh - 1m);
        }

        Add(entry + 1m, entry + half, rangeLow - probeBeyond, entry);
        return candles;
    }

    internal static List<Candle> BuildShortMigrated(
        decimal halfWidth,
        decimal barTr,
        decimal entryBelowHigh,
        int migrateBars,
        int tipBars,
        int flatAfter,
        decimal probeBeyond,
        int warmup)
    {
        var start = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        const decimal mid = 3000m;
        var half = barTr / 2m;
        var rangeLow = mid - halfWidth;
        var rangeHigh = mid + halfWidth;
        var entry = rangeHigh - entryBelowHigh;
        var candles = new List<Candle>();

        void Add(decimal o, decimal h, decimal l, decimal c)
        {
            var i = candles.Count;
            candles.Add(new Candle
            {
                SymbolId = 1, ExchangeId = 1, Timeframe = Timeframe.M5,
                OpenTimeUtc = start.AddMinutes(i * 5),
                CloseTimeUtc = start.AddMinutes(i * 5 + 5),
                Open = o, High = h, Low = l, Close = c,
                Volume = 100m + i, IsClosed = true, CreatedAtUtc = start
            });
        }

        void Bar(decimal center, decimal? floor = null, decimal? ceiling = null)
        {
            var c = center + ((candles.Count % 2 == 0) ? 0.5m : -0.5m);
            var lo = c - half;
            var hi = c + half;
            if (floor is not null)
            {
                lo = Math.Max(lo, floor.Value);
            }

            if (ceiling is not null)
            {
                hi = Math.Min(hi, ceiling.Value);
            }

            if (hi <= lo)
            {
                hi = lo + 1m;
            }

            Add(c - 0.5m, hi, lo, c + 0.5m);
        }

        for (var i = 0; i < warmup; i++)
        {
            Bar(mid);
        }

        var preTip = entry - Math.Max(28m, halfWidth * 0.22m);
        var migrateStep = (preTip - mid) / migrateBars;
        var px = mid;
        for (var i = 0; i < migrateBars; i++)
        {
            px += migrateStep;
            Bar(px);
        }

        Add(preTip - 0.5m, Math.Min(rangeHigh - 1m, preTip + half), rangeLow, preTip + 0.5m);
        for (var i = 0; i < 4; i++)
        {
            Bar(preTip, floor: rangeLow + 1m, ceiling: rangeHigh - 1m);
        }

        Add(preTip + 0.5m, rangeHigh, Math.Max(rangeLow + 1m, preTip - half), preTip - 0.5m);
        for (var i = 0; i < 4; i++)
        {
            Bar(preTip, floor: rangeLow + 1m, ceiling: rangeHigh - 1m);
        }

        var tipStep = (entry - preTip) / Math.Max(1, tipBars);
        px = preTip;
        for (var i = 0; i < tipBars; i++)
        {
            px += tipStep;
            Bar(px, floor: rangeLow + 1m, ceiling: rangeHigh - 1m);
        }

        for (var i = 0; i < flatAfter; i++)
        {
            Bar(entry, floor: rangeLow + 1m, ceiling: rangeHigh - 1m);
        }

        Add(entry - 1m, rangeHigh + probeBeyond, entry - half, entry);
        return candles;
    }

    private static Candle CloneCandle(
        Candle source,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        int openOffsetMinutes = 0) =>
        new()
        {
            SymbolId = source.SymbolId,
            ExchangeId = source.ExchangeId,
            Timeframe = source.Timeframe,
            OpenTimeUtc = source.OpenTimeUtc.AddMinutes(openOffsetMinutes),
            CloseTimeUtc = source.CloseTimeUtc.AddMinutes(openOffsetMinutes),
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = source.Volume,
            IsClosed = source.IsClosed,
            CreatedAtUtc = source.CreatedAtUtc
        };

    private static Candle CloneCandleExact(Candle source) =>
        CloneCandle(source, source.Open, source.High, source.Low, source.Close);
}
