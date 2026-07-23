using MomoQuant.Application.Ai;
using MomoQuant.Application.Ai.Dtos;

namespace MomoQuant.UnitTests.Ai;

public class AiFallbackFactoryTests
{
    [Fact]
    public void RegimeFallback_UsesUnknownRegimeAndZeroConfidence()
    {
        var fallback = AiFallbackFactory.CreateRegimeFallback();

        Assert.Equal("Unknown", fallback.Regime);
        Assert.Equal(0, fallback.Confidence);
        Assert.True(fallback.UsedFallback);
        Assert.Contains(AiFallbackFactory.UnavailableWarning, fallback.Reasons);
    }

    [Fact]
    public void ConfidenceFallback_UsesZeroScoreAndVeryLowClassification()
    {
        var fallback = AiFallbackFactory.CreateConfidenceFallback();

        Assert.Equal(0, fallback.ConfidenceScore);
        Assert.Equal("VeryLow", fallback.Classification);
        Assert.True(fallback.UsedFallback);
        Assert.Contains(AiFallbackFactory.UnavailableWarning, fallback.Warnings);
    }

    [Fact]
    public void IsTradeAllowed_ReturnsFalse_WhenFallbackUsed()
    {
        var regime = AiFallbackFactory.CreateRegimeFallback();
        var confidence = new ScoreConfidenceResponseDto
        {
            ConfidenceScore = 95,
            Classification = "VeryHigh",
            Reasons = [],
            Warnings = []
        };

        Assert.False(AiFallbackFactory.IsAdvisoryEligible(regime, confidence, null));
    }

    [Fact]
    public void IsTradeAllowed_ReturnsFalse_WhenConfidenceBelowThreshold()
    {
        var regime = new DetectRegimeResponseDto
        {
            Regime = "Trending",
            Confidence = 80,
            Reasons = []
        };

        var confidence = new ScoreConfidenceResponseDto
        {
            EvaluationStatus = "Evaluated",
            ConfidenceScore = 70,
            Classification = "Medium",
            Reasons = [],
            Warnings = []
        };

        Assert.False(AiFallbackFactory.IsAdvisoryEligible(regime, confidence, null));
    }

    [Fact]
    public void IsTradeAllowed_ReturnsTrue_WhenConfidenceHighAndNoFallback()
    {
        var regime = new DetectRegimeResponseDto
        {
            Regime = "Trending",
            Confidence = 80,
            Reasons = []
        };

        var confidence = new ScoreConfidenceResponseDto
        {
            EvaluationStatus = "Evaluated",
            ConfidenceScore = 85,
            Classification = "High",
            Reasons = [],
            Warnings = [],
            AdvisoryEligible = true
        };

        Assert.True(AiFallbackFactory.IsAdvisoryEligible(regime, confidence, null));
        Assert.True(AiFallbackFactory.IsTradeAllowed(regime, confidence, null));
    }
}
