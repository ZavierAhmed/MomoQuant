using MomoQuant.Application.Strategies.BbLiquiditySweep;
using MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.UnitTests.Strategies;

public class BbLiquiditySweepDiagnosticsTests
{
    [Fact]
    public void FunnelTracker_RecordsStagedRejectionBreakdown()
    {
        var tracker = new BbLiquiditySweepFunnelTracker();
        tracker.Reset(BbStrategyStrictnessProfile.BalancedResearch, "MOMO_APPROXIMATION", false);

        tracker.RecordCandleMetrics(new BbLiquiditySweepCandleMetrics
        {
            InAllowedSession = true,
            UpperBbWickBreak = true,
            StagedRejectionCode = BbLiquiditySweepRejectionCodes.NoLiquiditySweep
        });

        var snapshot = tracker.GetSnapshot();

        Assert.Equal(1, snapshot.Evaluations);
        Assert.Equal(1, snapshot.BollingerBandUpperWickBreaks);
        Assert.Equal(BbLiquiditySweepRejectionCodes.NoLiquiditySweep, snapshot.TopNoTradeReason);
        Assert.Equal(1, snapshot.NoTradeReasonBreakdown[BbLiquiditySweepRejectionCodes.NoLiquiditySweep]);
    }

    [Fact]
    public void WhyZeroTradesAnalyzer_WarnsWhenBbSweepsExistButNoLiquiditySweeps()
    {
        var funnel = new BbLiquiditySweepFunnelCounts
        {
            BollingerBandLowerWickBreaks = 12,
            BuySideLiquiditySweeps = 0,
            SellSideLiquiditySweeps = 0,
            OneMinuteLiquidityLevelsDetected = 4
        };

        var analysis = BbWhyZeroTradesAnalyzer.Analyze(funnel, useRsiFilter: false);

        Assert.Contains("liquidity level was close enough", analysis, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BalancedResearch_IsLessStrictThanOriginalStrict()
    {
        var balanced = BbLiquiditySweepParameters.ApplyStrictnessProfile(BbStrategyStrictnessProfile.BalancedResearch);
        var strict = BbLiquiditySweepParameters.ApplyStrictnessProfile(BbStrategyStrictnessProfile.OriginalStrict);

        Assert.True(balanced.MinRewardRisk < strict.MinRewardRisk);
        Assert.False(balanced.RequireCloseBackInsideBb);
        Assert.True(strict.RequireCloseBackInsideBb);
        Assert.True(balanced.MaxDistanceFromLiquidityAtrMultiplier > strict.MaxDistanceFromLiquidityAtrMultiplier);
    }

    [Fact]
    public void DetectorCalibrationProfile_CountsWithoutTradeCreation()
    {
        var profile = BbLiquiditySweepParameters.ApplyStrictnessProfile(BbStrategyStrictnessProfile.DetectorCalibration);

        Assert.False(profile.AllowTradeCreation);
        Assert.False(profile.UseSessionFilter);
        Assert.Equal(0m, profile.MinRewardRisk);
    }

    [Fact]
    public void LiquidityEngine_DetectsSingleSwingLevelsWhenEnabled()
    {
        var engine = new MomoLiquidityLineEngine();
        var candles = BuildSwingCandles();
        var parameters = BbLiquiditySweepParameters.ApplyStrictnessProfile(BbStrategyStrictnessProfile.BalancedResearch);

        var levels = engine.CalculateLtfLiquidityLevels(candles, "1m", parameters);

        Assert.NotEmpty(levels);
    }

    [Fact]
    public void LiquiditySweepDetector_DetectsWickCrossAndCloseBack()
    {
        var detector = new LiquiditySweepDetector();
        var level = new LiquidityLevelDto
        {
            Id = "sell-1",
            Timeframe = "3m",
            Direction = LiquidityDirection.SellSideLiquidity,
            Price = 100m,
            CreatedAtUtc = DateTime.UtcNow,
            ImplementationMode = "MOMO_APPROXIMATION",
            SourceIndicatorName = "#itsimpossible"
        };
        var bb = new BollingerBandsValueDto
        {
            TimeUtc = DateTime.UtcNow,
            Middle = 101m,
            Upper = 103m,
            Lower = 99m,
            Bandwidth = 4m,
            PercentB = 0.5m
        };
        var candle = new Candle
        {
            Id = 1,
            SymbolId = 1,
            Open = 100.5m,
            Close = 100.4m,
            High = 100.6m,
            Low = 98.5m,
            Volume = 100m,
            OpenTimeUtc = DateTime.UtcNow.AddMinutes(-3),
            CloseTimeUtc = DateTime.UtcNow,
            IsClosed = true
        };

        var signal = detector.DetectLongSweep(candle, level, bb, requireSweepOutsideBb: true, requireCloseBackInsideBb: false, requireCloseBackAcrossLiquidityLine: true);

        Assert.True(signal.IsValidSweep);
        Assert.True(signal.ClosedBackAcrossLiquidityLine);
    }

    [Fact]
    public void RejectionCodes_DoNotUseGenericSetupMessage()
    {
        var reason = BbLiquiditySweepRejectionCodes.ToDisplayReason(BbLiquiditySweepRejectionCodes.NoBollingerBandSweep);

        Assert.DoesNotContain("No valid entry setup met strategy conditions", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FunnelCounts_BuildPipelineSummary_ShowsZeroSweepMessage()
    {
        var funnel = new BbLiquiditySweepFunnelCounts();

        Assert.Equal("BB sweeps 0 → no setup.", funnel.BuildPipelineSummary());
    }

    private static List<Candle> BuildSwingCandles()
    {
        var candles = new List<Candle>();
        decimal price = 100m;
        for (var i = 0; i < 40; i++)
        {
            var swing = i % 7 == 3 ? 2m : -0.5m;
            candles.Add(new Candle
            {
                Id = i + 1,
                SymbolId = 1,
                Open = price,
                Close = price + swing,
                High = price + Math.Abs(swing) + 1m,
                Low = price - Math.Abs(swing) - 1m,
                Volume = 100m,
                OpenTimeUtc = DateTime.UtcNow.AddMinutes(-i),
                CloseTimeUtc = DateTime.UtcNow.AddMinutes(-i + 1),
                IsClosed = true
            });
            price += swing;
        }

        return candles;
    }
}
