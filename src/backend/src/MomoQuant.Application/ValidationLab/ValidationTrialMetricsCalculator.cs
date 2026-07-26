using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Full persisted trial metric snapshot for ValidationMetrics/v1.3.2 experiments
/// (Milestone 23.0D WP17). Serialized to ValidationParameterTrial.TrialMetricSnapshotJson;
/// its SHA-256 becomes the trial metric fingerprint.
/// </summary>
public sealed class ValidationTrialMetricSnapshot
{
    public const string Version = "ValidationTrialMetricSnapshot/v1";

    public string SnapshotVersion { get; init; } = Version;
    public long ValidationExperimentId { get; init; }
    public long? StrategyLabRunId { get; init; }
    public string ParameterFingerprint { get; init; } = string.Empty;
    public string MetricsVersion { get; init; } = ValidationMetricsContract.VersionV132;
    public string ScoreVersion { get; init; } = ValidationTrainingScoreVersions.V2;
    public int BoundaryCensoredCount { get; init; }
    public int BoundaryEligibleCandidateCount { get; init; }
    public ValidationPathMetricCostModel CostModel { get; init; } = new();
    public LayerSegmentMetrics RawStrategyTrainingMetrics { get; init; } = new();
    public ValidationTrainingScoreBreakdown Score { get; init; } = new();
    public ValidationGuardrailEvaluation Guardrails { get; init; } = new();
}

public sealed class ValidationTrialMetricsResult
{
    public LayerSegmentMetrics Metrics { get; init; } = new();
    public ValidationTrainingScoreBreakdown Score { get; init; } = new();
    public ValidationGuardrailEvaluation Guardrails { get; init; } = new();
    public ValidationTrialMetricSnapshot Snapshot { get; init; } = new();
    public string SnapshotJson { get; init; } = "{}";

    /// <summary>SHA-256 (lowercase hex) over <see cref="SnapshotJson"/>.</summary>
    public string MetricFingerprint { get; init; } = string.Empty;

    public int BoundaryCensoredCount { get; init; }
    public int BoundaryEligibleCandidateCount { get; init; }
    public decimal? FeeImpactPercent { get; init; }
}

/// <summary>
/// Computes trial training metrics for ValidationMetrics/v1.3.2 experiments (Milestone 23.0D WP16):
/// boundary eligibility → RawStrategy path inputs (normalized one-unit, frozen fees) →
/// FromPathTradesV132 population contract → v2 score + guardrails → persisted snapshot.
/// Must never fall back to StrategyLab summaries.
/// </summary>
public interface IValidationTrialMetricsCalculator
{
    ValidationTrialMetricsResult Calculate(
        ValidationExperiment experiment,
        StrategyLabRun run,
        IReadOnlyList<StrategyResearchCandidate> candidates,
        ValidationQualificationProfile profile,
        string? parameterFingerprint = null);
}

public sealed class ValidationTrialMetricsCalculator : IValidationTrialMetricsCalculator
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly IValidationPathMetricInputBuilder _pathMetricBuilder;
    private readonly IValidationRiskBasisService _riskBasis;
    private readonly IValidationRiskBasisStatusReducer _statusReducer;

    public ValidationTrialMetricsCalculator(
        IValidationPathMetricInputBuilder pathMetricBuilder,
        IValidationRiskBasisService riskBasis,
        IValidationRiskBasisStatusReducer statusReducer)
    {
        _pathMetricBuilder = pathMetricBuilder;
        _riskBasis = riskBasis;
        _statusReducer = statusReducer;
    }

    public ValidationTrialMetricsResult Calculate(
        ValidationExperiment experiment,
        StrategyLabRun run,
        IReadOnlyList<StrategyResearchCandidate> candidates,
        ValidationQualificationProfile profile,
        string? parameterFingerprint = null)
    {
        // 1. Boundary eligibility — identical semantics to the training segment writer.
        var boundary = 0;
        IReadOnlyList<StrategyResearchCandidate> metricsCandidates = candidates;
        if (experiment.ValidationStartUtc is DateTime validationStart)
        {
            boundary = ValidationMetricsMapper.CountBoundaryCensored(candidates, validationStart);
            metricsCandidates = ValidationMetricsMapper.ExcludeBoundaryFromMetrics(candidates, validationStart);
        }

        // 2. RawStrategy path inputs — normalized one-unit economics with frozen fees
        //    (taker on both legs, matching RawOutcomeSimulator and the segment writer).
        var draft = ValidationLabService.ParseDraft(experiment.DraftConfigurationJson);
        var costModel = new ValidationPathMetricCostModel
        {
            EntryFeeRate = draft.TakerFeeRate,
            ExitFeeRate = draft.TakerFeeRate,
            SlippagePercent = draft.SlippagePercent,
            ContractMultiplier = 1m
        };
        var pathTrades = _pathMetricBuilder.Build(
            experiment.Id,
            ValidationSegmentType.Training,
            ValidationLayerType.RawStrategy,
            metricsCandidates,
            riskOnlyShadow: null,
            fullPipelineShadow: null,
            costModel);

        // 3. Population contract aggregation.
        var metrics = ValidationMetricsContract.FromPathTradesV132(
            pathTrades,
            experiment.TrainingCandleCount,
            candidatePopulationCount: candidates.Count,
            boundaryEligibleCandidateCount: metricsCandidates.Count,
            boundary,
            ValidationLayerType.RawStrategy,
            _riskBasis,
            _statusReducer);

        // 4. Score + guardrails from the snapshot metrics only.
        var score = ValidationTrainingScoreCalculator.CalculateV2(metrics, profile.MinimumTrainingClosedTrades);
        var guardrails = ValidationGuardrailEvaluator.Evaluate(metrics, profile);

        decimal? feeImpact = null;
        if (metrics.TransactionCosts is decimal costs
            && metrics.GrossProfit is decimal grossProfit
            && grossProfit > 0m)
        {
            feeImpact = Math.Round(costs / grossProfit * 100m, 4);
        }

        var snapshot = new ValidationTrialMetricSnapshot
        {
            ValidationExperimentId = experiment.Id,
            StrategyLabRunId = run.Id,
            ParameterFingerprint = parameterFingerprint ?? string.Empty,
            BoundaryCensoredCount = boundary,
            BoundaryEligibleCandidateCount = metricsCandidates.Count,
            CostModel = costModel,
            RawStrategyTrainingMetrics = metrics,
            Score = score,
            Guardrails = guardrails
        };

        var snapshotJson = JsonSerializer.Serialize(snapshot, SnapshotJsonOptions);
        return new ValidationTrialMetricsResult
        {
            Metrics = metrics,
            Score = score,
            Guardrails = guardrails,
            Snapshot = snapshot,
            SnapshotJson = snapshotJson,
            MetricFingerprint = ComputeSha256(snapshotJson),
            BoundaryCensoredCount = boundary,
            BoundaryEligibleCandidateCount = metricsCandidates.Count,
            FeeImpactPercent = feeImpact
        };
    }

    /// <summary>
    /// Writes the calculator result onto the trial. Ranking/selection later reads only these
    /// persisted fields — no recalculation from StrategyLab summaries (WP20).
    /// </summary>
    public static void ApplyToTrial(ValidationParameterTrial trial, ValidationTrialMetricsResult result)
    {
        var metrics = result.Metrics;
        var passed = result.Guardrails.Passed;

        // Milestone 23.0E2C1 — when a durable audit execution is authoritative, do not set
        // Completed here; training orchestration finalizes audit first, then the completion gate.
        if (trial.AuthoritativeAuditExecutionId is not null)
        {
            trial.Status = passed ? ValidationTrialStatus.Running : ValidationTrialStatus.GuardrailRejected;
        }
        else
        {
            trial.Status = passed ? ValidationTrialStatus.Completed : ValidationTrialStatus.GuardrailRejected;
        }

        trial.CompletedAtUtc = DateTime.UtcNow;
        trial.RawCandidateCount = result.BoundaryEligibleCandidateCount;
        trial.ClosedTradeCount = metrics.ClosedOutcomePopulationCount ?? metrics.ClosedTradeCount;
        trial.WinnerCount = metrics.WinnerPopulationCount ?? metrics.WinnerCount;
        trial.LoserCount = metrics.LoserPopulationCount ?? metrics.LoserCount;
        trial.ExpiredCount = metrics.ExpiredCount;
        trial.NetExpectancyR = metrics.NetExpectancyR;
        trial.GrossPnl = metrics.GrossPnl;
        trial.NetPnl = metrics.NetPnl;
        trial.ProfitFactor = metrics.NetProfitFactor ?? metrics.ProfitFactor;
        trial.MaximumDrawdownPercent = metrics.MaximumRealizedDrawdownPercent;
        trial.FeeImpactPercent = result.FeeImpactPercent;
        trial.TrainingScore = result.Score.Total;
        trial.GuardrailDecision = result.Guardrails.Decision;
        trial.GuardrailFailureReasonsJson = result.Guardrails.FailureCodes.Count == 0
            ? null
            : JsonSerializer.Serialize(result.Guardrails.FailureCodes, SnapshotJsonOptions);
        trial.ErrorMessage = null;

        trial.TrialMetricSnapshotJson = result.SnapshotJson;
        trial.TrialMetricFingerprint = result.MetricFingerprint;
        trial.TrialMetricsVersion = metrics.MetricsVersion;
        trial.TrainingScoreVersion = result.Score.Version;
        trial.GuardrailEvaluationJson = JsonSerializer.Serialize(result.Guardrails, SnapshotJsonOptions);
        trial.CandidatePopulationCount = metrics.CandidatePopulationCount;
        trial.BoundaryEligibleCandidateCount = metrics.BoundaryEligibleCandidateCount;
        trial.IncludedPathInputCount = metrics.IncludedPathInputCount;
        trial.ExcludedPathInputCount = metrics.ExcludedPathInputCount;
        trial.ClosedOutcomePopulationCount = metrics.ClosedOutcomePopulationCount;
        trial.MonetaryPnlPopulationCount = metrics.MonetaryPnlPopulationCount;
        trial.GrossRPopulationCount = metrics.GrossRPopulationCount;
        trial.NetRPopulationCount = metrics.NetRPopulationCount;
        trial.IncludedPopulationRiskStatus = metrics.IncludedPopulationRiskStatus;
        trial.CompletePathInputIntegrityStatus = metrics.CompletePathInputIntegrityStatus;
        trial.TrialRankEligibility = result.Guardrails.IsRankEligible
            ? ValidationTrialRankEligibility.Eligible
            : ValidationTrialRankEligibility.Ineligible;
        trial.RankIneligibleReasonsJson = result.Guardrails.IsRankEligible
            ? null
            : JsonSerializer.Serialize(result.Guardrails.FailureCodes, SnapshotJsonOptions);
    }

    public static string ComputeSha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
