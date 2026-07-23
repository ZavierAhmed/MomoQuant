using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public sealed class HoldoutReuseWarning
{
    public IReadOnlyList<long> PriorExperimentIds { get; init; } = [];
    public decimal OverlapPercent { get; init; }
    public int RevealCount { get; init; }
    public DateTime? FirstRevealedAtUtc { get; init; }
    public string ContaminationRisk { get; init; } = "Low";
    public bool RepeatedHoldoutExposure { get; init; }
}

public static class ValidationHoldoutReuseDetector
{
    public static HoldoutReuseWarning Detect(
        ValidationExperiment current,
        IReadOnlyList<ValidationExperiment> priors)
    {
        if (current.ValidationStartUtc is null || current.ValidationEndUtc is null)
        {
            return new HoldoutReuseWarning();
        }

        var curStart = current.ValidationStartUtc.Value;
        var curEnd = current.ValidationEndUtc.Value;
        var curSpan = Math.Max((curEnd - curStart).TotalSeconds, 1);

        var matches = new List<ValidationExperiment>();
        decimal maxOverlap = 0m;
        DateTime? firstReveal = null;

        foreach (var p in priors)
        {
            if (p.Id == current.Id) continue;
            if (p.ValidationRevealStatus != ValidationRevealStatus.Revealed) continue;
            if (!string.Equals(p.StrategyCode, current.StrategyCode, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(p.StrategyVersion, current.StrategyVersion, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(p.Symbol, current.Symbol, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(p.Timeframe, current.Timeframe, StringComparison.OrdinalIgnoreCase)) continue;
            if (p.ValidationStartUtc is null || p.ValidationEndUtc is null) continue;

            var overlapStart = curStart > p.ValidationStartUtc.Value ? curStart : p.ValidationStartUtc.Value;
            var overlapEnd = curEnd < p.ValidationEndUtc.Value ? curEnd : p.ValidationEndUtc.Value;
            if (overlapEnd <= overlapStart) continue;

            var overlapPct = (decimal)((overlapEnd - overlapStart).TotalSeconds / curSpan * 100);
            if (overlapPct < 10m) continue;

            matches.Add(p);
            maxOverlap = Math.Max(maxOverlap, overlapPct);
            if (p.ValidationRevealedAtUtc.HasValue)
            {
                firstReveal = firstReveal is null || p.ValidationRevealedAtUtc < firstReveal
                    ? p.ValidationRevealedAtUtc
                    : firstReveal;
            }
        }

        var risk = matches.Count == 0 ? "Low"
            : maxOverlap >= 80m || matches.Count >= 3 ? "High"
            : maxOverlap >= 40m ? "Medium"
            : "Low";

        return new HoldoutReuseWarning
        {
            PriorExperimentIds = matches.Select(m => m.Id).ToList(),
            OverlapPercent = Math.Round(maxOverlap, 2),
            RevealCount = matches.Count,
            FirstRevealedAtUtc = firstReveal,
            ContaminationRisk = risk,
            RepeatedHoldoutExposure = matches.Count > 0
        };
    }

    public static string Serialize(HoldoutReuseWarning warning) =>
        JsonSerializer.Serialize(warning, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
}
