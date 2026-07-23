using System.Text.Json;
using System.Text.RegularExpressions;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.StrategyLab.Dtos;
using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public sealed class ValidationTrialRecoveryReport
{
    public IReadOnlyList<int> RecoveredTrialNumbers { get; init; } = [];
    public IReadOnlyList<int> UnrecoverableTrialNumbers { get; init; } = [];
    public IReadOnlyList<int> SkippedAlreadyPersisted { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
}

public interface IValidationTrialRecoveryService
{
    Task<ValidationTrialRecoveryReport> RecoverFromStrategyLabRunsAsync(
        ValidationExperiment experiment,
        IReadOnlyList<Dictionary<string, string>> combos,
        ValidationQualificationProfile profile,
        CancellationToken cancellationToken = default);
}

public sealed class ValidationTrialRecoveryService : IValidationTrialRecoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex TrialNameRegex = new(
        @"^VL-Train-(?<exp>\d+)-T(?<trial>\d+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IStrategyLabRunRepository _labRuns;
    private readonly IStrategyResearchCandidateRepository _candidates;
    private readonly IValidationParameterTrialRepository _trials;

    public ValidationTrialRecoveryService(
        IStrategyLabRunRepository labRuns,
        IStrategyResearchCandidateRepository candidates,
        IValidationParameterTrialRepository trials)
    {
        _labRuns = labRuns;
        _candidates = candidates;
        _trials = trials;
    }

    public async Task<ValidationTrialRecoveryReport> RecoverFromStrategyLabRunsAsync(
        ValidationExperiment experiment,
        IReadOnlyList<Dictionary<string, string>> combos,
        ValidationQualificationProfile profile,
        CancellationToken cancellationToken = default)
    {
        var prefix = $"VL-Train-{experiment.Id}-T";
        var runs = await _labRuns.GetByNamePrefixAsync(prefix, cancellationToken);
        var existing = await _trials.GetByExperimentIdAsync(experiment.Id, cancellationToken);
        var existingByFp = existing.ToDictionary(t => t.ParameterFingerprint, StringComparer.OrdinalIgnoreCase);

        var recovered = new List<int>();
        var skipped = new List<int>();
        var unrecoverable = new List<int>();

        for (var i = 0; i < combos.Count; i++)
        {
            var trialNumber = i + 1;
            var combo = combos[i];
            var fingerprint = ValidationLabService.ParameterFingerprint(combo);

            if (existingByFp.TryGetValue(fingerprint, out var persisted)
                && persisted.Status is ValidationTrialStatus.Completed or ValidationTrialStatus.GuardrailRejected)
            {
                skipped.Add(trialNumber);
                continue;
            }

            var runName = $"{prefix}{trialNumber}";
            var candidates = runs
                .Where(r => string.Equals(r.Name, runName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.CompletedAtUtc ?? r.CreatedAtUtc)
                .ToList();

            var run = candidates.FirstOrDefault(r => r.Status == StrategyLabRunStatus.Completed);
            if (run is null)
            {
                if (candidates.Count > 0)
                {
                    unrecoverable.Add(trialNumber);
                }

                continue;
            }

            if (!ParametersMatch(run.ParametersJson, combo))
            {
                unrecoverable.Add(trialNumber);
                continue;
            }

            var trial = await BuildRecoveredTrialAsync(
                experiment, trialNumber, combo, fingerprint, run, profile, cancellationToken);

            if (existingByFp.TryGetValue(fingerprint, out var update))
            {
                trial.Id = update.Id;
                await _trials.UpdateAsync(trial, cancellationToken);
            }
            else
            {
                await _trials.AddAsync(trial, cancellationToken);
                existingByFp[fingerprint] = trial;
            }

            recovered.Add(trialNumber);
        }

        return new ValidationTrialRecoveryReport
        {
            RecoveredTrialNumbers = recovered,
            UnrecoverableTrialNumbers = unrecoverable,
            SkippedAlreadyPersisted = skipped,
            Summary = $"Recovered {recovered.Count} trial(s); skipped {skipped.Count}; unrecoverable {unrecoverable.Count}."
        };
    }

    private async Task<ValidationParameterTrial> BuildRecoveredTrialAsync(
        ValidationExperiment experiment,
        int trialNumber,
        IReadOnlyDictionary<string, string> combo,
        string fingerprint,
        StrategyLabRun run,
        ValidationQualificationProfile profile,
        CancellationToken cancellationToken)
    {
        var candidateRows = await _candidates.GetByRunIdAsync(run.Id, cancellationToken);
        var boundary = experiment.ValidationStartUtc.HasValue
            ? ValidationMetricsMapper.CountBoundaryCensored(candidateRows, experiment.ValidationStartUtc.Value)
            : 0;
        var metricsCandidates = experiment.ValidationStartUtc.HasValue
            ? ValidationMetricsMapper.ExcludeBoundaryFromMetrics(candidateRows, experiment.ValidationStartUtc.Value)
            : candidateRows;

        var (summary, riskOnly, fullPipeline) = ParseResultSummary(run.ResultSummaryJson);
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

        var guardrailFailures = EvaluateGuardrails(rawMetrics, profile);
        var passed = guardrailFailures.Count == 0;

        return new ValidationParameterTrial
        {
            ValidationExperimentId = experiment.Id,
            TrialNumber = trialNumber,
            ParameterSnapshotJson = JsonSerializer.Serialize(combo, JsonOptions),
            ParameterFingerprint = fingerprint,
            Status = passed ? ValidationTrialStatus.Completed : ValidationTrialStatus.GuardrailRejected,
            StartedAtUtc = run.StartedAtUtc ?? run.CreatedAtUtc,
            CompletedAtUtc = run.CompletedAtUtc ?? DateTime.UtcNow,
            RawCandidateCount = metricsCandidates.Count,
            ClosedTradeCount = rawMetrics.ClosedTradeCount,
            WinnerCount = rawMetrics.WinnerCount,
            LoserCount = rawMetrics.LoserCount,
            ExpiredCount = rawMetrics.ExpiredCount,
            NetExpectancyR = rawMetrics.NetExpectancyR,
            GrossPnl = rawMetrics.GrossPnl,
            NetPnl = rawMetrics.NetPnl,
            ProfitFactor = rawMetrics.ProfitFactor,
            MaximumDrawdownPercent = rawMetrics.MaximumRealizedDrawdownPercent,
            FeeImpactPercent = feeImpact,
            TrainingScore = score.Total,
            GuardrailDecision = passed ? "Passed" : "Failed",
            GuardrailFailureReasonsJson = guardrailFailures.Count == 0
                ? null
                : JsonSerializer.Serialize(guardrailFailures, JsonOptions),
            StrategyLabRunId = run.Id,
            RecoverySource = ValidationTrialRecoverySource.ExistingStrategyLabRun
        };
    }

    private static List<string> EvaluateGuardrails(LayerSegmentMetrics rawMetrics, ValidationQualificationProfile profile)
    {
        var guardrailFailures = new List<string>();
        if (rawMetrics.ClosedTradeCount < profile.MinimumTrainingClosedTrades)
            guardrailFailures.Add($"ClosedTrades<{profile.MinimumTrainingClosedTrades}");
        if ((rawMetrics.ProfitFactor ?? 0m) < profile.MinimumTrainingProfitFactor)
            guardrailFailures.Add($"ProfitFactor<{profile.MinimumTrainingProfitFactor}");
        if ((rawMetrics.NetExpectancyR ?? 0m) < profile.MinimumTrainingNetExpectancyR)
            guardrailFailures.Add($"NetExpectancyR<{profile.MinimumTrainingNetExpectancyR}");
        if ((rawMetrics.MaximumRealizedDrawdownPercent ?? 0m) > profile.MaximumTrainingDrawdownPercent)
            guardrailFailures.Add($"MaxDD>{profile.MaximumTrainingDrawdownPercent}");
        return guardrailFailures;
    }

    private static bool ParametersMatch(string parametersJson, IReadOnlyDictionary<string, string> expected)
    {
        try
        {
            var actual = JsonSerializer.Deserialize<Dictionary<string, string>>(parametersJson, JsonOptions)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in expected)
            {
                if (!actual.TryGetValue(key, out var actualValue)
                    || !string.Equals(actualValue, value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return ValidationLabService.ParameterFingerprint(actual)
                == ValidationLabService.ParameterFingerprint(expected);
        }
        catch
        {
            return false;
        }
    }

    private static (StrategyLabPerformanceSummaryDto? summary, ShadowPortfolioSummaryDto? riskOnly, ShadowPortfolioSummaryDto? fullPipeline)
        ParseResultSummary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            StrategyLabPerformanceSummaryDto? summary = null;
            ShadowPortfolioSummaryDto? riskOnly = null;
            ShadowPortfolioSummaryDto? fullPipeline = null;
            if (root.TryGetProperty("summary", out var s) && s.ValueKind != JsonValueKind.Null)
            {
                summary = JsonSerializer.Deserialize<StrategyLabPerformanceSummaryDto>(s.GetRawText(), JsonOptions);
            }

            if (root.TryGetProperty("riskOnlyShadowPortfolio", out var ro) && ro.ValueKind != JsonValueKind.Null)
            {
                riskOnly = JsonSerializer.Deserialize<ShadowPortfolioSummaryDto>(ro.GetRawText(), JsonOptions);
            }

            if (root.TryGetProperty("fullPipelineShadowPortfolio", out var fp) && fp.ValueKind != JsonValueKind.Null)
            {
                fullPipeline = JsonSerializer.Deserialize<ShadowPortfolioSummaryDto>(fp.GetRawText(), JsonOptions);
            }

            return (summary, riskOnly, fullPipeline);
        }
        catch
        {
            return (null, null, null);
        }
    }
}
