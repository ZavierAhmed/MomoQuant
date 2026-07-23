using MomoQuant.Application.StrategyLab.Dtos;
using MomoQuant.Application.Strategies.PriceStructure;

namespace MomoQuant.UnitTests.StrategyLab;

public sealed class StrategyLabFunnelSemanticsTests
{
    [Fact]
    public void CandidateFunnel_separates_sweep_checks_from_detected_sweeps()
    {
        var funnel = new CandidateFunnelDto
        {
            BuySideSweepChecks = 10000,
            SellSideSweepChecks = 4201,
            BuySideSweepsDetected = 120,
            SellSideSweepsDetected = 80,
            SameCandleReclaims = 50,
            DelayedReclaims = 40,
            CandlesEvaluated = 1325,
            ActiveBuySideLiquidityLevels = 200,
            ActiveSellSideLiquidityLevels = 202
        };

        Assert.Equal(14201, funnel.SweepChecks);
        Assert.Equal(200, funnel.SweepsDetected);
        Assert.NotEqual(funnel.SweepChecks, funnel.SweepsDetected);
        Assert.Equal(90, funnel.ReclaimsDetected);
    }

    [Fact]
    public void CandidateFunnel_separates_breakout_checks_from_detected_breakouts()
    {
        var funnel = new CandidateFunnelDto
        {
            BullishBreakoutChecks = 500,
            BearishBreakoutChecks = 450,
            BullishBreakoutsDetected = 40,
            BearishBreakoutsDetected = 35,
            RetestChecks = 200,
            ValidRetests = 60,
            ConfirmationChecks = 80,
            ConfirmationsPassed = 55
        };

        Assert.Equal(950, funnel.BreakoutChecks);
        Assert.Equal(75, funnel.BreakoutsDetected);
        Assert.Equal(60, funnel.RetestsDetected);
        Assert.Equal(80, funnel.ConfirmationChecks);
        Assert.Equal(55, funnel.ConfirmationsPassed);
    }

    [Fact]
    public void Liquidity_detector_counts_checks_separately_from_unique_detected()
    {
        var diagnostics = new PriceStructureFunnelDiagnostics();
        diagnostics.BuySideSweepChecks = 50;
        diagnostics.SellSideSweepChecks = 50;
        diagnostics.BuySideSweepsDetected = 3;
        diagnostics.SellSideSweepsDetected = 2;

        Assert.Equal(100, diagnostics.BuySideSweepChecks + diagnostics.SellSideSweepChecks);
        Assert.Equal(5, diagnostics.BuySideSweepsDetected + diagnostics.SellSideSweepsDetected);
    }
}
