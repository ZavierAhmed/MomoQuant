using MomoQuant.Application.Options;
using MomoQuant.Application.TradingSystems;

namespace MomoQuant.UnitTests.TradingSystems;

public class SwingStructureServiceTests
{
    [Fact]
    public void DetectSwings_ZigZag_ReturnsSwingHighsAndLows()
    {
        var service = new SwingStructureService();
        var candles = SkTestData.FromPrices(SkTestData.ZigZagPrices());

        var swings = service.DetectSwings(candles, "Balanced", new SkSystemSettings());

        Assert.Contains(swings, swing => swing.Type == "High");
        Assert.Contains(swings, swing => swing.Type == "Low");
        Assert.All(swings, swing => Assert.True(swing.Strength > 0));
    }

    [Fact]
    public void DetectSwings_ProducesAlternatingStructure()
    {
        var service = new SwingStructureService();
        var candles = SkTestData.FromPrices(SkTestData.ZigZagPrices());

        var swings = service.DetectSwings(candles, "Balanced", new SkSystemSettings());

        for (var i = 1; i < swings.Count; i++)
        {
            Assert.NotEqual(swings[i - 1].Type, swings[i].Type);
        }
    }

    [Fact]
    public void DetectSwings_TooFewCandles_ReturnsEmpty()
    {
        var service = new SwingStructureService();
        var candles = SkTestData.FromPrices(new List<decimal> { 100m, 101m, 102m });

        var swings = service.DetectSwings(candles, "Balanced", new SkSystemSettings());

        Assert.Empty(swings);
    }
}
