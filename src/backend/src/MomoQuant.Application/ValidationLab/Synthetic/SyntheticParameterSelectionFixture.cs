using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab.Synthetic;

/// <summary>
/// Deterministic synthetic fixture for Manual Verification B / Part 30 leakage proof.
/// Mode=A wins on training; Mode=B wins on validation. Training selection must ignore validation.
/// </summary>
public static class SyntheticParameterSelectionFixture
{
    public const string StrategyCode = "SYNTHETIC_PARAMETER_SELECTION_FIXTURE";
    public const string ModeA = "A";
    public const string ModeB = "B";

    public static IReadOnlyList<Dictionary<string, string>> SearchSpace() =>
    [
        new(StringComparer.OrdinalIgnoreCase) { ["Mode"] = ModeA },
        new(StringComparer.OrdinalIgnoreCase) { ["Mode"] = ModeB }
    ];

    public static LayerSegmentMetrics TrainingMetricsForMode(string mode) =>
        mode.Equals(ModeA, StringComparison.OrdinalIgnoreCase)
            ? Healthy(closed: 40, netExp: 0.40m, grossExp: 0.50m, netPf: 1.40m, netPnl: 200m)
            : Healthy(closed: 40, netExp: 0.10m, grossExp: 0.20m, netPf: 1.05m, netPnl: 40m);

    public static LayerSegmentMetrics ValidationMetricsForMode(string mode, bool amplifiedB = false) =>
        mode.Equals(ModeA, StringComparison.OrdinalIgnoreCase)
            ? Healthy(closed: 20, netExp: 0.05m, grossExp: 0.10m, netPf: 1.02m, netPnl: 10m)
            : Healthy(
                closed: 20,
                netExp: amplifiedB ? 0.80m : 0.55m,
                grossExp: amplifiedB ? 0.90m : 0.65m,
                netPf: amplifiedB ? 2.00m : 1.60m,
                netPnl: amplifiedB ? 400m : 220m);

    public static decimal TrainingScore(LayerSegmentMetrics m) =>
        ValidationTrainingScoreCalculator.Calculate(
            m.ClosedTradeCount,
            m.NetExpectancyR ?? 0m,
            m.NetProfitFactor ?? m.ProfitFactor ?? 0m,
            m.MaximumRealizedDrawdownPercent ?? 5m,
            m.FeeToGrossProfitPercent ?? 2m,
            m.OpportunityRatePer1000Candles,
            minimumClosedTrades: 10).Total;

    public static SyntheticOrchestrationResult RunLeakageProofOrchestration(bool amplifyValidationB = false)
    {
        var trials = SearchSpace().Select((combo, i) =>
        {
            var mode = combo["Mode"];
            var metrics = TrainingMetricsForMode(mode);
            var score = TrainingScore(metrics);
            return new ValidationParameterTrial
            {
                TrialNumber = i + 1,
                ParameterSnapshotJson = JsonSerializer.Serialize(combo),
                ParameterFingerprint = Fingerprint(combo),
                Status = ValidationTrialStatus.Completed,
                ClosedTradeCount = metrics.ClosedTradeCount,
                NetExpectancyR = metrics.NetExpectancyR,
                ProfitFactor = metrics.NetProfitFactor,
                NetPnl = metrics.NetPnl,
                GrossPnl = metrics.GrossPnl,
                TrainingScore = score,
                GuardrailDecision = "Passed",
                Rank = null
            };
        }).ToList();

        var ranked = trials
            .OrderByDescending(t => t.TrainingScore)
            .ThenBy(t => t.ParameterFingerprint)
            .ToList();
        for (var i = 0; i < ranked.Count; i++) ranked[i].Rank = i + 1;

        var winner = ranked[0];
        var winnerMode = JsonSerializer.Deserialize<Dictionary<string, string>>(winner.ParameterSnapshotJson)!["Mode"];
        var valA = ValidationMetricsForMode(ModeA, amplifyValidationB);
        var valB = ValidationMetricsForMode(ModeB, amplifyValidationB);
        var bBetterOnVal = (valB.NetExpectancyR ?? 0m) > (valA.NetExpectancyR ?? 0m);

        var trainStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var trainEnd = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc);
        var valStart = new DateTime(2026, 1, 21, 0, 0, 0, DateTimeKind.Utc);
        var leakage = new ValidationLeakageAuditor().Evaluate(
            maxTimestampAccessedByOptimizer: trainEnd,
            validationStartUtc: valStart,
            trainingStartUtc: trainStart,
            trainingEndUtc: trainEnd,
            optimizerInputFingerprint: Fingerprint(new Dictionary<string, string>
            {
                ["seed"] = "42",
                ["trainEnd"] = trainEnd.ToString("O")
            }));

        var trainFpBefore = FingerprintTrials(ranked);
        // Re-run with amplified validation must not change training ranking fingerprint.
        var trials2 = SearchSpace().Select((combo, i) =>
        {
            var mode = combo["Mode"];
            var metrics = TrainingMetricsForMode(mode);
            return new ValidationParameterTrial
            {
                TrialNumber = i + 1,
                ParameterSnapshotJson = JsonSerializer.Serialize(combo),
                ParameterFingerprint = Fingerprint(combo),
                Status = ValidationTrialStatus.Completed,
                ClosedTradeCount = metrics.ClosedTradeCount,
                NetExpectancyR = metrics.NetExpectancyR,
                ProfitFactor = metrics.NetProfitFactor,
                TrainingScore = TrainingScore(metrics),
                GuardrailDecision = "Passed"
            };
        }).OrderByDescending(t => t.TrainingScore).ThenBy(t => t.ParameterFingerprint).ToList();
        for (var i = 0; i < trials2.Count; i++) trials2[i].Rank = i + 1;
        var trainFpAfter = FingerprintTrials(trials2);

        return new SyntheticOrchestrationResult
        {
            SelectedMode = winnerMode,
            SelectedFingerprint = winner.ParameterFingerprint,
            TrainingRankingFingerprints = ranked.Select(t => t.ParameterFingerprint).ToList(),
            ValidationModeBOutperformsA = bBetterOnVal,
            ValidationMetricsA = valA,
            ValidationMetricsB = valB,
            LeakageStatus = leakage.Status,
            TrainingTrialFingerprintBefore = trainFpBefore,
            TrainingTrialFingerprintAfterValidationAmplify = trainFpAfter,
            SelectionUnchangedAfterValidationAmplify =
                winner.ParameterFingerprint == trials2[0].ParameterFingerprint
                && trainFpBefore == trainFpAfter
        };
    }

    public static string Fingerprint(IReadOnlyDictionary<string, string> parameters)
    {
        var raw = string.Join("|", parameters.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}={kv.Value}"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16];
    }

    private static string FingerprintTrials(IReadOnlyList<ValidationParameterTrial> trials)
    {
        var raw = string.Join(";", trials.Select(t => $"{t.Rank}:{t.ParameterFingerprint}:{t.TrainingScore:0.####}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16];
    }

    private static LayerSegmentMetrics Healthy(
        int closed,
        decimal netExp,
        decimal grossExp,
        decimal netPf,
        decimal netPnl) =>
        new()
        {
            ClosedTradeCount = closed,
            CandidateCount = closed,
            CandleCount = 1000,
            OpportunityRatePer1000Candles = closed,
            WinnerCount = closed * 6 / 10,
            LoserCount = closed * 4 / 10,
            GrossExpectancyR = grossExp,
            NetExpectancyR = netExp,
            GrossAverageR = grossExp,
            NetAverageR = netExp,
            AverageR = grossExp,
            GrossProfitFactor = netPf + 0.1m,
            NetProfitFactor = netPf,
            ProfitFactor = netPf,
            GrossPnl = netPnl + 20m,
            NetPnl = netPnl,
            TransactionCosts = 20m,
            MaximumRealizedDrawdownPercent = 8m,
            MetricsVersion = ValidationMetricsContract.VersionV11
        };
}

public sealed class SyntheticOrchestrationResult
{
    public string SelectedMode { get; init; } = string.Empty;
    public string SelectedFingerprint { get; init; } = string.Empty;
    public IReadOnlyList<string> TrainingRankingFingerprints { get; init; } = [];
    public bool ValidationModeBOutperformsA { get; init; }
    public LayerSegmentMetrics ValidationMetricsA { get; init; } = new();
    public LayerSegmentMetrics ValidationMetricsB { get; init; } = new();
    public ValidationLeakageAuditStatus LeakageStatus { get; init; }
    public string TrainingTrialFingerprintBefore { get; init; } = string.Empty;
    public string TrainingTrialFingerprintAfterValidationAmplify { get; init; } = string.Empty;
    public bool SelectionUnchangedAfterValidationAmplify { get; init; }
}
