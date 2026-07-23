using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public sealed class ParameterStabilityResult
{
    public ParameterStabilityApplicability Applicability { get; init; } =
        ParameterStabilityApplicability.Applicable;
    public string? ApplicabilityReason { get; init; }
    public decimal? BestTrainingScore { get; init; }
    public int NearBestCount { get; init; }
    public decimal? NearBestScoreMin { get; init; }
    public decimal? NearBestScoreMax { get; init; }
    public decimal? NearBestScoreRange { get; init; }
    public IReadOnlyList<object> Top10Trials { get; init; } = [];
    public IReadOnlyList<string> ApplicableParameters { get; init; } = [];
    public IReadOnlyList<string> NonApplicableParameters { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool IsStable { get; init; }
    public decimal? StabilityScore { get; init; }
    public string StabilityStatus { get; init; } = "Unknown";
}

public static class ValidationParameterStabilityAnalyzer
{
    public static ParameterStabilityResult AnalyzeForExperimentType(
        ValidationExperimentType experimentType,
        IReadOnlyList<ValidationParameterTrial> trials,
        decimal nearBestDelta = 5m,
        int minimumClosedTradesForSearchRatio = 10,
        int minimumTrialsForStability = 3)
    {
        if (experimentType == ValidationExperimentType.ValidateExistingFrozenConfiguration)
        {
            return new ParameterStabilityResult
            {
                Applicability = ParameterStabilityApplicability.NotApplicable,
                ApplicabilityReason =
                    "No parameter search was performed. A single frozen configuration cannot be evaluated for neighborhood stability.",
                IsStable = true,
                StabilityStatus = "NotApplicable",
                Warnings = []
            };
        }

        var completed = trials
            .Where(t => t.Status is ValidationTrialStatus.Completed or ValidationTrialStatus.GuardrailRejected)
            .Where(t => t.TrainingScore.HasValue)
            .OrderByDescending(t => t.TrainingScore)
            .ThenByDescending(t => t.NetExpectancyR ?? decimal.MinValue)
            .ThenBy(t => t.ParameterFingerprint)
            .ToList();

        if (completed.Count < minimumTrialsForStability)
        {
            return new ParameterStabilityResult
            {
                Applicability = ParameterStabilityApplicability.InsufficientTrials,
                ApplicabilityReason = $"Only {completed.Count} scored trials; need at least {minimumTrialsForStability}.",
                IsStable = false,
                StabilityStatus = "InsufficientTrials",
                Warnings = ["InsufficientTrials"],
                Top10Trials = completed.Take(10).Select(MapTrial).ToList()
            };
        }

        var analyzed = Analyze(completed, nearBestDelta, minimumClosedTradesForSearchRatio);
        return new ParameterStabilityResult
        {
            Applicability = ParameterStabilityApplicability.Evaluated,
            ApplicabilityReason = "Training search trials evaluated for neighborhood stability.",
            BestTrainingScore = analyzed.BestTrainingScore,
            NearBestCount = analyzed.NearBestCount,
            NearBestScoreMin = analyzed.NearBestScoreMin,
            NearBestScoreMax = analyzed.NearBestScoreMax,
            NearBestScoreRange = analyzed.NearBestScoreRange,
            Top10Trials = analyzed.Top10Trials,
            ApplicableParameters = analyzed.ApplicableParameters,
            NonApplicableParameters = analyzed.NonApplicableParameters,
            Warnings = analyzed.Warnings,
            IsStable = analyzed.IsStable,
            StabilityScore = analyzed.StabilityScore,
            StabilityStatus = analyzed.IsStable ? "Stable" : "Unstable"
        };
    }

    public static ParameterStabilityResult Analyze(
        IReadOnlyList<ValidationParameterTrial> trials,
        decimal nearBestDelta = 5m,
        int minimumClosedTradesForSearchRatio = 10)
    {
        var completed = trials
            .Where(t => t.Status is ValidationTrialStatus.Completed or ValidationTrialStatus.GuardrailRejected)
            .Where(t => t.TrainingScore.HasValue)
            .OrderByDescending(t => t.TrainingScore)
            .ThenByDescending(t => t.NetExpectancyR ?? decimal.MinValue)
            .ThenBy(t => t.ParameterFingerprint)
            .ToList();

        if (completed.Count == 0)
        {
            return new ParameterStabilityResult
            {
                Applicability = ParameterStabilityApplicability.InsufficientTrials,
                Warnings = ["No completed trials for stability analysis."],
                IsStable = false,
                StabilityStatus = "InsufficientTrials"
            };
        }

        var best = completed[0].TrainingScore!.Value;
        var nearBest = completed.Where(t => t.TrainingScore!.Value >= best - nearBestDelta).ToList();
        var passedGuardrails = completed.Where(t => t.GuardrailDecision == "Passed").ToList();
        var warnings = new List<string>();

        if (passedGuardrails.Count <= 1)
        {
            warnings.Add("SingleWinnerDominance");
        }

        if (nearBest.Count >= 2)
        {
            var scores = nearBest.Select(t => t.TrainingScore!.Value).ToList();
            var range = scores.Max() - scores.Min();
            if (range >= 15m)
            {
                warnings.Add("LowParameterStability");
            }
        }

        var bestClosed = completed[0].ClosedTradeCount;
        if (completed.Count > Math.Max(20, bestClosed * 3) && bestClosed < minimumClosedTradesForSearchRatio * 3)
        {
            warnings.Add("ExcessiveSearchRelativeToSample");
        }

        if (completed.Count >= 2)
        {
            var second = completed.Skip(1).FirstOrDefault(t => t.ParameterFingerprint != completed[0].ParameterFingerprint);
            if (second?.TrainingScore is { } s2 && best - s2 >= 10m)
            {
                warnings.Add("ParameterCliff");
            }
        }

        var top10 = completed.Take(10).Select(MapTrial).ToList();
        var nearScores = nearBest.Select(t => t.TrainingScore!.Value).ToList();
        var isStable = !warnings.Contains("ParameterCliff")
            && !warnings.Contains("LowParameterStability")
            && !warnings.Contains("SingleWinnerDominance");

        return new ParameterStabilityResult
        {
            Applicability = ParameterStabilityApplicability.Evaluated,
            BestTrainingScore = best,
            NearBestCount = nearBest.Count,
            NearBestScoreMin = nearScores.Count > 0 ? nearScores.Min() : null,
            NearBestScoreMax = nearScores.Count > 0 ? nearScores.Max() : null,
            NearBestScoreRange = nearScores.Count > 0 ? nearScores.Max() - nearScores.Min() : null,
            Top10Trials = top10,
            ApplicableParameters = ["numeric"],
            NonApplicableParameters = ["enum", "boolean"],
            Warnings = warnings,
            IsStable = isStable,
            StabilityScore = isStable ? 1m : 0m,
            StabilityStatus = isStable ? "Stable" : "Unstable"
        };
    }

    public static string Serialize(ParameterStabilityResult result) =>
        JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private static object MapTrial(ValidationParameterTrial t) => new
    {
        t.TrialNumber,
        t.ParameterFingerprint,
        t.TrainingScore,
        t.NetExpectancyR,
        t.ProfitFactor,
        t.ClosedTradeCount,
        t.GuardrailDecision,
        t.Rank
    };
}
