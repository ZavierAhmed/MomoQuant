using MomoQuant.Application.StrategyLab.Dtos;

namespace MomoQuant.Application.StrategyLab;

public static class ScoreDistributionCalculator
{
    public static ScoreDistributionDiagnosticsDto Build(
        IReadOnlyList<decimal> scores,
        string modelKind)
    {
        if (scores.Count == 0)
        {
            return new ScoreDistributionDiagnosticsDto();
        }

        var ordered = scores.OrderBy(s => s).ToList();
        var unique = ordered.Distinct().ToList();
        var avg = ordered.Average();
        var variance = ordered.Sum(s => (s - avg) * (s - avg)) / ordered.Count;
        var std = (decimal)Math.Sqrt((double)variance);

        var groups = ordered.GroupBy(s => s).OrderByDescending(g => g.Count()).ToList();
        var mostCommon = groups[0];
        var mostCommonPct = (decimal)mostCommon.Count() / ordered.Count * 100m;

        string? code = null;
        string? message = null;
        if (ordered.Count >= 20 && unique.Count == 1)
        {
            code = modelKind == "risk" ? "RiskModelDegenerate" : "ConfidenceModelDegenerate";
            message = modelKind == "risk"
                ? "All evaluated candidates received the same financial risk score. Candidate-level risk ranking is not functioning."
                : "All candidates received the same confidence score. Candidate-level confidence ranking is not functioning.";
        }
        else if (ordered.Count >= 20 && mostCommonPct > 95m)
        {
            code = modelKind == "risk" ? "RiskModelLowVariance" : "ConfidenceModelLowVariance";
            message = $"One {modelKind} score occurs in {Math.Round(mostCommonPct, 1)}% of candidates. Analysis may be unreliable.";
        }

        return new ScoreDistributionDiagnosticsDto
        {
            UniqueScoreCount = unique.Count,
            MinScore = ordered[0],
            MaxScore = ordered[^1],
            AverageScore = Math.Round(avg, 2),
            StandardDeviation = Math.Round(std, 2),
            MostCommonScore = mostCommon.Key,
            MostCommonScorePercent = Math.Round(mostCommonPct, 2),
            DegenerateWarningCode = code,
            DegenerateWarningMessage = message
        };
    }
}
