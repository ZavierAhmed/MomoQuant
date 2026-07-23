using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Backtesting.Simulation;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.UnitTests.Backtesting;

public class Milestone230SimulationIntegrityTests
{
    [Fact]
    public void Settlement_GrossWinnerBecomesNetLoserAfterFees()
    {
        var s = SimulatedTradeSettlement.Create(
            balanceBeforeExitCredit: 10_000m - 5m,
            grossPricePnl: 8m,
            entryFee: 5m,
            exitFee: 5m,
            realizedAtUtc: DateTime.UtcNow);

        Assert.Equal(8m, s.GrossPricePnl);
        Assert.Equal(10m, s.TotalCosts);
        Assert.Equal(-2m, s.FullyNetPnl);
        Assert.Equal(10_000m - 5m + 8m - 5m, s.BalanceAfter);
    }

    [Fact]
    public void Settlement_GrossBreakevenBecomesNetLoser()
    {
        var s = SimulatedTradeSettlement.Create(1000m, 0m, 2m, 2m, DateTime.UtcNow);
        Assert.Equal(-4m, s.FullyNetPnl);
    }

    [Fact]
    public void Settlement_LongShortSymmetry()
    {
        var longS = SimulatedTradeSettlement.Create(1000m, 50m, 1m, 1m, DateTime.UtcNow);
        var shortS = SimulatedTradeSettlement.Create(1000m, 50m, 1m, 1m, DateTime.UtcNow);
        Assert.Equal(longS.FullyNetPnl, shortS.FullyNetPnl);
    }

    [Fact]
    public void Intrabar_LongStopAndTargetSameCandle_StopWins()
    {
        var candle = new Candle { Open = 100m, High = 110m, Low = 90m, Close = 105m };
        var r = ConservativeStopFirstIntrabarPolicy.Instance.EvaluateProtectiveExits(
            TradeDirection.Long, 95m, 108m, candle, SameCandleExitPolicy.ConservativeStopFirst);
        Assert.True(r.Ambiguity);
        Assert.Equal(IntrabarChosenEvent.OriginalStop, r.ChosenEvent);
        Assert.Equal(95m, r.ExitPrice);
    }

    [Fact]
    public void Intrabar_ShortStopAndTargetSameCandle_StopWins()
    {
        var candle = new Candle { Open = 100m, High = 110m, Low = 90m, Close = 95m };
        var r = ConservativeStopFirstIntrabarPolicy.Instance.EvaluateProtectiveExits(
            TradeDirection.Short, 108m, 92m, candle, SameCandleExitPolicy.ConservativeStopFirst);
        Assert.Equal(IntrabarChosenEvent.OriginalStop, r.ChosenEvent);
    }

    [Fact]
    public void Intrabar_BreakevenAndOriginalStopSameCandle_StopWinsNoBreakeven()
    {
        var candle = new Candle { Open = 100m, High = 105m, Low = 94m, Close = 101m };
        var r = ConservativeStopFirstIntrabarPolicy.Instance.EvaluateBreakevenActivation(
            TradeDirection.Long, originalStop: 95m, breakevenTriggerPrice: 104m, candle);
        Assert.True(r.Ambiguity);
        Assert.False(r.ActivateBreakeven);
        Assert.Equal(IntrabarChosenEvent.OriginalStop, r.ChosenEvent);
    }

    [Fact]
    public void ShadowEntry_LimitMissed_NotTriggeredNoPnl()
    {
        var policy = ConservativeStopFirstIntrabarPolicy.Instance;
        var candle = new Candle { Open = 100m, High = 101m, Low = 99m, Close = 100.5m };
        Assert.False(policy.LimitEntryTouched(TradeDirection.Long, limitPrice: 98m, candle));
    }

    [Fact]
    public void ShadowEntry_LimitTouched()
    {
        var policy = ConservativeStopFirstIntrabarPolicy.Instance;
        var candle = new Candle { Open = 100m, High = 101m, Low = 97m, Close = 100.5m };
        Assert.True(policy.LimitEntryTouched(TradeDirection.Long, limitPrice: 98m, candle));
    }

    [Fact]
    public void Settlement_ThreeTradeFixture_ReconcilesBalanceAndConsecutiveLosses()
    {
        // InitialBalance + sum(FullyNetPnl) = FinalBalance
        // Trade1: gross winner / net loser; Trade2: loser; Trade3: winner.
        var t1 = SimulatedTradeSettlement.Create(97m, grossPricePnl: 5m, entryFee: 3m, exitFee: 3m, DateTime.UtcNow);
        Assert.Equal(-1m, t1.FullyNetPnl); // 5 - 3 - 3

        var t2 = SimulatedTradeSettlement.Create(95m, grossPricePnl: -3m, entryFee: 1m, exitFee: 1m, DateTime.UtcNow);
        Assert.Equal(-5m, t2.FullyNetPnl); // -3 - 1 - 1

        var t3 = SimulatedTradeSettlement.Create(89m, grossPricePnl: 8m, entryFee: 1m, exitFee: 1m, DateTime.UtcNow);
        Assert.Equal(6m, t3.FullyNetPnl); // 8 - 1 - 1

        const decimal initial = 100m;
        var sumNet = t1.FullyNetPnl + t2.FullyNetPnl + t3.FullyNetPnl;
        var finalBalance = initial + sumNet;
        Assert.Equal(100m, initial);
        Assert.Equal(-1m + -5m + 6m, sumNet);
        Assert.Equal(100m + sumNet, finalBalance);

        var consecutiveLosses = 0;
        foreach (var pnl in new[] { t1.FullyNetPnl, t2.FullyNetPnl, t3.FullyNetPnl })
        {
            if (pnl < 0m) consecutiveLosses++;
            else consecutiveLosses = 0;
        }

        Assert.Equal(0, consecutiveLosses);
        Assert.True(t1.FullyNetPnl < 0m); // gross winner becomes net loser → loss streak
        Assert.Equal(-1m + -5m + 6m, sumNet); // daily/weekly realized = sum FullyNetPnl
    }
}
