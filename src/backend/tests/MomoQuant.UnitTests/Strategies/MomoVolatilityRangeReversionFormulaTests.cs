using System.Text.Json;
using MomoQuant.Application.Strategies.MomoRange;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A1C — Range Reversion default-contract formula evidence.
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
        Assert.True(candidate.StopLoss < candidate.EntryPrice);
        Assert.True(candidate.TakeProfit > candidate.EntryPrice);
        Assert.Equal(3000m, candidate.TakeProfit);
        Assert.True(candidate.RewardRisk >= 1.25m);
        Assert.True(candidate.Strength >= 65m);
        Assert.False(string.IsNullOrWhiteSpace(candidate.SetupFingerprint));
    }

    [Fact]
    public void ValidShort_WithCompleteDefaults()
    {
        var candles = BuildValidShort();
        var (candidate, reason) = Eval(candles, Defaults());
        Assert.NotNull(candidate);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.EntryConfirmed, reason);
        Assert.Equal(TradeDirection.Short, candidate!.Direction);
        Assert.True(candidate.StopLoss > candidate.EntryPrice);
        Assert.True(candidate.TakeProfit < candidate.EntryPrice);
        Assert.Equal(3000m, candidate.TakeProfit);
        Assert.True(candidate.RewardRisk >= 1.25m);
        Assert.True(candidate.Strength >= 65m);
        Assert.False(string.IsNullOrWhiteSpace(candidate.SetupFingerprint));
    }

    [Fact]
    public void ExactLongGeometryAndStrengthBreakdown()
    {
        var (candidate, _) = Eval(BuildValidLong(), Defaults());
        Assert.NotNull(candidate);
        using var doc = JsonDocument.Parse(candidate!.RawDataJson);
        var root = doc.RootElement;
        Assert.Equal(3150m, root.GetProperty("rangeHigh").GetDecimal());
        Assert.Equal(2850m, root.GetProperty("rangeLow").GetDecimal());
        Assert.Equal(3000m, root.GetProperty("rangeMidpoint").GetDecimal());
        Assert.Equal(candidate.EntryPrice, root.GetProperty("entry").GetDecimal());
        Assert.Equal(candidate.StopLoss, root.GetProperty("stop").GetDecimal());
        Assert.Equal(candidate.TakeProfit, root.GetProperty("takeProfit").GetDecimal());
        Assert.True(candidate.EntryPrice > 2850m);
        Assert.True(candidate.StopLoss < candidate.EntryPrice);
        Assert.Equal(3000m, candidate.TakeProfit);

        var breakdown = root.GetProperty("strengthBreakdown");
        var total =
            breakdown.GetProperty("rangeQuality").GetDecimal()
            + breakdown.GetProperty("volatilityQuality").GetDecimal()
            + breakdown.GetProperty("rsiExtremity").GetDecimal()
            + breakdown.GetProperty("wickQuality").GetDecimal()
            + breakdown.GetProperty("rewardRiskQuality").GetDecimal()
            + breakdown.GetProperty("trendFlatness").GetDecimal();
        Assert.Equal(breakdown.GetProperty("total").GetDecimal(), total);
        Assert.Equal(candidate.Strength, Math.Clamp(total, 0m, 100m));
        Assert.True(candidate.Strength >= 65m);
    }

    [Fact]
    public void ExactShortGeometryAndStrengthBreakdown()
    {
        var (candidate, _) = Eval(BuildValidShort(), Defaults());
        Assert.NotNull(candidate);
        using var doc = JsonDocument.Parse(candidate!.RawDataJson);
        var root = doc.RootElement;
        Assert.Equal(3150m, root.GetProperty("rangeHigh").GetDecimal());
        Assert.Equal(2850m, root.GetProperty("rangeLow").GetDecimal());
        Assert.Equal(3000m, root.GetProperty("rangeMidpoint").GetDecimal());
        Assert.True(candidate.EntryPrice < 3150m);
        Assert.True(candidate.StopLoss > candidate.EntryPrice);
        Assert.Equal(3000m, candidate.TakeProfit);

        var breakdown = root.GetProperty("strengthBreakdown");
        var total =
            breakdown.GetProperty("rangeQuality").GetDecimal()
            + breakdown.GetProperty("volatilityQuality").GetDecimal()
            + breakdown.GetProperty("rsiExtremity").GetDecimal()
            + breakdown.GetProperty("wickQuality").GetDecimal()
            + breakdown.GetProperty("rewardRiskQuality").GetDecimal()
            + breakdown.GetProperty("trendFlatness").GetDecimal();
        Assert.Equal(breakdown.GetProperty("total").GetDecimal(), total);
        Assert.Equal(candidate.Strength, Math.Clamp(total, 0m, 100m));
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
        var candles = BuildValidLong();
        var (baseline, _) = Eval(candles, Defaults());
        Assert.NotNull(baseline);
        using var doc = JsonDocument.Parse(baseline!.RawDataJson);
        var atr = doc.RootElement.GetProperty("fastAtr").GetDecimal();
        var tolerance = 0.15m * atr;
        var rangeLow = 2850m;
        var exact = rangeLow - tolerance;
        var last = candles[^1];
        candles[^1] = CloneCandle(last, close: last.Close, high: last.High, low: exact, open: last.Open);
        var (c, reason) = Eval(candles, Defaults());
        Assert.NotNull(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.EntryConfirmed, reason);
    }

    [Fact]
    public void PenetrationJustOutsideTolerance_Rejected()
    {
        var candles = BuildValidLong();
        var (baseline, _) = Eval(candles, Defaults());
        Assert.NotNull(baseline);
        using var doc = JsonDocument.Parse(baseline!.RawDataJson);
        // Deeper wick raises event ATR and expands tolerance; epsilon must exceed that feedback.
        var atr = doc.RootElement.GetProperty("fastAtr").GetDecimal();
        var tolerance = 0.15m * atr;
        var rangeLow = 2850m;
        var justOutside = rangeLow - tolerance - 0.50m;
        var last = candles[^1];
        candles[^1] = CloneCandle(last, close: last.Close, high: last.High, low: justOutside, open: last.Open);
        var (c, reason) = Eval(candles, Defaults());
        Assert.Null(c);
        Assert.Equal(MomoVolatilityRangeRejectionCodes.BoundaryPenetrationExceeded, reason);
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
        var candles = BuildValidLong();
        // Earliest expansion-window bar closes below prior low; later tip bars stay at entry so RSI remains oversold.
        var idx = candles.Count - 6;
        var bar = candles[idx];
        candles[idx] = CloneCandle(bar, close: 2849.5m, high: Math.Max(bar.High, 2860m), low: 2849.5m, open: 2855m);
        var last = candles[^1];
        candles[^1] = CloneCandle(last, close: last.Close, high: last.High, low: 2847.5m, open: last.Open);
        // Isolate expansion eligibility from the strength penalty of the rewritten range low.
        var p = Defaults();
        p["minStrength"] = "50";
        var (c, reason) = Eval(candles, p);
        Assert.True(c is not null, $"expected candidate, got {reason}");
        Assert.Equal(MomoVolatilityRangeRejectionCodes.EntryConfirmed, reason);
        Assert.NotEqual(MomoVolatilityRangeRejectionCodes.TrendFilterFailed, reason);
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

    [Fact]
    public void SameTime_FutureCandleMutation_HasNoEffect()
    {
        var throughT = BuildValidLong();
        var (first, reason1) = Eval(throughT, Defaults());
        Assert.NotNull(first);

        var mutated = throughT.Select(CloneCandleExact).ToList();
        var last = mutated[^1];
        mutated.Add(CloneCandle(
            last,
            close: last.Close + 500m,
            high: last.High + 500m,
            low: last.Low + 500m,
            open: last.Open + 500m,
            openOffsetMinutes: 5));

        var (second, reason2) = Eval(throughT, Defaults());
        Assert.NotNull(second);
        Assert.Equal(reason1, reason2);
        Assert.Equal(first!.Direction, second!.Direction);
        Assert.Equal(first.EntryPrice, second.EntryPrice);
        Assert.Equal(first.StopLoss, second.StopLoss);
        Assert.Equal(first.TakeProfit, second.TakeProfit);
        Assert.Equal(first.Strength, second.Strength);
        Assert.Equal(first.SetupFingerprint, second.SetupFingerprint);
        Assert.Equal(first.RawDataJson, second.RawDataJson);
        Assert.True(mutated.Count > throughT.Count);
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
