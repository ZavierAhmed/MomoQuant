using MomoQuant.Application.TradingSystems.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.TradingSystems;

/// <summary>Maps validated sequence candidates into SK-2 sequence anatomy DTOs.</summary>
public static class SkSequenceAnatomyMapper
{
    public static SkSequenceDto Map(
        SkSequenceCandidateDto candidate,
        string symbol,
        string timeframe,
        decimal currentPrice,
        bool htfAgrees,
        int competingStructureCount,
        bool selectedAsBest,
        string? reasonSelected)
    {
        var (clarityScore, clarityLabel) = SkConceptScoring.ComputeClarity(candidate, htfAgrees, competingStructureCount);
        var (usefulnessScore, usefulnessStatus) = SkConceptScoring.ComputeUsefulness(candidate, currentPrice);

        var upward = candidate.Direction == "Bullish";
        var sequenceHigh = new[]
        {
            candidate.PointZ?.Price ?? 0m,
            candidate.PointA?.Price ?? 0m,
            candidate.PointB?.Price ?? 0m,
            currentPrice
        }.Max();
        var sequenceLow = new[]
        {
            candidate.PointZ?.Price ?? decimal.MaxValue,
            candidate.PointA?.Price ?? decimal.MaxValue,
            candidate.PointB?.Price ?? decimal.MaxValue,
            currentPrice
        }.Min();

        var zoneLow = Math.Min(candidate.CorrectionZoneMin, candidate.CorrectionZoneMax);
        var zoneHigh = Math.Max(candidate.CorrectionZoneMin, candidate.CorrectionZoneMax);

        var beginnerExplanation = BuildBeginnerExplanation(candidate, upward, zoneLow, zoneHigh);
        var advancedExplanation = BuildAdvancedExplanation(candidate, upward);

        var warnings = candidate.Warnings.ToList();
        if (candidate.ValidationStatus == SkScenarioValidator.DirectionMismatch &&
            !warnings.Contains(SkScenarioValidator.DirectionMismatchMessage))
        {
            warnings.Add("This calculated structure was hidden because target direction does not match scenario direction.");
        }

        return new SkSequenceDto
        {
            Id = candidate.Id,
            Direction = upward ? "Upward" : "Downward",
            Timeframe = timeframe,
            Symbol = symbol,
            StartPoint = MapPoint(candidate.PointZ, "Start"),
            ImpulseEndPoint = MapPoint(candidate.PointA, "ImpulseEnd"),
            CorrectionPoint = MapPoint(candidate.PointB, "Correction"),
            CurrentPoint = new SkSequencePointDto
            {
                Label = "Current",
                TimeUtc = DateTime.UtcNow,
                Price = currentPrice,
                Description = "Latest close price on the analysis chart."
            },
            SequenceHigh = sequenceHigh,
            SequenceLow = sequenceLow,
            CorrectionZoneLow = zoneLow,
            CorrectionZoneHigh = zoneHigh,
            StrongCorrectionZoneLow = Math.Min(candidate.GoldenPocketMin, candidate.GoldenPocketMax),
            StrongCorrectionZoneHigh = Math.Max(candidate.GoldenPocketMin, candidate.GoldenPocketMax),
            InvalidationLevel = candidate.InvalidationLevel,
            Target1 = candidate.Target1,
            Target2 = candidate.Target2,
            ExtensionTarget = candidate.Extension1618,
            SequenceStatus = SkConceptScoring.MapSequenceStatus(candidate).ToString(),
            ValidityStatus = SkConceptScoring.MapValidity(candidate.ValidationStatus).ToString(),
            ClarityScore = clarityScore,
            ClarityLabel = clarityLabel,
            UsefulnessScore = usefulnessScore,
            UsefulnessStatus = usefulnessStatus.ToString(),
            SelectedAsBest = selectedAsBest,
            ReasonSelected = reasonSelected ?? string.Empty,
            InvalidationReason = candidate.ValidationStatus == SkScenarioValidator.StructureInvalidated
                ? SkScenarioValidator.StructureInvalidatedMessage
                : string.Empty,
            StructureCategory = SkConceptScoring.StructureCategoryLabel(candidate),
            HiddenFromBeginner = SkConceptScoring.IsHiddenFromBeginner(candidate),
            BeginnerExplanation = beginnerExplanation,
            AdvancedExplanation = advancedExplanation,
            WarningMessages = warnings,
            CalculationNotes = candidate.Notes,
            ValidationStatus = candidate.ValidationStatus,
            ValidationMessage = candidate.ValidationMessage,
            EligibleForBestIdea = candidate.EligibleForBestIdea
        };
    }

    private static SkSequencePointDto? MapPoint(SkSequencePointDto? point, string label)
    {
        if (point is null)
        {
            return null;
        }

        return new SkSequencePointDto
        {
            Label = label,
            CandleId = point.CandleId,
            TimeUtc = point.TimeUtc,
            Price = point.Price,
            CandleIndex = point.CandleIndex,
            Description = point.Description
        };
    }

    private static string BuildBeginnerExplanation(SkSequenceCandidateDto candidate, bool upward, decimal zoneLow, decimal zoneHigh)
    {
        if (candidate.ValidationStatus == SkScenarioValidator.DirectionMismatch)
        {
            return SkScenarioValidator.DirectionMismatchMessage;
        }

        if (upward)
        {
            return "Price first moved upward from the sequence start to the impulse high. It then pulled back into the reaction zone. " +
                   "This is why the analyzer is watching for a possible upward reaction.";
        }

        return "Price first moved downward from the sequence start to the impulse low. It then bounced into the reaction zone. " +
               "This is why the analyzer is watching for a possible downward rejection.";
    }

    private static string BuildAdvancedExplanation(SkSequenceCandidateDto candidate, bool upward)
    {
        var parts = new List<string>();

        if (candidate.PointZ is not null)
        {
            parts.Add($"Start (Z) selected at swing {(upward ? "low" : "high")} {candidate.PointZ.Price} on {candidate.PointZ.TimeUtc:yyyy-MM-dd HH:mm} UTC.");
        }

        if (candidate.PointA is not null)
        {
            parts.Add($"Impulse end (A) at swing {(upward ? "high" : "low")} {candidate.PointA.Price}.");
        }

        if (candidate.PointB is not null)
        {
            parts.Add($"Correction point (B) at {candidate.PointB.Price} — used for Fibonacci extension targets.");
        }

        parts.Add($"Fibonacci retracement zone: {candidate.CorrectionZoneMin}–{candidate.CorrectionZoneMax}; golden pocket: {candidate.GoldenPocketMin}–{candidate.GoldenPocketMax}.");
        parts.Add($"Targets from extension ratios: T1={candidate.Target1}, T2={candidate.Target2}, 1.618={candidate.Extension1618}.");
        parts.Add($"Invalidation at sequence start (Z) = {candidate.InvalidationLevel}.");

        return string.Join(" ", parts);
    }
}
