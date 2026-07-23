using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.StrategyLab.Synthetic;

internal static class SyntheticCandleFactory
{
    private static DateTime BaseTime => new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    public static Candle C(int index, decimal open, decimal high, decimal low, decimal close) => new()
    {
        Id = index + 1,
        ExchangeId = 1,
        SymbolId = 1,
        Timeframe = Timeframe.M15,
        OpenTimeUtc = BaseTime.AddMinutes(15 * index),
        CloseTimeUtc = BaseTime.AddMinutes(15 * (index + 1)),
        Open = open,
        High = high,
        Low = low,
        Close = close,
        Volume = 1000,
        QuoteVolume = 1000,
        TradeCount = 100,
        IsClosed = true,
        CreatedAtUtc = BaseTime
    };

    public static IReadOnlyList<Candle> Flat(int count, decimal price = 100m)
    {
        return Enumerable.Range(0, count).Select(i => C(i, price, price + 1, price - 1, price)).ToList();
    }
}

public sealed class BullishBreakoutRetestScenario : ISyntheticCandleScenario
{
    public string Name => "Bullish clean breakout retest";
    public string Description => "Confirmed swing high, breakout, retest, bullish confirmation.";
    public string StrategyCode => "PRICE_STRUCTURE_BREAKOUT_RETEST";
    public int ExpectedCandidateCount => 1;
    public TradeDirection? ExpectedDirection => TradeDirection.Long;
    public string? ExpectedNoTradeReason => null;

    public IReadOnlyList<Candle> BuildCandles()
    {
        var candles = new List<Candle>();
        for (var i = 0; i < 30; i++)
        {
            candles.Add(SyntheticCandleFactory.C(i, 100, 105, 99, 100));
        }

        candles[8] = SyntheticCandleFactory.C(8, 100, 101, 99, 100);
        candles[9] = SyntheticCandleFactory.C(9, 100, 102, 99, 101);
        candles[10] = SyntheticCandleFactory.C(10, 101, 112, 100, 111);
        candles[11] = SyntheticCandleFactory.C(11, 110, 111, 108, 109);
        candles[12] = SyntheticCandleFactory.C(12, 109, 110, 107, 108);
        candles[16] = SyntheticCandleFactory.C(16, 108, 109, 107, 113);
        candles[17] = SyntheticCandleFactory.C(17, 113, 114, 112.5m, 113.2m);
        candles[18] = SyntheticCandleFactory.C(18, 113.2m, 113.5m, 111.9m, 112.1m);
        candles[19] = SyntheticCandleFactory.C(19, 112.1m, 115, 111.8m, 114);
        return candles;
    }
}

public sealed class BearishBreakoutRetestScenario : ISyntheticCandleScenario
{
    public string Name => "Bearish clean breakout retest";
    public string Description => "Confirmed swing low, breakdown, retest, bearish confirmation.";
    public string StrategyCode => "PRICE_STRUCTURE_BREAKOUT_RETEST";
    public int ExpectedCandidateCount => 1;
    public TradeDirection? ExpectedDirection => TradeDirection.Short;
    public string? ExpectedNoTradeReason => null;

    public IReadOnlyList<Candle> BuildCandles()
    {
        var candles = new List<Candle>();
        for (var i = 0; i < 30; i++)
        {
            candles.Add(SyntheticCandleFactory.C(i, 100, 101, 99, 100));
        }

        candles[8] = SyntheticCandleFactory.C(8, 100, 101, 99, 100);
        candles[9] = SyntheticCandleFactory.C(9, 100, 101, 98, 99);
        candles[10] = SyntheticCandleFactory.C(10, 99, 100, 88, 89);
        candles[11] = SyntheticCandleFactory.C(11, 89, 92, 89.5m, 91);
        candles[12] = SyntheticCandleFactory.C(12, 91, 92, 90, 91);
        candles[16] = SyntheticCandleFactory.C(16, 91, 92, 85, 86);
        candles[17] = SyntheticCandleFactory.C(17, 86, 87, 85.5m, 86.5m);
        candles[18] = SyntheticCandleFactory.C(18, 86.5m, 88.2m, 87.9m, 87.5m);
        candles[19] = SyntheticCandleFactory.C(19, 87.5m, 88, 83, 84);
        return candles;
    }
}

public sealed class BreakoutWithoutRetestScenario : ISyntheticCandleScenario
{
    public string Name => "Breakout without retest";
    public string Description => "Breakout occurs but price never retests before expiry.";
    public string StrategyCode => "PRICE_STRUCTURE_BREAKOUT_RETEST";
    public int ExpectedCandidateCount => 0;
    public TradeDirection? ExpectedDirection => null;
    public string? ExpectedNoTradeReason => null;

    public IReadOnlyList<Candle> BuildCandles()
    {
        var candles = SyntheticCandleFactory.Flat(40, 100m).ToList();
        candles[10] = SyntheticCandleFactory.C(10, 101, 112, 100, 111);
        candles[16] = SyntheticCandleFactory.C(16, 108, 109, 107, 113);
        for (var i = 17; i < candles.Count; i++)
        {
            candles[i] = SyntheticCandleFactory.C(i, 114, 116, 113, 115);
        }

        return candles;
    }
}

public sealed class WickBreakoutNoCloseScenario : ISyntheticCandleScenario
{
    public string Name => "Wick above swing but no close breakout";
    public string Description => "Wick pierces swing high but close remains below.";
    public string StrategyCode => "PRICE_STRUCTURE_BREAKOUT_RETEST";
    public int ExpectedCandidateCount => 0;
    public TradeDirection? ExpectedDirection => null;
    public string? ExpectedNoTradeReason => null;

    public IReadOnlyList<Candle> BuildCandles()
    {
        var candles = SyntheticCandleFactory.Flat(25, 100m).ToList();
        candles[10] = SyntheticCandleFactory.C(10, 101, 112, 100, 111);
        candles[16] = SyntheticCandleFactory.C(16, 108, 113, 107, 109);
        return candles;
    }
}

public sealed class RetestInvalidatedScenario : ISyntheticCandleScenario
{
    public string Name => "Retest invalidates structure";
    public string Description => "Retest penetrates too far below broken level.";
    public string StrategyCode => "PRICE_STRUCTURE_BREAKOUT_RETEST";
    public int ExpectedCandidateCount => 0;
    public TradeDirection? ExpectedDirection => null;
    public string? ExpectedNoTradeReason => null;

    public IReadOnlyList<Candle> BuildCandles()
    {
        var candles = SyntheticCandleFactory.Flat(25, 100m).ToList();
        candles[10] = SyntheticCandleFactory.C(10, 101, 112, 100, 111);
        candles[16] = SyntheticCandleFactory.C(16, 108, 109, 107, 113);
        candles[18] = SyntheticCandleFactory.C(18, 112, 113, 105, 106);
        candles[19] = SyntheticCandleFactory.C(19, 106, 107, 104, 105);
        return candles;
    }
}

public sealed class BullishLiquiditySweepScenario : ISyntheticCandleScenario
{
    public string Name => "Bullish sell-side liquidity sweep and reclaim";
    public string Description => "Sweep below swing low and close back above.";
    public string StrategyCode => "PRICE_STRUCTURE_LIQUIDITY_SWEEP_RECLAIM";
    public int ExpectedCandidateCount => 1;
    public TradeDirection? ExpectedDirection => TradeDirection.Long;
    public string? ExpectedNoTradeReason => null;

    public IReadOnlyList<Candle> BuildCandles()
    {
        var candles = SyntheticCandleFactory.Flat(20, 100m).ToList();
        candles[10] = SyntheticCandleFactory.C(10, 99, 100, 88, 89);
        candles[15] = SyntheticCandleFactory.C(15, 90, 91, 87, 90.5m);
        return candles;
    }
}

public sealed class BearishLiquiditySweepScenario : ISyntheticCandleScenario
{
    public string Name => "Bearish buy-side liquidity sweep and reclaim";
    public string Description => "Sweep above swing high and close back below.";
    public string StrategyCode => "PRICE_STRUCTURE_LIQUIDITY_SWEEP_RECLAIM";
    public int ExpectedCandidateCount => 1;
    public TradeDirection? ExpectedDirection => TradeDirection.Short;
    public string? ExpectedNoTradeReason => null;

    public IReadOnlyList<Candle> BuildCandles()
    {
        var candles = SyntheticCandleFactory.Flat(20, 100m).ToList();
        candles[10] = SyntheticCandleFactory.C(10, 101, 112, 100, 111);
        candles[15] = SyntheticCandleFactory.C(15, 110, 113, 109, 109.5m);
        return candles;
    }
}

public sealed class SweepWithoutReclaimScenario : ISyntheticCandleScenario
{
    public string Name => "Sweep without reclaim";
    public string Description => "Price sweeps liquidity but fails to reclaim.";
    public string StrategyCode => "PRICE_STRUCTURE_LIQUIDITY_SWEEP_RECLAIM";
    public int ExpectedCandidateCount => 0;
    public TradeDirection? ExpectedDirection => null;
    public string? ExpectedNoTradeReason => null;

    public IReadOnlyList<Candle> BuildCandles()
    {
        var candles = SyntheticCandleFactory.Flat(20, 100m).ToList();
        candles[10] = SyntheticCandleFactory.C(10, 99, 100, 88, 89);
        candles[15] = SyntheticCandleFactory.C(15, 90, 91, 87, 87.5m);
        return candles;
    }
}

public static class SyntheticScenarioCatalog
{
    public static IReadOnlyList<ISyntheticCandleScenario> ForStrategy(string strategyCode) => strategyCode switch
    {
        "PRICE_STRUCTURE_BREAKOUT_RETEST" =>
        [
            new BullishBreakoutRetestScenario(),
            new BearishBreakoutRetestScenario(),
            new BreakoutWithoutRetestScenario(),
            new WickBreakoutNoCloseScenario(),
            new RetestInvalidatedScenario()
        ],
        "PRICE_STRUCTURE_LIQUIDITY_SWEEP_RECLAIM" =>
        [
            new BullishLiquiditySweepScenario(),
            new BearishLiquiditySweepScenario(),
            new SweepWithoutReclaimScenario()
        ],
        _ => []
    };

    public static IReadOnlyList<ISyntheticCandleScenario> All() =>
        ForStrategy("PRICE_STRUCTURE_BREAKOUT_RETEST")
            .Concat(ForStrategy("PRICE_STRUCTURE_LIQUIDITY_SWEEP_RECLAIM"))
            .ToList();
}
