using MomoQuant.Application.StrategyLab.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.StrategyLab;

public static class StrategyLabGateAnalysisCalculator
{
    private static readonly decimal[] SimulationThresholds = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90];

    public static StrategyLabGateAnalysisDto Build(
        StrategyLabRun run,
        IReadOnlyList<StrategyResearchCandidate> candidates)
    {
        var closed = candidates
            .Where(c => c.RawOutcomeStatus is RawOutcomeStatus.Winner or RawOutcomeStatus.Loser or RawOutcomeStatus.Breakeven)
            .ToList();
        var winners = closed.Where(c => c.RawOutcomeStatus == RawOutcomeStatus.Winner).ToList();
        var losers = closed.Where(c => c.RawOutcomeStatus == RawOutcomeStatus.Loser).ToList();

        var confidenceEvaluated = candidates
            .Where(c => c.ConfidenceDecision is ResearchConfidenceDecision.Approved or ResearchConfidenceDecision.Rejected)
            .ToList();
        var riskEvaluated = candidates
            .Where(c => c.RiskDecision is ResearchRiskDecision.Approved or ResearchRiskDecision.Rejected)
            .ToList();

        var currentConfidenceThreshold = confidenceEvaluated
            .Select(c => c.ConfidenceThreshold)
            .FirstOrDefault(t => t.HasValue)
            ?? InferConfidenceThreshold(confidenceEvaluated);

        var dto = new StrategyLabGateAnalysisDto
        {
            ExecutionMode = run.ExecutionMode,
            ConfidenceSummary = confidenceEvaluated.Count == 0
                ? null
                : BuildConfidenceSummary(confidenceEvaluated, winners, losers, currentConfidenceThreshold),
            ConfidenceRejectedWinners = confidenceEvaluated.Count == 0
                ? null
                : BuildRejectedGroup(
                    winners.Where(c => c.ConfidenceDecision == ResearchConfidenceDecision.Rejected).ToList(),
                    winners.Count),
            ConfidenceRejectedLosers = confidenceEvaluated.Count == 0
                ? null
                : BuildRejectedGroup(
                    losers.Where(c => c.ConfidenceDecision == ResearchConfidenceDecision.Rejected).ToList(),
                    losers.Count),
            ConfidenceBuckets = confidenceEvaluated.Count == 0
                ? []
                : BuildScoreBuckets(confidenceEvaluated, c => c.ConfidenceScore),
            ConfidenceThresholdSimulation = confidenceEvaluated.Count == 0
                ? []
                : BuildThresholdSimulation(confidenceEvaluated, currentConfidenceThreshold),
            RiskSummary = riskEvaluated.Count == 0
                ? null
                : BuildRiskSummary(riskEvaluated, winners, losers),
            RiskRejectedWinners = riskEvaluated.Count == 0
                ? null
                : BuildRejectedGroup(
                    winners.Where(c => c.RiskDecision == ResearchRiskDecision.Rejected).ToList(),
                    winners.Count,
                    useRiskScore: true),
            RiskRejectedLosers = riskEvaluated.Count == 0
                ? null
                : BuildRejectedGroup(
                    losers.Where(c => c.RiskDecision == ResearchRiskDecision.Rejected).ToList(),
                    losers.Count,
                    useRiskScore: true),
            RiskReasonAnalysis = riskEvaluated.Count == 0
                ? []
                : BuildRiskReasonAnalysis(riskEvaluated),
            OverallWinnerLoserComparison = BuildWinnerLoserComparison(winners, losers),
            ConfidenceScoreDiagnostics = confidenceEvaluated.Count == 0
                ? null
                : ScoreDistributionCalculator.Build(
                    confidenceEvaluated.Where(c => c.ConfidenceScore.HasValue).Select(c => c.ConfidenceScore!.Value).ToList(),
                    "confidence"),
            RiskScoreDiagnostics = riskEvaluated.Count == 0
                ? null
                : ScoreDistributionCalculator.Build(
                    riskEvaluated.Where(c => c.RiskScore.HasValue).Select(c => c.RiskScore!.Value).ToList(),
                    "risk"),
            Raw = BuildEnhancedSubset(candidates),
            ConfidenceApproved = BuildEnhancedSubset(candidates.Where(c => c.ConfidenceDecision == ResearchConfidenceDecision.Approved)),
            ConfidenceRejected = BuildEnhancedSubset(candidates.Where(c => c.ConfidenceDecision == ResearchConfidenceDecision.Rejected)),
            RiskApproved = BuildEnhancedSubset(candidates.Where(c => c.RiskDecision == ResearchRiskDecision.Approved)),
            RiskRejected = BuildEnhancedSubset(candidates.Where(c => c.RiskDecision == ResearchRiskDecision.Rejected)),
            FullPipeline = BuildEnhancedSubset(candidates.Where(c => c.FinalPipelineDecision == ResearchFinalPipelineDecision.Approved)),
            Interpretations = []
        };

        var avgWinner = dto.OverallWinnerLoserComparison.AverageWinnerConfidence;
        var avgLoser = dto.OverallWinnerLoserComparison.AverageLoserConfidence;
        dto.ConfidenceSeparation = avgWinner.HasValue && avgLoser.HasValue
            ? Math.Round(avgWinner.Value - avgLoser.Value, 2)
            : null;

        dto.Interpretations = BuildInterpretations(dto, run, currentConfidenceThreshold);
        return dto;
    }

    private static GateDecisionSummaryDto BuildConfidenceSummary(
        IReadOnlyList<StrategyResearchCandidate> evaluated,
        IReadOnlyList<StrategyResearchCandidate> winners,
        IReadOnlyList<StrategyResearchCandidate> losers,
        decimal? threshold)
    {
        var approved = evaluated.Count(c => c.ConfidenceDecision == ResearchConfidenceDecision.Approved);
        var rejected = evaluated.Count(c => c.ConfidenceDecision == ResearchConfidenceDecision.Rejected);
        var scores = evaluated.Where(c => c.ConfidenceScore.HasValue).Select(c => c.ConfidenceScore!.Value).ToList();
        var winnerScores = winners.Where(c => c.ConfidenceScore.HasValue).Select(c => c.ConfidenceScore!.Value).ToList();
        var loserScores = losers.Where(c => c.ConfidenceScore.HasValue).Select(c => c.ConfidenceScore!.Value).ToList();

        return new GateDecisionSummaryDto
        {
            EvaluatedCount = evaluated.Count,
            ApprovedCount = approved,
            RejectedCount = rejected,
            ApprovalRate = Rate(approved, evaluated.Count),
            RejectionRate = Rate(rejected, evaluated.Count),
            CurrentThreshold = threshold,
            AverageScore = Avg(scores),
            MedianScore = Median(scores),
            AverageWinnerScore = Avg(winnerScores),
            MedianWinnerScore = Median(winnerScores),
            AverageLoserScore = Avg(loserScores),
            MedianLoserScore = Median(loserScores)
        };
    }

    private static GateDecisionSummaryDto BuildRiskSummary(
        IReadOnlyList<StrategyResearchCandidate> evaluated,
        IReadOnlyList<StrategyResearchCandidate> winners,
        IReadOnlyList<StrategyResearchCandidate> losers)
    {
        var approved = evaluated.Count(c => c.RiskDecision == ResearchRiskDecision.Approved);
        var rejected = evaluated.Count(c => c.RiskDecision == ResearchRiskDecision.Rejected);
        var scores = evaluated.Where(c => c.RiskScore.HasValue).Select(c => c.RiskScore!.Value).ToList();
        var winnerScores = winners.Where(c => c.RiskScore.HasValue).Select(c => c.RiskScore!.Value).ToList();
        var loserScores = losers.Where(c => c.RiskScore.HasValue).Select(c => c.RiskScore!.Value).ToList();
        var threshold = evaluated.Select(c => c.RiskThreshold).FirstOrDefault(t => t.HasValue);

        return new GateDecisionSummaryDto
        {
            EvaluatedCount = evaluated.Count,
            ApprovedCount = approved,
            RejectedCount = rejected,
            ApprovalRate = Rate(approved, evaluated.Count),
            RejectionRate = Rate(rejected, evaluated.Count),
            CurrentThreshold = threshold,
            AverageScore = Avg(scores),
            MedianScore = Median(scores),
            AverageWinnerScore = Avg(winnerScores),
            MedianWinnerScore = Median(winnerScores),
            AverageLoserScore = Avg(loserScores),
            MedianLoserScore = Median(loserScores)
        };
    }

    private static RejectedOutcomeGroupDto BuildRejectedGroup(
        IReadOnlyList<StrategyResearchCandidate> group,
        int outcomeGroupTotal,
        bool useRiskScore = false)
    {
        var scores = useRiskScore
            ? group.Where(c => c.RiskScore.HasValue).Select(c => c.RiskScore!.Value).ToList()
            : group.Where(c => c.ConfidenceScore.HasValue).Select(c => c.ConfidenceScore!.Value).ToList();
        var margins = useRiskScore
            ? group.Where(c => c.RiskMargin.HasValue && c.RiskMargin < 0).Select(c => c.RiskMargin!.Value).ToList()
            : group.Select(ResolveConfidenceMargin).Where(m => m.HasValue && m < 0).Select(m => m!.Value).ToList();
        var rValues = group.Where(c => c.RawRMultiple.HasValue).Select(c => c.RawRMultiple!.Value).ToList();

        return new RejectedOutcomeGroupDto
        {
            Count = group.Count,
            PercentageOfOutcomeGroup = Rate(group.Count, outcomeGroupTotal),
            AverageScore = Avg(scores),
            MedianScore = Median(scores),
            AverageMarginBelowThreshold = margins.Count == 0 ? null : Math.Round(margins.Average(), 2),
            HypotheticalNetPnl = Math.Round(group.Sum(c => c.RawNetPnl ?? 0m), 4),
            HypotheticalAverageR = Avg(rValues)
        };
    }

    private static decimal? ResolveConfidenceMargin(StrategyResearchCandidate candidate)
    {
        if (candidate.ConfidenceMargin.HasValue)
        {
            return candidate.ConfidenceMargin;
        }

        if (!candidate.ConfidenceScore.HasValue)
        {
            return null;
        }

        var threshold = candidate.ConfidenceThreshold ?? TryParseThresholdFromReason(candidate.ConfidenceReason);
        return threshold.HasValue ? candidate.ConfidenceScore - threshold : null;
    }

    private static IReadOnlyList<ScoreBucketDto> BuildScoreBuckets(
        IReadOnlyList<StrategyResearchCandidate> evaluated,
        Func<StrategyResearchCandidate, decimal?> scoreSelector)
    {
        var buckets = new List<ScoreBucketDto>();
        for (var min = 0; min <= 90; min += 10)
        {
            var max = min == 90 ? 100 : min + 9;
            var inBucket = evaluated
                .Where(c =>
                {
                    var score = scoreSelector(c);
                    return score.HasValue && score.Value >= min && score.Value <= max;
                })
                .ToList();
            var winners = inBucket.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Winner);
            var losers = inBucket.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Loser);
            var closed = inBucket.Where(c =>
                c.RawOutcomeStatus is RawOutcomeStatus.Winner or RawOutcomeStatus.Loser or RawOutcomeStatus.Breakeven).ToList();
            var net = closed.Sum(c => c.RawNetPnl ?? 0m);
            var winPnl = closed.Where(c => (c.RawNetPnl ?? 0m) > 0).Sum(c => c.RawNetPnl ?? 0m);
            var lossPnl = Math.Abs(closed.Where(c => (c.RawNetPnl ?? 0m) < 0).Sum(c => c.RawNetPnl ?? 0m));

            buckets.Add(new ScoreBucketDto
            {
                Label = $"{min}–{max}",
                MinInclusive = min,
                MaxInclusive = max,
                CandidateCount = inBucket.Count,
                WinnerCount = winners,
                LoserCount = losers,
                WinRate = Rate(winners, winners + losers),
                NetPnl = Math.Round(net, 4),
                ProfitFactor = lossPnl > 0 ? Math.Round(winPnl / lossPnl, 2) : winPnl > 0 ? 999m : 0m,
                AverageR = Avg(closed.Where(c => c.RawRMultiple.HasValue).Select(c => c.RawRMultiple!.Value).ToList()),
                AverageMfe = Avg(closed.Where(c => c.Mfe.HasValue).Select(c => c.Mfe!.Value).ToList()),
                AverageMae = Avg(closed.Where(c => c.Mae.HasValue).Select(c => c.Mae!.Value).ToList())
            });
        }

        return buckets;
    }

    private static IReadOnlyList<ThresholdSimulationRowDto> BuildThresholdSimulation(
        IReadOnlyList<StrategyResearchCandidate> evaluated,
        decimal? currentThreshold)
    {
        var thresholds = SimulationThresholds.ToList();
        if (currentThreshold.HasValue && !thresholds.Contains(currentThreshold.Value))
        {
            thresholds.Add(currentThreshold.Value);
            thresholds.Sort();
        }

        var rawNet = evaluated.Sum(c => c.RawNetPnl ?? 0m);
        var rows = new List<ThresholdSimulationRowDto>();
        foreach (var threshold in thresholds)
        {
            var accepted = evaluated
                .Where(c => c.ConfidenceScore.HasValue && c.ConfidenceScore.Value >= threshold)
                .OrderBy(c => c.SetupDetectedAtUtc)
                .ToList();
            var rejectedCount = evaluated.Count - accepted.Count;
            var closed = accepted.Where(c =>
                c.RawOutcomeStatus is RawOutcomeStatus.Winner or RawOutcomeStatus.Loser or RawOutcomeStatus.Breakeven).ToList();
            var winners = closed.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Winner);
            var losers = closed.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Loser);
            var net = closed.Sum(c => c.RawNetPnl ?? 0m);
            var winPnl = closed.Where(c => (c.RawNetPnl ?? 0m) > 0).Sum(c => c.RawNetPnl ?? 0m);
            var lossPnl = Math.Abs(closed.Where(c => (c.RawNetPnl ?? 0m) < 0).Sum(c => c.RawNetPnl ?? 0m));

            rows.Add(new ThresholdSimulationRowDto
            {
                Threshold = threshold,
                IsCurrentThreshold = currentThreshold.HasValue && threshold == currentThreshold.Value,
                AcceptedCount = accepted.Count,
                RejectedCount = rejectedCount,
                AcceptedWinRate = Rate(winners, winners + losers),
                AcceptedNetPnl = Math.Round(net, 4),
                AcceptedProfitFactor = lossPnl > 0 ? Math.Round(winPnl / lossPnl, 2) : winPnl > 0 ? 999m : 0m,
                AcceptedMaxDrawdownPercent = ComputeMaxDrawdown(accepted),
                AcceptedAverageR = Avg(closed.Where(c => c.RawRMultiple.HasValue).Select(c => c.RawRMultiple!.Value).ToList()),
                PercentOfRawPnlPreserved = rawNet == 0
                    ? 0
                    : Math.Round(net / rawNet * 100m, 2)
            });
        }

        return rows;
    }

    private static IReadOnlyList<RiskRejectionReasonRowDto> BuildRiskReasonAnalysis(
        IReadOnlyList<StrategyResearchCandidate> evaluated)
    {
        return evaluated
            .Where(c => c.RiskDecision == ResearchRiskDecision.Rejected)
            .GroupBy(c => string.IsNullOrWhiteSpace(c.RiskRejectedRuleKey)
                ? (string.IsNullOrWhiteSpace(c.RiskReason) ? "Unknown" : c.RiskReason!)
                : c.RiskRejectedRuleKey!)
            .Select(g =>
            {
                var list = g.ToList();
                var winners = list.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Winner);
                var losers = list.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Loser);
                return new RiskRejectionReasonRowDto
                {
                    Reason = g.Key,
                    RejectedCount = list.Count,
                    WinnerCount = winners,
                    LoserCount = losers,
                    WinRate = Rate(winners, winners + losers),
                    HypotheticalNetPnl = Math.Round(list.Sum(c => c.RawNetPnl ?? 0m), 4),
                    AverageR = Avg(list.Where(c => c.RawRMultiple.HasValue).Select(c => c.RawRMultiple!.Value).ToList())
                };
            })
            .OrderByDescending(r => r.RejectedCount)
            .ToList();
    }

    private static WinnerLoserComparisonDto BuildWinnerLoserComparison(
        IReadOnlyList<StrategyResearchCandidate> winners,
        IReadOnlyList<StrategyResearchCandidate> losers) =>
        new()
        {
            WinnerCount = winners.Count,
            LoserCount = losers.Count,
            AverageWinnerConfidence = Avg(winners.Where(c => c.ConfidenceScore.HasValue).Select(c => c.ConfidenceScore!.Value).ToList()),
            AverageLoserConfidence = Avg(losers.Where(c => c.ConfidenceScore.HasValue).Select(c => c.ConfidenceScore!.Value).ToList()),
            MedianWinnerConfidence = Median(winners.Where(c => c.ConfidenceScore.HasValue).Select(c => c.ConfidenceScore!.Value).ToList()),
            MedianLoserConfidence = Median(losers.Where(c => c.ConfidenceScore.HasValue).Select(c => c.ConfidenceScore!.Value).ToList()),
            AverageWinnerRiskScore = Avg(winners.Where(c => c.RiskScore.HasValue).Select(c => c.RiskScore!.Value).ToList()),
            AverageLoserRiskScore = Avg(losers.Where(c => c.RiskScore.HasValue).Select(c => c.RiskScore!.Value).ToList()),
            MedianWinnerRiskScore = Median(winners.Where(c => c.RiskScore.HasValue).Select(c => c.RiskScore!.Value).ToList()),
            MedianLoserRiskScore = Median(losers.Where(c => c.RiskScore.HasValue).Select(c => c.RiskScore!.Value).ToList()),
            AverageWinnerMfe = Avg(winners.Where(c => c.Mfe.HasValue).Select(c => c.Mfe!.Value).ToList()),
            AverageLoserMfe = Avg(losers.Where(c => c.Mfe.HasValue).Select(c => c.Mfe!.Value).ToList()),
            AverageWinnerMae = Avg(winners.Where(c => c.Mae.HasValue).Select(c => c.Mae!.Value).ToList()),
            AverageLoserMae = Avg(losers.Where(c => c.Mae.HasValue).Select(c => c.Mae!.Value).ToList()),
            AverageWinnerStopDistancePercent = Avg(winners.Where(c => c.StopDistancePercent.HasValue).Select(c => c.StopDistancePercent!.Value).ToList()),
            AverageLoserStopDistancePercent = Avg(losers.Where(c => c.StopDistancePercent.HasValue).Select(c => c.StopDistancePercent!.Value).ToList()),
            AverageWinnerRewardRisk = Avg(winners.Select(c => c.RewardRisk).ToList()),
            AverageLoserRewardRisk = Avg(losers.Select(c => c.RewardRisk).ToList())
        };

    private static EnhancedGatedSubsetDto BuildEnhancedSubset(IEnumerable<StrategyResearchCandidate> source)
    {
        var list = source.OrderBy(c => c.SetupDetectedAtUtc).ToList();
        var closed = list.Where(c =>
            c.RawOutcomeStatus is RawOutcomeStatus.Winner or RawOutcomeStatus.Loser or RawOutcomeStatus.Breakeven).ToList();
        var winners = closed.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Winner);
        var losers = closed.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Loser);
        var net = closed.Sum(c => c.RawNetPnl ?? 0m);
        var winPnl = closed.Where(c => (c.RawNetPnl ?? 0m) > 0).Sum(c => c.RawNetPnl ?? 0m);
        var lossPnl = Math.Abs(closed.Where(c => (c.RawNetPnl ?? 0m) < 0).Sum(c => c.RawNetPnl ?? 0m));

        return new EnhancedGatedSubsetDto
        {
            CandidateCount = list.Count,
            ClosedTradeCount = closed.Count,
            Winners = winners,
            Losers = losers,
            WinRate = Rate(winners, winners + losers),
            NetPnl = Math.Round(net, 4),
            ProfitFactor = lossPnl > 0 ? Math.Round(winPnl / lossPnl, 2) : winPnl > 0 ? 999m : 0m,
            MaxDrawdownPercent = ComputeMaxDrawdown(list),
            AverageConfidence = Avg(list.Where(c => c.ConfidenceScore.HasValue).Select(c => c.ConfidenceScore!.Value).ToList()),
            AverageRiskScore = Avg(list.Where(c => c.RiskScore.HasValue).Select(c => c.RiskScore!.Value).ToList()),
            AverageR = Avg(closed.Where(c => c.RawRMultiple.HasValue).Select(c => c.RawRMultiple!.Value).ToList())
        };
    }

    private static IReadOnlyList<string> BuildInterpretations(
        StrategyLabGateAnalysisDto dto,
        StrategyLabRun run,
        decimal? threshold)
    {
        var lines = new List<string>();
        if (dto.ConfidenceSummary is { EvaluatedCount: > 0 } conf)
        {
            if (conf.RejectionRate >= 99.5m)
            {
                lines.Add("Confidence rejected 100% of raw candidates.");
            }
            else
            {
                lines.Add($"Confidence rejected {conf.RejectionRate:0.#}% of evaluated candidates.");
            }

            if (dto.ConfidenceRejectedWinners is { Count: > 0 } rejectedWinners
                && rejectedWinners.AverageScore.HasValue
                && threshold.HasValue)
            {
                lines.Add(
                    $"Rejected winners had an average confidence score of {rejectedWinners.AverageScore:0.##} while the configured threshold was {threshold:0.##}.");
            }

            if (dto.ConfidenceRejected?.NetPnl is not 0 and var rejectedPnl)
            {
                lines.Add($"Confidence-rejected candidates had a hypothetical net PnL of {rejectedPnl:+0.##;-0.##;0}.");
            }

            if (dto.ConfidenceSeparation.HasValue)
            {
                lines.Add($"Confidence separation (avg winner − avg loser) = {dto.ConfidenceSeparation:+0.##;-0.##;0}. Single-run evidence only.");
            }

            if (dto.ConfidenceScoreDiagnostics?.DegenerateWarningMessage is { } confWarn)
            {
                lines.Add($"{dto.ConfidenceScoreDiagnostics.DegenerateWarningCode}: {confWarn}");
            }
        }

        if (dto.RiskSummary is { EvaluatedCount: > 0 } risk)
        {
            lines.Add($"Risk rejected {risk.RejectionRate:0.#}% of evaluated candidates.");
            if (dto.RiskRejectedWinners is { Count: > 0 } rw)
            {
                lines.Add($"Risk rejected {rw.Count} winners with hypothetical PnL {rw.HypotheticalNetPnl:+0.##;-0.##;0}.");
            }

            if (dto.RiskScoreDiagnostics?.DegenerateWarningMessage is { } riskWarn)
            {
                lines.Add($"{dto.RiskScoreDiagnostics.DegenerateWarningCode}: {riskWarn}");
            }
        }

        if (run.ExecutionMode == StrategyLabExecutionMode.RawStrategy)
        {
            lines.Add("Execution mode is RawStrategy — confidence and risk were not evaluated.");
        }

        return lines;
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
                var dd = (peak - equity) / peak * 100m;
                if (dd > maxDd)
                {
                    maxDd = dd;
                }
            }
        }

        return Math.Round(maxDd, 2);
    }

    private static decimal? InferConfidenceThreshold(IReadOnlyList<StrategyResearchCandidate> evaluated)
    {
        foreach (var candidate in evaluated)
        {
            var parsed = TryParseThresholdFromReason(candidate.ConfidenceReason);
            if (parsed.HasValue)
            {
                return parsed;
            }
        }

        return null;
    }

    private static decimal? TryParseThresholdFromReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var parts = reason.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 4
            && decimal.TryParse(parts[^1], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var threshold))
        {
            return threshold;
        }

        return null;
    }

    private static decimal Rate(int numerator, int denominator) =>
        denominator <= 0 ? 0m : Math.Round((decimal)numerator / denominator * 100m, 2);

    private static decimal? Avg(IReadOnlyList<decimal> values) =>
        values.Count == 0 ? null : Math.Round(values.Average(), 2);

    private static decimal? Median(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        if (sorted.Count % 2 == 1)
        {
            return Math.Round(sorted[mid], 2);
        }

        return Math.Round((sorted[mid - 1] + sorted[mid]) / 2m, 2);
    }
}
