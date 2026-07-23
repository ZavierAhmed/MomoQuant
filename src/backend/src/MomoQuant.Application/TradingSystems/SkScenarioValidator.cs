using MomoQuant.Application.TradingSystems.Dtos;

namespace MomoQuant.Application.TradingSystems;

/// <summary>
/// Validates SK sequence scenarios for directional consistency, freshness, and invalidation.
/// Prevents upward ideas with targets below the reaction zone (and the mirror bearish case).
/// Analysis only — never creates trades or signals.
/// </summary>
public static class SkScenarioValidator
{
    public const string Valid = "Valid";
    public const string DirectionMismatch = "DirectionMismatch";
    public const string AlreadyReached = "AlreadyReached";
    public const string StructureInvalidated = "StructureInvalidated";
    public const string LowClarity = "LowClarity";
    public const string MissingData = "MissingData";

    public const string DirectionMismatchMessage =
        "Calculated target direction does not match scenario direction. This setup is hidden until recalculated.";

    public const string AlreadyReachedMessage =
        "This idea may be too late. Price has already moved into or beyond the expected area.";

    public const string StructureInvalidatedMessage =
        "This structure may no longer be valid because price crossed the invalidation area.";

    public static SkSequenceCandidateDto Validate(SkSequenceCandidateDto candidate, decimal currentPrice)
    {
        var warnings = candidate.Warnings.ToList();
        var status = Valid;
        var message = string.Empty;
        var eligible = true;
        var confidence = candidate.ConfidenceScore;

        var zoneLow = Math.Min(candidate.CorrectionZoneMin, candidate.CorrectionZoneMax);
        var zoneHigh = Math.Max(candidate.CorrectionZoneMin, candidate.CorrectionZoneMax);

        if (zoneLow >= zoneHigh)
        {
            return Rebuild(candidate, StructureInvalidated, "Reaction zone bounds are invalid.", false, confidence, warnings);
        }

        if (candidate.Direction == "Bullish")
        {
            if (candidate.Target1 <= zoneHigh || candidate.Target2 <= candidate.Target1)
            {
                status = DirectionMismatch;
                message = DirectionMismatchMessage;
                eligible = false;
                if (!warnings.Contains(message))
                {
                    warnings.Add(message);
                }
            }

            if (candidate.InvalidationLevel >= zoneLow)
            {
                status = DirectionMismatch;
                message = "Danger level must sit below the reaction zone for an upward scenario.";
                eligible = false;
                if (!warnings.Contains(message))
                {
                    warnings.Add(message);
                }
            }

            if (currentPrice <= candidate.InvalidationLevel || candidate.Status == "Invalidated")
            {
                status = StructureInvalidated;
                message = StructureInvalidatedMessage;
                eligible = false;
            }
            else if (candidate.Status == "Completed" || candidate.CurrentPricePosition == "NearTarget")
            {
                if (status == Valid)
                {
                    status = AlreadyReached;
                    message = AlreadyReachedMessage;
                    eligible = false;
                    confidence = Math.Max(0m, confidence - 30m);
                }
            }

            if (candidate.PointB is not null && candidate.PointZ is not null && candidate.PointB.Price <= candidate.PointZ.Price)
            {
                if (status == Valid)
                {
                    status = StructureInvalidated;
                    message = StructureInvalidatedMessage;
                    eligible = false;
                }
            }
        }
        else
        {
            if (candidate.Target1 >= zoneLow || candidate.Target2 >= candidate.Target1)
            {
                status = DirectionMismatch;
                message = DirectionMismatchMessage;
                eligible = false;
                if (!warnings.Contains(message))
                {
                    warnings.Add(message);
                }
            }

            if (candidate.InvalidationLevel <= zoneHigh)
            {
                status = DirectionMismatch;
                message = "Danger level must sit above the reaction zone for a downward scenario.";
                eligible = false;
                if (!warnings.Contains(message))
                {
                    warnings.Add(message);
                }
            }

            if (currentPrice >= candidate.InvalidationLevel || candidate.Status == "Invalidated")
            {
                status = StructureInvalidated;
                message = StructureInvalidatedMessage;
                eligible = false;
            }
            else if (candidate.Status == "Completed" || candidate.CurrentPricePosition == "NearTarget")
            {
                if (status == Valid)
                {
                    status = AlreadyReached;
                    message = AlreadyReachedMessage;
                    eligible = false;
                    confidence = Math.Max(0m, confidence - 30m);
                }
            }

            if (candidate.PointB is not null && candidate.PointZ is not null && candidate.PointB.Price >= candidate.PointZ.Price)
            {
                if (status == Valid)
                {
                    status = StructureInvalidated;
                    message = StructureInvalidatedMessage;
                    eligible = false;
                }
            }
        }

        if (status == Valid && confidence < 40m)
        {
            status = LowClarity;
        }

        return Rebuild(candidate, status, message, eligible, confidence, warnings);
    }

    private static SkSequenceCandidateDto Rebuild(
        SkSequenceCandidateDto candidate,
        string validationStatus,
        string validationMessage,
        bool eligibleForBestIdea,
        decimal confidence,
        List<string> warnings) =>
        new()
        {
            Id = candidate.Id,
            Direction = candidate.Direction,
            Status = candidate.Status,
            PointZ = candidate.PointZ,
            PointA = candidate.PointA,
            PointB = candidate.PointB,
            PointC = candidate.PointC,
            ImpulseStartTimeUtc = candidate.ImpulseStartTimeUtc,
            ImpulseEndTimeUtc = candidate.ImpulseEndTimeUtc,
            CorrectionZoneMin = candidate.CorrectionZoneMin,
            CorrectionZoneMax = candidate.CorrectionZoneMax,
            GoldenPocketMin = candidate.GoldenPocketMin,
            GoldenPocketMax = candidate.GoldenPocketMax,
            Target1 = candidate.Target1,
            Target2 = candidate.Target2,
            Extension1618 = candidate.Extension1618,
            InvalidationLevel = candidate.InvalidationLevel,
            CurrentPricePosition = candidate.CurrentPricePosition,
            ConfidenceScore = decimal.Round(confidence, 1),
            Notes = candidate.Notes,
            Warnings = warnings,
            ValidationStatus = validationStatus,
            ValidationMessage = validationMessage,
            EligibleForBestIdea = eligibleForBestIdea && validationStatus is Valid or LowClarity
        };
}
