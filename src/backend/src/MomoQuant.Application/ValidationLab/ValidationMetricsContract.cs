using System.Text.Json;
using MomoQuant.Application.StrategyLab.Dtos;
using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Canonical ValidationMetrics/v1.2 contract: separates gross R vs net expectancy and gross vs net PF.
/// Population counts and exact (unrounded) monetary values support holdout exclusivity qualification.
/// </summary>
public static class ValidationMetricsContract
{
    public const string VersionV12 = "ValidationMetrics/v1.2";
    public const string VersionV13 = "ValidationMetrics/v1.3";
    public const string VersionV131 = "ValidationMetrics/v1.3.1";
    public const string VersionV11 = "ValidationMetrics/v1.1";
    public const string VersionV1Legacy = "ValidationMetrics/v1";
    public const string Current = VersionV131;

    /// <summary>True for ValidationMetrics/v1.3 and v1.3.1 path-metric contracts.</summary>
    public static bool IsPathMetricsVersion(string? version) =>
        string.Equals(version, VersionV13, StringComparison.OrdinalIgnoreCase)
        || string.Equals(version, VersionV131, StringComparison.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static LayerSegmentMetrics FromCandidates(
        IReadOnlyList<StrategyResearchCandidate> candidates,
        int candleCount,
        int boundaryCensored,
        ValidationLayerType layer,
        ExpiredTradeMetricPolicy expiredPolicy = ExpiredTradeMetricPolicy.IncludeAsClosedAtExpiry)
    {
        IEnumerable<StrategyResearchCandidate> filtered = layer switch
        {
            ValidationLayerType.ConfidenceQualified =>
                candidates.Where(c => c.ConfidenceDecision == ResearchConfidenceDecision.Approved),
            ValidationLayerType.RiskOnly =>
                candidates.Where(c => c.RiskOnlyEntryDecision == ShadowEntryDecision.Opened
                    || (c.RiskOnlyEntryDecision is null && c.RiskDecision == ResearchRiskDecision.Approved)),
            ValidationLayerType.FullPipeline =>
                candidates.Where(c => c.FinalPipelineDecision == ResearchFinalPipelineDecision.Approved),
            _ => candidates
        };

        var list = filtered.ToList();
        var closed = SelectClosedTrades(list, expiredPolicy);
        var tradeMetrics = closed.Select(ComputeTradeMetrics).ToList();

        var winners = closed.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Winner);
        var losers = closed.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Loser);
        var expired = closed.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Expired);
        var expiredIncluded = expired;
        var expiredExcluded = list.Count(c =>
            c.RawOutcomeStatus == RawOutcomeStatus.Expired && !closed.Contains(c));

        var grossRs = tradeMetrics.Where(t => t.GrossR.HasValue).Select(t => t.GrossR!.Value).ToList();
        var netRs = tradeMetrics.Where(t => t.NetR.HasValue).Select(t => t.NetR!.Value).ToList();

        var grossAvg = AverageOrNull(grossRs);
        var netAvg = AverageOrNull(netRs);
        var grossMedian = MedianOrNull(grossRs);
        var netMedian = MedianOrNull(netRs);

        var grossPnl = tradeMetrics.Sum(t => t.GrossPnl);
        var netPnl = tradeMetrics.Sum(t => t.NetPnl);
        var costs = grossPnl - netPnl;
        if (costs < 0m) costs = 0m;

        var grossProfit = tradeMetrics.Where(t => t.GrossPnl > 0m).Sum(t => t.GrossPnl);
        var grossLoss = tradeMetrics.Where(t => t.GrossPnl < 0m).Sum(t => Math.Abs(t.GrossPnl));
        var netProfit = tradeMetrics.Where(t => t.NetPnl > 0m).Sum(t => t.NetPnl);
        var netLoss = tradeMetrics.Where(t => t.NetPnl < 0m).Sum(t => Math.Abs(t.NetPnl));

        var grossPf = ComputeProfitFactor(grossProfit, grossLoss);
        var netPf = ComputeProfitFactor(netProfit, netLoss);
        var breakeven = closed.Count(c =>
            c.RawOutcomeStatus != RawOutcomeStatus.Winner
            && c.RawOutcomeStatus != RawOutcomeStatus.Loser
            && c.RawOutcomeStatus != RawOutcomeStatus.Expired
            && (c.RawNetPnl ?? c.RawGrossPnl ?? 0m) == 0m);

        return new LayerSegmentMetrics
        {
            CandleCount = candleCount,
            CandidateCount = list.Count,
            OpportunityRatePer1000Candles = candleCount > 0
                ? Math.Round(list.Count * 1000m / candleCount, 4)
                : 0m,
            ClosedTradeCount = closed.Count,
            WinnerCount = winners,
            LoserCount = losers,
            ExpiredCount = expired,
            ExpiredIncludedInClosedCount = expiredIncluded,
            ExpiredExcludedCount = expiredExcluded,
            BreakevenCount = breakeven,
            WinRate = closed.Count > 0
                ? Math.Round((decimal)winners / closed.Count * 100m, 4)
                : null,
            AverageR = grossAvg,
            MedianR = grossMedian,
            GrossAverageR = grossAvg,
            GrossMedianR = grossMedian,
            NetAverageR = netAvg,
            NetMedianR = netMedian,
            GrossExpectancyR = grossAvg,
            NetExpectancyR = netAvg,
            // Exact (unrounded) monetary values for qualification under v1.2
            GrossPnl = grossPnl,
            TransactionCosts = costs,
            NetPnl = netPnl,
            GrossProfit = grossProfit,
            GrossLoss = grossLoss,
            NetProfit = netProfit,
            NetLoss = netLoss,
            GrossProfitFactor = grossPf.NumericValue,
            NetProfitFactor = netPf.NumericValue,
            ProfitFactor = netPf.NumericValue,
            ProfitFactorStatus = netPf.Status,
            GrossProfitFactorStatus = grossPf.Status,
            NetProfitFactorStatus = netPf.Status,
            BoundaryCensoredCount = boundaryCensored,
            MetricIncludedCandidateCount = list.Count,
            IncludedOutcomeTypes = "Winner,Loser,Expired(policy)",
            PnlSizingMode = "RawCandidate",
            ExpectancyCalculationMode = "GrossR=RawRMultiple;NetR=RawNetPnl/RiskOrDerived",
            ProfitFactorCalculationMode = "SeparateGrossNet",
            CostModelVersion = "StrategyLabRawNet",
            MetricsVersion = VersionV12
        };
    }

    /// <summary>
    /// ValidationMetrics/v1.3 — dimensionally consistent R using ValidationRiskBasis/v1.
    /// </summary>
    public static LayerSegmentMetrics FromCandidatesV13(
        IReadOnlyList<StrategyResearchCandidate> candidates,
        int candleCount,
        int boundaryCensored,
        ValidationLayerType layer,
        IValidationRiskBasisService riskBasis,
        ExpiredTradeMetricPolicy expiredPolicy = ExpiredTradeMetricPolicy.IncludeAsClosedAtExpiry)
    {
        IEnumerable<StrategyResearchCandidate> filtered = layer switch
        {
            ValidationLayerType.ConfidenceQualified =>
                candidates.Where(c => c.ConfidenceDecision == ResearchConfidenceDecision.Approved),
            ValidationLayerType.RiskOnly =>
                candidates.Where(c => c.RiskOnlyEntryDecision == ShadowEntryDecision.Opened
                    || (c.RiskOnlyEntryDecision is null && c.RiskDecision == ResearchRiskDecision.Approved)),
            ValidationLayerType.FullPipeline =>
                candidates.Where(c => c.FinalPipelineDecision == ResearchFinalPipelineDecision.Approved),
            _ => candidates
        };

        var list = filtered.ToList();
        var closed = SelectClosedTrades(list, expiredPolicy);
        var audit = riskBasis.AuditSegment(list, layer, expiredPolicy);

        var grossRs = new List<decimal>();
        var netRs = new List<decimal>();
        var grossPnls = new List<decimal>();
        var netPnls = new List<decimal>();

        foreach (var c in closed)
        {
            var basis = riskBasis.ComputeTradeBasis(c, layer);
            if (basis.GrossRMultiple is decimal gR) grossRs.Add(gR);
            if (basis.NetRMultiple is decimal nR) netRs.Add(nR);
            if (basis.NormalizedGrossPnl is decimal gp) grossPnls.Add(gp);
            if (basis.NormalizedNetPnl is decimal np) netPnls.Add(np);
        }

        var grossAvg = AverageOrNull(grossRs);
        var netAvg = audit.NetExpectancyApplicability == ValidationMetricApplicability.Evaluated
            ? AverageOrNull(netRs)
            : null;
        var grossPnl = grossPnls.Sum();
        var netPnl = netPnls.Sum();
        var costs = grossPnl - netPnl;
        if (costs < 0m) costs = 0m;

        var grossProfit = grossPnls.Where(x => x > 0m).Sum();
        var grossLoss = grossPnls.Where(x => x < 0m).Sum(x => Math.Abs(x));
        var netProfit = netPnls.Where(x => x > 0m).Sum();
        var netLoss = netPnls.Where(x => x < 0m).Sum(x => Math.Abs(x));
        var grossPf = ComputeProfitFactor(grossProfit, grossLoss);
        var netPf = ComputeProfitFactor(netProfit, netLoss);

        var winners = closed.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Winner);
        var losers = closed.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Loser);

        return new LayerSegmentMetrics
        {
            CandleCount = candleCount,
            CandidateCount = list.Count,
            OpportunityRatePer1000Candles = candleCount > 0
                ? Math.Round(list.Count * 1000m / candleCount, 4)
                : 0m,
            ClosedTradeCount = closed.Count,
            WinnerCount = winners,
            LoserCount = losers,
            WinRate = closed.Count > 0
                ? Math.Round((decimal)winners / closed.Count * 100m, 4)
                : null,
            GrossExpectancyR = grossAvg,
            NetExpectancyR = netAvg,
            GrossAverageR = grossAvg,
            NetAverageR = netAvg,
            AverageR = grossAvg,
            GrossPnl = grossPnl,
            NetPnl = netPnl,
            TransactionCosts = costs,
            GrossProfit = grossProfit,
            GrossLoss = grossLoss,
            NetProfit = netProfit,
            NetLoss = netLoss,
            GrossProfitFactor = grossPf.NumericValue,
            NetProfitFactor = netPf.NumericValue,
            ProfitFactor = netPf.NumericValue,
            GrossProfitFactorStatus = grossPf.Status,
            NetProfitFactorStatus = netPf.Status,
            ProfitFactorStatus = netPf.Status,
            BoundaryCensoredCount = boundaryCensored,
            MetricIncludedCandidateCount = list.Count,
            ExpectancyCalculationMode = "ValidationRiskBasis/v1: GrossR=GrossPnl/DerivedRisk; NetR=NetPnl/DerivedRisk",
            ProfitFactorCalculationMode = "SeparateGrossNet",
            CostModelVersion = "StrategyLabRawNet",
            MetricsVersion = VersionV13,
            RiskBasisVersion = ValidationRiskBasisService.Version,
            RiskBasisType = layer == ValidationLayerType.RawStrategy
                ? ValidationRiskBasisType.NormalizedOneUnit
                : ValidationRiskBasisType.PositionSized,
            NetExpectancyApplicability = audit.NetExpectancyApplicability,
            NetExpectancyIncludedTradeCount = audit.IncludedTradeCount,
            NetExpectancyExcludedTradeCount = audit.ExcludedTradeCount,
            NetExpectancyExclusionReasons = audit.ExclusionReasons.Concat(audit.Diagnostics).Distinct().ToList()
        };
    }

    /// <summary>
    /// ValidationMetrics/v1.3 from immutable path-specific trade inputs (all four layers).
    /// </summary>
    public static LayerSegmentMetrics FromPathTradesV13(
        IReadOnlyList<ValidationPathTradeMetricInput> trades,
        int candleCount,
        int candidateCount,
        int boundaryCensored,
        ValidationLayerType layer,
        IValidationRiskBasisService riskBasis)
    {
        var audit = riskBasis.AuditPathTrades(trades);
        var grossRs = new List<decimal>();
        var netRs = new List<decimal>();
        var grossPnls = new List<decimal>();
        var netPnls = new List<decimal>();
        var costs = 0m;
        ValidationRiskBasisType? basisType = null;
        ValidationRiskBasisValidationStatus? aggregateStatus = null;

        foreach (var trade in trades)
        {
            var basis = riskBasis.ComputePathTradeBasis(trade);
            basisType ??= basis.RiskBasisType;
            aggregateStatus = basis.Status;
            if (basis.GrossRMultiple is decimal gR)
            {
                grossRs.Add(gR);
            }

            if (basis.NetRMultiple is decimal nR)
            {
                netRs.Add(nR);
            }

            if (basis.Status is ValidationRiskBasisValidationStatus.Valid
                or ValidationRiskBasisValidationStatus.PersistedRiskMismatch)
            {
                grossPnls.Add(basis.GrossPnl);
                netPnls.Add(basis.NetPnl);
                costs += basis.TotalTransactionCosts;
            }
        }

        var grossAvg = AverageOrNull(grossRs);
        var netAvg = audit.NetExpectancyApplicability == ValidationMetricApplicability.Evaluated
            ? AverageOrNull(netRs)
            : null;

        var grossProfit = grossPnls.Where(x => x > 0m).Sum();
        var grossLoss = grossPnls.Where(x => x < 0m).Sum(x => Math.Abs(x));
        var netProfit = netPnls.Where(x => x > 0m).Sum();
        var netLoss = netPnls.Where(x => x < 0m).Sum(x => Math.Abs(x));
        var grossPf = ComputeProfitFactor(grossProfit, grossLoss);
        var netPf = ComputeProfitFactor(netProfit, netLoss);

        var winners = trades.Count(t =>
            string.Equals(t.Outcome, "Winner", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.Outcome, nameof(RawOutcomeStatus.Winner), StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.Outcome, "Profitable", StringComparison.OrdinalIgnoreCase));
        var losers = trades.Count(t =>
            string.Equals(t.Outcome, "Loser", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.Outcome, nameof(RawOutcomeStatus.Loser), StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.Outcome, "Losing", StringComparison.OrdinalIgnoreCase));

        var warningBearingIncluded = trades
            .Where(t => t.MetricInclusionStatus == ValidationPathMetricInclusionStatus.Included
                        && t.MetricWarningCodes is { Count: > 0 })
            .ToList();
        var warningCodes = warningBearingIncluded
            .SelectMany(t => t.MetricWarningCodes)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new LayerSegmentMetrics
        {
            CandleCount = candleCount,
            CandidateCount = candidateCount,
            OpportunityRatePer1000Candles = candleCount > 0
                ? Math.Round(candidateCount * 1000m / candleCount, 4)
                : 0m,
            ClosedTradeCount = trades.Count,
            WinnerCount = winners,
            LoserCount = losers,
            WinRate = trades.Count > 0
                ? Math.Round((decimal)winners / trades.Count * 100m, 4)
                : null,
            GrossExpectancyR = grossAvg,
            NetExpectancyR = netAvg,
            GrossAverageR = grossAvg,
            NetAverageR = netAvg,
            AverageR = grossAvg,
            GrossPnl = grossPnls.Sum(),
            TransactionCosts = costs,
            NetPnl = netPnls.Sum(),
            GrossProfit = grossProfit,
            GrossLoss = grossLoss,
            NetProfit = netProfit,
            NetLoss = netLoss,
            GrossProfitFactor = grossPf.NumericValue,
            NetProfitFactor = netPf.NumericValue,
            ProfitFactor = netPf.NumericValue,
            GrossProfitFactorStatus = grossPf.Status,
            NetProfitFactorStatus = netPf.Status,
            ProfitFactorStatus = netPf.Status,
            BoundaryCensoredCount = boundaryCensored,
            MetricIncludedCandidateCount = candidateCount,
            ExpectancyCalculationMode =
                "ValidationRiskBasis/v1:PathTrade GrossR=GrossPnl/DerivedRisk; NetR=NetPnl/DerivedRisk",
            ProfitFactorCalculationMode = "SeparateGrossNet",
            CostModelVersion = "PathSpecificTransactionCosts",
            MetricsVersion = Current,
            RiskBasisVersion = ValidationRiskBasisService.Version,
            RiskBasisType = basisType ?? (layer is ValidationLayerType.RawStrategy or ValidationLayerType.ConfidenceQualified
                ? ValidationRiskBasisType.NormalizedOneUnit
                : ValidationRiskBasisType.ShadowPortfolioPosition),
            RiskBasisValidationStatus = aggregateStatus,
            NetExpectancyApplicability = audit.NetExpectancyApplicability,
            NetExpectancyIncludedTradeCount = audit.IncludedTradeCount,
            NetExpectancyExcludedTradeCount = audit.ExcludedTradeCount,
            NetExpectancyExclusionReasons = audit.ExclusionReasons.Concat(audit.Diagnostics).Distinct().ToList(),
            MetricWarningBearingIncludedTradeCount = warningBearingIncluded.Count,
            MetricWarningCodes = warningCodes.Count == 0 ? null : warningCodes
        };
    }

    public static IReadOnlyList<StrategyResearchCandidate> SelectClosedTradesPublic(
        IReadOnlyList<StrategyResearchCandidate> list,
        ExpiredTradeMetricPolicy policy) => SelectClosedTrades(list, policy);

    public static LayerSegmentMetrics FromStrategyLabSummary(
        StrategyLabPerformanceSummaryDto? summary,
        int candleCount,
        int candidateCount,
        int boundaryCensored,
        ShadowPortfolioSummaryDto? riskOnly = null,
        ShadowPortfolioSummaryDto? fullPipeline = null,
        ValidationLayerType layer = ValidationLayerType.RawStrategy)
    {
        if (layer == ValidationLayerType.RiskOnly && riskOnly is not null)
        {
            return FromShadow(riskOnly, candleCount, candidateCount, boundaryCensored);
        }

        if (layer == ValidationLayerType.FullPipeline && fullPipeline is not null)
        {
            return FromShadow(fullPipeline, candleCount, candidateCount, boundaryCensored);
        }

        if (summary is null)
        {
            var emptyOpp = candleCount > 0 ? candidateCount * 1000m / candleCount : 0m;
            return new LayerSegmentMetrics
            {
                CandleCount = candleCount,
                CandidateCount = candidateCount,
                OpportunityRatePer1000Candles = Math.Round(emptyOpp, 4),
                BoundaryCensoredCount = boundaryCensored,
                MetricsVersion = VersionV11
            };
        }

        // Summary AverageR is gross R; NetExpectancyR must not alias AverageR (v1 bug).
        var closed = summary.RawClosedTrades;
        var winners = summary.Winners;
        var losers = summary.Losers;
        var grossAvg = summary.AverageR;
        var net = summary.NetPnl;
        var grossWinner = summary.GrossWinnerPnl;
        var grossLoserAbs = Math.Abs(summary.GrossLoserPnl);
        var grossPnl = grossWinner - grossLoserAbs;
        var fees = Math.Max(0m, Math.Abs(grossPnl) - Math.Abs(net));
        if (grossPnl != 0m && net != 0m)
        {
            fees = Math.Max(0m, Math.Abs(grossPnl) - Math.Abs(net));
            // Prefer gross - net when signs align with fee drag.
            var inferred = grossPnl - net;
            if (inferred >= 0m) fees = inferred;
        }

        decimal? netAvgR = null;
        if (closed > 0 && grossAvg != 0m && grossPnl != 0m)
        {
            // Approximate NetR from monetary net using gross R risk scale.
            var riskPerTrade = Math.Abs(grossPnl / closed / grossAvg);
            if (riskPerTrade > 0m)
            {
                netAvgR = Math.Round(net / closed / riskPerTrade, 8);
            }
        }
        else if (closed > 0 && grossPnl != 0m)
        {
            netAvgR = Math.Round(grossAvg * (net / grossPnl), 8);
        }

        var grossPf = ComputeProfitFactor(grossWinner, grossLoserAbs);
        var netGains = net > 0 ? net : 0m;
        var netLosses = net < 0 ? Math.Abs(net) : 0m;
        // Summary lacks per-trade net; keep PF from summary when present else approximate.
        var netPf = summary.ProfitFactor > 0
            ? new ProfitFactorResult(
                summary.ProfitFactor,
                summary.ProfitFactor >= 999m ? ProfitFactorStatus.Infinity : ProfitFactorStatus.Finite)
            : ComputeProfitFactor(netGains, netLosses);

        var opp = candleCount > 0 ? candidateCount * 1000m / candleCount : 0m;
        return new LayerSegmentMetrics
        {
            CandleCount = candleCount,
            CandidateCount = candidateCount,
            OpportunityRatePer1000Candles = Math.Round(opp, 4),
            ClosedTradeCount = closed,
            WinnerCount = winners,
            LoserCount = losers,
            ExpiredCount = Math.Max(0, candidateCount - closed),
            WinRate = closed > 0 ? Math.Round((decimal)winners / closed * 100m, 4) : null,
            AverageR = grossAvg,
            MedianR = grossAvg,
            GrossAverageR = grossAvg,
            GrossMedianR = grossAvg,
            NetAverageR = netAvgR,
            NetMedianR = netAvgR,
            GrossExpectancyR = grossAvg,
            NetExpectancyR = netAvgR,
            GrossPnl = Math.Round(grossPnl, 8),
            TransactionCosts = fees > 0 ? Math.Round(fees, 8) : null,
            NetPnl = net,
            NetReturnPercent = summary.PnlPercent,
            GrossProfit = Math.Round(grossWinner, 8),
            GrossLoss = Math.Round(grossLoserAbs, 8),
            GrossProfitFactor = grossPf.NumericValue,
            NetProfitFactor = netPf.NumericValue,
            ProfitFactor = netPf.NumericValue,
            MaximumRealizedDrawdownPercent = summary.MaxDrawdownPercent,
            FeeToGrossProfitPercent = grossWinner > 0 && fees > 0
                ? Math.Round(fees / grossWinner * 100m, 4)
                : null,
            BoundaryCensoredCount = boundaryCensored,
            ExpectancyCalculationMode = "SummaryDerived/v1.1",
            ProfitFactorCalculationMode = "SeparateGrossNet",
            MetricsVersion = VersionV11,
            GrossProfitFactorStatus = grossPf.Status,
            NetProfitFactorStatus = netPf.Status,
            ProfitFactorStatus = netPf.Status
        };
    }

    public static LayerSegmentMetrics FromShadow(
        ShadowPortfolioSummaryDto shadow,
        int candleCount,
        int candidateCount,
        int boundaryCensored)
    {
        var closed = shadow.TradesOpened > 0 ? shadow.TradesOpened : shadow.TradesAccepted;
        var winners = shadow.ProfitableTrades;
        var losers = shadow.LosingTrades;
        var gross = shadow.GrossPnl;
        var net = shadow.RealizedNetPnl;
        var costs = shadow.TotalTransactionCosts;
        decimal? netExpR = null;
        if (shadow.Ledger is { Count: > 0 })
        {
            var netRs = shadow.Ledger
                .Where(e => e.GrossR != 0m)
                .Select(e =>
                {
                    var risk = Math.Abs(e.GrossPnl / e.GrossR);
                    return risk > 0m ? e.NetPnl / risk : (decimal?)null;
                })
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();
            if (netRs.Count > 0)
            {
                netExpR = Math.Round(netRs.Average(), 8);
            }
        }

        decimal? grossPf = null;
        decimal? netPf = null;
        ProfitFactorStatus netStatus = ProfitFactorStatus.Undefined;
        if (shadow.Ledger is { Count: > 0 })
        {
            var gGain = shadow.Ledger.Where(x => x.GrossPnl > 0).Sum(x => x.GrossPnl);
            var gLoss = shadow.Ledger.Where(x => x.GrossPnl < 0).Sum(x => Math.Abs(x.GrossPnl));
            var nGain = shadow.Ledger.Where(x => x.NetPnl > 0).Sum(x => x.NetPnl);
            var nLoss = shadow.Ledger.Where(x => x.NetPnl < 0).Sum(x => Math.Abs(x.NetPnl));
            var g = ComputeProfitFactor(gGain, gLoss);
            var n = ComputeProfitFactor(nGain, nLoss);
            grossPf = g.NumericValue;
            netPf = n.NumericValue;
            netStatus = n.Status;
        }

        return new LayerSegmentMetrics
        {
            CandleCount = candleCount,
            CandidateCount = candidateCount,
            OpportunityRatePer1000Candles = candleCount > 0
                ? Math.Round(candidateCount * 1000m / candleCount, 4)
                : 0m,
            ClosedTradeCount = closed,
            WinnerCount = winners,
            LoserCount = losers,
            WinRate = closed > 0 ? Math.Round((decimal)winners / closed * 100m, 4) : null,
            GrossPnl = gross,
            TransactionCosts = costs,
            NetPnl = net,
            NetReturnPercent = shadow.NetReturnAfterCostsPercent,
            MaximumRealizedDrawdownPercent = shadow.MaxRealizedDrawdownPercent,
            GrossExpectancyR = null,
            NetExpectancyR = netExpR,
            NetAverageR = netExpR,
            GrossProfitFactor = grossPf,
            NetProfitFactor = netPf,
            ProfitFactor = netPf,
            ProfitFactorStatus = netStatus,
            BoundaryCensoredCount = boundaryCensored,
            MetricsVersion = VersionV11,
            ExpectancyCalculationMode = "ShadowLedgerNetRPerTrade",
            ProfitFactorCalculationMode = "LedgerGrossNet"
        };
    }

    public static TradeLevelMetrics ComputeTradeMetrics(StrategyResearchCandidate c)
    {
        var grossPnl = c.RawGrossPnl ?? c.RawNetPnl ?? 0m;
        var netPnl = c.RawNetPnl ?? c.RawGrossPnl ?? 0m;
        var grossR = c.RawRMultiple;

        decimal? netR = null;
        var risk = ResolveRiskAmount(c);
        if (risk.HasValue && risk.Value > 0m)
        {
            netR = Math.Round(netPnl / risk.Value, 8);
        }
        else if (grossR.HasValue && grossR.Value != 0m && grossPnl != 0m)
        {
            // NetR ≈ RawNetPnl / (RawGrossPnl / RawRMultiple)
            var derivedRisk = Math.Abs(grossPnl / grossR.Value);
            if (derivedRisk > 0m)
            {
                netR = Math.Round(netPnl / derivedRisk, 8);
            }
        }
        else if (grossR.HasValue && grossPnl != 0m && netPnl != 0m)
        {
            netR = Math.Round(grossR.Value * (netPnl / grossPnl), 8);
        }
        else if (grossR.HasValue && Math.Abs(grossPnl - netPnl) < 0.0000001m)
        {
            netR = grossR;
        }

        return new TradeLevelMetrics
        {
            SetupFingerprint = c.SetupFingerprint,
            GrossPnl = grossPnl,
            NetPnl = netPnl,
            GrossR = grossR,
            NetR = netR,
            RiskAmount = risk
        };
    }

    public static decimal? ResolveRiskAmount(StrategyResearchCandidate c)
    {
        if (c.RiskAmount is > 0m) return c.RiskAmount;
        var qty = c.ProposedPositionSize;
        if (qty is > 0m)
        {
            var stopDist = Math.Abs(c.ProposedEntryPrice - c.StopLoss);
            if (stopDist > 0m) return stopDist * qty.Value;
        }

        return null;
    }

    public static ProfitFactorResult ComputeProfitFactor(decimal profit, decimal lossAbs)
    {
        if (lossAbs <= 0m)
        {
            if (profit > 0m)
            {
                return new ProfitFactorResult(null, ProfitFactorStatus.Infinity);
            }

            if (profit == 0m)
            {
                return new ProfitFactorResult(null, ProfitFactorStatus.NotMeaningful);
            }

            return new ProfitFactorResult(0m, ProfitFactorStatus.Finite);
        }

        return new ProfitFactorResult(Math.Round(profit / lossAbs, 8), ProfitFactorStatus.Finite);
    }

    public static string SerializeMetrics(LayerSegmentMetrics m) =>
        JsonSerializer.Serialize(m, JsonOptions);

    public static LayerSegmentMetrics? DeserializeMetrics(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return null;
        try
        {
            return JsonSerializer.Deserialize<LayerSegmentMetrics>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static List<StrategyResearchCandidate> SelectClosedTrades(
        IReadOnlyList<StrategyResearchCandidate> list,
        ExpiredTradeMetricPolicy policy)
    {
        var closed = new List<StrategyResearchCandidate>();
        foreach (var c in list)
        {
            var isClosedStatus = c.CandidateStatus is StrategyResearchCandidateStatus.Closed
                or StrategyResearchCandidateStatus.Simulated
                || c.RawOutcomeStatus is RawOutcomeStatus.Winner or RawOutcomeStatus.Loser or RawOutcomeStatus.Expired;

            if (!isClosedStatus) continue;

            if (c.RawOutcomeStatus == RawOutcomeStatus.Expired)
            {
                switch (policy)
                {
                    case ExpiredTradeMetricPolicy.ExcludeFromClosedMetrics:
                        continue;
                    case ExpiredTradeMetricPolicy.IncludeOnlyWhenExitPriceKnown:
                        if (c.RawExitPrice is null) continue;
                        break;
                    case ExpiredTradeMetricPolicy.IncludeAsClosedAtExpiry:
                    default:
                        if (c.RawExitPrice is null && c.RawRMultiple is null && c.RawNetPnl is null)
                        {
                            continue;
                        }

                        break;
                }
            }

            closed.Add(c);
        }

        return closed;
    }

    private static decimal? AverageOrNull(IReadOnlyList<decimal> values) =>
        values.Count == 0 ? null : Math.Round(values.Average(), 8);

    private static decimal? MedianOrNull(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0) return null;
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        if (sorted.Count % 2 == 1) return sorted[mid];
        return Math.Round((sorted[mid - 1] + sorted[mid]) / 2m, 8);
    }
}

public sealed class TradeLevelMetrics
{
    public string SetupFingerprint { get; init; } = string.Empty;
    public decimal GrossPnl { get; init; }
    public decimal NetPnl { get; init; }
    public decimal? GrossR { get; init; }
    public decimal? NetR { get; init; }
    public decimal? RiskAmount { get; init; }
}

public enum ProfitFactorStatus
{
    Finite = 1,
    Infinity = 2,
    NotMeaningful = 3,
    Undefined = 4
}

public readonly record struct ProfitFactorResult(decimal? NumericValue, ProfitFactorStatus Status);
