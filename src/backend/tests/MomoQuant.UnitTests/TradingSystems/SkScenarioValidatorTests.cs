using MomoQuant.Application.TradingSystems;
using MomoQuant.Application.TradingSystems.Dtos;

namespace MomoQuant.UnitTests.TradingSystems;

public class SkScenarioValidatorTests
{
    private static SkSequenceCandidateDto BullishCandidate(
        decimal zoneLow,
        decimal zoneHigh,
        decimal danger,
        decimal target1,
        decimal target2,
        string status = "Potential",
        string position = "BeforeCorrectionZone") =>
        new()
        {
            Id = "bull-1",
            Direction = "Bullish",
            Status = status,
            CorrectionZoneMin = zoneLow,
            CorrectionZoneMax = zoneHigh,
            GoldenPocketMin = zoneLow,
            GoldenPocketMax = zoneHigh,
            Target1 = target1,
            Target2 = target2,
            InvalidationLevel = danger,
            CurrentPricePosition = position,
            ConfidenceScore = 65m,
            ImpulseStartTimeUtc = DateTime.UtcNow.AddDays(-2),
            ImpulseEndTimeUtc = DateTime.UtcNow.AddDays(-1),
            PointZ = new SkSequencePointDto { Label = "Z", TimeUtc = DateTime.UtcNow.AddDays(-2), Price = danger },
            PointB = new SkSequencePointDto { Label = "B", TimeUtc = DateTime.UtcNow.AddDays(-1), Price = (zoneLow + zoneHigh) / 2 }
        };

    [Fact]
    public void Validate_BullishWithTargetsBelowZone_MarksDirectionMismatch()
    {
        var candidate = BullishCandidate(
            zoneLow: 62746.30m,
            zoneHigh: 62862.95m,
            danger: 62610.00m,
            target1: 61706.30m,
            target2: 61817.63m);

        var validated = SkScenarioValidator.Validate(candidate, currentPrice: 63000m);

        Assert.Equal(SkScenarioValidator.DirectionMismatch, validated.ValidationStatus);
        Assert.False(validated.EligibleForBestIdea);
        Assert.Contains(SkScenarioValidator.DirectionMismatchMessage, validated.ValidationMessage);
    }

    [Fact]
    public void Validate_BullishWithTargetsAboveZone_RemainsEligible()
    {
        var candidate = BullishCandidate(
            zoneLow: 62000m,
            zoneHigh: 62500m,
            danger: 61000m,
            target1: 64000m,
            target2: 65000m);

        var validated = SkScenarioValidator.Validate(candidate, currentPrice: 63000m);

        Assert.True(validated.EligibleForBestIdea);
        Assert.Equal(SkScenarioValidator.Valid, validated.ValidationStatus);
    }

    [Fact]
    public void Validate_BearishWithTargetsAboveZone_MarksDirectionMismatch()
    {
        var candidate = new SkSequenceCandidateDto
        {
            Id = "bear-1",
            Direction = "Bearish",
            Status = "Potential",
            CorrectionZoneMin = 62000m,
            CorrectionZoneMax = 62500m,
            GoldenPocketMin = 62000m,
            GoldenPocketMax = 62500m,
            Target1 = 64000m,
            Target2 = 65000m,
            InvalidationLevel = 66000m,
            CurrentPricePosition = "BeforeCorrectionZone",
            ConfidenceScore = 60m,
            ImpulseStartTimeUtc = DateTime.UtcNow.AddDays(-2),
            ImpulseEndTimeUtc = DateTime.UtcNow.AddDays(-1)
        };

        var validated = SkScenarioValidator.Validate(candidate, currentPrice: 61500m);

        Assert.Equal(SkScenarioValidator.DirectionMismatch, validated.ValidationStatus);
        Assert.False(validated.EligibleForBestIdea);
    }

    [Fact]
    public void Validate_AlreadyNearTarget_MarksAlreadyReached()
    {
        var candidate = BullishCandidate(
            zoneLow: 62000m,
            zoneHigh: 62500m,
            danger: 61000m,
            target1: 64000m,
            target2: 65000m,
            status: "Completed",
            position: "NearTarget");

        var validated = SkScenarioValidator.Validate(candidate, currentPrice: 64100m);

        Assert.Equal(SkScenarioValidator.AlreadyReached, validated.ValidationStatus);
        Assert.False(validated.EligibleForBestIdea);
    }

    [Fact]
    public void Validate_InvalidatedStructure_IsNotEligible()
    {
        var candidate = BullishCandidate(
            zoneLow: 62000m,
            zoneHigh: 62500m,
            danger: 61000m,
            target1: 64000m,
            target2: 65000m,
            status: "Invalidated",
            position: "Invalidated");

        var validated = SkScenarioValidator.Validate(candidate, currentPrice: 60500m);

        Assert.Equal(SkScenarioValidator.StructureInvalidated, validated.ValidationStatus);
        Assert.False(validated.EligibleForBestIdea);
    }

    [Fact]
    public void Validate_BullishWithDangerAboveZone_MarksDirectionMismatch()
    {
        var candidate = BullishCandidate(
            zoneLow: 62000m,
            zoneHigh: 62500m,
            danger: 62100m,
            target1: 64000m,
            target2: 65000m);

        var validated = SkScenarioValidator.Validate(candidate, currentPrice: 63000m);

        Assert.Equal(SkScenarioValidator.DirectionMismatch, validated.ValidationStatus);
        Assert.False(validated.EligibleForBestIdea);
    }

    [Fact]
    public void Validate_DirectionMismatch_CannotBeBestIdea()
    {
        var candidate = BullishCandidate(
            zoneLow: 62746.30m,
            zoneHigh: 62862.95m,
            danger: 62610.00m,
            target1: 61706.30m,
            target2: 61817.63m);

        var validated = SkScenarioValidator.Validate(candidate, currentPrice: 63000m);

        Assert.False(validated.EligibleForBestIdea);
        Assert.Equal(SkScenarioValidator.DirectionMismatch, validated.ValidationStatus);
    }
}
