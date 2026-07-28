using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies;

/// <summary>
/// Efficient no-lookahead views over a preloaded HTF candle series.
/// Only closed candles with CloseTimeUtc &lt;= evaluation time T are visible.
/// </summary>
public static class HigherTimeframeCandleView
{
    public static IReadOnlyList<Candle> SliceClosedThrough(
        IReadOnlyList<Candle>? higherTimeframeCandles,
        DateTime evaluationCloseTimeUtc)
    {
        if (higherTimeframeCandles is null || higherTimeframeCandles.Count == 0)
        {
            return Array.Empty<Candle>();
        }

        // Binary search for last candle with CloseTimeUtc <= T among closed candles.
        // Series is assumed chronological by CloseTimeUtc.
        var hi = higherTimeframeCandles.Count - 1;
        var lo = 0;
        var lastEligible = -1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);
            var candle = higherTimeframeCandles[mid];
            if (candle.CloseTimeUtc <= evaluationCloseTimeUtc)
            {
                lastEligible = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (lastEligible < 0)
        {
            return Array.Empty<Candle>();
        }

        // Trim any open candles that slipped into the prefix (defensive).
        while (lastEligible >= 0 && !higherTimeframeCandles[lastEligible].IsClosed)
        {
            lastEligible--;
        }

        if (lastEligible < 0)
        {
            return Array.Empty<Candle>();
        }

        if (lastEligible == higherTimeframeCandles.Count - 1
            && higherTimeframeCandles.All(c => c.IsClosed && c.CloseTimeUtc <= evaluationCloseTimeUtc))
        {
            return higherTimeframeCandles;
        }

        var slice = new Candle[lastEligible + 1];
        for (var i = 0; i <= lastEligible; i++)
        {
            var candle = higherTimeframeCandles[i];
            if (!candle.IsClosed || candle.CloseTimeUtc > evaluationCloseTimeUtc)
            {
                // Rebuild tightly if an open/future candle appears earlier than lastEligible.
                return MaterializeClosedThrough(higherTimeframeCandles, evaluationCloseTimeUtc, lastEligible);
            }

            slice[i] = candle;
        }

        return slice;
    }

    private static IReadOnlyList<Candle> MaterializeClosedThrough(
        IReadOnlyList<Candle> higherTimeframeCandles,
        DateTime evaluationCloseTimeUtc,
        int upperBoundInclusive)
    {
        var list = new List<Candle>(upperBoundInclusive + 1);
        for (var i = 0; i <= upperBoundInclusive; i++)
        {
            var candle = higherTimeframeCandles[i];
            if (candle.IsClosed && candle.CloseTimeUtc <= evaluationCloseTimeUtc)
            {
                list.Add(candle);
            }
        }

        return list;
    }
}
