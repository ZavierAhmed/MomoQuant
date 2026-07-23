using MomoQuant.Application.StrategyLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.UnitTests.StrategyLab;

public class StrategyLabCandleWindowFactoryTests
{
    [Fact]
    public void ProductionFactory_ReturnsCandlePrefixView()
    {
        var source = Enumerable.Range(0, 20).Select(MakeCandle).ToList();
        var factory = new CandlePrefixViewStrategyLabCandleWindowFactory();

        var window = factory.CreateVisibleWindow(source, 7);

        Assert.IsType<CandlePrefixView>(window);
        Assert.Equal(7, window.Count);
        Assert.Same(source[6], window[6]);
    }

    [Fact]
    public void CopiedFactory_ReturnsIndependentList()
    {
        var source = Enumerable.Range(0, 20).Select(MakeCandle).ToList();
        var factory = new CopiedListStrategyLabCandleWindowFactory();

        var window = factory.CreateVisibleWindow(source, 5);

        Assert.IsType<List<Candle>>(window);
        Assert.Equal(5, window.Count);
        Assert.Equal(source[4].OpenTimeUtc, window[4].OpenTimeUtc);
        Assert.NotSame(source, window);
    }

    [Fact]
    public void CopiedFactory_AllocationGrows_WithRepeatedWindows()
    {
        var source = Enumerable.Range(0, 200).Select(MakeCandle).ToList();
        var factory = new CopiedListStrategyLabCandleWindowFactory();

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 1; i <= 100; i++)
        {
            _ = factory.CreateVisibleWindow(source, i);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Light check: repeated Take().ToList() must allocate a material amount of memory.
        Assert.True(allocated > 50_000, $"Expected copied windows to allocate; allocated={allocated}");
    }

    private static Candle MakeCandle(int i)
    {
        var t = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i);
        return new Candle
        {
            ExchangeId = 1,
            SymbolId = 1,
            Timeframe = Timeframe.M1,
            OpenTimeUtc = t,
            CloseTimeUtc = t.AddMinutes(1).AddTicks(-1),
            Open = 100m + i,
            High = 101m + i,
            Low = 99m + i,
            Close = 100.5m + i,
            Volume = 1m,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
