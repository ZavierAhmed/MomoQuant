using Microsoft.Extensions.Logging.Abstractions;
using MomoQuant.Application.TradingSystems;
using MomoQuant.Application.TradingSystems.Dtos;

namespace MomoQuant.UnitTests.TradingSystems;

public class SkSequenceAnatomyMapperTests
{
    [Fact]
    public void Map_IncludesStartImpulseAndCorrectionPoints()
    {
        var candidate = new SkSequenceCandidateDto
        {
            Id = "bull-1",
            Direction = "Bullish",
            Status = "Active",
            CorrectionZoneMin = 62000m,
            CorrectionZoneMax = 62500m,
            GoldenPocketMin = 62100m,
            GoldenPocketMax = 62300m,
            Target1 = 64000m,
            Target2 = 65000m,
            Extension1618 = 66000m,
            InvalidationLevel = 61000m,
            CurrentPricePosition = "InsideCorrectionZone",
            ConfidenceScore = 70m,
            ValidationStatus = SkScenarioValidator.Valid,
            EligibleForBestIdea = true,
            ImpulseStartTimeUtc = DateTime.UtcNow.AddDays(-2),
            ImpulseEndTimeUtc = DateTime.UtcNow.AddDays(-1),
            PointZ = new SkSequencePointDto { Label = "Z", TimeUtc = DateTime.UtcNow.AddDays(-2), Price = 61000m },
            PointA = new SkSequencePointDto { Label = "A", TimeUtc = DateTime.UtcNow.AddDays(-1), Price = 63000m },
            PointB = new SkSequencePointDto { Label = "B", TimeUtc = DateTime.UtcNow, Price = 62200m }
        };

        var sequence = SkSequenceAnatomyMapper.Map(
            candidate,
            symbol: "BTCUSDT",
            timeframe: "1h",
            currentPrice: 62250m,
            htfAgrees: true,
            competingStructureCount: 1,
            selectedAsBest: true,
            reasonSelected: "Highest clarity valid upward structure.");

        Assert.NotNull(sequence.StartPoint);
        Assert.NotNull(sequence.ImpulseEndPoint);
        Assert.NotNull(sequence.CorrectionPoint);
        Assert.Equal("Upward", sequence.Direction);
        Assert.True(sequence.SelectedAsBest);
        Assert.Contains("upward", sequence.BeginnerExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Fibonacci", sequence.AdvancedExplanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Map_DirectionMismatch_IsHiddenFromBeginner()
    {
        var candidate = new SkSequenceCandidateDto
        {
            Id = "bull-bad",
            Direction = "Bullish",
            Status = "Potential",
            CorrectionZoneMin = 62000m,
            CorrectionZoneMax = 62500m,
            GoldenPocketMin = 62100m,
            GoldenPocketMax = 62300m,
            Target1 = 61000m,
            Target2 = 60500m,
            Extension1618 = 60000m,
            InvalidationLevel = 60000m,
            CurrentPricePosition = "BeforeCorrectionZone",
            ConfidenceScore = 50m,
            ValidationStatus = SkScenarioValidator.DirectionMismatch,
            ValidationMessage = SkScenarioValidator.DirectionMismatchMessage,
            EligibleForBestIdea = false,
            ImpulseStartTimeUtc = DateTime.UtcNow.AddDays(-2),
            ImpulseEndTimeUtc = DateTime.UtcNow.AddDays(-1)
        };

        var sequence = SkSequenceAnatomyMapper.Map(
            candidate, "BTCUSDT", "1h", 63000m, false, 2, false, null);

        Assert.True(sequence.HiddenFromBeginner);
        Assert.Equal("DirectionMismatch", sequence.ValidityStatus);
    }
}
