using System.Text.Json;
using MomoQuant.Application.StrategyLab.Dtos;
using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Thin facade: delegates to ValidationMetricsContract (ValidationMetrics/v1.1).
/// </summary>
public static class ValidationMetricsMapper
{
    public static LayerSegmentMetrics FromStrategyLabSummary(
        StrategyLabPerformanceSummaryDto? summary,
        int candleCount,
        int candidateCount,
        int boundaryCensored,
        ShadowPortfolioSummaryDto? riskOnly = null,
        ShadowPortfolioSummaryDto? fullPipeline = null,
        ValidationLayerType layer = ValidationLayerType.RawStrategy) =>
        ValidationMetricsContract.FromStrategyLabSummary(
            summary, candleCount, candidateCount, boundaryCensored, riskOnly, fullPipeline, layer);

    public static LayerSegmentMetrics FromShadow(
        ShadowPortfolioSummaryDto shadow,
        int candleCount,
        int candidateCount,
        int boundaryCensored) =>
        ValidationMetricsContract.FromShadow(shadow, candleCount, candidateCount, boundaryCensored);

    public static int CountBoundaryCensored(
        IReadOnlyList<StrategyResearchCandidate> candidates,
        DateTime validationStartUtc)
    {
        var start = DateTime.SpecifyKind(validationStartUtc, DateTimeKind.Utc);
        return candidates.Count(c =>
            c.SetupDetectedAtUtc < start
            && c.RawExitTimeUtc.HasValue
            && DateTime.SpecifyKind(c.RawExitTimeUtc.Value, DateTimeKind.Utc) >= start);
    }

    public static IReadOnlyList<StrategyResearchCandidate> ExcludeBoundaryFromMetrics(
        IReadOnlyList<StrategyResearchCandidate> candidates,
        DateTime validationStartUtc)
    {
        var start = DateTime.SpecifyKind(validationStartUtc, DateTimeKind.Utc);
        return candidates.Where(c =>
            !(c.SetupDetectedAtUtc < start
              && c.RawExitTimeUtc.HasValue
              && DateTime.SpecifyKind(c.RawExitTimeUtc.Value, DateTimeKind.Utc) >= start)).ToList();
    }

    public static LayerSegmentMetrics FromCandidates(
        IReadOnlyList<StrategyResearchCandidate> candidates,
        int candleCount,
        int boundaryCensored,
        ValidationLayerType layer,
        ExpiredTradeMetricPolicy expiredPolicy = ExpiredTradeMetricPolicy.IncludeAsClosedAtExpiry) =>
        ValidationMetricsContract.FromCandidates(
            candidates, candleCount, boundaryCensored, layer, expiredPolicy);

    public static LayerSegmentMetrics FromCandidatesV13(
        IReadOnlyList<StrategyResearchCandidate> candidates,
        int candleCount,
        int boundaryCensored,
        ValidationLayerType layer,
        IValidationRiskBasisService riskBasis,
        ExpiredTradeMetricPolicy expiredPolicy = ExpiredTradeMetricPolicy.IncludeAsClosedAtExpiry) =>
        ValidationMetricsContract.FromCandidatesV13(
            candidates, candleCount, boundaryCensored, layer, riskBasis, expiredPolicy);

    public static LayerSegmentMetrics FromPathTradesV13(
        IReadOnlyList<ValidationPathTradeMetricInput> trades,
        int candleCount,
        int candidateCount,
        int boundaryCensored,
        ValidationLayerType layer,
        IValidationRiskBasisService riskBasis) =>
        ValidationMetricsContract.FromPathTradesV13(
            trades, candleCount, candidateCount, boundaryCensored, layer, riskBasis);

    public static LayerSegmentMetrics FromPathTradesV132(
        IReadOnlyList<ValidationPathTradeMetricInput> trades,
        int candleCount,
        int candidatePopulationCount,
        int boundaryEligibleCandidateCount,
        int boundaryCensored,
        ValidationLayerType layer,
        IValidationRiskBasisService riskBasis,
        IValidationRiskBasisStatusReducer? statusReducer = null) =>
        ValidationMetricsContract.FromPathTradesV132(
            trades,
            candleCount,
            candidatePopulationCount,
            boundaryEligibleCandidateCount,
            boundaryCensored,
            layer,
            riskBasis,
            statusReducer);

    public static string SerializeMetrics(LayerSegmentMetrics m) =>
        ValidationMetricsContract.SerializeMetrics(m);
}
