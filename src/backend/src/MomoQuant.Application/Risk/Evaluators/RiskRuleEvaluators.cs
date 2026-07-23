using MomoQuant.Application.Common;
using MomoQuant.Application.Trading;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Risk;

namespace MomoQuant.Application.Risk.Evaluators;

internal sealed class EmergencyStopEvaluator : IRiskRuleEvaluator
{
    public string RuleKey => RiskRuleKeys.EmergencyStopEnabled;

    public RiskEvaluationResult? Evaluate(RiskContext context, Models.RiskRuleSet rules)
    {
        if (!rules.EmergencyStopEnabled && !context.EmergencyStopEnabled)
        {
            return null;
        }

        return RiskEvaluationResult.Reject(
            RuleKey,
            "Emergency stop is active. All new trades are blocked.",
            RiskDecisionType.EmergencyBlocked);
    }
}

internal sealed class RequireStopLossEvaluator : IRiskRuleEvaluator
{
    public string RuleKey => RiskRuleKeys.RequireStopLoss;

    public RiskEvaluationResult? Evaluate(RiskContext context, Models.RiskRuleSet rules)
    {
        if (!rules.RequireStopLoss || context.SuggestedStopLoss.HasValue)
        {
            return null;
        }

        return RiskEvaluationResult.Reject(
            RuleKey,
            "Stop loss is required but was not provided.");
    }
}

internal sealed class MinConfidenceEvaluator : IRiskRuleEvaluator
{
    public string RuleKey => RiskRuleKeys.MinConfidenceScore;

    public RiskEvaluationResult? Evaluate(RiskContext context, Models.RiskRuleSet rules)
    {
        var threshold = context.EffectiveMinConfidenceScore > 0m
            ? context.EffectiveMinConfidenceScore
            : rules.MinConfidenceScore;

        if (context.ConfidenceScore >= threshold)
        {
            return null;
        }

        var scoreLabel = string.Equals(context.ConfidenceSource, "Combined", StringComparison.OrdinalIgnoreCase)
            ? "Combined confidence"
            : "Confidence score";

        var diagnosticsJson = TradingPipelineConfidence.BuildConfidenceDiagnosticsJson(
            context.RawConfidenceScore,
            context.ConfidenceScore,
            threshold,
            context.ConfidenceSource ?? "Strategy");

        return RiskEvaluationResult.Reject(
            RuleKey,
            $"{scoreLabel} {ConfidenceScoreNormalizer.Format(context.ConfidenceScore)} is below minimum {ConfidenceScoreNormalizer.Format(threshold)}.",
            rawDataJson: diagnosticsJson);
    }
}

internal sealed class MaxDailyLossEvaluator : IRiskRuleEvaluator
{
    public string RuleKey => RiskRuleKeys.MaxDailyLossPercent;

    public RiskEvaluationResult? Evaluate(RiskContext context, Models.RiskRuleSet rules)
    {
        if (context.AccountBalance <= 0)
        {
            return RiskEvaluationResult.Reject(RuleKey, "Account balance must be greater than zero.");
        }

        var dailyLossPercent = context.DailyPnl < 0
            ? Math.Abs(context.DailyPnl) / context.AccountBalance * 100m
            : 0m;

        if (dailyLossPercent < rules.MaxDailyLossPercent)
        {
            return null;
        }

        return RiskEvaluationResult.Reject(
            RuleKey,
            $"Daily loss {dailyLossPercent:0.##}% has reached or exceeded the limit of {rules.MaxDailyLossPercent:0.##}%.");
    }
}

internal sealed class MaxWeeklyLossEvaluator : IRiskRuleEvaluator
{
    public string RuleKey => RiskRuleKeys.MaxWeeklyLossPercent;

    public RiskEvaluationResult? Evaluate(RiskContext context, Models.RiskRuleSet rules)
    {
        if (context.AccountBalance <= 0)
        {
            return RiskEvaluationResult.Reject(RuleKey, "Account balance must be greater than zero.");
        }

        var weeklyLossPercent = context.WeeklyPnl < 0
            ? Math.Abs(context.WeeklyPnl) / context.AccountBalance * 100m
            : 0m;

        if (weeklyLossPercent < rules.MaxWeeklyLossPercent)
        {
            return null;
        }

        return RiskEvaluationResult.Reject(
            RuleKey,
            $"Weekly loss {weeklyLossPercent:0.##}% has reached or exceeded the limit of {rules.MaxWeeklyLossPercent:0.##}%.");
    }
}

internal sealed class MaxOpenPositionsEvaluator : IRiskRuleEvaluator
{
    public string RuleKey => RiskRuleKeys.MaxOpenPositions;

    public RiskEvaluationResult? Evaluate(RiskContext context, Models.RiskRuleSet rules)
    {
        if (context.OpenPositionCount < rules.MaxOpenPositions)
        {
            return null;
        }

        return RiskEvaluationResult.Reject(
            RuleKey,
            $"Open position count {context.OpenPositionCount} has reached the maximum allowed {rules.MaxOpenPositions}.");
    }
}

internal sealed class ExposureEvaluator : IRiskRuleEvaluator
{
    public string RuleKey => RiskRuleKeys.MaxExposurePerSymbolPercent;

    public RiskEvaluationResult? Evaluate(RiskContext context, Models.RiskRuleSet rules)
    {
        if (context.OpenSymbolExposure >= rules.MaxExposurePerSymbolPercent)
        {
            return RiskEvaluationResult.Reject(
                RiskRuleKeys.MaxExposurePerSymbolPercent,
                $"Symbol exposure {context.OpenSymbolExposure:0.##}% exceeds the limit of {rules.MaxExposurePerSymbolPercent:0.##}%.");
        }

        if (context.TotalExposure >= rules.MaxTotalExposurePercent)
        {
            return RiskEvaluationResult.Reject(
                RiskRuleKeys.MaxTotalExposurePercent,
                $"Total exposure {context.TotalExposure:0.##}% exceeds the limit of {rules.MaxTotalExposurePercent:0.##}%.");
        }

        var correlatedExposure = context.OpenSymbolExposure + context.TotalExposure;
        if (correlatedExposure >= rules.MaxCorrelatedExposurePercent)
        {
            return RiskEvaluationResult.Reject(
                RiskRuleKeys.MaxCorrelatedExposurePercent,
                $"Correlated exposure estimate {correlatedExposure:0.##}% exceeds the limit of {rules.MaxCorrelatedExposurePercent:0.##}%.");
        }

        return null;
    }
}

internal sealed class ConsecutiveLossEvaluator : IRiskRuleEvaluator
{
    public string RuleKey => RiskRuleKeys.MaxConsecutiveLosses;

    public RiskEvaluationResult? Evaluate(RiskContext context, Models.RiskRuleSet rules)
    {
        if (context.ConsecutiveLosses < rules.MaxConsecutiveLosses)
        {
            return null;
        }

        return RiskEvaluationResult.Reject(
            RuleKey,
            $"Consecutive losses {context.ConsecutiveLosses} have reached the limit of {rules.MaxConsecutiveLosses}.");
    }
}

internal sealed class SpreadEvaluator : IRiskRuleEvaluator
{
    public string RuleKey => RiskRuleKeys.MaxSpreadPercent;

    public RiskEvaluationResult? Evaluate(RiskContext context, Models.RiskRuleSet rules)
    {
        if (!context.SpreadPercent.HasValue || context.SpreadPercent.Value <= rules.MaxSpreadPercent)
        {
            return null;
        }

        return RiskEvaluationResult.Reject(
            RuleKey,
            $"Spread {context.SpreadPercent.Value:0.####}% exceeds the maximum allowed {rules.MaxSpreadPercent:0.####}%.");
    }
}

internal sealed class VolatilityEvaluator : IRiskRuleEvaluator
{
    public string RuleKey => RiskRuleKeys.MaxAtrPercent;

    public RiskEvaluationResult? Evaluate(RiskContext context, Models.RiskRuleSet rules)
    {
        if (!context.AtrPercent.HasValue || context.AtrPercent.Value <= rules.MaxAtrPercent)
        {
            return null;
        }

        return RiskEvaluationResult.Reject(
            RuleKey,
            $"ATR percent {context.AtrPercent.Value:0.##}% exceeds the maximum allowed {rules.MaxAtrPercent:0.##}%.");
    }
}

internal sealed class RewardRiskEvaluator : IRiskRuleEvaluator
{
    public string RuleKey => RiskRuleKeys.MinRewardRiskRatio;

    public RiskEvaluationResult? Evaluate(RiskContext context, Models.RiskRuleSet rules)
    {
        if (!context.SuggestedStopLoss.HasValue || !context.SuggestedTakeProfit.HasValue)
        {
            return null;
        }

        var riskPerUnit = Math.Abs(context.EntryPrice - context.SuggestedStopLoss.Value);
        if (riskPerUnit <= 0)
        {
            return RiskEvaluationResult.Reject(RuleKey, "Reward-risk ratio cannot be calculated because stop loss equals entry price.");
        }

        var rewardPerUnit = Math.Abs(context.SuggestedTakeProfit.Value - context.EntryPrice);
        var ratio = rewardPerUnit / riskPerUnit;

        if (ratio >= rules.MinRewardRiskRatio)
        {
            return null;
        }

        return RiskEvaluationResult.Reject(
            RuleKey,
            $"Reward-risk ratio {ratio:0.##} is below the minimum required {rules.MinRewardRiskRatio:0.##}.");
    }
}

internal sealed class MaxRiskPerTradeEvaluator : IRiskRuleEvaluator
{
    public string RuleKey => RiskRuleKeys.MaxRiskPerTradePercent;

    public RiskEvaluationResult? Evaluate(RiskContext context, Models.RiskRuleSet rules)
    {
        if (!context.SuggestedStopLoss.HasValue)
        {
            return null;
        }

        var riskPerUnit = Math.Abs(context.EntryPrice - context.SuggestedStopLoss.Value);
        if (riskPerUnit > 0)
        {
            return null;
        }

        return RiskEvaluationResult.Reject(RuleKey, "Stop loss must be different from entry price.");
    }
}
