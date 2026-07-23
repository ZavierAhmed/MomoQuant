using System.Text.Json;
using MomoQuant.Infrastructure.MarketData;

namespace MomoQuant.UnitTests.MarketData;

public class BinanceKlineParserTests
{
    private const string SampleKlineJson =
        """
        [
          [
            1499040000000,
            "0.01634790",
            "0.80000000",
            "0.01575800",
            "0.01577100",
            "148976.11427815",
            1499644799999,
            "2434.19055334",
            308,
            "1756.87402397",
            "28.466943",
            "0"
          ]
        ]
        """;

    [Fact]
    public void ParseKlines_MapsSampleJsonToHistoricalCandleDefinition()
    {
        var candles = BinanceKlineParser.ParseKlines(SampleKlineJson);

        Assert.Single(candles);

        var candle = candles[0];
        Assert.Equal(new DateTime(2017, 7, 3, 0, 0, 0, DateTimeKind.Utc), candle.OpenTimeUtc);
        Assert.Equal(BinanceKlineParser.FromUnixMilliseconds(JsonDocument.Parse("1499644799999").RootElement), candle.CloseTimeUtc);
        Assert.Equal(0.01634790m, candle.Open);
        Assert.Equal(0.80000000m, candle.High);
        Assert.Equal(0.01575800m, candle.Low);
        Assert.Equal(0.01577100m, candle.Close);
        Assert.Equal(148976.11427815m, candle.Volume);
        Assert.Equal(2434.19055334m, candle.QuoteVolume);
        Assert.Equal(308, candle.TradeCount);
        Assert.True(candle.IsClosed);
    }

    [Fact]
    public void FromUnixMilliseconds_ConvertsToUtcDateTime()
    {
        var openTime = BinanceKlineParser.FromUnixMilliseconds(JsonDocument.Parse("1499040000000").RootElement);

        Assert.Equal(DateTimeKind.Utc, openTime.Kind);
        Assert.Equal(new DateTime(2017, 7, 3, 0, 0, 0, DateTimeKind.Utc), openTime);
    }

    [Fact]
    public void ParseDecimal_ParsesStringAndNumericValues()
    {
        var stringValue = BinanceKlineParser.ParseDecimal(JsonDocument.Parse("\"123.45678901\"").RootElement);
        var numericValue = BinanceKlineParser.ParseDecimal(JsonDocument.Parse("42.5").RootElement);

        Assert.Equal(123.45678901m, stringValue);
        Assert.Equal(42.5m, numericValue);
    }
}
