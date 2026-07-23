using MomoQuant.Application.StrategyLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.UnitTests.StrategyLab;

/// <summary>
/// Proves the Strategy Lab candle-window factory seam: production prefix view and
/// copied-list reference factory yield equivalent visible windows.
/// </summary>
public class StrategyLabRunnerWindowEquivalenceTests
{
    [Fact]
    public void ProductionAndCopiedFactories_ProduceEquivalentVisibleWindows()
    {
        var source = Enumerable.Range(0, 250).Select(MakeCandle).ToList();
        IStrategyLabCandleWindowFactory production = new CandlePrefixViewStrategyLabCandleWindowFactory();
        IStrategyLabCandleWindowFactory copied = new CopiedListStrategyLabCandleWindowFactory();

        for (var visible = 0; visible <= source.Count; visible += 17)
        {
            var prod = production.CreateVisibleWindow(source, visible);
            var copy = copied.CreateVisibleWindow(source, visible);

            Assert.Equal(copy.Count, prod.Count);
            for (var i = 0; i < visible; i++)
            {
                Assert.Equal(copy[i].OpenTimeUtc, prod[i].OpenTimeUtc);
                Assert.Equal(copy[i].Close, prod[i].Close);
                Assert.Equal(copy[i].Volume, prod[i].Volume);
            }
        }
    }

    [Fact]
    public void RunnerStyleGrowingPrefix_MatchesBetweenFactories()
    {
        var source = Enumerable.Range(0, 100).Select(MakeCandle).ToList();
        var production = new CandlePrefixViewStrategyLabCandleWindowFactory();
        var copied = new CopiedListStrategyLabCandleWindowFactory();

        // Mirrors StrategyLabRunner: reuse CandlePrefixView via SetVisibleCount; copied reallocates.
        var reusable = production.CreateVisibleWindow(source, 0);
        Assert.IsType<CandlePrefixView>(reusable);

        var prodDigest = new List<string>(source.Count);
        var copyDigest = new List<string>(source.Count);

        for (var i = 0; i < source.Count; i++)
        {
            ((CandlePrefixView)reusable).SetVisibleCount(i + 1);
            var copyWindow = copied.CreateVisibleWindow(source, i + 1);

            Assert.Equal(copyWindow.Count, reusable.Count);
            Assert.Equal(copyWindow[^1].Close, reusable[^1].Close);

            prodDigest.Add($"{reusable[^1].OpenTimeUtc:O}|{reusable[^1].Close}");
            copyDigest.Add($"{copyWindow[^1].OpenTimeUtc:O}|{copyWindow[^1].Close}");
        }

        Assert.Equal(copyDigest, prodDigest);
    }

    private static Candle MakeCandle(int i)
    {
        var t = new DateTime(2021, 6, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i);
        var px = 50m + (i % 13) * 0.5m;
        return new Candle
        {
            ExchangeId = 1,
            SymbolId = 1,
            Timeframe = Timeframe.M1,
            OpenTimeUtc = t,
            CloseTimeUtc = t.AddMinutes(1).AddTicks(-1),
            Open = px,
            High = px + 1m,
            Low = px - 1m,
            Close = px + 0.25m,
            Volume = 2m + (i % 3),
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
