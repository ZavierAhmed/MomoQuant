using MomoQuant.Application.Indicators.Calculators;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.UnitTests.Indicators;

public class EmaCalculatorTests
{
    [Fact]
    public void CalculateInitial_ReturnsSimpleAverageForKnownSeries()
    {
        var closes = Enumerable.Range(1, 20).Select(value => (decimal)value).ToList();

        var ema = EmaCalculator.CalculateInitial(closes, 20);

        Assert.Equal(10.5m, ema);
    }

    [Fact]
    public void CalculateNext_UpdatesUsingClosePrice()
    {
        var initial = 10m;
        var next = EmaCalculator.CalculateNext(initial, 12m, 20);

        Assert.True(next > initial);
    }
}

public class VwapCalculatorTests
{
    [Fact]
    public void CalculateCumulative_UsesTypicalPriceAndVolume()
    {
        var candles = new List<Candle>
        {
            CreateCandle(1m, 2m, 1.5m, 10m),
            CreateCandle(2m, 3m, 2.5m, 20m)
        };

        var vwap = VwapCalculator.CalculateCumulative(candles, 1);

        var expected = ((1.5m * 10m) + (2.5m * 20m)) / 30m;
        Assert.Equal(expected, vwap);
    }

    private static Candle CreateCandle(decimal low, decimal high, decimal close, decimal volume) => new()
    {
        High = high,
        Low = low,
        Close = close,
        Volume = volume
    };
}

public class RsiCalculatorTests
{
    [Fact]
    public void CalculateNext_StaysBetweenZeroAndOneHundred()
    {
        var state = new RsiCalculator.State();
        decimal? lastRsi = null;

        for (var close = 100m; close <= 130m; close += 1m)
        {
            lastRsi = RsiCalculator.CalculateNext(close, state);
        }

        Assert.NotNull(lastRsi);
        Assert.InRange(lastRsi.Value, 0m, 100m);
    }
}

public class AtrCalculatorTests
{
    [Fact]
    public void CalculateNext_IncreasesWhenRangesIncrease()
    {
        var state = new AtrCalculator.State();
        decimal? previousAtr = null;

        for (var index = 0; index < 20; index++)
        {
            var high = 10m + index;
            var candle = new Candle { Low = 10m, High = high, Close = high - 0.5m };
            var atr = AtrCalculator.CalculateNext(candle, state);

            if (atr.HasValue && previousAtr.HasValue && index >= 15)
            {
                Assert.True(atr.Value >= previousAtr.Value);
            }

            previousAtr = atr;
        }

        Assert.NotNull(previousAtr);
    }
}

public class VolumeSmaCalculatorTests
{
    [Fact]
    public void Calculate_ReturnsAverageOfLastTwentyVolumes()
    {
        var candles = Enumerable.Range(1, 20)
            .Select(index => new Candle { Volume = index })
            .ToList();

        var sma = VolumeSmaCalculator.Calculate(candles, 19);

        Assert.Equal(10.5m, sma);
    }
}

public class SwingPointCalculatorTests
{
    [Fact]
    public void DetectSwingHigh_RequiresNeighborsOnBothSides()
    {
        var candles = new List<Candle>
        {
            new() { High = 10m, Low = 9m },
            new() { High = 11m, Low = 9.5m },
            new() { High = 15m, Low = 10m },
            new() { High = 12m, Low = 9.8m },
            new() { High = 11m, Low = 9.6m }
        };

        Assert.Null(SwingPointCalculator.DetectSwingHigh(candles, 0));
        Assert.Equal(15m, SwingPointCalculator.DetectSwingHigh(candles, 2));
    }

    [Fact]
    public void DetectSwingLow_RequiresNeighborsOnBothSides()
    {
        var candles = new List<Candle>
        {
            new() { High = 10m, Low = 5m },
            new() { High = 9m, Low = 4m },
            new() { High = 8m, Low = 2m },
            new() { High = 9m, Low = 4.5m },
            new() { High = 10m, Low = 5m }
        };

        Assert.Null(SwingPointCalculator.DetectSwingLow(candles, 0));
        Assert.Equal(2m, SwingPointCalculator.DetectSwingLow(candles, 2));
    }
}

public class MarketStructureCalculatorTests
{
    [Fact]
    public void Classify_ReturnsUnknownForInsufficientData()
    {
        var structure = MarketStructureCalculator.Classify(
            [new MarketStructureCalculator.SwingPoint { Price = 10m, Index = 1 }],
            [new MarketStructureCalculator.SwingPoint { Price = 5m, Index = 2 }]);

        Assert.Equal(MarketStructure.Unknown, structure);
    }

    [Fact]
    public void Classify_ReturnsBullishWhenSwingsRise()
    {
        var structure = MarketStructureCalculator.Classify(
            [
                new MarketStructureCalculator.SwingPoint { Price = 10m, Index = 1 },
                new MarketStructureCalculator.SwingPoint { Price = 12m, Index = 3 }
            ],
            [
                new MarketStructureCalculator.SwingPoint { Price = 5m, Index = 2 },
                new MarketStructureCalculator.SwingPoint { Price = 6m, Index = 4 }
            ]);

        Assert.Equal(MarketStructure.Bullish, structure);
    }
}
