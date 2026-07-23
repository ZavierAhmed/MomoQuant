using MomoQuant.Application.TradingSystems.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.TradingSystems;

/// <summary>
/// Computes clarity and usefulness scores for SK sequences. Analysis only — never creates trades.
/// </summary>
public static class SkConceptScoring
{
    public static (decimal Score, string Label) ComputeClarity(
        SkSequenceCandidateDto candidate,
        bool htfAgrees,
        int competingStructureCount)
    {
        var score = candidate.ConfidenceScore;

        if (candidate.PointZ is not null && candidate.PointA is not null && candidate.PointB is not null)
        {
            score += 8m;
        }

        if (candidate.CurrentPricePosition == "InsideCorrectionZone")
        {
            score += 12m;
        }
        else if (candidate.CurrentPricePosition is "BeforeCorrectionZone" or "LeftCorrectionZone")
        {
            score -= 8m;
        }

        if (candidate.InvalidationLevel > 0)
        {
            score += 5m;
        }

        if (htfAgrees)
        {
            score += 10m;
        }
        else
        {
            score -= 12m;
        }

        if (candidate.ValidationStatus == SkScenarioValidator.Valid)
        {
            score += 5m;
        }

        if (competingStructureCount > 3)
        {
            score -= (competingStructureCount - 3) * 3m;
        }

        foreach (var _ in candidate.Warnings)
        {
            score -= 4m;
        }

        score = ApplyValidationCaps(score, candidate.ValidationStatus);
        score = Math.Clamp(decimal.Round(score, 1), 0m, 100m);

        return (score, ClarityLabelFromScore(score));
    }

    public static (decimal Score, SkUsefulnessStatus Status) ComputeUsefulness(
        SkSequenceCandidateDto candidate,
        decimal currentPrice)
    {
        if (candidate.ValidationStatus == SkScenarioValidator.DirectionMismatch)
        {
            return (0m, SkUsefulnessStatus.DirectionMismatch);
        }

        if (candidate.ValidationStatus == SkScenarioValidator.StructureInvalidated ||
            candidate.Status == "Invalidated")
        {
            return (5m, SkUsefulnessStatus.Invalidated);
        }

        var zoneLow = Math.Min(candidate.CorrectionZoneMin, candidate.CorrectionZoneMax);
        var zoneHigh = Math.Max(candidate.CorrectionZoneMin, candidate.CorrectionZoneMax);

        if (candidate.ValidationStatus == SkScenarioValidator.AlreadyReached ||
            candidate.CurrentPricePosition == "NearTarget" ||
            candidate.Status == "Completed")
        {
            return (15m, SkUsefulnessStatus.AlreadyReached);
        }

        var insideZone = currentPrice >= zoneLow && currentPrice <= zoneHigh;
        if (insideZone)
        {
            return (85m, SkUsefulnessStatus.InZone);
        }

        var zoneMid = (zoneLow + zoneHigh) / 2m;
        var distance = Math.Abs(currentPrice - zoneMid);
        var range = Math.Max(zoneHigh - zoneLow, zoneMid * 0.001m);
        var distanceRatio = distance / range;

        if (distanceRatio <= 1.5m)
        {
            return (70m, SkUsefulnessStatus.WaitingForReaction);
        }

        if (distanceRatio <= 4m)
        {
            return (45m, SkUsefulnessStatus.Fresh);
        }

        return (20m, SkUsefulnessStatus.TooFarAway);
    }

    public static decimal ApplyValidationCaps(decimal score, string validationStatus) =>
        validationStatus switch
        {
            SkScenarioValidator.DirectionMismatch => Math.Min(score, 20m),
            SkScenarioValidator.StructureInvalidated => Math.Min(score, 30m),
            _ => score
        };

    public static string ClarityLabelFromScore(decimal score) => score switch
    {
        <= 25m => "Very low",
        <= 45m => "Low",
        <= 70m => "Medium",
        <= 85m => "High",
        _ => "Very high"
    };

    public static SkValidityStatus MapValidity(string validationStatus) => validationStatus switch
    {
        SkScenarioValidator.Valid => SkValidityStatus.Valid,
        SkScenarioValidator.LowClarity => SkValidityStatus.Weak,
        SkScenarioValidator.DirectionMismatch => SkValidityStatus.DirectionMismatch,
        SkScenarioValidator.AlreadyReached => SkValidityStatus.AlreadyReached,
        SkScenarioValidator.StructureInvalidated => SkValidityStatus.StructureInvalidated,
        SkScenarioValidator.MissingData => SkValidityStatus.InsufficientData,
        _ => SkValidityStatus.Invalid
    };

    public static SkSequenceStatus MapSequenceStatus(SkSequenceCandidateDto candidate)
    {
        if (candidate.ValidationStatus == SkScenarioValidator.DirectionMismatch)
        {
            return SkSequenceStatus.DirectionMismatch;
        }

        if (candidate.ValidationStatus == SkScenarioValidator.StructureInvalidated ||
            candidate.Status == "Invalidated")
        {
            return SkSequenceStatus.Invalidated;
        }

        if (candidate.ValidationStatus == SkScenarioValidator.AlreadyReached ||
            candidate.Status == "Completed")
        {
            return SkSequenceStatus.Completed;
        }

        if (candidate.ValidationStatus == SkScenarioValidator.LowClarity)
        {
            return SkSequenceStatus.LowClarity;
        }

        return candidate.CurrentPricePosition switch
        {
            "InsideCorrectionZone" => SkSequenceStatus.InsideCorrectionZone,
            "BeforeCorrectionZone" => SkSequenceStatus.WaitingForCorrection,
            "LeftCorrectionZone" => SkSequenceStatus.ReactingFromZone,
            "NearTarget" => SkSequenceStatus.TargetReached,
            _ => candidate.Status == "Active" ? SkSequenceStatus.InsideCorrectionZone : SkSequenceStatus.Building
        };
    }

    public static bool IsPrimaryScenario(SkSequenceCandidateDto candidate) =>
        candidate.EligibleForBestIdea &&
        candidate.ValidationStatus is SkScenarioValidator.Valid or SkScenarioValidator.LowClarity;

    public static bool IsHiddenFromBeginner(SkSequenceCandidateDto candidate) =>
        !candidate.EligibleForBestIdea ||
        candidate.ValidationStatus is SkScenarioValidator.DirectionMismatch
            or SkScenarioValidator.StructureInvalidated
            or SkScenarioValidator.AlreadyReached;

    public static string StructureCategoryLabel(SkSequenceCandidateDto candidate)
    {
        if (candidate.ValidationStatus == SkScenarioValidator.DirectionMismatch)
        {
            return "Direction mismatch";
        }

        if (candidate.ValidationStatus == SkScenarioValidator.StructureInvalidated)
        {
            return "Invalidated";
        }

        if (candidate.ValidationStatus == SkScenarioValidator.AlreadyReached)
        {
            return "Already reached";
        }

        if (candidate.ValidationStatus == SkScenarioValidator.LowClarity)
        {
            return "Low clarity";
        }

        if (candidate.EligibleForBestIdea)
        {
            return "Primary";
        }

        return "Alternative only";
    }
}
