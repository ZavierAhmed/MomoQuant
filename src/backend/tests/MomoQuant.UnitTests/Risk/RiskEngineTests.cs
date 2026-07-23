using MomoQuant.Application.Risk.Models;
using MomoQuant.Application.Risk;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Risk;

namespace MomoQuant.UnitTests.Risk;

public class RiskEngineTests
{
    private readonly RiskEngine _engine = new(new PositionSizingService());

    [Fact]
    public void Evaluate_ApprovesTradeWhenAllRulesPass()
    {
        var result = _engine.Evaluate(CreateContext(confidenceScore: 85m));

        Assert.True(result.Approved);
        Assert.Equal(RiskDecisionType.Approved, result.Decision);
        Assert.NotNull(result.PositionSize);
        Assert.True(result.PositionSize > 0);
        Assert.Equal(50m, result.RiskAmount);
        Assert.Equal(0.5m, result.ApprovedRiskPercent);
    }

    [Fact]
    public void Evaluate_RejectsWhenConfidenceBelowMinimum()
    {
        var result = _engine.Evaluate(CreateContext(confidenceScore: 70m));

        Assert.False(result.Approved);
        Assert.Equal(RiskDecisionType.Rejected, result.Decision);
        Assert.Equal(RiskRuleKeys.MinConfidenceScore, result.RejectedRuleKey);
        Assert.Contains("Confidence score", result.Reason);
    }

    [Fact]
    public void Evaluate_RejectsWhenDailyLossLimitReached()
    {
        var result = _engine.Evaluate(CreateContext(dailyPnl: -200m));

        Assert.Equal(RiskRuleKeys.MaxDailyLossPercent, result.RejectedRuleKey);
        Assert.Contains("Daily loss", result.Reason);
    }

    [Fact]
    public void Evaluate_RejectsWhenWeeklyLossLimitReached()
    {
        var result = _engine.Evaluate(CreateContext(weeklyPnl: -500m));

        Assert.Equal(RiskRuleKeys.MaxWeeklyLossPercent, result.RejectedRuleKey);
        Assert.Contains("Weekly loss", result.Reason);
    }

    [Fact]
    public void Evaluate_RejectsWhenMaxOpenPositionsReached()
    {
        var result = _engine.Evaluate(CreateContext(openPositionCount: 2));

        Assert.Equal(RiskRuleKeys.MaxOpenPositions, result.RejectedRuleKey);
        Assert.Contains("Open position count", result.Reason);
    }

    [Fact]
    public void Evaluate_RejectsWhenEmergencyStopActive()
    {
        var result = _engine.Evaluate(CreateContext(emergencyStopEnabled: true));

        Assert.Equal(RiskDecisionType.EmergencyBlocked, result.Decision);
        Assert.Equal(RiskRuleKeys.EmergencyStopEnabled, result.RejectedRuleKey);
        Assert.Contains("Emergency stop", result.Reason);
    }

    [Fact]
    public void Evaluate_RejectsWhenStopLossMissingAndRequired()
    {
        var result = _engine.Evaluate(CreateContext(suggestedStopLoss: null));

        Assert.Equal(RiskRuleKeys.RequireStopLoss, result.RejectedRuleKey);
        Assert.Contains("Stop loss is required", result.Reason);
    }

    [Fact]
    public void Evaluate_RejectsWhenRewardRiskRatioTooLow()
    {
        var result = _engine.Evaluate(CreateContext(suggestedTakeProfit: 65100m));

        Assert.Equal(RiskRuleKeys.MinRewardRiskRatio, result.RejectedRuleKey);
        Assert.Contains("Reward-risk ratio", result.Reason);
    }

    [Fact]
    public void Evaluate_RejectsWhenSpreadTooHigh()
    {
        var result = _engine.Evaluate(CreateContext(spreadPercent: 0.10m));

        Assert.Equal(RiskRuleKeys.MaxSpreadPercent, result.RejectedRuleKey);
        Assert.Contains("Spread", result.Reason);
    }

    [Fact]
    public void Evaluate_RejectsWhenAtrPercentTooHigh()
    {
        var result = _engine.Evaluate(CreateContext(atrPercent: 3.0m));

        Assert.Equal(RiskRuleKeys.MaxAtrPercent, result.RejectedRuleKey);
        Assert.Contains("ATR percent", result.Reason);
    }

    [Fact]
    public void Evaluate_CalculatesPositionSizeCorrectly()
    {
        var result = _engine.Evaluate(CreateContext());

        Assert.Equal(0.1m, result.PositionSize);
        Assert.Equal(50m, result.RiskAmount);
    }

    [Fact]
    public void Evaluate_RejectsWhenConsecutiveLossesReached()
    {
        var result = _engine.Evaluate(CreateContext(consecutiveLosses: 3));

        Assert.Equal(RiskRuleKeys.MaxConsecutiveLosses, result.RejectedRuleKey);
        Assert.Contains("Consecutive losses", result.Reason);
    }

    [Fact]
    public void Evaluate_ReturnsHumanReadableReasonAndRejectedRuleKey()
    {
        var result = _engine.Evaluate(CreateContext(confidenceScore: 70m));

        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        Assert.Equal(RiskRuleKeys.MinConfidenceScore, result.RejectedRuleKey);
    }

    [Fact]
    public void Evaluate_IsPureAndDoesNotPlaceOrders()
    {
        var context = CreateContext();
        var first = _engine.Evaluate(context);
        var second = _engine.Evaluate(context);

        Assert.Equal(first.Decision, second.Decision);
        Assert.Equal(first.PositionSize, second.PositionSize);
    }

    private static RiskContext CreateContext(
        decimal confidenceScore = 85m,
        decimal dailyPnl = 0m,
        decimal weeklyPnl = 0m,
        int openPositionCount = 0,
        bool emergencyStopEnabled = false,
        decimal? suggestedStopLoss = 64500m,
        decimal? suggestedTakeProfit = 66000m,
        decimal? spreadPercent = 0.01m,
        decimal? atrPercent = 1.2m,
        int consecutiveLosses = 0) => new()
    {
        SymbolId = 1,
        Symbol = "BTCUSDT",
        Direction = TradeDirection.Long,
        EntryPrice = 65000m,
        SuggestedStopLoss = suggestedStopLoss,
        SuggestedTakeProfit = suggestedTakeProfit,
        ConfidenceScore = confidenceScore,
        RawConfidenceScore = confidenceScore,
        EffectiveMinConfidenceScore = 80m,
        ConfidenceSource = "Strategy",
        AccountBalance = 10000m,
        DailyPnl = dailyPnl,
        WeeklyPnl = weeklyPnl,
        OpenPositionCount = openPositionCount,
        OpenSymbolExposure = 0m,
        TotalExposure = 0m,
        ConsecutiveLosses = consecutiveLosses,
        SpreadPercent = spreadPercent,
        AtrPercent = atrPercent,
        EmergencyStopEnabled = emergencyStopEnabled,
        Rules = CreateBalancedRules(),
        EvaluationTimeUtc = DateTime.UtcNow
    };

    private static IReadOnlyList<RiskRule> CreateBalancedRules() =>
    [
        CreateRule(RiskRuleKeys.MaxRiskPerTradePercent, "0.5", SettingValueType.Decimal),
        CreateRule(RiskRuleKeys.MaxDailyLossPercent, "2", SettingValueType.Decimal),
        CreateRule(RiskRuleKeys.MaxWeeklyLossPercent, "5", SettingValueType.Decimal),
        CreateRule(RiskRuleKeys.MaxOpenPositions, "2", SettingValueType.Int),
        CreateRule(RiskRuleKeys.MaxExposurePerSymbolPercent, "25", SettingValueType.Decimal),
        CreateRule(RiskRuleKeys.MaxTotalExposurePercent, "50", SettingValueType.Decimal),
        CreateRule(RiskRuleKeys.MaxCorrelatedExposurePercent, "50", SettingValueType.Decimal),
        CreateRule(RiskRuleKeys.MaxConsecutiveLosses, "3", SettingValueType.Int),
        CreateRule(RiskRuleKeys.MinConfidenceScore, "80", SettingValueType.Decimal),
        CreateRule(RiskRuleKeys.MaxSpreadPercent, "0.05", SettingValueType.Decimal),
        CreateRule(RiskRuleKeys.MaxAtrPercent, "2.5", SettingValueType.Decimal),
        CreateRule(RiskRuleKeys.EmergencyStopEnabled, "false", SettingValueType.Bool),
        CreateRule(RiskRuleKeys.RequireStopLoss, "true", SettingValueType.Bool),
        CreateRule(RiskRuleKeys.MinRewardRiskRatio, "1.2", SettingValueType.Decimal)
    ];

    private static RiskRule CreateRule(string key, string value, SettingValueType valueType) => new()
    {
        RuleKey = key,
        RuleValue = value,
        ValueType = valueType,
        IsEnabled = true,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };
}

public class PositionSizingServiceTests
{
    [Fact]
    public void Calculate_UsesMaxRiskPerTradePercent()
    {
        var service = new PositionSizingService();
        var context = new RiskContext
        {
            SymbolId = 1,
            Direction = TradeDirection.Long,
            EntryPrice = 100m,
            SuggestedStopLoss = 95m,
            SuggestedTakeProfit = 110m,
            ConfidenceScore = 90m,
            AccountBalance = 10000m,
            DailyPnl = 0m,
            WeeklyPnl = 0m,
            OpenPositionCount = 0,
            OpenSymbolExposure = 0m,
            TotalExposure = 0m,
            ConsecutiveLosses = 0,
            EmergencyStopEnabled = false,
            Rules = [],
            EvaluationTimeUtc = DateTime.UtcNow
        };

        var rules = RiskRuleSet.FromRules(
        [
            new RiskRule
            {
                RuleKey = RiskRuleKeys.MaxRiskPerTradePercent,
                RuleValue = "0.5",
                ValueType = SettingValueType.Decimal,
                IsEnabled = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            },
            new RiskRule
            {
                RuleKey = RiskRuleKeys.RequireStopLoss,
                RuleValue = "true",
                ValueType = SettingValueType.Bool,
                IsEnabled = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            }
        ]);

        var result = service.Calculate(context, rules);

        Assert.True(result.Succeeded);
        Assert.Equal(10m, result.PositionSize);
        Assert.Equal(50m, result.RiskAmount);
    }
}
