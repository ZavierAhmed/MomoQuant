using System.Text.Json;
using MomoQuant.Application.Strategies.PriceStructure.Dtos;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.StrategyLab.Confidence;

/// <summary>
/// Deterministic candidate-specific confidence model (0–100).
/// Uses only information available at candidate creation time.
/// Does not use MFE/MAE, winner/loser, future candles, exit price, or PnL.
/// </summary>
public sealed class StrategySetupQualityScorer : ICandidateConfidenceScorer
{
    public const string ModelVersion = "StrategySetupQuality/v1";
    public const decimal StrategyBaselineStrength = 70m;

    public CandidateConfidenceResult Score(CandidateConfidenceContext context)
    {
        if (context.StrategyCode == StrategyCodes.PriceStructureBreakoutRetest
            || string.Equals(context.Structure.SetupType, "BreakoutRetest", StringComparison.OrdinalIgnoreCase))
        {
            return ScoreBreakout(context);
        }

        if (context.StrategyCode == StrategyCodes.PriceStructureLiquiditySweepReclaim
            || string.Equals(context.Structure.SetupType, "LiquiditySweepReclaim", StringComparison.OrdinalIgnoreCase))
        {
            return ScoreLiquidity(context);
        }

        return new CandidateConfidenceResult
        {
            Score = 50m,
            ModelVersion = ModelVersion,
            Components =
            [
                Comp("fallback", "Fallback", 50m, 100m, "Unsupported strategy for StrategySetupQuality/v1; neutral score applied.")
            ],
            Explanation = "Unsupported strategy code — neutral score.",
            Warnings = ["UnsupportedStrategyForSetupQuality"]
        };
    }

    private static CandidateConfidenceResult ScoreBreakout(CandidateConfidenceContext context)
    {
        var candles = context.CandlesThroughSetup;
        var structure = context.Structure;
        var level = structure.BrokenOrSweptLevel;
        var breakoutIdx = structure.BreakoutIndex ?? InferIndex(candles, structure.BreakoutOrSweepTimeUtc);
        var retestIdx = structure.RetestIndex ?? InferIndex(candles, structure.RetestOrReclaimTimeUtc);
        var confirmIdx = structure.ConfirmationIndex ?? InferIndex(candles, structure.ConfirmationTimeUtc) ?? candles.Count - 1;
        var swingIdx = structure.SwingIndex ?? InferIndex(candles, structure.SwingTimeUtc);

        var breakout = SafeCandle(candles, breakoutIdx);
        var retest = SafeCandle(candles, retestIdx);
        var confirm = SafeCandle(candles, confirmIdx);

        var components = new List<CandidateConfidenceScoreComponent>
        {
            ScoreBreakoutQuality(context.Direction, level, breakout),
            ScoreRetestQuality(context.Direction, level, breakoutIdx, retestIdx, retest),
            ScoreConfirmationQuality(context.Direction, confirm, retest),
            ScoreStructureQuality(candles, swingIdx, level, context.Direction),
            ScoreSetupFreshness(breakoutIdx, retestIdx, maxBars: 20, maxPoints: 10m),
            ScoreTradeGeometry(context, maxPoints: 10m),
            ScoreContextConsistency(candles, confirmIdx, context.Direction, maxPoints: 5m)
        };

        return Finalize(components, "Breakout retest setup quality.");
    }

    private static CandidateConfidenceResult ScoreLiquidity(CandidateConfidenceContext context)
    {
        var candles = context.CandlesThroughSetup;
        var structure = context.Structure;
        var level = structure.BrokenOrSweptLevel;
        var swingIdx = structure.SwingIndex ?? InferIndex(candles, structure.SwingTimeUtc);
        var sweepIdx = structure.BreakoutIndex ?? InferIndex(candles, structure.BreakoutOrSweepTimeUtc);
        var reclaimIdx = structure.RetestIndex
            ?? structure.ConfirmationIndex
            ?? InferIndex(candles, structure.RetestOrReclaimTimeUtc)
            ?? InferIndex(candles, structure.ConfirmationTimeUtc);
        if (!reclaimIdx.HasValue && candles.Count > 0)
        {
            reclaimIdx = candles.Count - 1;
        }

        var sweep = SafeCandle(candles, sweepIdx);
        var reclaim = SafeCandle(candles, reclaimIdx);
        var sameCandle = sweepIdx.HasValue && reclaimIdx.HasValue && sweepIdx.Value == reclaimIdx.Value;

        var components = new List<CandidateConfidenceScoreComponent>
        {
            ScoreLiquidityLevelQuality(candles, swingIdx, level, context.Direction),
            ScoreSweepQuality(context.Direction, level, sweep),
            ScoreReclaimQuality(context.Direction, level, reclaim, sameCandle),
            ScoreRejectionCandleQuality(context.Direction, reclaim),
            ScoreLevelFreshness(candles, swingIdx, reclaimIdx, level, context.Direction, maxPoints: 10m),
            ScoreTradeGeometry(context, maxPoints: 5m)
        };

        return Finalize(components, "Liquidity sweep reclaim setup quality.");
    }

    private static CandidateConfidenceResult Finalize(
        IReadOnlyList<CandidateConfidenceScoreComponent> components,
        string explanation)
    {
        var total = Math.Clamp(Math.Round(components.Sum(c => c.Score), 2), 0m, 100m);
        var rounded = components.Select(c => c with { Score = Math.Round(c.Score, 2) }).ToList();
        // Adjust last component by tiny epsilon to keep sum == total after rounding
        var sumRounded = rounded.Sum(c => c.Score);
        if (rounded.Count > 0 && sumRounded != total)
        {
            var last = rounded[^1];
            rounded[^1] = last with { Score = Math.Clamp(last.Score + (total - sumRounded), 0m, last.Max) };
        }

        return new CandidateConfidenceResult
        {
            Score = total,
            ModelVersion = ModelVersion,
            Components = rounded,
            Explanation = $"{explanation} Total {total:0.##}/100."
        };
    }

    // ---- Breakout components ----

    private static CandidateConfidenceScoreComponent ScoreBreakoutQuality(
        TradeDirection direction, decimal level, Candle? breakout)
    {
        const decimal max = 20m;
        if (breakout is null || level <= 0)
        {
            return Comp("breakoutQuality", "Breakout Quality", 6m, max, "Missing breakout candle — low score.");
        }

        var range = Math.Max(breakout.High - breakout.Low, 0.0000001m);
        var body = Math.Abs(breakout.Close - breakout.Open);
        var bodyRatio = body / range;
        var closeLoc = (breakout.Close - breakout.Low) / range;
        if (direction == TradeDirection.Short)
        {
            closeLoc = (breakout.High - breakout.Close) / range;
        }

        var beyond = direction == TradeDirection.Long
            ? (breakout.Close - level) / level * 100m
            : (level - breakout.Close) / level * 100m;
        beyond = Math.Max(0m, beyond);

        var score = 0m;
        score += Clamp01(beyond / 0.35m) * 8m;           // close beyond level
        score += Clamp01(bodyRatio) * 6m;                // decisive body
        score += Clamp01(closeLoc) * 4m;                 // close location
        if (beyond <= 0.02m && bodyRatio < 0.35m)
        {
            score *= 0.55m; // weak wick-only bias
        }

        return Comp(
            "breakoutQuality",
            "Breakout Quality",
            score,
            max,
            $"Beyond={beyond:0.###}% body/range={bodyRatio:0.##} closeLoc={closeLoc:0.##}.");
    }

    private static CandidateConfidenceScoreComponent ScoreRetestQuality(
        TradeDirection direction,
        decimal level,
        int? breakoutIdx,
        int? retestIdx,
        Candle? retest)
    {
        const decimal max = 20m;
        if (retest is null || level <= 0)
        {
            return Comp("retestQuality", "Retest Quality", 6m, max, "Missing retest candle.");
        }

        var distancePct = Math.Abs(retest.Close - level) / level * 100m;
        var penetration = direction == TradeDirection.Long
            ? Math.Max(0m, (level - retest.Low) / level * 100m)
            : Math.Max(0m, (retest.High - level) / level * 100m);
        var bars = breakoutIdx.HasValue && retestIdx.HasValue
            ? Math.Max(0, retestIdx.Value - breakoutIdx.Value)
            : 10;

        var score = 0m;
        // Prefer close proximity to level without deep penetration
        score += (1m - Clamp01(distancePct / 0.40m)) * 8m;
        score += (1m - Clamp01(penetration / 0.50m)) * 8m;
        score += (1m - Clamp01(bars / 20m)) * 4m;

        return Comp(
            "retestQuality",
            "Retest Quality",
            score,
            max,
            $"Distance={distancePct:0.###}% penetration={penetration:0.###}% barsAfterBreakout={bars}.");
    }

    private static CandidateConfidenceScoreComponent ScoreConfirmationQuality(
        TradeDirection direction,
        Candle? confirm,
        Candle? prior)
    {
        const decimal max = 20m;
        if (confirm is null)
        {
            return Comp("confirmationQuality", "Confirmation Quality", 6m, max, "Missing confirmation candle.");
        }

        var range = Math.Max(confirm.High - confirm.Low, 0.0000001m);
        var body = Math.Abs(confirm.Close - confirm.Open);
        var bodyRatio = body / range;
        var bullish = confirm.Close > confirm.Open;
        var directionOk = direction == TradeDirection.Long ? bullish : !bullish;
        var closeLoc = direction == TradeDirection.Long
            ? (confirm.Close - confirm.Low) / range
            : (confirm.High - confirm.Close) / range;
        var rejectionWick = direction == TradeDirection.Long
            ? (Math.Min(confirm.Open, confirm.Close) - confirm.Low) / range
            : (confirm.High - Math.Max(confirm.Open, confirm.Close)) / range;

        var score = 0m;
        score += directionOk ? 6m : 1m;
        score += Clamp01(bodyRatio) * 6m;
        score += Clamp01(closeLoc) * 4m;
        score += Clamp01(rejectionWick) * 4m;
        if (prior is not null)
        {
            var continuation = direction == TradeDirection.Long
                ? confirm.Close > prior.Close
                : confirm.Close < prior.Close;
            if (continuation)
            {
                score = Math.Min(max, score + 0m); // already embedded via closeLoc/body
            }
        }

        return Comp(
            "confirmationQuality",
            "Confirmation Quality",
            Math.Min(max, score),
            max,
            $"DirectionOk={directionOk} body/range={bodyRatio:0.##} closeLoc={closeLoc:0.##}.");
    }

    private static CandidateConfidenceScoreComponent ScoreStructureQuality(
        IReadOnlyList<Candle> candles,
        int? swingIdx,
        decimal level,
        TradeDirection direction)
    {
        const decimal max = 15m;
        if (!swingIdx.HasValue || swingIdx.Value < 0 || swingIdx.Value >= candles.Count || level <= 0)
        {
            return Comp("structureQuality", "Structure Quality", 5m, max, "Missing swing reference.");
        }

        var ageBars = Math.Max(0, candles.Count - 1 - swingIdx.Value);
        var freshness = 1m - Clamp01(ageBars / 120m);
        var touches = CountLevelTouches(candles, 0, swingIdx.Value, level, 0.15m);
        var significance = Math.Min(1m, (decimal)Math.Min(swingIdx.Value, 8) / 8m);

        var score = freshness * 6m + (1m - Clamp01((touches - 1) / 4m)) * 5m + significance * 4m;
        return Comp(
            "structureQuality",
            "Structure Quality",
            score,
            max,
            $"SwingAgeBars={ageBars} priorTouches={touches}.");
    }

    private static CandidateConfidenceScoreComponent ScoreSetupFreshness(
        int? fromIdx,
        int? toIdx,
        int maxBars,
        decimal maxPoints)
    {
        if (!fromIdx.HasValue || !toIdx.HasValue)
        {
            return Comp("setupFreshness", "Setup Freshness", maxPoints * 0.4m, maxPoints, "Timing unknown.");
        }

        var bars = Math.Max(0, toIdx.Value - fromIdx.Value);
        var score = (1m - Clamp01((decimal)bars / maxBars)) * maxPoints;
        return Comp("setupFreshness", "Setup Freshness", score, maxPoints, $"Bars between stages={bars}.");
    }

    private static CandidateConfidenceScoreComponent ScoreContextConsistency(
        IReadOnlyList<Candle> candles,
        int? confirmIdx,
        TradeDirection direction,
        decimal maxPoints)
    {
        if (!confirmIdx.HasValue || confirmIdx.Value < 2)
        {
            return Comp("contextConsistency", "Context Consistency", maxPoints * 0.4m, maxPoints, "Insufficient history.");
        }

        var window = candles.Skip(Math.Max(0, confirmIdx.Value - 4)).Take(5).ToList();
        if (window.Count < 2)
        {
            return Comp("contextConsistency", "Context Consistency", maxPoints * 0.4m, maxPoints, "Insufficient history.");
        }

        var ups = 0;
        for (var i = 1; i < window.Count; i++)
        {
            if (window[i].Close > window[i - 1].Close)
            {
                ups++;
            }
        }

        var bullishShare = (decimal)ups / (window.Count - 1);
        var aligned = direction == TradeDirection.Long ? bullishShare : 1m - bullishShare;
        return Comp(
            "contextConsistency",
            "Context Consistency",
            aligned * maxPoints,
            maxPoints,
            $"Micro-structure alignment={aligned:0.##}.");
    }

    // ---- Liquidity components ----

    private static CandidateConfidenceScoreComponent ScoreLiquidityLevelQuality(
        IReadOnlyList<Candle> candles,
        int? swingIdx,
        decimal level,
        TradeDirection direction)
    {
        const decimal max = 20m;
        if (!swingIdx.HasValue || level <= 0)
        {
            return Comp("liquidityLevelQuality", "Liquidity Level Quality", 6m, max, "Missing liquidity swing.");
        }

        var age = Math.Max(0, candles.Count - 1 - swingIdx.Value);
        var ageScore = (1m - Clamp01(age / 200m)) * 8m;
        var priorTouches = CountLevelTouches(candles, 0, swingIdx.Value, level, 0.12m);
        var unusedScore = (1m - Clamp01(priorTouches / 3m)) * 8m;
        var swing = candles[swingIdx.Value];
        var prominence = direction == TradeDirection.Long
            ? Math.Abs(swing.Low - level) / Math.Max(level, 0.0000001m) * 100m
            : Math.Abs(swing.High - level) / Math.Max(level, 0.0000001m) * 100m;
        var prominenceScore = (1m - Clamp01(prominence / 0.25m)) * 4m;

        return Comp(
            "liquidityLevelQuality",
            "Liquidity Level Quality",
            ageScore + unusedScore + prominenceScore,
            max,
            $"AgeBars={age} priorTouches={priorTouches}.");
    }

    private static CandidateConfidenceScoreComponent ScoreSweepQuality(
        TradeDirection direction,
        decimal level,
        Candle? sweep)
    {
        const decimal max = 25m;
        if (sweep is null || level <= 0)
        {
            return Comp("sweepQuality", "Sweep Quality", 8m, max, "Missing sweep candle.");
        }

        var sweepDist = direction == TradeDirection.Long
            ? Math.Max(0m, (level - sweep.Low) / level * 100m)
            : Math.Max(0m, (sweep.High - level) / level * 100m);
        var range = Math.Max(sweep.High - sweep.Low, 0.0000001m);
        var wick = direction == TradeDirection.Long
            ? (Math.Min(sweep.Open, sweep.Close) - sweep.Low) / range
            : (sweep.High - Math.Max(sweep.Open, sweep.Close)) / range;

        // Prefer decisive but not absurd sweeps
        var distScore = sweepDist <= 0.02m
            ? 4m
            : sweepDist <= 0.35m
                ? 12m + Clamp01((0.35m - sweepDist) / 0.35m) * 4m
                : Math.Max(4m, 10m - Clamp01((sweepDist - 0.35m) / 0.65m) * 6m);

        var score = distScore + Clamp01(wick) * 9m;
        return Comp(
            "sweepQuality",
            "Sweep Quality",
            Math.Min(max, score),
            max,
            $"SweepDist={sweepDist:0.###}% wickShare={wick:0.##}.");
    }

    private static CandidateConfidenceScoreComponent ScoreReclaimQuality(
        TradeDirection direction,
        decimal level,
        Candle? reclaim,
        bool sameCandle)
    {
        const decimal max = 25m;
        if (reclaim is null || level <= 0)
        {
            return Comp("reclaimQuality", "Reclaim Quality", 8m, max, "Missing reclaim candle.");
        }

        var beyond = direction == TradeDirection.Long
            ? (reclaim.Close - level) / level * 100m
            : (level - reclaim.Close) / level * 100m;
        beyond = Math.Max(0m, beyond);
        var range = Math.Max(reclaim.High - reclaim.Low, 0.0000001m);
        var closeLoc = direction == TradeDirection.Long
            ? (reclaim.Close - reclaim.Low) / range
            : (reclaim.High - reclaim.Close) / range;

        var score = Clamp01(beyond / 0.25m) * 12m + Clamp01(closeLoc) * 8m + (sameCandle ? 5m : 2m);
        return Comp(
            "reclaimQuality",
            "Reclaim Quality",
            Math.Min(max, score),
            max,
            $"CloseBeyond={beyond:0.###}% sameCandle={sameCandle} closeLoc={closeLoc:0.##}.");
    }

    private static CandidateConfidenceScoreComponent ScoreRejectionCandleQuality(
        TradeDirection direction,
        Candle? candle)
    {
        const decimal max = 15m;
        if (candle is null)
        {
            return Comp("rejectionCandleQuality", "Rejection Candle Quality", 5m, max, "Missing rejection candle.");
        }

        var range = Math.Max(candle.High - candle.Low, 0.0000001m);
        var body = Math.Abs(candle.Close - candle.Open) / range;
        var wick = direction == TradeDirection.Long
            ? (Math.Min(candle.Open, candle.Close) - candle.Low) / range
            : (candle.High - Math.Max(candle.Open, candle.Close)) / range;
        var dirOk = direction == TradeDirection.Long ? candle.Close >= candle.Open : candle.Close <= candle.Open;
        var score = Clamp01(wick) * 7m + Clamp01(body) * 5m + (dirOk ? 3m : 0m);
        return Comp(
            "rejectionCandleQuality",
            "Rejection Candle Quality",
            Math.Min(max, score),
            max,
            $"Wick={wick:0.##} body={body:0.##}.");
    }

    private static CandidateConfidenceScoreComponent ScoreLevelFreshness(
        IReadOnlyList<Candle> candles,
        int? swingIdx,
        int? eventIdx,
        decimal level,
        TradeDirection direction,
        decimal maxPoints)
    {
        if (!swingIdx.HasValue || !eventIdx.HasValue || level <= 0)
        {
            return Comp("levelFreshness", "Level Freshness", maxPoints * 0.4m, maxPoints, "Freshness unknown.");
        }

        var touches = CountLevelTouches(candles, swingIdx.Value + 1, eventIdx.Value, level, 0.12m);
        var score = (1m - Clamp01(touches / 3m)) * maxPoints;
        return Comp(
            "levelFreshness",
            "Level Freshness",
            score,
            maxPoints,
            $"Touches since swing={touches} (fresh preferred).");
    }

    private static CandidateConfidenceScoreComponent ScoreTradeGeometry(
        CandidateConfidenceContext context,
        decimal maxPoints)
    {
        var stopPct = context.EntryPrice > 0
            ? Math.Abs(context.EntryPrice - context.StopLoss) / context.EntryPrice * 100m
            : 0m;
        var rr = context.RewardRisk;

        // Ideal stop ~0.15–1.5%, RR >= 1.5
        var stopScore = stopPct <= 0
            ? 0m
            : stopPct < 0.08m
                ? maxPoints * 0.35m
                : stopPct <= 1.8m
                    ? maxPoints * 0.7m
                    : maxPoints * Math.Max(0.15m, 1m - Clamp01((stopPct - 1.8m) / 3m) * 0.55m);
        var rrScore = Clamp01(rr / 2.5m) * (maxPoints * 0.3m);
        var score = Math.Min(maxPoints, stopScore + rrScore);

        return Comp(
            "tradeGeometryQuality",
            "Trade Geometry Quality",
            score,
            maxPoints,
            $"Stop%={stopPct:0.###} R:R={rr:0.##}.");
    }

    // ---- helpers ----

    private static int CountLevelTouches(
        IReadOnlyList<Candle> candles,
        int fromInclusive,
        int toExclusive,
        decimal level,
        decimal tolerancePercent)
    {
        if (level <= 0 || candles.Count == 0)
        {
            return 0;
        }

        fromInclusive = Math.Clamp(fromInclusive, 0, candles.Count);
        toExclusive = Math.Clamp(toExclusive, 0, candles.Count);
        var tol = level * tolerancePercent / 100m;
        var count = 0;
        for (var i = fromInclusive; i < toExclusive; i++)
        {
            var c = candles[i];
            if (c.Low <= level + tol && c.High >= level - tol)
            {
                count++;
            }
        }

        return count;
    }

    private static Candle? SafeCandle(IReadOnlyList<Candle> candles, int? index)
    {
        if (!index.HasValue || index.Value < 0 || index.Value >= candles.Count)
        {
            return null;
        }

        return candles[index.Value];
    }

    private static int? InferIndex(IReadOnlyList<Candle> candles, DateTime? timeUtc)
    {
        if (!timeUtc.HasValue || candles.Count == 0)
        {
            return null;
        }

        for (var i = candles.Count - 1; i >= 0; i--)
        {
            if (candles[i].OpenTimeUtc <= timeUtc.Value && candles[i].CloseTimeUtc >= timeUtc.Value)
            {
                return i;
            }

            if (candles[i].CloseTimeUtc == timeUtc.Value || candles[i].OpenTimeUtc == timeUtc.Value)
            {
                return i;
            }
        }

        // nearest prior close
        for (var i = candles.Count - 1; i >= 0; i--)
        {
            if (candles[i].CloseTimeUtc <= timeUtc.Value)
            {
                return i;
            }
        }

        return null;
    }

    private static decimal Clamp01(decimal value) => Math.Clamp(value, 0m, 1m);

    private static CandidateConfidenceScoreComponent Comp(
        string key,
        string label,
        decimal score,
        decimal max,
        string reason) =>
        new()
        {
            Key = key,
            Label = label,
            Score = Math.Clamp(score, 0m, max),
            Max = max,
            Reason = reason
        };

    public static string SerializeComponents(IReadOnlyList<CandidateConfidenceScoreComponent> components)
    {
        var map = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var c in components)
        {
            map[c.Key] = new
            {
                score = c.Score,
                max = c.Max,
                label = c.Label,
                reason = c.Reason
            };
        }

        return JsonSerializer.Serialize(map, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
    }
}
