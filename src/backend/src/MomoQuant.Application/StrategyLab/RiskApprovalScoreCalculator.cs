using MomoQuant.Application.Risk.Models;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Risk;

namespace MomoQuant.Application.StrategyLab;

/// <summary>
/// Deterministic observational RiskApprovalScore (0–100).
/// Higher = more comfortably within configured risk limits.
/// Does not change the risk engine decision.
///
/// Components (20 points each when rules configured, otherwise redistributed):
/// 1. Confidence headroom vs MinConfidence
/// 2. Reward:Risk headroom vs MinRewardRisk
/// 3. Per-trade risk headroom vs MaxRiskPerTradePercent
/// 4. Daily loss usage headroom vs MaxDailyLossPercent
/// 5. Open position / exposure headroom
/// </summary>
public static class RiskApprovalScoreCalculator
{
    public const decimal DefaultPassThreshold = 50m;

    public sealed class Result
    {
        public decimal Score { get; init; }
        public decimal Threshold { get; init; } = DefaultPassThreshold;
        public decimal Margin => Score - Threshold;
        public decimal? RiskPerTradePercent { get; init; }
        public decimal? ProposedPositionSize { get; init; }
        public decimal? ProposedLeverage { get; init; }
        public decimal? StopDistancePercent { get; init; }
        public decimal? CurrentExposurePercent { get; init; }
        public decimal? DailyLossUsagePercent { get; init; }
        public decimal? CurrentDrawdownPercent { get; init; }
        public string? RejectedRuleKey { get; init; }
        public IReadOnlyDictionary<string, decimal> ComponentScores { get; init; } = new Dictionary<string, decimal>();
    }

    public static Result Calculate(
        RiskContext context,
        RiskEvaluationResult evaluation,
        decimal rewardRisk)
    {
        var rules = RiskRuleSet.FromRules(context.Rules);
        var stopDistancePercent = ComputeStopDistancePercent(context.EntryPrice, context.SuggestedStopLoss, context.Direction);
        var riskPerTradePercent = evaluation.ApprovedRiskPercent
            ?? (rules.MaxRiskPerTradePercent > 0 ? rules.MaxRiskPerTradePercent : (decimal?)null);
        var exposurePercent = context.AccountBalance > 0
            ? context.TotalExposure / context.AccountBalance * 100m
            : 0m;
        var dailyLossUsage = context.AccountBalance > 0 && context.DailyPnl < 0
            ? Math.Abs(context.DailyPnl) / context.AccountBalance * 100m
            : 0m;

        var components = new Dictionary<string, decimal>(StringComparer.Ordinal);

        // Confidence headroom
        components["ConfidenceHeadroom"] = ScoreHeadroom(
            context.ConfidenceScore,
            rules.MinConfidenceScore,
            higherIsBetter: true,
            softBand: 20m);

        // Reward:Risk headroom
        var minRr = rules.MinRewardRiskRatio;
        components["RewardRiskHeadroom"] = ScoreHeadroom(rewardRisk, minRr, higherIsBetter: true, softBand: 1m);

        // Per-trade risk (lower usage is better when max is set)
        var maxRisk = rules.MaxRiskPerTradePercent > 0 ? rules.MaxRiskPerTradePercent : 100m;
        var estimatedRiskPct = stopDistancePercent.HasValue
            ? Math.Min(riskPerTradePercent ?? maxRisk, maxRisk)
            : riskPerTradePercent ?? 0m;
        components["PerTradeRiskHeadroom"] = ScoreHeadroom(estimatedRiskPct, maxRisk, higherIsBetter: false, softBand: maxRisk);

        // Daily loss usage
        var maxDaily = rules.MaxDailyLossPercent > 0 ? rules.MaxDailyLossPercent : 100m;
        components["DailyLossHeadroom"] = ScoreHeadroom(dailyLossUsage, maxDaily, higherIsBetter: false, softBand: maxDaily);

        // Open positions / exposure
        var maxOpen = rules.MaxOpenPositions > 0 ? rules.MaxOpenPositions : int.MaxValue;
        var positionUsage = maxOpen == int.MaxValue || maxOpen <= 0
            ? 0m
            : (decimal)context.OpenPositionCount / maxOpen * 100m;
        var maxExposure = rules.MaxTotalExposurePercent > 0 ? rules.MaxTotalExposurePercent : 100m;
        var exposureScore = ScoreHeadroom(exposurePercent, maxExposure, higherIsBetter: false, softBand: maxExposure);
        var positionScore = ScoreHeadroom(positionUsage, 100m, higherIsBetter: false, softBand: 100m);
        components["CapacityHeadroom"] = Math.Round((exposureScore + positionScore) / 2m, 2);

        var score = Math.Round(components.Values.Average(), 2);
        if (evaluation.Decision == RiskDecisionType.Rejected)
        {
            // Cap rejected scores below observational threshold while preserving relative headroom.
            score = Math.Min(score, DefaultPassThreshold - 1m);
        }

        decimal? proposedLeverage = context.AccountBalance > 0 && evaluation.PositionSize.HasValue && context.EntryPrice > 0
            ? evaluation.PositionSize.Value * context.EntryPrice / context.AccountBalance
            : null;

        return new Result
        {
            Score = Math.Clamp(score, 0m, 100m),
            Threshold = DefaultPassThreshold,
            RiskPerTradePercent = riskPerTradePercent,
            ProposedPositionSize = evaluation.PositionSize,
            ProposedLeverage = proposedLeverage.HasValue ? Math.Round(proposedLeverage.Value, 4) : null,
            StopDistancePercent = stopDistancePercent,
            CurrentExposurePercent = Math.Round(exposurePercent, 4),
            DailyLossUsagePercent = Math.Round(dailyLossUsage, 4),
            CurrentDrawdownPercent = null,
            RejectedRuleKey = evaluation.RejectedRuleKey,
            ComponentScores = components
        };
    }

    public static decimal? ComputeStopDistancePercent(decimal entry, decimal? stop, TradeDirection direction)
    {
        if (!stop.HasValue || entry <= 0)
        {
            return null;
        }

        var distance = direction == TradeDirection.Long
            ? entry - stop.Value
            : stop.Value - entry;
        if (distance <= 0)
        {
            return null;
        }

        return Math.Round(distance / entry * 100m, 4);
    }

    private static decimal ScoreHeadroom(decimal value, decimal limit, bool higherIsBetter, decimal softBand)
    {
        if (limit <= 0 && higherIsBetter)
        {
            return 100m;
        }

        if (!higherIsBetter && limit >= 100m && value <= 0)
        {
            return 100m;
        }

        if (higherIsBetter)
        {
            if (value >= limit + softBand)
            {
                return 100m;
            }

            if (value <= limit)
            {
                var deficit = limit - value;
                return Math.Clamp(50m - deficit / Math.Max(softBand, 0.0001m) * 50m, 0m, 49m);
            }

            return Math.Clamp(50m + (value - limit) / Math.Max(softBand, 0.0001m) * 50m, 50m, 100m);
        }

        // lower is better vs upper limit
        if (value <= 0)
        {
            return 100m;
        }

        if (value >= limit)
        {
            var over = value - limit;
            return Math.Clamp(49m - over / Math.Max(softBand, 0.0001m) * 49m, 0m, 49m);
        }

        var usage = value / Math.Max(limit, 0.0001m);
        return Math.Clamp(100m - usage * 50m, 50m, 100m);
    }
}
