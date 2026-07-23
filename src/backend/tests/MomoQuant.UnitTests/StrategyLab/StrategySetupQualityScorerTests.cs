using MomoQuant.Application.Strategies.PriceStructure.Dtos;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.StrategyLab.Confidence;
using MomoQuant.Application.StrategyLab.Dtos;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.StrategyLab;

public sealed class StrategySetupQualityScorerTests
{
    private readonly StrategySetupQualityScorer _scorer = new();

    [Fact]
    public void Stronger_breakout_scores_higher_than_marginal_breakout()
    {
        var strong = ScoreBreakout(beyondClose: 0.40m, bodyRatio: 0.85m);
        var weak = ScoreBreakout(beyondClose: 0.01m, bodyRatio: 0.20m);

        Assert.True(strong.Score > weak.Score, $"Strong={strong.Score} Weak={weak.Score}");
        Assert.Equal(StrategySetupQualityScorer.ModelVersion, strong.ModelVersion);
        Assert.Equal(Math.Round(strong.Components.Sum(c => c.Score), 2), strong.Score);
    }

    [Fact]
    public void Identical_setup_is_deterministic()
    {
        var a = ScoreBreakout(beyondClose: 0.20m, bodyRatio: 0.6m);
        var b = ScoreBreakout(beyondClose: 0.20m, bodyRatio: 0.6m);
        Assert.Equal(a.Score, b.Score);
        Assert.Equal(
            StrategySetupQualityScorer.SerializeComponents(a.Components),
            StrategySetupQualityScorer.SerializeComponents(b.Components));
    }

    [Fact]
    public void Score_clamped_0_to_100_and_components_sum_to_score()
    {
        var result = ScoreBreakout(beyondClose: 1.0m, bodyRatio: 0.95m);
        Assert.InRange(result.Score, 0m, 100m);
        Assert.Equal(Math.Round(result.Components.Sum(c => c.Score), 2), result.Score);
    }

    [Fact]
    public void Clean_liquidity_sweep_scores_higher_than_marginal()
    {
        var clean = ScoreLiquidity(sweepDistPct: 0.20m, reclaimBeyondPct: 0.18m, sameCandle: true, priorTouches: 0);
        var marginal = ScoreLiquidity(sweepDistPct: 0.01m, reclaimBeyondPct: 0.01m, sameCandle: false, priorTouches: 4);

        Assert.True(clean.Score > marginal.Score, $"Clean={clean.Score} Marginal={marginal.Score}");
    }

    [Fact]
    public void Fresh_liquidity_level_scores_higher_than_repeatedly_tested()
    {
        var fresh = ScoreLiquidity(sweepDistPct: 0.15m, reclaimBeyondPct: 0.12m, sameCandle: true, priorTouches: 0);
        var stale = ScoreLiquidity(sweepDistPct: 0.15m, reclaimBeyondPct: 0.12m, sameCandle: true, priorTouches: 6);
        var freshComponent = fresh.Components.First(c => c.Key == "levelFreshness").Score;
        var staleComponent = stale.Components.First(c => c.Key == "levelFreshness").Score;
        Assert.True(
            freshComponent > staleComponent,
            $"FreshLevel={freshComponent} StaleLevel={staleComponent} FreshReason={fresh.Components.First(c => c.Key == "levelFreshness").Reason} StaleReason={stale.Components.First(c => c.Key == "levelFreshness").Reason}");
        Assert.Equal(10m, freshComponent);
        Assert.Equal(0m, staleComponent);
    }

    [Fact]
    public void All_same_score_triggers_ConfidenceModelDegenerate()
    {
        var scores = Enumerable.Repeat(70m, 25).ToList();
        var diagnostics = ScoreDistributionCalculator.Build(scores, "confidence");
        Assert.Equal(1, diagnostics.UniqueScoreCount);
        Assert.Equal("ConfidenceModelDegenerate", diagnostics.DegenerateWarningCode);
    }

    [Fact]
    public void PnL_percent_uses_initial_balance_not_absolute_pnl()
    {
        var candidates = new List<StrategyResearchCandidate>
        {
            Closed(-21098.24m)
        };

        var summary = StrategyLabPerformanceCalculator.BuildSummary(
            candidates,
            new StrategyOpportunityMetricsDto(),
            StrategyEvidenceQuality.Medium,
            initialBalance: 10000m);

        Assert.Equal(-21098.24m, summary.NetPnl);
        Assert.Equal(-210.9824m, summary.PnlPercent);
        Assert.False(summary.PortfolioMetricsAvailable);
        Assert.Contains("Independent", summary.NetPnlLabel);
        Assert.Contains(summary.MetricWarnings, w => w.Contains("Independent candidate outcomes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NetPnl_1000_on_10000_is_10_percent()
    {
        var summary = StrategyLabPerformanceCalculator.BuildSummary(
            [Closed(1000m)],
            new StrategyOpportunityMetricsDto(),
            StrategyEvidenceQuality.Medium,
            10000m);
        Assert.Equal(10m, summary.PnlPercent);
    }

    [Fact]
    public void Cleaner_retest_scores_higher_than_deep_invalidating_retest()
    {
        var clean = ScoreBreakoutWithRetest(penetrationPct: 0.02m, retestCloseDistancePct: 0.05m);
        var deep = ScoreBreakoutWithRetest(penetrationPct: 0.80m, retestCloseDistancePct: 0.60m);
        var cleanRetest = clean.Components.First(c => c.Key == "retestQuality").Score;
        var deepRetest = deep.Components.First(c => c.Key == "retestQuality").Score;
        Assert.True(cleanRetest > deepRetest, $"Clean={cleanRetest} Deep={deepRetest}");
    }

    [Fact]
    public void Stronger_confirmation_scores_higher_than_weak_confirmation()
    {
        var strong = ScoreBreakoutWithConfirmation(bodyRatio: 0.85m, bullish: true);
        var weak = ScoreBreakoutWithConfirmation(bodyRatio: 0.15m, bullish: false);
        var strongConf = strong.Components.First(c => c.Key == "confirmationQuality").Score;
        var weakConf = weak.Components.First(c => c.Key == "confirmationQuality").Score;
        Assert.True(strongConf > weakConf, $"Strong={strongConf} Weak={weakConf}");
    }

    [Fact]
    public void Confidence_context_has_no_outcome_mfe_mae_or_future_fields()
    {
        var props = typeof(CandidateConfidenceContext).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("RawNetPnl", props);
        Assert.DoesNotContain("Mfe", props);
        Assert.DoesNotContain("Mae", props);
        Assert.DoesNotContain("RawOutcomeStatus", props);
        Assert.DoesNotContain("Winner", props);
        Assert.Contains("CandlesThroughSetup", props);
    }

    [Fact]
    public void Max_drawdown_uses_peak_to_trough_equity_percent()
    {
        var summary = StrategyLabPerformanceCalculator.BuildSummary(
            [
                Closed(1000m, exitOffsetMinutes: 1),
                Closed(-500m, exitOffsetMinutes: 2),
                Closed(-300m, exitOffsetMinutes: 3)
            ],
            new StrategyOpportunityMetricsDto(),
            StrategyEvidenceQuality.Medium,
            initialBalance: 10000m);

        // Equity path: 10000 -> 11000 peak -> 10500 -> 10200
        // Max DD = (11000-10200)/11000*100 ≈ 7.2727%
        Assert.Equal(7.27m, summary.MaxDrawdownPercent);
        Assert.Contains("Independent Setup Sequence", summary.MaxDrawdownLabel);
        Assert.False(summary.PortfolioMetricsAvailable);
    }

    private CandidateConfidenceResult ScoreBreakout(decimal beyondClose, decimal bodyRatio)
    {
        var level = 100m;
        var open = 100.10m;
        var close = level * (1m + beyondClose / 100m);
        // Force body/range ≈ bodyRatio
        var range = Math.Abs(close - open) / Math.Max(bodyRatio, 0.05m);
        var high = Math.Max(open, close) + (range - Math.Abs(close - open)) * 0.5m;
        var low = Math.Min(open, close) - (range - Math.Abs(close - open)) * 0.5m;

        var candles = BuildCandlesAround(level, open, high, low, close, TradeDirection.Long);
        return ScoreBreakoutCandles(candles, level, close, low);
    }

    private CandidateConfidenceResult ScoreBreakoutWithRetest(decimal penetrationPct, decimal retestCloseDistancePct)
    {
        var level = 100m;
        var close = level * 1.002m;
        var open = 100.10m;
        var range = Math.Abs(close - open) / 0.7m;
        var high = Math.Max(open, close) + (range - Math.Abs(close - open)) * 0.5m;
        var low = Math.Min(open, close) - (range - Math.Abs(close - open)) * 0.5m;
        var candles = BuildCandlesAround(level, open, high, low, close, TradeDirection.Long);

        var retestLow = level * (1m - penetrationPct / 100m);
        var retestClose = level * (1m + retestCloseDistancePct / 100m);
        candles[14] = CandleAt(
            candles[14].OpenTimeUtc,
            retestClose + 0.02m,
            Math.Max(retestClose + 0.05m, level + 0.05m),
            retestLow,
            retestClose);

        return ScoreBreakoutCandles(candles, level, close, low);
    }

    private CandidateConfidenceResult ScoreBreakoutWithConfirmation(decimal bodyRatio, bool bullish)
    {
        var level = 100m;
        var close = level * 1.002m;
        var open = 100.10m;
        var range = Math.Abs(close - open) / 0.7m;
        var high = Math.Max(open, close) + (range - Math.Abs(close - open)) * 0.5m;
        var low = Math.Min(open, close) - (range - Math.Abs(close - open)) * 0.5m;
        var candles = BuildCandlesAround(level, open, high, low, close, TradeDirection.Long);

        var confirmOpen = bullish ? level + 0.02m : level + 0.20m;
        var confirmClose = bullish ? level + 0.20m : level + 0.02m;
        var body = Math.Abs(confirmClose - confirmOpen);
        var confirmRange = body / Math.Max(bodyRatio, 0.05m);
        var confirmHigh = Math.Max(confirmOpen, confirmClose) + (confirmRange - body) * 0.3m;
        var confirmLow = Math.Min(confirmOpen, confirmClose) - (confirmRange - body) * 0.7m;
        candles[15] = CandleAt(candles[15].OpenTimeUtc, confirmOpen, confirmHigh, confirmLow, confirmClose);

        return ScoreBreakoutCandles(candles, level, close, low);
    }

    private CandidateConfidenceResult ScoreBreakoutCandles(
        List<Candle> candles,
        decimal level,
        decimal entry,
        decimal breakoutLow)
    {
        return _scorer.Score(new CandidateConfidenceContext
        {
            StrategyCode = StrategyCodes.PriceStructureBreakoutRetest,
            Direction = TradeDirection.Long,
            EntryPrice = entry,
            StopLoss = breakoutLow * 0.999m,
            Target1 = entry + (entry - breakoutLow * 0.999m) * 2m,
            RewardRisk = 2m,
            Structure = new PriceStructureSetupDto
            {
                SetupType = "BreakoutRetest",
                Direction = "Long",
                BrokenOrSweptLevel = level,
                SwingIndex = 5,
                BreakoutIndex = 12,
                RetestIndex = 14,
                ConfirmationIndex = 15
            },
            CandlesThroughSetup = candles
        });
    }

    private CandidateConfidenceResult ScoreLiquidity(
        decimal sweepDistPct,
        decimal reclaimBeyondPct,
        bool sameCandle,
        int priorTouches)
    {
        var level = 100m;
        var candles = new List<Candle>();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 20; i++)
        {
            // Keep default candles away from the level so level-touch counts are controlled.
            // Touch window after swing (idx 8): idxs 9..(9+priorTouches-1), never including sweep/reclaim.
            var touch = priorTouches > 0 && i >= 9 && i < Math.Min(15, 9 + priorTouches);
            candles.Add(touch
                ? CandleAt(t0.AddMinutes(i * 15), 99.95m, 100.05m, 99.9m, 100.02m)
                : CandleAt(t0.AddMinutes(i * 15), 98.5m, 99.2m, 98.4m, 99.0m));
        }

        candles[8] = CandleAt(t0.AddMinutes(8 * 15), 100.2m, 100.4m, 100.0m, 100.3m); // swing
        // Ensure bars after swing that are not intentional prior touches stay clear of the level.
        for (var i = 9; i < 15; i++)
        {
            var shouldTouch = priorTouches > 0 && i < 9 + priorTouches;
            if (!shouldTouch)
            {
                candles[i] = CandleAt(t0.AddMinutes(i * 15), 98.5m, 99.2m, 98.4m, 99.0m);
            }
        }

        var sweepLow = level * (1m - sweepDistPct / 100m);
        var reclaimClose = level * (1m + reclaimBeyondPct / 100m);
        candles[15] = CandleAt(t0.AddMinutes(15 * 15), 100.05m, 100.2m, sweepLow, reclaimClose);
        if (!sameCandle)
        {
            candles[16] = CandleAt(t0.AddMinutes(16 * 15), reclaimClose - 0.05m, reclaimClose + 0.05m, reclaimClose - 0.1m, reclaimClose);
        }

        return _scorer.Score(new CandidateConfidenceContext
        {
            StrategyCode = StrategyCodes.PriceStructureLiquiditySweepReclaim,
            Direction = TradeDirection.Long,
            EntryPrice = reclaimClose,
            StopLoss = sweepLow * 0.999m,
            Target1 = reclaimClose + 1m,
            RewardRisk = 2m,
            Structure = new PriceStructureSetupDto
            {
                SetupType = "LiquiditySweepReclaim",
                Direction = "Long",
                BrokenOrSweptLevel = level,
                SwingIndex = 8,
                BreakoutIndex = 15,
                RetestIndex = sameCandle ? 15 : 16,
                ConfirmationIndex = sameCandle ? 15 : 16
            },
            CandlesThroughSetup = candles
        });
    }

    private static List<Candle> BuildCandlesAround(decimal level, decimal open, decimal high, decimal low, decimal close, TradeDirection _)
    {
        var candles = new List<Candle>();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 16; i++)
        {
            candles.Add(CandleAt(t0.AddMinutes(i * 15), level - 0.2m, level + 0.2m, level - 0.3m, level));
        }

        candles[5] = CandleAt(t0.AddMinutes(5 * 15), level - 0.1m, level + 0.1m, level - 0.5m, level - 0.05m);
        candles[12] = CandleAt(t0.AddMinutes(12 * 15), open, high, low, close);
        candles[14] = CandleAt(t0.AddMinutes(14 * 15), close - 0.05m, close + 0.05m, level - 0.05m, level + 0.02m);
        candles[15] = CandleAt(t0.AddMinutes(15 * 15), level + 0.02m, close + 0.1m, level, close + 0.05m);
        return candles;
    }

    private static Candle CandleAt(DateTime openUtc, decimal open, decimal high, decimal low, decimal close) => new()
    {
        OpenTimeUtc = openUtc,
        CloseTimeUtc = openUtc.AddMinutes(15).AddTicks(-1),
        Open = open,
        High = high,
        Low = low,
        Close = close,
        Volume = 100,
        ExchangeId = 1,
        SymbolId = 1,
        Timeframe = Timeframe.M15,
        IsClosed = true,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static StrategyResearchCandidate Closed(decimal pnl, int exitOffsetMinutes = 0) => new()
    {
        StrategyLabRunId = 1,
        StrategyCode = StrategyCodes.PriceStructureBreakoutRetest,
        Direction = TradeDirection.Long,
        SetupDetectedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ProposedEntryTimeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ProposedEntryPrice = 100,
        StopLoss = 99,
        Target1 = 102,
        RewardRisk = 2,
        CandidateStatus = StrategyResearchCandidateStatus.Closed,
        RawOutcomeStatus = pnl >= 0 ? RawOutcomeStatus.Winner : RawOutcomeStatus.Loser,
        RawNetPnl = pnl,
        RawExitTimeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(exitOffsetMinutes),
        CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}
