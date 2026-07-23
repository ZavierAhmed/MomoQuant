using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Indicators.Calculators;

public static class MarketStructureCalculator
{
    public sealed class SwingPoint
    {
        public required decimal Price { get; init; }
        public required int Index { get; init; }
    }

    public static MarketStructure Classify(
        IReadOnlyList<SwingPoint> swingHighs,
        IReadOnlyList<SwingPoint> swingLows)
    {
        if (swingHighs.Count < 2 || swingLows.Count < 2)
        {
            return MarketStructure.Unknown;
        }

        var recentHigh = swingHighs[^1].Price;
        var priorHigh = swingHighs[^2].Price;
        var recentLow = swingLows[^1].Price;
        var priorLow = swingLows[^2].Price;

        var highsRising = recentHigh > priorHigh;
        var highsFalling = recentHigh < priorHigh;
        var lowsRising = recentLow > priorLow;
        var lowsFalling = recentLow < priorLow;

        if (highsRising && lowsRising)
        {
            return MarketStructure.Bullish;
        }

        if (highsFalling && lowsFalling)
        {
            return MarketStructure.Bearish;
        }

        if (!highsRising && !highsFalling && !lowsRising && !lowsFalling)
        {
            return MarketStructure.RangeBound;
        }

        return MarketStructure.RangeBound;
    }
}
