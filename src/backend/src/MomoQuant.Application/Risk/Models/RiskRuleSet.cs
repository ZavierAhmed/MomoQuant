using System.Globalization;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Risk;

namespace MomoQuant.Application.Risk.Models;

public sealed class RiskRuleSet
{
    public decimal MaxRiskPerTradePercent { get; init; }
    public decimal MaxDailyLossPercent { get; init; }
    public decimal MaxWeeklyLossPercent { get; init; }
    public int MaxOpenPositions { get; init; }
    public decimal MaxExposurePerSymbolPercent { get; init; }
    public decimal MaxTotalExposurePercent { get; init; }
    public decimal MaxCorrelatedExposurePercent { get; init; }
    public int MaxConsecutiveLosses { get; init; }
    public decimal MinConfidenceScore { get; init; }
    public decimal MaxSpreadPercent { get; init; }
    public decimal MaxAtrPercent { get; init; }
    public bool EmergencyStopEnabled { get; init; }
    public bool RequireStopLoss { get; init; }
    public decimal MinRewardRiskRatio { get; init; }

    public static RiskRuleSet FromRules(IReadOnlyList<RiskRule> rules) =>
        new()
        {
            MaxRiskPerTradePercent = GetDecimal(rules, RiskRuleKeys.MaxRiskPerTradePercent, 0.5m),
            MaxDailyLossPercent = GetDecimal(rules, RiskRuleKeys.MaxDailyLossPercent, 2m),
            MaxWeeklyLossPercent = GetDecimal(rules, RiskRuleKeys.MaxWeeklyLossPercent, 5m),
            MaxOpenPositions = GetInt(rules, RiskRuleKeys.MaxOpenPositions, 2),
            MaxExposurePerSymbolPercent = GetDecimal(rules, RiskRuleKeys.MaxExposurePerSymbolPercent, 25m),
            MaxTotalExposurePercent = GetDecimal(rules, RiskRuleKeys.MaxTotalExposurePercent, 50m),
            MaxCorrelatedExposurePercent = GetDecimal(rules, RiskRuleKeys.MaxCorrelatedExposurePercent, 50m),
            MaxConsecutiveLosses = GetInt(rules, RiskRuleKeys.MaxConsecutiveLosses, 3),
            MinConfidenceScore = GetDecimal(rules, RiskRuleKeys.MinConfidenceScore, 80m),
            MaxSpreadPercent = GetDecimal(rules, RiskRuleKeys.MaxSpreadPercent, 0.05m),
            MaxAtrPercent = GetDecimal(rules, RiskRuleKeys.MaxAtrPercent, 2.5m),
            EmergencyStopEnabled = GetBool(rules, RiskRuleKeys.EmergencyStopEnabled, false),
            RequireStopLoss = GetBool(rules, RiskRuleKeys.RequireStopLoss, true),
            MinRewardRiskRatio = GetDecimal(rules, RiskRuleKeys.MinRewardRiskRatio, 1.2m)
        };

    private static bool IsEnabled(IReadOnlyList<RiskRule> rules, string key) =>
        rules.FirstOrDefault(rule => rule.RuleKey == key && rule.IsEnabled) is not null;

    private static string GetValue(IReadOnlyList<RiskRule> rules, string key, string defaultValue)
    {
        var rule = rules.FirstOrDefault(item => item.RuleKey == key && item.IsEnabled);
        return rule?.RuleValue ?? defaultValue;
    }

    private static decimal GetDecimal(IReadOnlyList<RiskRule> rules, string key, decimal defaultValue)
    {
        if (!IsEnabled(rules, key))
        {
            return defaultValue;
        }

        return decimal.TryParse(GetValue(rules, key, defaultValue.ToString(CultureInfo.InvariantCulture)), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    }

    private static int GetInt(IReadOnlyList<RiskRule> rules, string key, int defaultValue)
    {
        if (!IsEnabled(rules, key))
        {
            return defaultValue;
        }

        return int.TryParse(GetValue(rules, key, defaultValue.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    }

    private static bool GetBool(IReadOnlyList<RiskRule> rules, string key, bool defaultValue)
    {
        if (!IsEnabled(rules, key))
        {
            return defaultValue;
        }

        return bool.TryParse(GetValue(rules, key, defaultValue.ToString()), out var value)
            ? value
            : defaultValue;
    }
}
