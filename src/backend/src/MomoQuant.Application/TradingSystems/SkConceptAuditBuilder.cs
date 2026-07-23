using MomoQuant.Application.TradingSystems.Dtos;

namespace MomoQuant.Application.TradingSystems;

/// <summary>Builds the SK Concept Audit panel data for UI and PDF review.</summary>
public static class SkConceptAuditBuilder
{
    public static SkConceptAuditDto Build(
        IReadOnlyList<SkSequenceDto> sequences,
        SkSequenceDto? selectedSequence,
        SkTimeframeContextDto? htfContext,
        SkTimeframeContextDto? ltfContext,
        SkIdeaDto? bestBull,
        SkIdeaDto? bestBear)
    {
        var hiddenCount = sequences.Count(s => s.HiddenFromBeginner);
        var mismatchCount = sequences.Count(s =>
            s.ValidationStatus == SkScenarioValidator.DirectionMismatch);

        var targetValidation = selectedSequence is null
            ? "No primary sequence selected."
            : ValidateTargets(selectedSequence);

        var alreadyReached = selectedSequence?.UsefulnessStatus == "AlreadyReached";
        var invalidated = selectedSequence?.ValidityStatus is "StructureInvalidated" or "Invalid";

        return new SkConceptAuditDto
        {
            HtfDirection = htfContext?.Direction ?? "Unknown",
            LtfDirection = ltfContext?.Direction ?? "Unknown",
            HtfLtfAgreement = htfContext?.AgreesWithPrimary ?? false,
            SelectedSequenceDirection = selectedSequence?.Direction ?? "Unknown",
            SequenceStatus = selectedSequence?.SequenceStatus ?? "MissingData",
            ValidityStatus = selectedSequence?.ValidityStatus ?? "InsufficientData",
            UsefulnessStatus = selectedSequence?.UsefulnessStatus ?? "TooFarAway",
            ClarityScore = selectedSequence?.ClarityScore ?? 0m,
            ClarityLabel = selectedSequence?.ClarityLabel ?? "Very low",
            UsefulnessScore = selectedSequence?.UsefulnessScore ?? 0m,
            ReasonSelected = selectedSequence?.ReasonSelected ?? string.Empty,
            SequencePoints = BuildPointSummaries(selectedSequence),
            ReactionZoneText = selectedSequence is null
                ? "—"
                : $"{selectedSequence.CorrectionZoneLow} – {selectedSequence.CorrectionZoneHigh}",
            StrongReactionZoneText = selectedSequence is null
                ? "—"
                : $"{selectedSequence.StrongCorrectionZoneLow} – {selectedSequence.StrongCorrectionZoneHigh}",
            InvalidationLevelText = selectedSequence?.InvalidationLevel.ToString() ?? "—",
            TargetValidation = targetValidation,
            AlreadyReachedCheck = alreadyReached ? "Price is at or beyond target area." : "Target not yet reached.",
            InvalidationCheck = invalidated
                ? "Structure may be invalidated."
                : "Invalidation level not crossed.",
            HiddenStructuresCount = hiddenCount,
            DirectionMismatchStructuresCount = mismatchCount,
            PrimaryUpwardId = bestBull?.CandidateId,
            PrimaryDownwardId = bestBear?.CandidateId,
            HiddenStructureIds = sequences.Where(s => s.HiddenFromBeginner).Select(s => s.Id).ToList(),
            InvalidStructureIds = sequences.Where(s =>
                s.ValidationStatus is SkScenarioValidator.DirectionMismatch
                    or SkScenarioValidator.StructureInvalidated).Select(s => s.Id).ToList()
        };
    }

    private static string ValidateTargets(SkSequenceDto sequence)
    {
        if (sequence.Direction == "Upward")
        {
            if (sequence.Target1 <= sequence.CorrectionZoneHigh || sequence.Target2 <= sequence.Target1)
            {
                return "Failed: upward targets must sit above the reaction zone.";
            }

            if (sequence.InvalidationLevel >= sequence.CorrectionZoneLow)
            {
                return "Failed: danger level should be below the reaction zone.";
            }

            return "Passed: upward targets and danger level are directionally consistent.";
        }

        if (sequence.Target1 >= sequence.CorrectionZoneLow || sequence.Target2 >= sequence.Target1)
        {
            return "Failed: downward targets must sit below the reaction zone.";
        }

        if (sequence.InvalidationLevel <= sequence.CorrectionZoneHigh)
        {
            return "Failed: danger level should be above the reaction zone.";
        }

        return "Passed: downward targets and danger level are directionally consistent.";
    }

    private static IReadOnlyList<string> BuildPointSummaries(SkSequenceDto? sequence)
    {
        if (sequence is null)
        {
            return [];
        }

        var points = new List<string>();
        AppendPoint(points, sequence.StartPoint, "Start");
        AppendPoint(points, sequence.ImpulseEndPoint, "Impulse end");
        AppendPoint(points, sequence.CorrectionPoint, "Correction");
        AppendPoint(points, sequence.CurrentPoint, "Current");
        return points;
    }

    private static void AppendPoint(List<string> points, SkSequencePointDto? point, string name)
    {
        if (point is null)
        {
            return;
        }

        points.Add($"{name}: {point.Price} @ {point.TimeUtc:yyyy-MM-dd HH:mm} UTC");
    }
}
