using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Risk.Models;
using MomoQuant.Application.StrategyLab.Dtos;
using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Builds per-layer <see cref="ValidationSegmentResult"/> rows for a Strategy Lab run (v1.3 path
/// metrics with cost model, all four <see cref="ValidationLayerType"/> layers, holdout exclusivity,
/// and upsert persistence) and persists them. Extracted from <see cref="ValidationLabService"/> so the
/// segment-result build/persist behavior can be exercised and reused independently.
/// </summary>
public interface IValidationSegmentResultWriter
{
    Task BuildAndPersistSegmentResultsAsync(
        ValidationExperiment experiment,
        long labRunId,
        ValidationSegmentType segmentType,
        int candleCount,
        CancellationToken cancellationToken);
}

public sealed class ValidationSegmentResultWriter : IValidationSegmentResultWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly IStrategyLabRunRepository _labRuns;
    private readonly IStrategyResearchCandidateRepository _candidates;
    private readonly IValidationHoldoutExclusivityService _exclusivity;
    private readonly IValidationSegmentResultRepository _segments;
    private readonly IValidationPathMetricInputBuilder _pathMetricBuilder;
    private readonly IValidationRiskBasisService _riskBasis;

    public ValidationSegmentResultWriter(
        IStrategyLabRunRepository labRuns,
        IStrategyResearchCandidateRepository candidates,
        IValidationHoldoutExclusivityService exclusivity,
        IValidationSegmentResultRepository segments,
        IValidationPathMetricInputBuilder pathMetricBuilder,
        IValidationRiskBasisService riskBasis)
    {
        _labRuns = labRuns;
        _candidates = candidates;
        _exclusivity = exclusivity;
        _segments = segments;
        _pathMetricBuilder = pathMetricBuilder;
        _riskBasis = riskBasis;
    }

    public async Task BuildAndPersistSegmentResultsAsync(
        ValidationExperiment experiment,
        long labRunId,
        ValidationSegmentType segmentType,
        int candleCount,
        CancellationToken cancellationToken)
    {
        var run = await _labRuns.GetByIdAsync(labRunId, cancellationToken);
        var candidates = await _candidates.GetByRunIdAsync(labRunId, cancellationToken);
        var persistedCount = candidates.Count;
        var boundary = 0;
        IReadOnlyList<StrategyResearchCandidate> metricsCandidates = candidates;
        HoldoutExclusivityReport? exclusivityReport = null;
        var exclusivityApplied = false;
        var crossSegmentOverlapCount = 0;
        var metricExcludedCount = 0;

        if (experiment.ValidationStartUtc is not null)
        {
            boundary = ValidationMetricsMapper.CountBoundaryCensored(candidates, experiment.ValidationStartUtc.Value);
            if (segmentType == ValidationSegmentType.Training)
            {
                metricsCandidates = ValidationMetricsMapper.ExcludeBoundaryFromMetrics(
                    candidates, experiment.ValidationStartUtc.Value);
            }
        }

        if (segmentType == ValidationSegmentType.Validation
            && experiment.TrainingStrategyLabRunId is long trainRunId)
        {
            var trainCandidates = await _candidates.GetByRunIdAsync(trainRunId, cancellationToken);
            exclusivityReport = _exclusivity.Apply(
                trainCandidates,
                candidates,
                experiment.ValidationStartUtc);
            var partition = _exclusivity.ApplyExclusivityToValidationCandidates(candidates, exclusivityReport);
            metricsCandidates = partition.MetricIncluded;
            exclusivityApplied = true;
            crossSegmentOverlapCount = exclusivityReport.CrossSegmentOverlapCount;
            metricExcludedCount = partition.AuditOnly.Count;

            experiment.HoldoutExclusivityJson = ValidationHoldoutExclusivityService.Serialize(exclusivityReport);
            experiment.HoldoutExclusivityPolicyVersion = ValidationHoldoutExclusivityVersions.Current;
            experiment.CrossSegmentOverlapCount = crossSegmentOverlapCount;
        }
        else if (segmentType == ValidationSegmentType.Training)
        {
            metricExcludedCount = boundary;
        }

        var (summary, riskOnly, fullPipeline) = ValidationLabService.ParseResultSummary(run?.ResultSummaryJson);
        ShadowPortfolioSummaryDto? recomputedRiskOnly = null;
        ShadowPortfolioSummaryDto? recomputedFullPipeline = null;
        var shadowRecomputed = false;
        if (exclusivityApplied
            && metricsCandidates.Count > 0
            && TryRecomputeShadows(
                experiment,
                run,
                metricsCandidates,
                out recomputedRiskOnly,
                out recomputedFullPipeline))
        {
            shadowRecomputed = true;
            riskOnly = recomputedRiskOnly;
            fullPipeline = recomputedFullPipeline;
        }

        foreach (ValidationLayerType layer in Enum.GetValues(typeof(ValidationLayerType)))
        {
            LayerSegmentMetrics metrics;
            var usePathMetrics = ValidationMetricsContract.IsPathMetricsVersion(
                experiment.ValidationMetricsVersion);

            if (usePathMetrics)
            {
                var draft = ValidationLabService.ParseDraft(experiment.DraftConfigurationJson);
                var costModel = new ValidationPathMetricCostModel
                {
                    // Raw Strategy Lab outcomes use taker for both legs (RawOutcomeSimulator).
                    EntryFeeRate = draft.TakerFeeRate,
                    ExitFeeRate = draft.TakerFeeRate,
                    SlippagePercent = draft.SlippagePercent,
                    ContractMultiplier = 1m
                };
                var pathTrades = _pathMetricBuilder.Build(
                    experiment.Id,
                    segmentType,
                    layer,
                    metricsCandidates,
                    riskOnly,
                    fullPipeline,
                    costModel);
                metrics = ValidationMetricsMapper.FromPathTradesV13(
                    pathTrades,
                    candleCount,
                    metricsCandidates.Count,
                    boundary,
                    layer,
                    _riskBasis);
            }
            else if (layer is ValidationLayerType.RiskOnly or ValidationLayerType.FullPipeline)
            {
                if (exclusivityApplied)
                {
                    if (shadowRecomputed
                        && ((layer == ValidationLayerType.RiskOnly && riskOnly is not null)
                            || (layer == ValidationLayerType.FullPipeline && fullPipeline is not null)))
                    {
                        metrics = ValidationMetricsMapper.FromStrategyLabSummary(
                            summary,
                            candleCount,
                            metricsCandidates.Count,
                            boundary,
                            riskOnly,
                            fullPipeline,
                            layer);
                    }
                    else
                    {
                        metrics = ValidationMetricsMapper.FromCandidates(
                            metricsCandidates, candleCount, boundary, layer);
                    }
                }
                else
                {
                    metrics = ValidationMetricsMapper.FromStrategyLabSummary(
                        summary,
                        candleCount,
                        metricsCandidates.Count,
                        boundary,
                        riskOnly,
                        fullPipeline,
                        layer);
                }
            }
            else
            {
                var useExactCandidates =
                    exclusivityApplied
                    || string.Equals(
                        experiment.ValidationMetricsVersion,
                        ValidationMetricsContract.VersionV12,
                        StringComparison.OrdinalIgnoreCase);

                metrics = ValidationMetricsMapper.FromCandidates(
                    metricsCandidates, candleCount, boundary, layer);
                if (!useExactCandidates
                    && layer == ValidationLayerType.RawStrategy
                    && summary is not null)
                {
                    metrics = ValidationMetricsMapper.FromStrategyLabSummary(
                        summary, candleCount, metricsCandidates.Count, boundary, riskOnly, fullPipeline, layer);
                }
            }

            metrics.PersistedCandidateRowCount = persistedCount;
            metrics.MetricIncludedCandidateCount = metricsCandidates.Count;
            metrics.MetricExcludedCandidateCount = metricExcludedCount;
            metrics.CrossSegmentOverlapCount = crossSegmentOverlapCount;
            if (segmentType == ValidationSegmentType.Training)
            {
                // Training population after boundary censor = TrainingIncluded.
                metrics.MetricIncludedCandidateCount = metricsCandidates.Count;
            }

            var result = new ValidationSegmentResult
            {
                ValidationExperimentId = experiment.Id,
                SegmentType = segmentType,
                LayerType = layer,
                StrategyLabRunId = labRunId,
                MetricsJson = ValidationMetricsMapper.SerializeMetrics(metrics),
                CandleCount = metrics.CandleCount,
                CandidateCount = metrics.CandidateCount,
                ClosedTradeCount = metrics.ClosedTradeCount,
                NetExpectancyR = metrics.NetExpectancyR,
                ProfitFactor = metrics.NetProfitFactor ?? metrics.ProfitFactor,
                NetPnl = metrics.NetPnl,
                NetReturnPercent = metrics.NetReturnPercent,
                MaximumDrawdownPercent = metrics.MaximumRealizedDrawdownPercent,
                TransactionCosts = metrics.TransactionCosts,
                BoundaryCensoredCount = boundary,
                ResultFingerprint = ValidationLabService.ParameterFingerprint(new Dictionary<string, string>
                {
                    ["segment"] = segmentType.ToString(),
                    ["layer"] = layer.ToString(),
                    ["closed"] = metrics.ClosedTradeCount.ToString(),
                    ["net"] = (metrics.NetPnl ?? 0m).ToString("G29"),
                    ["included"] = metricsCandidates.Count.ToString()
                }),
                CreatedAtUtc = DateTime.UtcNow,
                ResultCalculationVersion = experiment.ValidationMetricsVersion
                    ?? ValidationMetricsContract.VersionV12,
                GrossExpectancyR = metrics.GrossExpectancyR,
                GrossProfitFactor = metrics.GrossProfitFactor,
                NetProfitFactor = metrics.NetProfitFactor ?? metrics.ProfitFactor,
                GrossAverageR = metrics.GrossAverageR ?? metrics.AverageR,
                NetAverageR = metrics.NetAverageR,
                GrossPnl = metrics.GrossPnl,
                PersistedCandidateRowCount = persistedCount,
                MetricIncludedCandidateCount = metricsCandidates.Count,
                MetricExcludedCandidateCount = metricExcludedCount,
                CrossSegmentOverlapCount = crossSegmentOverlapCount,
                GrossProfit = metrics.GrossProfit,
                GrossLoss = metrics.GrossLoss,
                NetProfit = metrics.NetProfit,
                NetLoss = metrics.NetLoss
            };

            await _segments.UpsertAsync(result, cancellationToken);
        }

        if (segmentType == ValidationSegmentType.Training)
        {
            experiment.BoundaryCensoredCount = boundary;
        }
    }

    private bool TryRecomputeShadows(
        ValidationExperiment experiment,
        StrategyLabRun? run,
        IReadOnlyList<StrategyResearchCandidate> metricIncluded,
        out ShadowPortfolioSummaryDto? riskOnly,
        out ShadowPortfolioSummaryDto? fullPipeline)
    {
        riskOnly = null;
        fullPipeline = null;
        try
        {
            var snapshotJson = run?.RiskProfileSnapshotJson ?? experiment.FrozenRiskSnapshotJson;
            if (string.IsNullOrWhiteSpace(snapshotJson))
            {
                return false;
            }

            var snapshot = JsonSerializer.Deserialize<RiskProfileSnapshotDto>(snapshotJson, JsonOptions);
            if (snapshot is null)
            {
                return false;
            }

            var draft = ValidationLabService.ParseDraft(experiment.DraftConfigurationJson);
            var maker = draft.MakerFeeRate;
            var taker = draft.TakerFeeRate;
            var slippageBps = draft.SlippagePercent > 0 ? draft.SlippagePercent * 100m : 0m;
            if (!string.IsNullOrWhiteSpace(experiment.FrozenCostModelSnapshotJson))
            {
                ValidationLabService.TryParseFees(experiment.FrozenCostModelSnapshotJson, out maker, out taker);
                ValidationLabService.TryParseSlippage(experiment.FrozenCostModelSnapshotJson, out var slipPct);
                if (slipPct > 0) slippageBps = slipPct * 100m;
            }

            var costSnapshot = StrategyLabCostSnapshot.CreateDefault(maker, taker, slippageBps);
            var rules = RuleSetFromSnapshot(snapshot);
            var clones = CloneCandidatesForShadow(metricIncluded);
            if (clones.Count == 0)
            {
                return false;
            }

            var shadow = ChronologicalShadowProcessor.Process(
                clones,
                snapshot,
                rules,
                new StrategyLabRiskObserver(),
                experiment.InitialBalance > 0 ? experiment.InitialBalance : (run?.InitialBalance ?? 10000m),
                costSnapshot);

            riskOnly = shadow.RiskOnlySummary;
            fullPipeline = shadow.FullPipelineSummary;
            return riskOnly is not null || fullPipeline is not null;
        }
        catch
        {
            riskOnly = null;
            fullPipeline = null;
            return false;
        }
    }

    private static RiskRuleSet RuleSetFromSnapshot(RiskProfileSnapshotDto snap) =>
        new()
        {
            MaxRiskPerTradePercent = snap.RiskPerTradePercent > 0 ? snap.RiskPerTradePercent : 0.5m,
            MaxDailyLossPercent = snap.MaxDailyLossPercent > 0 ? snap.MaxDailyLossPercent : 2m,
            MaxWeeklyLossPercent = snap.MaxDrawdownPercent > 0 ? snap.MaxDrawdownPercent : 5m,
            MaxOpenPositions = snap.MaxConcurrentPositions > 0 ? snap.MaxConcurrentPositions : 2,
            MaxExposurePerSymbolPercent = snap.LegacyMaxExposurePerSymbolPercent
                ?? snap.MaxNotionalExposurePerSymbolPercent
                ?? 25m,
            MaxTotalExposurePercent = snap.LegacyMaxTotalExposurePercent
                ?? snap.MaxTotalNotionalExposurePercent
                ?? 50m,
            MaxCorrelatedExposurePercent = 50m,
            MaxConsecutiveLosses = 3,
            MinConfidenceScore = snap.PolicyMinimumConfidence ?? 80m,
            MaxSpreadPercent = 0.05m,
            MaxAtrPercent = 2.5m,
            EmergencyStopEnabled = false,
            RequireStopLoss = true,
            MinRewardRiskRatio = snap.MinimumRewardRisk > 0 ? snap.MinimumRewardRisk : 1.2m
        };

    private static List<StrategyResearchCandidate> CloneCandidatesForShadow(
        IReadOnlyList<StrategyResearchCandidate> source)
    {
        try
        {
            var json = JsonSerializer.Serialize(source, JsonOptions);
            return JsonSerializer.Deserialize<List<StrategyResearchCandidate>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
