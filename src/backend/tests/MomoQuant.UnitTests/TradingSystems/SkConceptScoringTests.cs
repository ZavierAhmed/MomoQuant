using MomoQuant.Application.TradingSystems;
using MomoQuant.Application.TradingSystems.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.UnitTests.TradingSystems;

public class SkConceptScoringTests
{
    private static SkSequenceCandidateDto ValidBullish() => new()
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

    [Fact]
    public void ComputeClarity_HtfAgreement_IncreasesScore()
    {
        var (withAgreement, _) = SkConceptScoring.ComputeClarity(ValidBullish(), htfAgrees: true, competingStructureCount: 1);
        var (withoutAgreement, _) = SkConceptScoring.ComputeClarity(ValidBullish(), htfAgrees: false, competingStructureCount: 1);

        Assert.True(withAgreement > withoutAgreement);
    }

    [Fact]
    public void ComputeClarity_DirectionMismatch_CappedAtTwenty()
    {
        var candidate = ValidBullish();
        candidate = Copy(candidate, validationStatus: SkScenarioValidator.DirectionMismatch, eligible: false);

        var (score, label) = SkConceptScoring.ComputeClarity(candidate, htfAgrees: true, competingStructureCount: 1);

        Assert.True(score <= 20m);
        Assert.Equal("Very low", label);
    }

    [Fact]
    public void ComputeClarity_StructureInvalidated_CappedAtThirty()
    {
        var candidate = Copy(ValidBullish(), validationStatus: SkScenarioValidator.StructureInvalidated, eligible: false);

        var (score, _) = SkConceptScoring.ComputeClarity(candidate, htfAgrees: true, competingStructureCount: 1);

        Assert.True(score <= 30m);
    }

    [Fact]
    public void ComputeUsefulness_PriceInsideZone_ReturnsInZone()
    {
        var candidate = ValidBullish();
        var (_, status) = SkConceptScoring.ComputeUsefulness(candidate, currentPrice: 62250m);

        Assert.Equal(SkUsefulnessStatus.InZone, status);
    }

    [Fact]
    public void ComputeUsefulness_PriceBeyondTarget_ReturnsAlreadyReached()
    {
        var candidate = Copy(ValidBullish(),
            validationStatus: SkScenarioValidator.AlreadyReached,
            position: "NearTarget",
            status: "Completed");

        var (_, status) = SkConceptScoring.ComputeUsefulness(candidate, currentPrice: 64500m);

        Assert.Equal(SkUsefulnessStatus.AlreadyReached, status);
    }

    [Fact]
    public void IsHiddenFromBeginner_DirectionMismatch_ReturnsTrue()
    {
        var candidate = Copy(ValidBullish(),
            validationStatus: SkScenarioValidator.DirectionMismatch,
            eligible: false);

        Assert.True(SkConceptScoring.IsHiddenFromBeginner(candidate));
    }

    private static SkSequenceCandidateDto Copy(
        SkSequenceCandidateDto source,
        string? validationStatus = null,
        bool? eligible = null,
        string? position = null,
        string? status = null) =>
        new()
        {
            Id = source.Id,
            Direction = source.Direction,
            Status = status ?? source.Status,
            PointZ = source.PointZ,
            PointA = source.PointA,
            PointB = source.PointB,
            PointC = source.PointC,
            ImpulseStartTimeUtc = source.ImpulseStartTimeUtc,
            ImpulseEndTimeUtc = source.ImpulseEndTimeUtc,
            CorrectionZoneMin = source.CorrectionZoneMin,
            CorrectionZoneMax = source.CorrectionZoneMax,
            GoldenPocketMin = source.GoldenPocketMin,
            GoldenPocketMax = source.GoldenPocketMax,
            Target1 = source.Target1,
            Target2 = source.Target2,
            Extension1618 = source.Extension1618,
            InvalidationLevel = source.InvalidationLevel,
            CurrentPricePosition = position ?? source.CurrentPricePosition,
            ConfidenceScore = source.ConfidenceScore,
            Notes = source.Notes,
            Warnings = source.Warnings,
            ValidationStatus = validationStatus ?? source.ValidationStatus,
            ValidationMessage = source.ValidationMessage,
            EligibleForBestIdea = eligible ?? source.EligibleForBestIdea
        };
}
