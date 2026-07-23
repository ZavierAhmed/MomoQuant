using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.StrategyLab.Risk;

/// <summary>
/// CandidateRiskQuality/v1.1 — uses explicit futures quantities and only enabled limits.
/// Notional exposure above 100% does not automatically zero Exposure Impact.
/// </summary>
public sealed class CandidateRiskQualityScorer
{
    public const string ModelVersion = RiskObservationVersions.CandidateRiskModel;

    public CandidateRiskQualityResult Score(
        StrategyResearchCandidate candidate,
        PositionSizingObservation sizing,
        RiskProfileSnapshotDto snapshot,
        IReadOnlyList<RiskRuleResultDto> financialRules,
        ChronologicalShadowPortfolio? shadow)
    {
        var components = new List<CandidateRiskScoreComponent>
        {
            ScoreStopGeometry(candidate, sizing, max: 20m),
            ScorePositionSizing(sizing, max: 20m),
            ScoreLeverage(sizing, snapshot.MaxLeverage, max: 15m),
            ScoreExposure(sizing, snapshot, shadow, max: 15m),
            ScoreCostEfficiency(sizing, max: 10m),
            ScoreRewardRisk(candidate.RewardRisk, snapshot.MinimumRewardRisk, max: 5m),
            ScoreSafetyMargin(sizing, snapshot, financialRules, shadow, max: 15m)
        };

        var total = Math.Clamp(Math.Round(components.Sum(c => c.Score), 2), 0m, 100m);
        var rounded = components.Select(c => c with { Score = Math.Round(c.Score, 2) }).ToList();
        var sum = rounded.Sum(c => c.Score);
        if (rounded.Count > 0 && sum != total)
        {
            var last = rounded[^1];
            rounded[^1] = last with { Score = Math.Clamp(last.Score + (total - sum), 0m, last.Max) };
        }

        return new CandidateRiskQualityResult
        {
            Score = total,
            ModelVersion = ModelVersion,
            Components = rounded,
            Explanation = $"Candidate financial risk quality {total:0.##}/100 under profile '{snapshot.RiskProfileName}' ({ModelVersion})."
        };
    }

    private static CandidateRiskScoreComponent ScoreStopGeometry(
        StrategyResearchCandidate candidate,
        PositionSizingObservation sizing,
        decimal max)
    {
        if (sizing.UnavailableReason is not null || !sizing.StopDistancePercent.HasValue)
        {
            return Comp("stopGeometry", "Stop Geometry Quality", 4m, max, sizing.UnavailableReason ?? "Stop geometry unavailable.");
        }

        var pct = sizing.StopDistancePercent.Value;
        decimal score;
        string reason;
        if (pct < 0.05m)
        {
            score = max * 0.25m;
            reason = $"Extremely tight stop ({pct:0.####}%).";
        }
        else if (pct < 0.15m)
        {
            score = max * 0.55m;
            reason = $"Tight stop ({pct:0.####}%).";
        }
        else if (pct <= 1.5m)
        {
            score = max * (0.85m + Clamp01((1.5m - pct) / 1.35m) * 0.15m);
            reason = $"Stop distance {pct:0.####}% within preferred band.";
        }
        else if (pct <= 3.5m)
        {
            score = max * (0.45m + (1m - Clamp01((pct - 1.5m) / 2m)) * 0.3m);
            reason = $"Wide stop ({pct:0.####}%).";
        }
        else
        {
            score = max * 0.2m;
            reason = $"Excessively wide stop ({pct:0.####}%).";
        }

        if (candidate.Direction == TradeDirection.Long && candidate.StopLoss >= candidate.ProposedEntryPrice)
        {
            score = 0m;
            reason = "Invalid long stop geometry.";
        }
        else if (candidate.Direction == TradeDirection.Short && candidate.StopLoss <= candidate.ProposedEntryPrice)
        {
            score = 0m;
            reason = "Invalid short stop geometry.";
        }

        return Comp("stopGeometry", "Stop Geometry Quality", score, max, reason);
    }

    private static CandidateRiskScoreComponent ScorePositionSizing(PositionSizingObservation sizing, decimal max)
    {
        if (sizing.Quantity is null || sizing.Quantity <= 0)
        {
            return Comp("positionSizing", "Position Sizing Feasibility", 2m, max,
                sizing.UnavailableReason ?? "Position size unavailable.");
        }

        var score = max * 0.75m;
        if (sizing.RiskAmount > 0 && sizing.PositionNotional > 0)
        {
            score = max * 0.9m;
        }

        return Comp("positionSizing", "Position Sizing Feasibility", score, max,
            $"Qty={sizing.Quantity:0.########} riskAmount={sizing.RiskAmount:0.####}.");
    }

    private static CandidateRiskScoreComponent ScoreLeverage(
        PositionSizingObservation sizing,
        decimal maxLeverage,
        decimal max)
    {
        if (!sizing.MinimumRequiredLeverage.HasValue || maxLeverage <= 0)
        {
            return Comp("leverage", "Leverage Requirement", max * 0.4m, max, "Leverage not available.");
        }

        var required = sizing.MinimumRequiredLeverage.Value;
        if (required > maxLeverage)
        {
            return Comp("leverage", "Leverage Requirement", 0m, max,
                $"Minimum required {required:0.##}x exceeds max {maxLeverage:0.##}x.");
        }

        var headroom = 1m - Clamp01(required / maxLeverage);
        var score = max * (0.35m + headroom * 0.65m);
        var assess = sizing.AssessmentLeverage.HasValue ? $" assessment={sizing.AssessmentLeverage:0.##}x" : string.Empty;
        return Comp("leverage", "Leverage Requirement", score, max,
            $"Min required {required:0.##}x / max {maxLeverage:0.##}x{assess}.");
    }

    private static CandidateRiskScoreComponent ScoreExposure(
        PositionSizingObservation sizing,
        RiskProfileSnapshotDto snapshot,
        ChronologicalShadowPortfolio? shadow,
        decimal max)
    {
        var parts = new List<(decimal ScorePart, string Reason)>();

        if (snapshot.MaxNotionalExposurePerSymbolPercent is { } notionalLimit)
        {
            if (!sizing.NotionalExposurePercent.HasValue)
            {
                parts.Add((0.35m, "Notional exposure unavailable."));
            }
            else
            {
                var usage = sizing.NotionalExposurePercent.Value / notionalLimit;
                parts.Add((usage >= 1m ? 0m : 1m - Clamp01(usage) * 0.7m,
                    $"Notional {sizing.NotionalExposurePercent:0.##}% / limit {notionalLimit:0.##}%."));
            }
        }

        if (snapshot.MaxMarginUsagePerSymbolPercent is { } marginLimit)
        {
            if (!sizing.MarginUsagePercent.HasValue)
            {
                parts.Add((0.35m, "Margin usage unavailable."));
            }
            else
            {
                var usage = sizing.MarginUsagePercent.Value / marginLimit;
                parts.Add((usage >= 1m ? 0m : 1m - Clamp01(usage) * 0.7m,
                    $"Margin {sizing.MarginUsagePercent:0.##}% / limit {marginLimit:0.##}%."));
            }
        }

        if (snapshot.MaxConcurrentRiskPercent is { } riskLimit && shadow is not null)
        {
            var projected = (shadow.ConcurrentRiskPercent ?? 0m)
                + (sizing.RiskAmount > 0 && shadow.CurrentBalance > 0
                    ? sizing.RiskAmount / shadow.CurrentBalance * 100m
                    : 0m);
            var usage = projected / riskLimit;
            parts.Add((usage >= 1m ? 0m : 1m - Clamp01(usage) * 0.7m,
                $"Projected concurrent risk {projected:0.##}% / limit {riskLimit:0.##}%."));
        }

        if (parts.Count == 0)
        {
            // No exposure limits enabled — do not penalize notional > 100%.
            var notional = sizing.NotionalExposurePercent;
            var margin = sizing.MarginUsagePercent;
            return Comp("exposure", "Exposure Impact", max * 0.85m, max,
                $"No explicit exposure limits enabled. Notional={notional:0.##}% Margin={margin:0.##}%.");
        }

        var avg = parts.Average(p => p.ScorePart);
        var reason = string.Join(" ", parts.Select(p => p.Reason));
        return Comp("exposure", "Exposure Impact", avg * max, max, reason);
    }

    private static CandidateRiskScoreComponent ScoreCostEfficiency(PositionSizingObservation sizing, decimal max)
    {
        if (!sizing.FeeToTargetPercent.HasValue || sizing.TargetGrossProfit <= 0)
        {
            return Comp("costEfficiency", "Cost Efficiency", max * 0.4m, max, "Fee/target unavailable.");
        }

        var feePct = sizing.FeeToTargetPercent.Value;
        var score = feePct >= 100m
            ? 0m
            : max * (1m - Clamp01(feePct / 60m));
        return Comp("costEfficiency", "Cost Efficiency", score, max,
            $"Fees are {feePct:0.##}% of target gross profit.");
    }

    private static CandidateRiskScoreComponent ScoreRewardRisk(decimal rr, decimal minRr, decimal max)
    {
        if (minRr <= 0)
        {
            minRr = 1m;
        }

        if (rr < minRr)
        {
            return Comp("rewardRisk", "Reward/Risk Validity", max * 0.2m, max,
                $"R:R {rr:0.##} below minimum {minRr:0.##}.");
        }

        var headroom = Clamp01((rr - minRr) / Math.Max(minRr, 0.5m));
        return Comp("rewardRisk", "Reward/Risk Validity", max * (0.55m + headroom * 0.45m), max,
            $"R:R {rr:0.##} vs min {minRr:0.##}.");
    }

    private static CandidateRiskScoreComponent ScoreSafetyMargin(
        PositionSizingObservation sizing,
        RiskProfileSnapshotDto snapshot,
        IReadOnlyList<RiskRuleResultDto> financialRules,
        ChronologicalShadowPortfolio? shadow,
        decimal max)
    {
        var parts = new List<decimal>();
        if (sizing.MinimumRequiredLeverage.HasValue && snapshot.MaxLeverage > 0)
        {
            parts.Add(1m - Clamp01(sizing.MinimumRequiredLeverage.Value / snapshot.MaxLeverage));
        }

        if (snapshot.MaxNotionalExposurePerSymbolPercent is { } nLim && sizing.NotionalExposurePercent.HasValue)
        {
            parts.Add(1m - Clamp01(sizing.NotionalExposurePercent.Value / nLim));
        }

        if (snapshot.MaxMarginUsagePerSymbolPercent is { } mLim && sizing.MarginUsagePercent.HasValue)
        {
            parts.Add(1m - Clamp01(sizing.MarginUsagePercent.Value / mLim));
        }

        if (snapshot.MaxConcurrentRiskPercent is { } cLim && shadow?.ConcurrentRiskPercent is { } cRisk)
        {
            parts.Add(1m - Clamp01(cRisk / cLim));
        }

        if (shadow?.CurrentDrawdownPercent is { } dd && snapshot.MaxDrawdownPercent > 0)
        {
            parts.Add(1m - Clamp01(dd / snapshot.MaxDrawdownPercent));
        }

        // Disabled rules must not reduce score — only enabled applicable headroom above.
        var failedHard = financialRules.Count(r =>
            r.Status == nameof(RiskRuleResultStatus.Failed)
            && r.Severity == nameof(RiskRuleSeverity.HardReject));
        if (failedHard > 0)
        {
            parts.Add(0m);
        }

        var avg = parts.Count == 0 ? 0.5m : parts.Average();
        return Comp("safetyMargin", "Financial Safety Margin", avg * max, max,
            $"Enabled-limit headroom average {avg:0.##}; hardRejects={failedHard}.");
    }

    private static CandidateRiskScoreComponent Comp(string key, string label, decimal score, decimal max, string reason) =>
        new()
        {
            Key = key,
            Label = label,
            Score = Math.Clamp(score, 0m, max),
            Max = max,
            Reason = reason
        };

    private static decimal Clamp01(decimal value) => Math.Clamp(value, 0m, 1m);
}
