using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public sealed class SegmentMetricAuditResult
{
    public ValidationSegmentType SegmentType { get; init; }
    public ValidationLayerType LayerType { get; init; }
    public int MetricIncludedClosedTradeCount { get; init; }
    public decimal GrossRSum { get; init; }
    public decimal NetRSum { get; init; }
    public decimal? GrossExpectancyR { get; init; }
    public decimal? NetExpectancyR { get; init; }
    public decimal? CorrectedNetExpectancyR { get; init; }
    public decimal? PersistedGrossExpectancyR { get; init; }
    public decimal? PersistedNetExpectancyR { get; init; }
    public decimal? NetPnl { get; init; }
    public decimal? GrossProfitFactor { get; init; }
    public decimal? NetProfitFactor { get; init; }
    public decimal? AverageRiskAmountAtEntry { get; init; }
    public string UnitInterpretation { get; init; } = "Average net R per metric-included closed trade (R/trade).";
    public bool HasRiskAmountUnitWarning { get; init; }
    public bool MatchesPersisted { get; init; }
    public IReadOnlyList<string> SourceFields { get; init; } = [];
    public IReadOnlyList<string> Issues { get; init; } = [];
}

public sealed class ExperimentMetricAuditReport
{
    public long ExperimentId { get; init; }
    public string MetricsVersion { get; init; } = ValidationMetricsContract.Current;
    public IReadOnlyList<SegmentMetricAuditResult> Segments { get; init; } = [];
    public bool IsConsistent { get; init; }
    public string Summary { get; init; } = string.Empty;
}

public interface IValidationMetricAuditService
{
    SegmentMetricAuditResult AuditSegment(
        ValidationSegmentType segmentType,
        ValidationLayerType layerType,
        IReadOnlyList<StrategyResearchCandidate> metricIncludedCandidates,
        ValidationSegmentResult? persistedSegment);

    ExperimentMetricAuditReport AuditExperiment(
        ValidationExperiment experiment,
        IReadOnlyList<ValidationSegmentResult> segments,
        IReadOnlyDictionary<(ValidationSegmentType Segment, ValidationLayerType Layer), IReadOnlyList<StrategyResearchCandidate>> candidateSets);
}

public sealed class ValidationMetricAuditService : IValidationMetricAuditService
{
    private const decimal Tolerance = 0.0001m;

    public SegmentMetricAuditResult AuditSegment(
        ValidationSegmentType segmentType,
        ValidationLayerType layerType,
        IReadOnlyList<StrategyResearchCandidate> metricIncludedCandidates,
        ValidationSegmentResult? persistedSegment)
    {
        var metrics = ValidationMetricsContract.FromCandidates(
            metricIncludedCandidates,
            persistedSegment?.CandleCount ?? 0,
            persistedSegment?.BoundaryCensoredCount ?? 0,
            layerType);

        var tradeMetrics = metricIncludedCandidates
            .Where(c => c.CandidateStatus is StrategyResearchCandidateStatus.Closed
                or StrategyResearchCandidateStatus.Simulated
                || c.RawOutcomeStatus is RawOutcomeStatus.Winner or RawOutcomeStatus.Loser or RawOutcomeStatus.Expired)
            .Select(ValidationMetricsContract.ComputeTradeMetrics)
            .ToList();

        var grossRs = tradeMetrics.Where(t => t.GrossR.HasValue).Select(t => t.GrossR!.Value).ToList();
        var netRs = tradeMetrics.Where(t => t.NetR.HasValue).Select(t => t.NetR!.Value).ToList();
        var risks = tradeMetrics.Where(t => t.RiskAmount is > 0m).Select(t => t.RiskAmount!.Value).ToList();

        var grossSum = grossRs.Sum();
        var netSum = netRs.Sum();
        var grossExp = grossRs.Count > 0 ? grossSum / grossRs.Count : (decimal?)null;
        var netExp = netRs.Count > 0 ? netSum / netRs.Count : (decimal?)null;

        var correctedNetRs = metricIncludedCandidates
            .Where(c => c.RawOutcomeStatus is RawOutcomeStatus.Winner or RawOutcomeStatus.Loser or RawOutcomeStatus.Expired)
            .Select(c =>
            {
                var grossPnl = c.RawGrossPnl ?? c.RawNetPnl ?? 0m;
                var netPnl = c.RawNetPnl ?? c.RawGrossPnl ?? 0m;
                if (c.RawRMultiple is not decimal grossR || grossR == 0m || grossPnl == 0m)
                {
                    return (decimal?)null;
                }

                var derivedRisk = Math.Abs(grossPnl / grossR);
                return derivedRisk > 0m ? netPnl / derivedRisk : (decimal?)null;
            })
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();
        var correctedNetExp = correctedNetRs.Count > 0 ? correctedNetRs.Average() : (decimal?)null;

        var issues = new List<string>();
        var persistenceIssues = new List<string>();
        if (metrics.NetExpectancyR.HasValue && netExp.HasValue
            && Math.Abs(metrics.NetExpectancyR.Value - netExp.Value) > Tolerance)
        {
            persistenceIssues.Add($"Recomputed NetExpectancyR {netExp:F8} != contract value {metrics.NetExpectancyR:F8}.");
        }

        if (persistedSegment?.NetExpectancyR is decimal persistedNet
            && netExp.HasValue
            && Math.Abs(persistedNet - netExp.Value) > Tolerance)
        {
            if (persistedSegment.ClosedTradeCount > 0
                && persistedSegment.NetPnl is decimal netPnl
                && Math.Abs(persistedNet - netPnl / persistedSegment.ClosedTradeCount) < Tolerance)
            {
                persistenceIssues.Add(
                    "Persisted NetExpectancyR matches NetPnl/ClosedTradeCount (monetary per trade), not average R/trade.");
            }
            else
            {
                persistenceIssues.Add($"Persisted NetExpectancyR {persistedNet:F8} != recomputed {netExp:F8}.");
            }
        }

        issues.AddRange(persistenceIssues);

        var avgRisk = risks.Count > 0 ? risks.Average() : (decimal?)null;
        var hasRiskUnitWarning = grossExp.HasValue && netExp.HasValue
            && Math.Abs(grossExp.Value) > 0.0001m
            && Math.Abs(netExp.Value) > Math.Abs(grossExp.Value) * 10m;
        if (hasRiskUnitWarning)
        {
            issues.Add(
                $"NetExpectancyR ({netExp:F4}) exceeds GrossExpectancyR ({grossExp:F4}) by >10×. " +
                $"Average RiskAmountAtEntry ({avgRisk:F4}) appears mis-scaled relative to RawRMultiple; " +
                $"use CorrectedNetExpectancyR ({correctedNetExp:F4} R/trade) for gross-R-aligned interpretation.");
        }

        return new SegmentMetricAuditResult
        {
            SegmentType = segmentType,
            LayerType = layerType,
            MetricIncludedClosedTradeCount = metrics.ClosedTradeCount,
            GrossRSum = grossSum,
            NetRSum = netSum,
            GrossExpectancyR = grossExp,
            NetExpectancyR = netExp ?? metrics.NetExpectancyR,
            CorrectedNetExpectancyR = correctedNetExp,
            PersistedGrossExpectancyR = persistedSegment?.GrossExpectancyR,
            PersistedNetExpectancyR = persistedSegment?.NetExpectancyR,
            NetPnl = metrics.NetPnl ?? persistedSegment?.NetPnl,
            GrossProfitFactor = metrics.GrossProfitFactor,
            NetProfitFactor = metrics.NetProfitFactor,
            AverageRiskAmountAtEntry = avgRisk,
            HasRiskAmountUnitWarning = hasRiskUnitWarning,
            MatchesPersisted = persistenceIssues.Count == 0,
            SourceFields =
            [
                "StrategyResearchCandidate.RawRMultiple",
                "StrategyResearchCandidate.RawNetPnl",
                "StrategyResearchCandidate.RiskAmount",
                "ValidationSegmentResult.NetExpectancyR"
            ],
            Issues = issues
        };
    }

    public ExperimentMetricAuditReport AuditExperiment(
        ValidationExperiment experiment,
        IReadOnlyList<ValidationSegmentResult> segments,
        IReadOnlyDictionary<(ValidationSegmentType Segment, ValidationLayerType Layer), IReadOnlyList<StrategyResearchCandidate>> candidateSets)
    {
        var results = segments
            .Where(s => s.LayerType == ValidationLayerType.RawStrategy)
            .Select(segment =>
            {
                candidateSets.TryGetValue((segment.SegmentType, segment.LayerType), out var candidates);
                return AuditSegment(segment.SegmentType, segment.LayerType, candidates ?? [], segment);
            })
            .ToList();

        var consistent = results.All(r => r.MatchesPersisted);
        return new ExperimentMetricAuditReport
        {
            ExperimentId = experiment.Id,
            MetricsVersion = experiment.ValidationMetricsVersion,
            Segments = results,
            IsConsistent = consistent,
            Summary = consistent
                ? "Trade-level NetR averages match persisted RawStrategy segment metrics."
                : "One or more segments have metric unit or persistence mismatches."
        };
    }
}

public static class ValidationMetricDisplayFormatter
{
    public const string ExpectancyUnit = "R/trade";

    public static string FormatExpectancyR(decimal? value, int displayDigits = 4) =>
        value.HasValue ? $"{Math.Round(value.Value, displayDigits)} {ExpectancyUnit}" : "—";

    public static decimal? ToPercentOfRisk(decimal? expectancyR) =>
        expectancyR.HasValue ? Math.Round(expectancyR.Value * 100m, 2) : null;
}
