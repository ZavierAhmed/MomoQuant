using MomoQuant.Application.Options;
using MomoQuant.Application.TradingSystems.Dtos;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.TradingSystems;

public sealed class SwingStructureService : ISwingStructureService
{
    public IReadOnlyList<SwingPointDto> DetectSwings(
        IReadOnlyList<Candle> candles,
        string sensitivity,
        SkSystemSettings settings)
    {
        if (candles is null || candles.Count == 0)
        {
            return [];
        }

        var normalizedSensitivity = SkSystemConstants.NormalizeSensitivity(sensitivity);
        var bars = SkSystemConstants.ResolveSwingBars(normalizedSensitivity, settings.MinSwingCandles);
        var useWicks = settings.UseWicksForSwingPoints;

        // Only closed candles participate in structure to avoid repainting.
        var closed = candles.Where(candle => candle.IsClosed).OrderBy(candle => candle.OpenTimeUtc).ToList();
        if (closed.Count == 0)
        {
            closed = candles.OrderBy(candle => candle.OpenTimeUtc).ToList();
        }

        if (closed.Count < (bars * 2) + 1)
        {
            return [];
        }

        var raw = new List<SwingPointDto>();

        for (var i = bars; i < closed.Count - bars; i++)
        {
            var current = closed[i];
            var highPrice = useWicks ? current.High : Math.Max(current.Open, current.Close);
            var lowPrice = useWicks ? current.Low : Math.Min(current.Open, current.Close);

            var isHigh = true;
            var isLow = true;
            var maxNeighborHigh = decimal.MinValue;
            var minNeighborLow = decimal.MaxValue;

            for (var j = i - bars; j <= i + bars; j++)
            {
                if (j == i)
                {
                    continue;
                }

                var neighbor = closed[j];
                var neighborHigh = useWicks ? neighbor.High : Math.Max(neighbor.Open, neighbor.Close);
                var neighborLow = useWicks ? neighbor.Low : Math.Min(neighbor.Open, neighbor.Close);

                if (neighborHigh >= highPrice)
                {
                    isHigh = false;
                }

                if (neighborLow <= lowPrice)
                {
                    isLow = false;
                }

                maxNeighborHigh = Math.Max(maxNeighborHigh, neighborHigh);
                minNeighborLow = Math.Min(minNeighborLow, neighborLow);
            }

            if (isHigh)
            {
                raw.Add(BuildSwing(current, highPrice, "High", bars, useWicks, highPrice - minNeighborLow));
            }
            else if (isLow)
            {
                raw.Add(BuildSwing(current, lowPrice, "Low", bars, useWicks, maxNeighborHigh - lowPrice));
            }
        }

        return ReduceToAlternating(raw, normalizedSensitivity, settings);
    }

    private static SwingPointDto BuildSwing(
        Candle candle,
        decimal price,
        string type,
        int bars,
        bool useWicks,
        decimal excursion)
    {
        var strength = price <= 0
            ? 0m
            : Math.Clamp(excursion / price * 100m * 15m, 1m, 100m);

        return new SwingPointDto
        {
            Id = $"{type[0]}-{candle.OpenTimeUtc:yyyyMMddHHmmss}",
            CandleId = candle.Id,
            TimeUtc = candle.OpenTimeUtc,
            Price = price,
            Type = type,
            Strength = decimal.Round(strength, 2),
            LeftBars = bars,
            RightBars = bars,
            Source = useWicks ? "Wick" : "Close"
        };
    }

    private static IReadOnlyList<SwingPointDto> ReduceToAlternating(
        List<SwingPointDto> raw,
        string sensitivity,
        SkSystemSettings settings)
    {
        if (raw.Count == 0)
        {
            return [];
        }

        var minDistancePercent =
            settings.MinSwingDistancePercent * SkSystemConstants.ResolveMinDistanceMultiplier(sensitivity);

        var ordered = raw.OrderBy(swing => swing.TimeUtc).ToList();
        var result = new List<SwingPointDto>();

        foreach (var swing in ordered)
        {
            if (result.Count == 0)
            {
                result.Add(swing);
                continue;
            }

            var last = result[^1];
            if (last.Type == swing.Type)
            {
                var replace = swing.Type == "High"
                    ? swing.Price > last.Price
                    : swing.Price < last.Price;

                if (replace)
                {
                    result[^1] = swing;
                }

                continue;
            }

            var referencePrice = last.Price == 0 ? swing.Price : last.Price;
            var movePercent = referencePrice == 0
                ? 0m
                : Math.Abs(swing.Price - last.Price) / Math.Abs(referencePrice) * 100m;

            if (movePercent >= minDistancePercent)
            {
                result.Add(swing);
            }
        }

        return result;
    }
}
