using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.PriceStructure;

public sealed record ConfirmedSwing(
    int Index,
    decimal Price,
    bool IsHigh,
    DateTime OpenTimeUtc);

public static class PriceStructureSwingDetector
{
    public static IReadOnlyList<ConfirmedSwing> DetectConfirmedSwings(
        IReadOnlyList<Candle> candles,
        int swingLeftBars,
        int swingRightBars,
        bool useWicksForSwing,
        int maxConfirmedIndexInclusive)
    {
        if (candles.Count < swingLeftBars + swingRightBars + 1)
        {
            return [];
        }

        var swings = new List<ConfirmedSwing>();
        var lastConfirmable = Math.Min(maxConfirmedIndexInclusive, candles.Count - swingRightBars - 1);

        for (var i = swingLeftBars; i <= lastConfirmable; i++)
        {
            var current = candles[i];
            var highPrice = useWicksForSwing ? current.High : Math.Max(current.Open, current.Close);
            var lowPrice = useWicksForSwing ? current.Low : Math.Min(current.Open, current.Close);

            var isHigh = true;
            var isLow = true;

            for (var j = i - swingLeftBars; j <= i + swingRightBars; j++)
            {
                if (j == i)
                {
                    continue;
                }

                var neighbor = candles[j];
                var neighborHigh = useWicksForSwing ? neighbor.High : Math.Max(neighbor.Open, neighbor.Close);
                var neighborLow = useWicksForSwing ? neighbor.Low : Math.Min(neighbor.Open, neighbor.Close);

                if (neighborHigh >= highPrice)
                {
                    isHigh = false;
                }

                if (neighborLow <= lowPrice)
                {
                    isLow = false;
                }
            }

            if (isHigh)
            {
                swings.Add(new ConfirmedSwing(i, highPrice, true, current.OpenTimeUtc));
            }
            else if (isLow)
            {
                swings.Add(new ConfirmedSwing(i, lowPrice, false, current.OpenTimeUtc));
            }
        }

        return swings;
    }
}
