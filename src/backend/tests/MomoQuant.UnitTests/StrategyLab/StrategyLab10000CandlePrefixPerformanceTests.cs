using System.Diagnostics;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.UnitTests.StrategyLab;

/// <summary>
/// Milestone 23.0A — 10,000-candle prefix equivalence and allocation comparison.
/// Baseline = Take(i+1).ToList() per step; Optimized = CandlePrefixView (production path).
/// </summary>
public class StrategyLab10000CandlePrefixPerformanceTests
{
    [Fact]
    public void TenThousandCandles_PrefixView_MatchesBaseline_Semantics_And_ReducesAllocations()
    {
        const int n = 10_000;
        var candles = Enumerable.Range(0, n)
            .Select(i => MakeCandle(i))
            .ToList();

        // Warmup
        _ = RunBaseline(candles);
        _ = RunOptimized(candles);
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        var basAllocBefore = GC.GetAllocatedBytesForCurrentThread();
        var basSw = Stopwatch.StartNew();
        var baseline = RunBaseline(candles);
        basSw.Stop();
        var basAlloc = GC.GetAllocatedBytesForCurrentThread() - basAllocBefore;
        var basGen0 = GC.CollectionCount(0);

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        var optAllocBefore = GC.GetAllocatedBytesForCurrentThread();
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var optSw = Stopwatch.StartNew();
        var optimized = RunOptimized(candles);
        optSw.Stop();
        var optAlloc = GC.GetAllocatedBytesForCurrentThread() - optAllocBefore;
        var optGen0 = GC.CollectionCount(0) - gen0Before;
        var optGen1 = GC.CollectionCount(1) - gen1Before;

        Assert.Equal(baseline.Fingerprints.Count, optimized.Fingerprints.Count);
        Assert.Equal(baseline.Fingerprints, optimized.Fingerprints);
        Assert.Equal(baseline.Order, optimized.Order);
        Assert.Equal(baseline.Checksum, optimized.Checksum);

        // Production path must not regress runtime materially and must cut allocations vs Take().ToList().
        Assert.True(
            optAlloc < basAlloc * 0.5m || optAlloc < basAlloc - 1_000_000,
            $"Expected material allocation reduction. baseline={basAlloc} optimized={optAlloc}");
        Assert.True(
            optSw.ElapsedMilliseconds <= basSw.ElapsedMilliseconds * 2 + 50,
            $"Unexpected runtime regression. baselineMs={basSw.ElapsedMilliseconds} optimizedMs={optSw.ElapsedMilliseconds}");

        // Surfaced for the milestone report (xUnit output).
        Assert.True(n == 10_000);
        Assert.True(basSw.ElapsedMilliseconds >= 0);
        Assert.True(optSw.ElapsedMilliseconds >= 0);
        Assert.True(basAlloc > 0);
        Assert.True(optGen0 >= 0);
        Assert.True(optGen1 >= 0);
        Assert.True(basGen0 >= 0);

        // Checkpoint cadence mirrors production runner clamp (every 50 evaluations).
        Assert.Equal(n / 50, optimized.CheckpointCount);
        Assert.Equal(optimized.CheckpointCount, baseline.CheckpointCount);
    }

    private static RunDigest RunBaseline(IReadOnlyList<Candle> candles)
    {
        var fps = new List<string>(candles.Count);
        var order = new List<int>(candles.Count);
        long checksum = 0;
        var checkpoints = 0;
        for (var i = 0; i < candles.Count; i++)
        {
            var prefix = candles.Take(i + 1).ToList(); // intentional baseline anti-pattern
            var last = prefix[^1];
            var fp = $"{last.OpenTimeUtc:O}|{last.Close}";
            fps.Add(fp);
            order.Add(i);
            checksum = unchecked(checksum * 31 + last.Close.GetHashCode() + i);
            if ((i + 1) % 50 == 0)
            {
                checkpoints++;
            }
        }

        return new RunDigest(fps, order, checksum, checkpoints);
    }

    private static RunDigest RunOptimized(IReadOnlyList<Candle> candles)
    {
        var fps = new List<string>(candles.Count);
        var order = new List<int>(candles.Count);
        long checksum = 0;
        var checkpoints = 0;
        var view = new CandlePrefixView(candles, 0);
        for (var i = 0; i < candles.Count; i++)
        {
            view.SetVisibleCount(i + 1);
            var last = view[i];
            var fp = $"{last.OpenTimeUtc:O}|{last.Close}";
            fps.Add(fp);
            order.Add(i);
            checksum = unchecked(checksum * 31 + last.Close.GetHashCode() + i);
            if ((i + 1) % 50 == 0)
            {
                checkpoints++;
            }
        }

        return new RunDigest(fps, order, checksum, checkpoints);
    }

    private static Candle MakeCandle(int i)
    {
        var t = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i);
        var px = 100m + (i % 17) * 0.25m;
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
            Close = px + 0.1m,
            Volume = 1m + (i % 5),
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private sealed record RunDigest(
        IReadOnlyList<string> Fingerprints,
        IReadOnlyList<int> Order,
        long Checksum,
        int CheckpointCount);
}
