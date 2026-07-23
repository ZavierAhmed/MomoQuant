using System.Text.Json;
using MomoQuant.Application.StrategyLab.Dtos;
using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.StrategyLab;

public static class StrategyLabRiskAnalysisCalculator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static StrategyLabRiskAnalysisDto Build(IReadOnlyList<StrategyResearchCandidate> candidates)
    {
        var evaluated = candidates
            .Where(c => c.RiskDecision is ResearchRiskDecision.Approved or ResearchRiskDecision.Rejected)
            .ToList();
        var scores = evaluated
            .Select(c => c.CandidateRiskScore ?? c.RiskScore)
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .ToList();
        var winners = evaluated.Where(c => c.RawOutcomeStatus == RawOutcomeStatus.Winner).ToList();
        var losers = evaluated.Where(c => c.RawOutcomeStatus == RawOutcomeStatus.Loser).ToList();
        var approved = evaluated.Count(c => c.RiskDecision == ResearchRiskDecision.Approved);
        var rejected = evaluated.Count(c => c.RiskDecision == ResearchRiskDecision.Rejected);
        var distribution = ScoreDistributionCalculator.Build(scores, "risk");

        var diagnostics = new List<string>();
        if (distribution.DegenerateWarningMessage is not null)
        {
            diagnostics.Add($"{distribution.DegenerateWarningCode}: {distribution.DegenerateWarningMessage}");
        }

        foreach (var c in evaluated)
        {
            if (c.RiskDecision == ResearchRiskDecision.Rejected
                && string.IsNullOrWhiteSpace(c.RiskFailedRuleKeysJson)
                && string.IsNullOrWhiteSpace(c.RiskRejectedRuleKey))
            {
                diagnostics.Add("RiskDecisionInconsistent: Rejected without failed rule keys.");
                break;
            }

            if (c.ProposedPositionSize is null && string.IsNullOrWhiteSpace(c.PositionSizingUnavailableReason)
                && c.RiskAssessmentVersion == RiskObservationVersions.Current)
            {
                diagnostics.Add("RiskSizingDataMissing: Null position size without unavailable reason.");
                break;
            }
        }

        var winnerScores = winners.Select(c => c.CandidateRiskScore ?? c.RiskScore).Where(s => s.HasValue).Select(s => s!.Value).ToList();
        var loserScores = losers.Select(c => c.CandidateRiskScore ?? c.RiskScore).Where(s => s.HasValue).Select(s => s!.Value).ToList();

        return new StrategyLabRiskAnalysisDto
        {
            RiskAssessmentVersion = evaluated
                .Select(c => c.RiskAssessmentVersion)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? RiskObservationVersions.Legacy,
            FinancialRiskSummary = new StrategyLabFinancialRiskSummaryDto
            {
                EvaluatedCandidateCount = evaluated.Count,
                ApprovedCount = approved,
                RejectedCount = rejected,
                ApprovalRate = evaluated.Count == 0 ? 0 : Math.Round((decimal)approved / evaluated.Count * 100m, 2),
                AverageCandidateRiskScore = Avg(scores),
                MedianCandidateRiskScore = Median(scores),
                MinimumCandidateRiskScore = scores.Count == 0 ? null : scores.Min(),
                MaximumCandidateRiskScore = scores.Count == 0 ? null : scores.Max(),
                StandardDeviation = distribution.StandardDeviation,
                UniqueScoreCount = distribution.UniqueScoreCount
            },
            CandidateRiskScoreDistribution = distribution,
            WinnerLoserRiskComparison = new StrategyLabWinnerLoserRiskDto
            {
                AverageWinnerRiskScore = Avg(winnerScores),
                MedianWinnerRiskScore = Median(winnerScores),
                AverageLoserRiskScore = Avg(loserScores),
                MedianLoserRiskScore = Median(loserScores),
                RiskScoreSeparation = Avg(winnerScores) is { } aw && Avg(loserScores) is { } al ? aw - al : null
            },
            RejectedWinnerAnalysis = BuildRejectedSubset(
                winners.Where(c => c.RiskDecision == ResearchRiskDecision.Rejected).ToList(),
                winners.Count),
            RejectedLoserAnalysis = BuildRejectedSubset(
                losers.Where(c => c.RiskDecision == ResearchRiskDecision.Rejected).ToList(),
                losers.Count),
            RuleEffectiveness = BuildRuleEffectiveness(evaluated),
            RiskScoreBuckets = BuildBuckets(evaluated),
            RiskPolicySummary = new StrategyLabRiskPolicySummaryDto
            {
                EvaluatedCount = candidates.Count(c =>
                    c.RiskPolicyEligibilityDecision is ResearchRiskPolicyEligibilityDecision.Eligible
                        or ResearchRiskPolicyEligibilityDecision.Ineligible),
                EligibleCount = candidates.Count(c => c.RiskPolicyEligibilityDecision == ResearchRiskPolicyEligibilityDecision.Eligible),
                IneligibleCount = candidates.Count(c => c.RiskPolicyEligibilityDecision == ResearchRiskPolicyEligibilityDecision.Ineligible),
                TopPolicyReasons = candidates
                    .Where(c => c.RiskPolicyEligibilityDecision == ResearchRiskPolicyEligibilityDecision.Ineligible)
                    .GroupBy(c => c.RiskPolicyReason ?? "Unknown")
                    .OrderByDescending(g => g.Count())
                    .Take(5)
                    .Select(g => $"{g.Key} ({g.Count()})")
                    .ToList()
            },
            PortfolioRiskSummary = new StrategyLabPortfolioRiskSummaryDto
            {
                Status = evaluated.Any(c => c.PortfolioRiskAssessmentStatus == PortfolioRiskAssessmentStatus.Evaluated)
                    ? nameof(PortfolioRiskAssessmentStatus.Evaluated)
                    : nameof(PortfolioRiskAssessmentStatus.Unavailable),
                Note = "Chronological dual-shadow portfolios keep positions open until RawExitTimeUtc. Drawdown mode: RealizedOnly. PnL is applied only at exit."
            },
            ExposureAnalytics = new StrategyLabExposureAnalyticsDto
            {
                AverageNotionalExposurePercent = Avg(evaluated
                    .Select(c => c.NotionalExposurePercent ?? c.PositionExposurePercent)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList()),
                MedianNotionalExposurePercent = Median(evaluated
                    .Select(c => c.NotionalExposurePercent ?? c.PositionExposurePercent)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList()),
                AverageMarginUsagePercent = Avg(evaluated
                    .Where(c => c.MarginUsagePercent.HasValue).Select(c => c.MarginUsagePercent!.Value).ToList()),
                MedianMarginUsagePercent = Median(evaluated
                    .Where(c => c.MarginUsagePercent.HasValue).Select(c => c.MarginUsagePercent!.Value).ToList()),
                AverageMinimumRequiredLeverage = Avg(evaluated
                    .Select(c => c.MinimumRequiredLeverage ?? c.ProposedLeverage)
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList()),
                AverageAssessmentLeverage = Avg(evaluated
                    .Where(c => c.AssessmentLeverage.HasValue).Select(c => c.AssessmentLeverage!.Value).ToList()),
                AverageConcurrentRiskPercent = Avg(evaluated
                    .Where(c => c.ConcurrentRiskPercent.HasValue).Select(c => c.ConcurrentRiskPercent!.Value).ToList())
            },
            Diagnostics = diagnostics
        };
    }

    public static StrategyLabRiskProfileComparisonDto Compare(
        StrategyLabRun runA,
        IReadOnlyList<StrategyResearchCandidate> candidatesA,
        StrategyLabRun runB,
        IReadOnlyList<StrategyResearchCandidate> candidatesB)
    {
        var reasons = new List<string>();
        if (!string.Equals(runA.StrategyCode, runB.StrategyCode, StringComparison.OrdinalIgnoreCase))
            reasons.Add("StrategyCode differs.");
        if (!string.Equals(runA.StrategyVersion, runB.StrategyVersion, StringComparison.OrdinalIgnoreCase))
            reasons.Add("StrategyVersion differs.");
        if (runA.ExchangeId != runB.ExchangeId) reasons.Add("Exchange differs.");
        if (runA.SymbolId != runB.SymbolId) reasons.Add("Symbol differs.");
        if (!string.Equals(runA.Timeframe, runB.Timeframe, StringComparison.OrdinalIgnoreCase))
            reasons.Add("Timeframe differs.");
        if (runA.FromUtc != runB.FromUtc || runA.ToUtc != runB.ToUtc)
            reasons.Add("Date range differs.");
        if (!string.Equals(runA.ParametersJson, runB.ParametersJson, StringComparison.Ordinal))
            reasons.Add("Strategy parameters differ.");

        var mapA = candidatesA.ToDictionary(c => c.SetupFingerprint, StringComparer.Ordinal);
        var mapB = candidatesB.ToDictionary(c => c.SetupFingerprint, StringComparer.Ordinal);
        if (mapA.Count != mapB.Count || mapA.Keys.Any(k => !mapB.ContainsKey(k)))
        {
            reasons.Add("Raw candidate fingerprints differ.");
        }
        else
        {
            foreach (var (fp, a) in mapA)
            {
                var b = mapB[fp];
                if (a.Direction != b.Direction
                    || a.ProposedEntryPrice != b.ProposedEntryPrice
                    || a.StopLoss != b.StopLoss
                    || a.Target1 != b.Target1
                    || a.RawOutcomeStatus != b.RawOutcomeStatus
                    || a.RawNetPnl != b.RawNetPnl
                    || a.ConfidenceScore != b.ConfidenceScore)
                {
                    reasons.Add("ControlledComparisonInvalid: raw candidate fields differ for identical fingerprints.");
                    break;
                }
            }
        }

        if (reasons.Count > 0)
        {
            return new StrategyLabRiskProfileComparisonDto
            {
                Comparable = false,
                IncompatibilityReasons = reasons
            };
        }

        var differences = new List<StrategyLabCandidateRiskDecisionDifferenceDto>();
        foreach (var (fp, a) in mapA)
        {
            var b = mapB[fp];
            if (a.RiskDecision != b.RiskDecision
                || a.RiskPolicyEligibilityDecision != b.RiskPolicyEligibilityDecision
                || a.ProposedPositionSize != b.ProposedPositionSize)
            {
                differences.Add(new StrategyLabCandidateRiskDecisionDifferenceDto
                {
                    SetupFingerprint = fp,
                    ProfileADecision = a.RiskDecision?.ToString(),
                    ProfileBDecision = b.RiskDecision?.ToString(),
                    ProfileAFailedRules = a.RiskFailedRuleKeysJson,
                    ProfileBFailedRules = b.RiskFailedRuleKeysJson,
                    ProfileAPositionSize = a.ProposedPositionSize,
                    ProfileBPositionSize = b.ProposedPositionSize,
                    ProfileALeverage = a.ProposedLeverage,
                    ProfileBLeverage = b.ProposedLeverage
                });
            }
        }

        return new StrategyLabRiskProfileComparisonDto
        {
            Comparable = true,
            IncompatibilityReasons = [],
            ProfileA = SummarizeProfile(runA, candidatesA),
            ProfileB = SummarizeProfile(runB, candidatesB),
            CandidateDecisionDifferences = differences,
            Summary =
            [
                $"Compared {mapA.Count} identical raw candidates.",
                $"{differences.Count} candidates had different risk/policy/sizing outcomes."
            ]
        };
    }

    private static StrategyLabRiskProfileSideSummaryDto SummarizeProfile(
        StrategyLabRun run,
        IReadOnlyList<StrategyResearchCandidate> candidates)
    {
        RiskProfileSnapshotDto? snap = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(run.RiskProfileSnapshotJson))
            {
                snap = JsonSerializer.Deserialize<RiskProfileSnapshotDto>(run.RiskProfileSnapshotJson, JsonOptions);
            }
        }
        catch
        {
            // ignored
        }

        return new StrategyLabRiskProfileSideSummaryDto
        {
            RiskProfileId = run.RiskProfileId,
            ProfileName = snap?.RiskProfileName,
            RiskPerTradePercent = snap?.RiskPerTradePercent
                ?? candidates.Select(c => c.RiskPerTradePercent).FirstOrDefault(v => v.HasValue),
            MaxLeverage = snap?.MaxLeverage,
            FinancialRiskApproved = candidates.Count(c => c.RiskDecision == ResearchRiskDecision.Approved),
            FinancialRiskRejected = candidates.Count(c => c.RiskDecision == ResearchRiskDecision.Rejected),
            RiskPolicyRejected = candidates.Count(c =>
                c.RiskPolicyEligibilityDecision == ResearchRiskPolicyEligibilityDecision.Ineligible),
            WinnersApproved = candidates.Count(c =>
                c.RawOutcomeStatus == RawOutcomeStatus.Winner && c.RiskDecision == ResearchRiskDecision.Approved),
            WinnersRejected = candidates.Count(c =>
                c.RawOutcomeStatus == RawOutcomeStatus.Winner && c.RiskDecision == ResearchRiskDecision.Rejected),
            LosersApproved = candidates.Count(c =>
                c.RawOutcomeStatus == RawOutcomeStatus.Loser && c.RiskDecision == ResearchRiskDecision.Approved),
            LosersRejected = candidates.Count(c =>
                c.RawOutcomeStatus == RawOutcomeStatus.Loser && c.RiskDecision == ResearchRiskDecision.Rejected),
            AverageRiskScore = Avg(candidates.Select(c => c.CandidateRiskScore ?? c.RiskScore).Where(s => s.HasValue).Select(s => s!.Value).ToList()),
            AverageRequiredLeverage = Avg(candidates.Where(c => c.ProposedLeverage.HasValue).Select(c => c.ProposedLeverage!.Value).ToList()),
            AverageExposure = Avg(candidates.Where(c => c.PositionExposurePercent.HasValue).Select(c => c.PositionExposurePercent!.Value).ToList())
        };
    }

    private static StrategyLabRejectedRiskSubsetDto BuildRejectedSubset(
        IReadOnlyList<StrategyResearchCandidate> group,
        int outcomeGroupCount)
    {
        var scores = group.Select(c => c.CandidateRiskScore ?? c.RiskScore).Where(s => s.HasValue).Select(s => s!.Value).ToList();
        var reasons = group
            .Select(c => c.RiskRejectedRuleKey ?? c.RiskReason ?? "Unknown")
            .GroupBy(x => x)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => $"{g.Key} ({g.Count()})")
            .ToList();

        return new StrategyLabRejectedRiskSubsetDto
        {
            Count = group.Count,
            PercentageOfOutcomeGroup = outcomeGroupCount <= 0
                ? 0
                : Math.Round((decimal)group.Count / outcomeGroupCount * 100m, 2),
            AverageRiskScore = Avg(scores),
            HypotheticalNetPnl = Math.Round(group.Sum(c => c.RawNetPnl ?? 0m), 4),
            AverageR = Avg(group.Where(c => c.RawRMultiple.HasValue).Select(c => c.RawRMultiple!.Value).ToList()),
            TopRejectionReasons = reasons
        };
    }

    private static IReadOnlyList<StrategyLabRiskRuleEffectivenessDto> BuildRuleEffectiveness(
        IReadOnlyList<StrategyResearchCandidate> evaluated)
    {
        var stats = new Dictionary<string, StrategyLabRiskRuleEffectivenessDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in evaluated)
        {
            foreach (var rule in ParseRules(c.RiskRuleResultsJson))
            {
                if (!string.Equals(rule.Category, nameof(RiskRuleCategory.Financial), StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(rule.Category, nameof(RiskRuleCategory.Portfolio), StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(rule.Category, nameof(RiskRuleCategory.Sizing), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!stats.TryGetValue(rule.RuleKey, out var row))
                {
                    row = new StrategyLabRiskRuleEffectivenessDto { RuleKey = rule.RuleKey, RuleName = rule.RuleName };
                    stats[rule.RuleKey] = row;
                }

                row.EvaluatedCount++;
                if (rule.Status == nameof(RiskRuleResultStatus.Passed)) row.PassedCount++;
                if (rule.Status == nameof(RiskRuleResultStatus.Failed)) row.FailedCount++;
                if (rule.Status == nameof(RiskRuleResultStatus.Warning)) row.WarningCount++;

                if (rule.Status == nameof(RiskRuleResultStatus.Failed)
                    && rule.Severity == nameof(RiskRuleSeverity.HardReject))
                {
                    if (c.RawOutcomeStatus == RawOutcomeStatus.Winner) row.RejectedWinners++;
                    if (c.RawOutcomeStatus == RawOutcomeStatus.Loser) row.RejectedLosers++;
                    row.HypotheticalPnlOfRejected += c.RawNetPnl ?? 0m;
                }
            }
        }

        return stats.Values
            .Select(r =>
            {
                var denom = r.RejectedWinners + r.RejectedLosers;
                return new StrategyLabRiskRuleEffectivenessDto
                {
                    RuleKey = r.RuleKey,
                    RuleName = r.RuleName,
                    EvaluatedCount = r.EvaluatedCount,
                    PassedCount = r.PassedCount,
                    FailedCount = r.FailedCount,
                    WarningCount = r.WarningCount,
                    RejectedWinners = r.RejectedWinners,
                    RejectedLosers = r.RejectedLosers,
                    RejectedWinnerPercent = denom == 0 ? 0 : Math.Round((decimal)r.RejectedWinners / denom * 100m, 2),
                    RejectedLoserPercent = denom == 0 ? 0 : Math.Round((decimal)r.RejectedLosers / denom * 100m, 2),
                    HypotheticalPnlOfRejected = Math.Round(r.HypotheticalPnlOfRejected, 4)
                };
            })
            .OrderByDescending(r => r.FailedCount)
            .ToList();
    }

    private static IReadOnlyList<StrategyLabRiskScoreBucketDto> BuildBuckets(
        IReadOnlyList<StrategyResearchCandidate> evaluated)
    {
        var labels = new[]
        {
            (0m, 9m, "0–9"), (10m, 19m, "10–19"), (20m, 29m, "20–29"), (30m, 39m, "30–39"), (40m, 49m, "40–49"),
            (50m, 59m, "50–59"), (60m, 69m, "60–69"), (70m, 79m, "70–79"), (80m, 89m, "80–89"), (90m, 100m, "90–100")
        };

        return labels.Select(band =>
        {
            var subset = evaluated
                .Where(c =>
                {
                    var s = c.CandidateRiskScore ?? c.RiskScore;
                    return s.HasValue && s.Value >= band.Item1 && s.Value <= band.Item2;
                })
                .ToList();
            var winners = subset.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Winner);
            var losers = subset.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Loser);
            return new StrategyLabRiskScoreBucketDto
            {
                Label = band.Item3,
                CandidateCount = subset.Count,
                WinnerCount = winners,
                LoserCount = losers,
                ExpiredCount = subset.Count(c => c.RawOutcomeStatus == RawOutcomeStatus.Expired),
                WinRate = winners + losers == 0 ? 0 : Math.Round((decimal)winners / (winners + losers) * 100m, 2),
                NetRawPnl = Math.Round(subset.Sum(c => c.RawNetPnl ?? 0m), 4),
                AverageR = Avg(subset.Where(c => c.RawRMultiple.HasValue).Select(c => c.RawRMultiple!.Value).ToList()),
                AverageStopDistancePercent = Avg(subset.Where(c => c.StopDistancePercent.HasValue).Select(c => c.StopDistancePercent!.Value).ToList()),
                AverageLeverage = Avg(subset.Where(c => c.ProposedLeverage.HasValue).Select(c => c.ProposedLeverage!.Value).ToList()),
                AverageExposure = Avg(subset.Where(c => c.PositionExposurePercent.HasValue).Select(c => c.PositionExposurePercent!.Value).ToList())
            };
        }).ToList();
    }

    private static List<RiskRuleResultDto> ParseRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<RiskRuleResultDto>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static decimal? Avg(IReadOnlyList<decimal> values) =>
        values.Count == 0 ? null : Math.Round(values.Average(), 2);

    private static decimal? Median(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0) return null;
        var ordered = values.OrderBy(v => v).ToList();
        var mid = ordered.Count / 2;
        return ordered.Count % 2 == 0
            ? Math.Round((ordered[mid - 1] + ordered[mid]) / 2m, 2)
            : ordered[mid];
    }
}
