using MomoQuant.Application.TradingSystems;

namespace MomoQuant.UnitTests.TradingSystems;

public class SkTimeframeValidationTests
{
    [Theory]
    [InlineData("4h", "1h")]
    [InlineData("1h", "15m")]
    [InlineData("30m", "5m")]
    [InlineData("1d", "4h")]
    [InlineData("1w", "1d")]
    [InlineData("15m", "1m")]
    public void ValidateSkPair_AcceptsValidPairs(string higher, string primary)
    {
        var result = SkTimeframeValidation.ValidateSkPair(higher, primary);
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("15m", "1h")]
    [InlineData("5m", "15m")]
    [InlineData("1h", "1h")]
    [InlineData("1m", "5m")]
    public void ValidateSkPair_RejectsInvalidPairs(string higher, string primary)
    {
        var result = SkTimeframeValidation.ValidateSkPair(higher, primary);
        Assert.False(result.Succeeded);
        Assert.Equal("timeframe", result.ErrorField);
        Assert.Contains(SkTimeframeValidation.HigherMustExceedPrimaryMessage, result.ErrorMessage);
    }

    [Theory]
    [InlineData("2h")]
    [InlineData("10m")]
    [InlineData("invalid")]
    public void ValidateSkPair_RejectsUnsupportedHigherTimeframe(string higher)
    {
        var result = SkTimeframeValidation.ValidateSkPair(higher, "1h");
        Assert.False(result.Succeeded);
        Assert.Equal("higherTimeframe", result.ErrorField);
    }

    [Theory]
    [InlineData("2h")]
    [InlineData("10m")]
    [InlineData("invalid")]
    public void ValidateSkPair_RejectsUnsupportedPrimaryTimeframe(string primary)
    {
        var result = SkTimeframeValidation.ValidateSkPair("4h", primary);
        Assert.False(result.Succeeded);
        Assert.Equal("primaryTimeframe", result.ErrorField);
    }
}
