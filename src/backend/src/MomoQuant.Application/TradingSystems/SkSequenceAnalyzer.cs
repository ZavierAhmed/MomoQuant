using MomoQuant.Application.Options;
using MomoQuant.Application.TradingSystems.Dtos;

namespace MomoQuant.Application.TradingSystems;

public sealed class SkSequenceAnalyzer : ISkSequenceAnalyzer
{
    public SkSequenceAnalysisResult Analyze(
        IReadOnlyList<SwingPointDto> swings,
        decimal currentPrice,
        string directionMode,
        SkSystemSettings settings)
    {
        if (swings is null || swings.Count < 3)
        {
            return new SkSequenceAnalysisResult();
        }

        var mode = SkSystemConstants.NormalizeDirectionMode(directionMode);
        var ordered = swings.OrderBy(swing => swing.TimeUtc).ToList();
        var candidates = new List<SkSequenceCandidateDto>();

        for (var i = 0; i + 2 < ordered.Count; i++)
        {
            var s0 = ordered[i];
            var s1 = ordered[i + 1];
            var s2 = ordered[i + 2];
            SwingPointDto? s4 = i + 4 < ordered.Count ? ordered[i + 4] : null;

            var isBullish = s0.Type == "Low" && s1.Type == "High" && s2.Type == "Low"
                && s1.Price > s0.Price && s2.Price < s1.Price;
            var isBearish = s0.Type == "High" && s1.Type == "Low" && s2.Type == "High"
                && s1.Price < s0.Price && s2.Price > s1.Price;

            if (isBullish && mode != "BearishOnly")
            {
                candidates.Add(BuildCandidate("Bullish", s0, s1, s2, s4, currentPrice, settings));
            }
            else if (isBearish && mode != "BullishOnly")
            {
                candidates.Add(BuildCandidate("Bearish", s0, s1, s2, s4, currentPrice, settings));
            }
        }

        var selected = candidates
            .OrderByDescending(candidate => candidate.ImpulseEndTimeUtc)
            .Take(Math.Max(settings.MaxSequenceCandidates, 1))
            .Select(candidate => SkScenarioValidator.Validate(candidate, currentPrice))
            .ToList();

        var fibZones = new List<SkFibonacciZoneDto>();
        var keyLevels = new List<SkKeyLevelDto>();

        foreach (var candidate in selected)
        {
            fibZones.AddRange(BuildFibZones(candidate, settings));
            keyLevels.AddRange(BuildKeyLevels(candidate));
        }

        return new SkSequenceAnalysisResult
        {
            Candidates = selected,
            FibonacciZones = fibZones,
            KeyLevels = keyLevels
        };
    }

    private static SkSequenceCandidateDto BuildCandidate(
        string direction,
        SwingPointDto z,
        SwingPointDto a,
        SwingPointDto b,
        SwingPointDto? c,
        decimal currentPrice,
        SkSystemSettings settings)
    {
        var impulseStart = z.Price;
        var impulseEnd = a.Price;
        var range = Math.Abs(impulseEnd - impulseStart);

        var correctionAtMin = SkFibonacciCalculator.Retracement(impulseStart, impulseEnd, settings.FibonacciCorrectionLevels.Min());
        var correctionAtMax = SkFibonacciCalculator.Retracement(impulseStart, impulseEnd, settings.FibonacciCorrectionLevels.Max());
        var correctionZoneMin = Math.Min(correctionAtMin, correctionAtMax);
        var correctionZoneMax = Math.Max(correctionAtMin, correctionAtMax);

        var gpA = SkFibonacciCalculator.Retracement(impulseStart, impulseEnd, settings.GoldenPocketMin);
        var gpB = SkFibonacciCalculator.Retracement(impulseStart, impulseEnd, settings.GoldenPocketMax);
        var goldenPocketMin = Math.Min(gpA, gpB);
        var goldenPocketMax = Math.Max(gpA, gpB);

        var correctionPoint = b.Price;
        var ext = settings.FibonacciExtensionLevels;
        var target1Ratio = ext.Count > 0 ? ext[0] : 1.0m;
        var target2Ratio = ext.Count > 1 ? ext[1] : 1.272m;
        var target1 = SkFibonacciCalculator.Extension(impulseStart, impulseEnd, correctionPoint, target1Ratio);
        var target2 = SkFibonacciCalculator.Extension(impulseStart, impulseEnd, correctionPoint, target2Ratio);
        var extension1618 = SkFibonacciCalculator.Extension(impulseStart, impulseEnd, correctionPoint, 1.618m);

        var invalidation = z.Price;

        var position = ResolvePosition(direction, currentPrice, correctionZoneMin, correctionZoneMax, invalidation, target1);
        var status = ResolveStatus(direction, currentPrice, invalidation, target1, correctionZoneMin, correctionZoneMax);

        var warnings = new List<string>();
        if (range <= 0)
        {
            warnings.Add("Impulse range is zero; levels may be unreliable.");
        }

        if (direction == "Bullish" && b.Price <= z.Price)
        {
            warnings.Add("Correction dipped below the sequence low; structure may already be invalidated.");
        }
        else if (direction == "Bearish" && b.Price >= z.Price)
        {
            warnings.Add("Correction pushed above the sequence high; structure may already be invalidated.");
        }

        var confidence = ComputeConfidence(z, a, b, position, warnings.Count);

        var notes = direction == "Bullish"
            ? "Approximate bullish sequence: swing low (Z) → impulse high (A) → correction (B). Levels are calculated, not certified."
            : "Approximate bearish sequence: swing high (Z) → impulse low (A) → correction (B). Levels are calculated, not certified.";

        return new SkSequenceCandidateDto
        {
            Id = $"{direction[0]}-{z.TimeUtc:yyyyMMddHHmmss}-{a.TimeUtc:yyyyMMddHHmmss}",
            Direction = direction,
            Status = status,
            PointZ = ToPoint("Z", z),
            PointA = ToPoint("A", a),
            PointB = ToPoint("B", b),
            PointC = c is not null && c.Type == b.Type ? ToPoint("C", c) : null,
            ImpulseStartTimeUtc = z.TimeUtc,
            ImpulseEndTimeUtc = a.TimeUtc,
            CorrectionZoneMin = decimal.Round(correctionZoneMin, 8),
            CorrectionZoneMax = decimal.Round(correctionZoneMax, 8),
            GoldenPocketMin = decimal.Round(goldenPocketMin, 8),
            GoldenPocketMax = decimal.Round(goldenPocketMax, 8),
            Target1 = decimal.Round(target1, 8),
            Target2 = decimal.Round(target2, 8),
            Extension1618 = decimal.Round(extension1618, 8),
            InvalidationLevel = decimal.Round(invalidation, 8),
            CurrentPricePosition = position,
            ConfidenceScore = decimal.Round(confidence, 1),
            Notes = notes,
            Warnings = warnings
        };
    }

    private static SkSequencePointDto ToPoint(string label, SwingPointDto swing) => new()
    {
        Label = label,
        CandleId = swing.CandleId,
        TimeUtc = swing.TimeUtc,
        Price = swing.Price,
        Description = label switch
        {
            "Z" => "Sequence start — swing point selected for structure anchor.",
            "A" => "Impulse end — where the first strong move finished.",
            "B" => "Correction point — pullback used for Fibonacci extensions.",
            "C" => "Follow-through point — optional continuation swing.",
            _ => "Structure point selected from detected swings."
        }
    };

    private static string ResolvePosition(
        string direction,
        decimal price,
        decimal zoneMin,
        decimal zoneMax,
        decimal invalidation,
        decimal target1)
    {
        if (direction == "Bullish")
        {
            if (price <= invalidation)
            {
                return "Invalidated";
            }

            if (price >= target1)
            {
                return "NearTarget";
            }

            if (price >= zoneMin && price <= zoneMax)
            {
                return "InsideCorrectionZone";
            }

            return price > zoneMax ? "BeforeCorrectionZone" : "LeftCorrectionZone";
        }

        if (price >= invalidation)
        {
            return "Invalidated";
        }

        if (price <= target1)
        {
            return "NearTarget";
        }

        if (price >= zoneMin && price <= zoneMax)
        {
            return "InsideCorrectionZone";
        }

        return price < zoneMin ? "BeforeCorrectionZone" : "LeftCorrectionZone";
    }

    private static string ResolveStatus(
        string direction,
        decimal price,
        decimal invalidation,
        decimal target1,
        decimal zoneMin,
        decimal zoneMax)
    {
        if (direction == "Bullish")
        {
            if (price <= invalidation)
            {
                return "Invalidated";
            }

            if (price >= target1)
            {
                return "Completed";
            }

            return price >= zoneMin && price <= zoneMax ? "Active" : "Potential";
        }

        if (price >= invalidation)
        {
            return "Invalidated";
        }

        if (price <= target1)
        {
            return "Completed";
        }

        return price >= zoneMin && price <= zoneMax ? "Active" : "Potential";
    }

    private static decimal ComputeConfidence(
        SwingPointDto z,
        SwingPointDto a,
        SwingPointDto b,
        string position,
        int warningCount)
    {
        var avgStrength = (z.Strength + a.Strength + b.Strength) / 3m;
        var score = 35m + (avgStrength * 0.4m);

        score += position switch
        {
            "InsideCorrectionZone" => 15m,
            "BeforeCorrectionZone" => 8m,
            "NearTarget" => 5m,
            "Invalidated" => -25m,
            _ => 0m
        };

        score -= warningCount * 10m;
        return Math.Clamp(score, 0m, 100m);
    }

    private static IEnumerable<SkFibonacciZoneDto> BuildFibZones(
        SkSequenceCandidateDto candidate,
        SkSystemSettings settings)
    {
        var impulseStart = candidate.PointZ?.Price ?? 0m;
        var impulseEnd = candidate.PointA?.Price ?? 0m;
        var correctionPoint = candidate.PointB?.Price ?? impulseEnd;

        foreach (var ratio in settings.FibonacciCorrectionLevels)
        {
            var price = SkFibonacciCalculator.Retracement(impulseStart, impulseEnd, ratio);
            var isGolden = ratio >= settings.GoldenPocketMin && ratio <= settings.GoldenPocketMax;
            yield return new SkFibonacciZoneDto
            {
                SequenceId = candidate.Id,
                Kind = "Retracement",
                Ratio = ratio,
                Price = decimal.Round(price, 8),
                Label = $"{candidate.Direction} {ratio:0.###} retracement{(isGolden ? " (golden pocket)" : string.Empty)}",
                IsGoldenPocket = isGolden
            };
        }

        foreach (var ratio in settings.FibonacciExtensionLevels)
        {
            var price = SkFibonacciCalculator.Extension(impulseStart, impulseEnd, correctionPoint, ratio);
            yield return new SkFibonacciZoneDto
            {
                SequenceId = candidate.Id,
                Kind = "Extension",
                Ratio = ratio,
                Price = decimal.Round(price, 8),
                Label = $"{candidate.Direction} {ratio:0.###} extension",
                IsGoldenPocket = false
            };
        }
    }

    private static IEnumerable<SkKeyLevelDto> BuildKeyLevels(SkSequenceCandidateDto candidate)
    {
        yield return new SkKeyLevelDto
        {
            Label = $"{candidate.Direction} invalidation",
            Price = candidate.InvalidationLevel,
            Kind = "Invalidation",
            SequenceId = candidate.Id
        };

        yield return new SkKeyLevelDto
        {
            Label = $"{candidate.Direction} target 1",
            Price = candidate.Target1,
            Kind = "Target",
            SequenceId = candidate.Id
        };

        yield return new SkKeyLevelDto
        {
            Label = $"{candidate.Direction} target 2",
            Price = candidate.Target2,
            Kind = "Target",
            SequenceId = candidate.Id
        };
    }
}
