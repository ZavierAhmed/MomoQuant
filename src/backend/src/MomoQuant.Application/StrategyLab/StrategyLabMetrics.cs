using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Application.StrategyLab.Dtos;

namespace MomoQuant.Application.StrategyLab;

public static class StrategyOpportunityMetricsCalculator
{
    public static StrategyOpportunityMetricsDto Calculate(
        int evaluations,
        IReadOnlyList<StrategyResearchCandidate> candidates,
        int totalCandles,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var rawCandidates = candidates.Count;
        var longCount = candidates.Count(c => c.Direction == TradeDirection.Long);
        var shortCount = candidates.Count(c => c.Direction == TradeDirection.Short);
        var per1000 = totalCandles > 0 ? (decimal)rawCandidates / totalCandles * 1000m : 0m;
        var spanDays = Math.Max(1m, (decimal)(toUtc - fromUtc).TotalDays);
        var perDay = rawCandidates / spanDays;
        var per30Days = perDay * 30m;

        var gaps = new List<int>();
        if (candidates.Count > 1)
        {
            var ordered = candidates.OrderBy(c => c.SetupDetectedAtUtc).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                gaps.Add((int)(ordered[i].SetupDetectedAtUtc - ordered[i - 1].SetupDetectedAtUtc).TotalMinutes);
            }
        }

        return new StrategyOpportunityMetricsDto
        {
            Evaluations = evaluations,
            RawCandidates = rawCandidates,
            CandidatesPer1000Candles = Math.Round(per1000, 2),
            CandidatesPerDay = Math.Round(perDay, 2),
            CandidatesPer30Days = Math.Round(per30Days, 2),
            AverageBarsBetweenCandidates = gaps.Count > 0 ? gaps.Average() : null,
            MedianBarsBetweenCandidates = gaps.Count > 0 ? gaps.OrderBy(g => g).ElementAt(gaps.Count / 2) : null,
            LongestGapBetweenCandidates = gaps.Count > 0 ? gaps.Max() : null,
            LongCandidateCount = longCount,
            ShortCandidateCount = shortCount
        };
    }
}

public static class EvidenceQualityCalculator
{
    public static StrategyEvidenceQuality Calculate(int closedRawTrades) => closedRawTrades switch
    {
        < 10 => StrategyEvidenceQuality.VeryLow,
        < 30 => StrategyEvidenceQuality.Low,
        < 100 => StrategyEvidenceQuality.Medium,
        _ => StrategyEvidenceQuality.High
    };

    public static string Describe(StrategyEvidenceQuality quality) => quality switch
    {
        StrategyEvidenceQuality.VeryLow => "Very Low",
        StrategyEvidenceQuality.Low => "Low",
        StrategyEvidenceQuality.Medium => "Medium",
        StrategyEvidenceQuality.High => "High",
        _ => quality.ToString()
    };
}

public static class StrategyLabPerformanceCalculator
{
    public static StrategyLabPerformanceSummaryDto BuildSummary(
        IReadOnlyList<StrategyResearchCandidate> candidates,
        StrategyOpportunityMetricsDto opportunity,
        StrategyEvidenceQuality evidenceQuality,
        decimal initialBalance)
    {
        var closed = candidates.Where(c => c.CandidateStatus == StrategyResearchCandidateStatus.Closed).ToList();
        var winners = closed.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Winner);
        var losers = closed.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Loser);
        var breakeven = closed.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Breakeven);
        var netPnl = closed.Sum(c => c.RawNetPnl ?? 0m);
        var grossWins = closed.Where(c => (c.RawNetPnl ?? 0m) > 0).Sum(c => c.RawNetPnl ?? 0m);
        var grossLosses = Math.Abs(closed.Where(c => (c.RawNetPnl ?? 0m) < 0).Sum(c => c.RawNetPnl ?? 0m));
        var profitFactor = grossLosses > 0 ? grossWins / grossLosses : grossWins > 0 ? 999m : 0m;
        var winRate = closed.Count > 0 ? (decimal)winners / closed.Count * 100m : 0m;
        var avgR = closed.Count > 0 ? closed.Average(c => c.RawRMultiple ?? 0m) : 0m;
        var expectancy = closed.Count > 0 ? closed.Average(c => c.RawNetPnl ?? 0m) : 0m;
        // Independent setup scale metric only — NOT capital-aware portfolio return.
        var pnlPercent = initialBalance > 0 ? netPnl / initialBalance * 100m : 0m;

        var equity = initialBalance;
        var peak = equity;
        var maxDrawdown = 0m;
        var equityWentNonPositive = false;
        foreach (var trade in closed.OrderBy(c => c.RawExitTimeUtc ?? c.SetupDetectedAtUtc))
        {
            equity += trade.RawNetPnl ?? 0m;
            if (equity <= 0)
            {
                equityWentNonPositive = true;
            }

            peak = Math.Max(peak, equity);
            if (peak > 0)
            {
                var dd = (peak - equity) / peak * 100m;
                maxDrawdown = Math.Max(maxDrawdown, dd);
            }
        }

        var warnings = new List<string>();
        var expectedPnlPercent = initialBalance > 0 ? Math.Round(netPnl / initialBalance * 100m, 2) : 0m;
        if (Math.Abs(Math.Round(pnlPercent, 2) - expectedPnlPercent) > 0.01m)
        {
            warnings.Add("MetricCalculationWarning: PnL percent inconsistent with Net PnL and Initial Balance.");
        }

        if (equityWentNonPositive)
        {
            warnings.Add("MetricCalculationWarning: Independent setup equity sequence fell to or below zero — drawdown can exceed 100% and is not a portfolio result.");
        }

        if (maxDrawdown > 100m)
        {
            warnings.Add("MetricCalculationWarning: Independent setup sequence drawdown exceeds 100% because overlapping candidates are summed as if traded concurrently.");
        }

        warnings.Add("Independent candidate outcomes are displayed as setup research metrics, not portfolio simulation.");
        if (closed.Count >= 2)
        {
            warnings.Add("Overlapping candidate trades are not capital-constrained; portfolio metrics unavailable.");
        }

        return new StrategyLabPerformanceSummaryDto
        {
            RawCandidates = candidates.Count,
            RawClosedTrades = closed.Count,
            Winners = winners,
            Losers = losers,
            Breakeven = breakeven,
            WinRate = Math.Round(winRate, 2),
            NetPnl = Math.Round(netPnl, 4),
            NetPnlLabel = "Independent Setup PnL",
            PnlPercent = Math.Round(pnlPercent, 4),
            PnlPercentLabel = "Independent Setup PnL % of Initial Balance",
            ProfitFactor = Math.Round(profitFactor, 2),
            Expectancy = Math.Round(expectancy, 4),
            AverageR = Math.Round(avgR, 2),
            MaxDrawdownPercent = Math.Round(maxDrawdown, 2),
            MaxDrawdownLabel = "Independent Setup Sequence Drawdown %",
            PortfolioMetricsAvailable = false,
            PortfolioMetricsNote =
                "Portfolio metrics unavailable. Candidates are simulated independently; overlapping capital is not modeled.",
            InitialBalance = initialBalance,
            GrossWinnerPnl = Math.Round(grossWins, 4),
            GrossLoserPnl = Math.Round(grossLosses, 4),
            MetricWarnings = warnings,
            Opportunity = opportunity,
            EvidenceQuality = evidenceQuality,
            EvidenceQualityLabel = EvidenceQualityCalculator.Describe(evidenceQuality)
        };
    }

    public static RawVsGatedComparisonDto BuildGatedComparison(IReadOnlyList<StrategyResearchCandidate> candidates)
    {
        GatedSubsetDto BuildSubset(Func<StrategyResearchCandidate, bool> predicate)
        {
            var subset = candidates.Where(predicate).OrderBy(c => c.SetupDetectedAtUtc).ToList();
            var closed = subset.Where(c =>
                c.RawOutcomeStatus is RawOutcomeStatus.Winner or RawOutcomeStatus.Loser or RawOutcomeStatus.Breakeven).ToList();
            var net = closed.Sum(c => c.RawNetPnl ?? 0m);
            var wins = closed.Where(c => (c.RawNetPnl ?? 0m) > 0).Sum(c => c.RawNetPnl ?? 0m);
            var losses = Math.Abs(closed.Where(c => (c.RawNetPnl ?? 0m) < 0).Sum(c => c.RawNetPnl ?? 0m));
            var winners = closed.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Winner);
            var losers = closed.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Loser);
            var confScores = subset.Where(c => c.ConfidenceScore.HasValue).Select(c => c.ConfidenceScore!.Value).ToList();
            var riskScores = subset.Where(c => c.RiskScore.HasValue).Select(c => c.RiskScore!.Value).ToList();

            return new GatedSubsetDto
            {
                CandidateCount = subset.Count,
                ClosedTradeCount = closed.Count,
                NetPnl = Math.Round(net, 4),
                ProfitFactor = losses > 0 ? Math.Round(wins / losses, 2) : wins > 0 ? 999m : 0m,
                Winners = winners,
                Losers = losers,
                WinRate = winners + losers <= 0 ? 0m : Math.Round((decimal)winners / (winners + losers) * 100m, 2),
                MaxDrawdownPercent = ComputeMaxDrawdown(subset),
                AverageConfidence = confScores.Count == 0 ? null : Math.Round(confScores.Average(), 2),
                AverageRiskScore = riskScores.Count == 0 ? null : Math.Round(riskScores.Average(), 2)
            };
        }

        var raw = BuildSubset(_ => true);
        var confApproved = BuildSubset(c => c.ConfidenceDecision == ResearchConfidenceDecision.Approved);
        var confRejected = BuildSubset(c => c.ConfidenceDecision == ResearchConfidenceDecision.Rejected);
        var riskApproved = BuildSubset(c => c.RiskDecision == ResearchRiskDecision.Approved);
        var riskRejected = BuildSubset(c => c.RiskDecision == ResearchRiskDecision.Rejected);
        var pipelineApproved = BuildSubset(c => c.FinalPipelineDecision == ResearchFinalPipelineDecision.Approved);

        var interpretations = new List<string>();
        if (candidates.Count > 0 && confRejected.CandidateCount > 0)
        {
            var pct = (decimal)confRejected.CandidateCount / candidates.Count * 100m;
            interpretations.Add($"Confidence rejected {Math.Round(pct, 0)}% of raw candidates.");
            if (confRejected.NetPnl != 0)
            {
                interpretations.Add($"Confidence-rejected candidates had a hypothetical net PnL of {confRejected.NetPnl:+#.##;-#.##;0}.");
            }

            var rejectedWinners = candidates
                .Where(c => c.ConfidenceDecision == ResearchConfidenceDecision.Rejected
                    && c.RawOutcomeStatus == RawOutcomeStatus.Winner
                    && c.ConfidenceScore.HasValue)
                .Select(c => c.ConfidenceScore!.Value)
                .ToList();
            var threshold = candidates
                .Where(c => c.ConfidenceThreshold.HasValue)
                .Select(c => c.ConfidenceThreshold!.Value)
                .FirstOrDefault();
            if (rejectedWinners.Count > 0 && threshold > 0)
            {
                interpretations.Add(
                    $"Rejected winners had an average confidence score of {Math.Round(rejectedWinners.Average(), 2)} while the configured threshold was {threshold}.");
            }
        }

        return new RawVsGatedComparisonDto
        {
            Raw = raw,
            ConfidenceApproved = confApproved,
            ConfidenceRejected = confRejected,
            RiskApproved = riskApproved,
            RiskRejected = riskRejected,
            FullPipeline = pipelineApproved,
            Interpretations = interpretations
        };
    }

    private static decimal ComputeMaxDrawdown(IReadOnlyList<StrategyResearchCandidate> ordered)
    {
        decimal equity = 0;
        decimal peak = 0;
        decimal maxDd = 0;
        foreach (var candidate in ordered)
        {
            equity += candidate.RawNetPnl ?? 0m;
            if (equity > peak)
            {
                peak = equity;
            }

            if (peak > 0)
            {
                maxDd = Math.Max(maxDd, (peak - equity) / peak * 100m);
            }
        }

        return Math.Round(maxDd, 2);
    }
}
