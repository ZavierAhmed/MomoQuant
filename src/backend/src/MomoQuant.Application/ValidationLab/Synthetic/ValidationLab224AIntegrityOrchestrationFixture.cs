using System.Text.Json;
using MomoQuant.Application.Strategies.Optimization;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab.Synthetic;

/// <summary>
/// Controlled Validation Laboratory orchestration fixtures for Milestone 22.4A.
/// Does not execute market backtests — trial rows are pre-seeded before RunTrainingAsync.
/// </summary>
public static class ValidationLab224AIntegrityOrchestrationFixture
{
    public const int DeterministicSeed = 224001;

    public static IReadOnlyList<IReadOnlyDictionary<string, string>> BuildThreeTrialGrid(
        IStrategyParameterDefinitionProvider definitions)
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["swingLeftBarsMin"] = "1",
            ["swingLeftBarsMax"] = "3",
            ["swingLeftBarsStep"] = "1",
            ["swingRightBarsMin"] = "1",
            ["swingRightBarsMax"] = "1",
            ["swingRightBarsStep"] = "1",
            ["retestTolerancePercentMin"] = "0.3",
            ["retestTolerancePercentMax"] = "0.3",
            ["retestTolerancePercentStep"] = "0.1",
            ["maxRetestBarsMin"] = "10",
            ["maxRetestBarsMax"] = "10",
            ["maxRetestBarsStep"] = "1",
            ["fixedRewardRiskMin"] = "2",
            ["fixedRewardRiskMax"] = "2",
            ["fixedRewardRiskStep"] = "0.5",
            ["stopBufferPercentMin"] = "0.05",
            ["stopBufferPercentMax"] = "0.05",
            ["stopBufferPercentStep"] = "0.05"
        };

        var grid = definitions
            .GenerateGridCombinations(StrategyCodes.PriceStructureBreakoutRetest, 3, overrides)
            .Select(c => new Dictionary<string, string>(c, StringComparer.OrdinalIgnoreCase) as IReadOnlyDictionary<string, string>)
            .ToList();

        DeterministicShuffle(grid, DeterministicSeed);
        return grid.Take(3).ToList();
    }

    public static IReadOnlyList<ValidationParameterTrial> SeedAllRejected(
        long experimentId,
        IReadOnlyList<IReadOnlyDictionary<string, string>> combos) =>
        combos.Select((combo, i) => BuildTrial(
            experimentId,
            i + 1,
            combo,
            eligible: false,
            trainingScore: 1m)).ToList();

    public static IReadOnlyList<ValidationParameterTrial> SeedOneEligible(
        long experimentId,
        IReadOnlyList<IReadOnlyDictionary<string, string>> combos,
        int eligibleIndex = 1) =>
        combos.Select((combo, i) => BuildTrial(
            experimentId,
            i + 1,
            combo,
            eligible: i == eligibleIndex,
            trainingScore: i == eligibleIndex ? 50m : 1m)).ToList();

    public static IReadOnlyList<ValidationParameterTrial> SeedMultipleEligible(
        long experimentId,
        IReadOnlyList<IReadOnlyDictionary<string, string>> combos) =>
        combos.Select((combo, i) => BuildTrial(
            experimentId,
            i + 1,
            combo,
            eligible: true,
            trainingScore: (3 - i) * 10m)).ToList();

    private static ValidationParameterTrial BuildTrial(
        long experimentId,
        int trialNumber,
        IReadOnlyDictionary<string, string> combo,
        bool eligible,
        decimal trainingScore)
    {
        var snapshotJson = JsonSerializer.Serialize(combo);
        var fingerprint = ValidationLabService.ParameterFingerprint(combo);

        return new ValidationParameterTrial
        {
            ValidationExperimentId = experimentId,
            TrialNumber = trialNumber,
            ParameterSnapshotJson = snapshotJson,
            ParameterFingerprint = fingerprint,
            Status = eligible ? ValidationTrialStatus.Completed : ValidationTrialStatus.GuardrailRejected,
            GuardrailDecision = eligible ? "Passed" : "Failed",
            GuardrailFailureReasonsJson = eligible ? null : "[\"ControlledFixtureRejected\"]",
            TrainingScore = trainingScore,
            NetExpectancyR = eligible ? 0.25m : -0.50m,
            ProfitFactor = eligible ? 1.50m : 0.80m,
            ClosedTradeCount = eligible ? 20 : 5,
            StrategyLabRunId = 900000 + trialNumber,
            CompletedAtUtc = DateTime.UtcNow
        };
    }

    private static void DeterministicShuffle<T>(IList<T> items, int seed)
    {
        var rng = new Random(seed);
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
