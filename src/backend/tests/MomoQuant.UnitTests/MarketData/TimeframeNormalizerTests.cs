using MomoQuant.Application.Common;
using MomoQuant.Application.Indicators.Dtos;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Replay.Dtos;

namespace MomoQuant.UnitTests.MarketData;

public class TimeframeNormalizerTests
{
    [Theory]
    [InlineData("15m", "15m")]
    [InlineData("15 minutes", "15m")]
    [InlineData("3 minutes", "3m")]
    [InlineData("1 hour", "1h")]
    [InlineData("4 hours", "4h")]
    public void TryNormalize_AcceptsAliases(string input, string expected)
    {
        Assert.True(TimeframeNormalizer.TryNormalize(input, out var canonical));
        Assert.Equal(expected, canonical);
    }

    [Fact]
    public void TryNormalize_RejectsUnsupported()
    {
        Assert.False(TimeframeNormalizer.TryNormalize("2m", out _));
    }

    [Fact]
    public void TimeframeParser_AcceptsAliasValues()
    {
        Assert.True(TimeframeParser.TryParse("15 minutes", out _));
        Assert.True(TimeframeParser.TryParse("3m", out _));
    }

    [Fact]
    public void ToDisplayLabel_ReturnsHumanReadable()
    {
        Assert.Equal("15 minutes", TimeframeNormalizer.ToDisplayLabel("15m"));
    }

    [Fact]
    public void NormalizeRecalculate_AcceptsDateOnlyRange()
    {
        var normalized = TimeframeRequestNormalizer.NormalizeRecalculate(new RecalculateIndicatorsRequest
        {
            SymbolId = 1,
            Timeframe = "15 minutes",
            FromDate = "2026-06-01",
            ToDate = "2026-06-30"
        });

        Assert.Equal("15m", normalized.Timeframe);
        Assert.Equal(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), normalized.FromUtc);
        Assert.Equal(new DateTime(2026, 6, 30, 23, 59, 59, 999, DateTimeKind.Utc), normalized.ToUtc);
    }

    [Fact]
    public void NormalizeReplay_AcceptsCanonicalTimeframe()
    {
        var normalized = TimeframeRequestNormalizer.NormalizeReplay(new CreateReplaySessionRequest
        {
            Name = "Test",
            ExchangeId = 1,
            SymbolId = 1,
            Timeframe = "3m",
            FromDate = "2026-06-01",
            ToDate = "2026-06-02",
            StrategyIds = []
        });

        Assert.Equal("3m", normalized.Timeframe);
    }
}
