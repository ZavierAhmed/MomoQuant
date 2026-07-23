using MomoQuant.Application.MarketData;
using MomoQuant.Domain.Enums;

namespace MomoQuant.UnitTests.MarketData;

public class TimeframeParserTests
{
    [Theory]
    [InlineData("1m", Timeframe.M1, 1)]
    [InlineData("3m", Timeframe.M3, 3)]
    [InlineData("5m", Timeframe.M5, 5)]
    [InlineData("15m", Timeframe.M15, 15)]
    [InlineData("30m", Timeframe.M30, 30)]
    [InlineData("1h", Timeframe.H1, 60)]
    [InlineData("4h", Timeframe.H4, 240)]
    [InlineData("1d", Timeframe.D1, 1440)]
    [InlineData("1w", Timeframe.W1, 10080)]
    public void TryParse_ParsesSupportedValues(string input, Timeframe expected, int expectedMinutes)
    {
        var parsed = TimeframeParser.TryParse(input, out var timeframe);

        Assert.True(parsed);
        Assert.Equal(expected, timeframe);
        Assert.Equal(expectedMinutes, TimeframeParser.GetDurationMinutes(timeframe));
    }

    [Fact]
    public void ToApiString_ReturnsLowercaseToken()
    {
        Assert.Equal("3m", TimeframeParser.ToApiString(Timeframe.M3));
        Assert.Equal("1w", TimeframeParser.ToApiString(Timeframe.W1));
    }

    [Theory]
    [InlineData("4h", "1h", true)]
    [InlineData("1h", "15m", true)]
    [InlineData("30m", "5m", true)]
    [InlineData("15m", "1h", false)]
    [InlineData("1h", "1h", false)]
    [InlineData("1m", "5m", false)]
    public void IsHigherTimeframe_ValidatesDurationRelationship(string higher, string primary, bool expected)
    {
        Assert.Equal(expected, TimeframeParser.IsHigherTimeframe(higher, primary));
    }

    [Theory]
    [InlineData("2h")]
    [InlineData("10m")]
    [InlineData("bad")]
    public void TryParse_RejectsUnsupportedValues(string input)
    {
        var parsed = TimeframeParser.TryParse(input, out _);
        Assert.False(parsed);
    }
}
