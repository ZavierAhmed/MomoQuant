using MomoQuant.Application.TradingSystems;

namespace MomoQuant.UnitTests.TradingSystems;

public class SkPriceFormatterTests
{
    [Theory]
    [InlineData(63415.7, "BTCUSDT", "63,415.70")]
    [InlineData(59653.8362, "BTCUSDT", "59,653.84")]
    [InlineData(69649.5608, "BTCUSDT", "69,649.56")]
    [InlineData(3450.25, "ETHUSDT", "3,450.25")]
    public void Format_LargePrices_UsesTwoDecimalsAndGrouping(decimal price, string symbol, string expected)
    {
        Assert.Equal(expected, SkPriceFormatter.Format(price, symbol));
    }

    [Fact]
    public void ResolveDecimals_SmallCoins_UsesMoreDecimals()
    {
        Assert.Equal(2, SkPriceFormatter.ResolveDecimals(63415.7m));
        Assert.Equal(4, SkPriceFormatter.ResolveDecimals(2.5m));
        Assert.Equal(6, SkPriceFormatter.ResolveDecimals(0.05m));
        Assert.Equal(8, SkPriceFormatter.ResolveDecimals(0.0001m));
    }

    [Fact]
    public void Range_FormatsBothBounds()
    {
        Assert.Equal("59,650.00 – 61,275.00", SkPriceFormatter.Range(59650m, 61275m, 2));
    }
}
