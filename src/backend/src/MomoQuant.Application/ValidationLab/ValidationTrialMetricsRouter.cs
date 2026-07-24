using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Legacy (pre-ValidationMetrics/v1.3.2) trial metric population: candidate/summary-derived
/// metrics, ValidationTrainingScore/v1, and the historical "(metric ?? 0m)" guardrails.
/// Kept verbatim so older MetricsVersion experiments reproduce historical behavior.
/// </summary>
public interface IValidationLegacyTrialMetricsMapper
{
    void Apply(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        StrategyLabRun run,
        IReadOnlyList<StrategyResearchCandidate> candidates,
        ValidationQualificationProfile profile);
}

public sealed class ValidationLegacyTrialMetricsMapper : IValidationLegacyTrialMetricsMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public void Apply(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        StrategyLabRun run,
        IReadOnlyList<StrategyResearchCandidate> candidates,
        ValidationQualificationProfile profile)
    {
        var boundary = experiment.ValidationStartUtc.HasValue
            ? ValidationMetricsMapper.CountBoundaryCensored(candidates, experiment.ValidationStartUtc.Value)
            : 0;
        var metricsCandidates = experiment.ValidationStartUtc.HasValue
            ? ValidationMetricsMapper.ExcludeBoundaryFromMetrics(candidates, experiment.ValidationStartUtc.Value)
            : candidates;

        var (summary, riskOnly, fullPipeline) = ValidationLabService.ParseResultSummary(run.ResultSummaryJson);
        var rawMetrics = ValidationMetricsMapper.FromCandidates(
            metricsCandidates,
            experiment.TrainingCandleCount,
            boundary,
            ValidationLayerType.RawStrategy);
        if (summary is not null)
        {
            rawMetrics = ValidationMetricsMapper.FromStrategyLabSummary(
                summary,
                experiment.TrainingCandleCount,
                metricsCandidates.Count,
                boundary,
                riskOnly,
                fullPipeline,
                ValidationLayerType.RawStrategy);
        }

        var feeImpact = rawMetrics.FeeToGrossProfitPercent;
        var oppRate = rawMetrics.OpportunityRatePer1000Candles;
        var score = ValidationTrainingScoreCalculator.Calculate(
            rawMetrics.ClosedTradeCount,
            rawMetrics.NetExpectancyR,
            rawMetrics.ProfitFactor,
            rawMetrics.MaximumRealizedDrawdownPercent,
            feeImpact,
            oppRate,
            profile.MinimumTrainingClosedTrades);

        // Historical guardrails intentionally preserved (including null-coalescing) for
        // legacy MetricsVersion routing only. v1.3.2 uses ValidationGuardrailEvaluator.
        var guardrailFailures = new List<string>();
        if (rawMetrics.ClosedTradeCount < profile.MinimumTrainingClosedTrades)
            guardrailFailures.Add($"ClosedTrades<{profile.MinimumTrainingClosedTrades}");
        if ((rawMetrics.ProfitFactor ?? 0m) < profile.MinimumTrainingProfitFactor)
            guardrailFailures.Add($"ProfitFactor<{profile.MinimumTrainingProfitFactor}");
        if ((rawMetrics.NetExpectancyR ?? 0m) < profile.MinimumTrainingNetExpectancyR)
            guardrailFailures.Add($"NetExpectancyR<{profile.MinimumTrainingNetExpectancyR}");
        if ((rawMetrics.MaximumRealizedDrawdownPercent ?? 0m) > profile.MaximumTrainingDrawdownPercent)
            guardrailFailures.Add($"MaxDD>{profile.MaximumTrainingDrawdownPercent}");

        var passed = guardrailFailures.Count == 0;
        trial.Status = passed ? ValidationTrialStatus.Completed : ValidationTrialStatus.GuardrailRejected;
        trial.CompletedAtUtc = DateTime.UtcNow;
        trial.RawCandidateCount = metricsCandidates.Count;
        trial.ClosedTradeCount = rawMetrics.ClosedTradeCount;
        trial.WinnerCount = rawMetrics.WinnerCount;
        trial.LoserCount = rawMetrics.LoserCount;
        trial.ExpiredCount = rawMetrics.ExpiredCount;
        trial.NetExpectancyR = rawMetrics.NetExpectancyR;
        trial.GrossPnl = rawMetrics.GrossPnl;
        trial.NetPnl = rawMetrics.NetPnl;
        trial.ProfitFactor = rawMetrics.ProfitFactor;
        trial.MaximumDrawdownPercent = rawMetrics.MaximumRealizedDrawdownPercent;
        trial.FeeImpactPercent = feeImpact;
        trial.TrainingScore = score.Total;
        trial.GuardrailDecision = passed ? "Passed" : "Failed";
        trial.GuardrailFailureReasonsJson = guardrailFailures.Count == 0
            ? null
            : JsonSerializer.Serialize(guardrailFailures, JsonOptions);
        trial.ErrorMessage = null;
    }
}

/// <summary>
/// Milestone 23.0D WP23 — explicit MetricsVersion routing for trial metric population.
/// ValidationMetrics/v1.3.2 experiments use <see cref="IValidationTrialMetricsCalculator"/>;
/// known older versions use the legacy mapper; unknown versions throw (no silent upgrade).
/// </summary>
public interface IValidationTrialMetricsRouter
{
    void ApplyTrialMetrics(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        StrategyLabRun run,
        IReadOnlyList<StrategyResearchCandidate> candidates,
        ValidationQualificationProfile profile);
}

public sealed class ValidationTrialMetricsRouter : IValidationTrialMetricsRouter
{
    private static readonly string[] KnownLegacyVersions =
    [
        ValidationMetricsContract.VersionV1Legacy,
        ValidationMetricsContract.VersionV11,
        ValidationMetricsContract.VersionV12,
        ValidationMetricsContract.VersionV13,
        ValidationMetricsContract.VersionV131
    ];

    private readonly IValidationTrialMetricsCalculator _calculator;
    private readonly IValidationLegacyTrialMetricsMapper _legacyMapper;

    public ValidationTrialMetricsRouter(
        IValidationTrialMetricsCalculator calculator,
        IValidationLegacyTrialMetricsMapper legacyMapper)
    {
        _calculator = calculator;
        _legacyMapper = legacyMapper;
    }

    public void ApplyTrialMetrics(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        StrategyLabRun run,
        IReadOnlyList<StrategyResearchCandidate> candidates,
        ValidationQualificationProfile profile)
    {
        var version = experiment.ValidationMetricsVersion;
        if (ValidationMetricsContract.IsPopulationPathMetricsVersion(version))
        {
            var result = _calculator.Calculate(
                experiment, run, candidates, profile, trial.ParameterFingerprint);
            ValidationTrialMetricsCalculator.ApplyToTrial(trial, result);
            trial.StrategyLabRunId = run.Id;
            return;
        }

        if (KnownLegacyVersions.Contains(version, StringComparer.OrdinalIgnoreCase))
        {
            _legacyMapper.Apply(experiment, trial, run, candidates, profile);
            trial.StrategyLabRunId = run.Id;
            return;
        }

        throw new InvalidOperationException(
            $"Unsupported ValidationMetricsVersion '{version}' for experiment {experiment.Id}: "
            + "explicit routing is required and silent upgrades are not permitted.");
    }
}
