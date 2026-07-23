using MomoQuant.Application.Common;
using MomoQuant.Application.Risk;
using MomoQuant.Application.Risk.Models;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Application.Trading;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Risk;

namespace MomoQuant.UnitTests.Trading;

public class TradingPipelineConfidenceTests
{
    [Fact]
    public void ResolveEffectiveMinimum_UsesMaxOfSessionAndProfile()
    {
        var rules = new[]
        {
            CreateRule(RiskRuleKeys.MinConfidenceScore, "50")
        };

        var effective = TradingPipelineConfidence.ResolveEffectiveMinimum(40m, rules);
        Assert.Equal(50m, effective);
    }

    [Fact]
    public void ResolveEffectiveMinimum_UsesProfileWhenSessionIsZero()
    {
        var rules = new[]
        {
            CreateRule(RiskRuleKeys.MinConfidenceScore, "80")
        };

        var effective = TradingPipelineConfidence.ResolveEffectiveMinimum(0m, rules);
        Assert.Equal(80m, effective);
    }

    [Fact]
    public void ShouldEvaluateRisk_IgnoresNoTradeSignals()
    {
        var noTrade = new StrategyEvaluationResult
        {
            StrategyCode = "EMA",
            StrategyName = "EMA",
            Evaluated = true,
            Skipped = true,
            SkipReason = "No setup",
            SignalType = SignalType.NoTrade,
            Direction = TradeDirection.None,
            Strength = 0m,
            ConfidenceContribution = 0m,
            Reason = "No setup",
            IsValid = true
        };

        Assert.False(TradingPipelineConfidence.ShouldEvaluateRisk(noTrade));
    }

    [Fact]
    public void ShouldEvaluateRisk_AllowsValidEntrySignals()
    {
        var entry = new StrategyEvaluationResult
        {
            StrategyCode = "EMA",
            StrategyName = "EMA",
            Evaluated = true,
            Skipped = false,
            SignalType = SignalType.Entry,
            Direction = TradeDirection.Long,
            Strength = 75m,
            ConfidenceContribution = 75m,
            EntryPrice = 100m,
            Reason = "Entry setup",
            IsValid = true
        };

        Assert.True(TradingPipelineConfidence.ShouldEvaluateRisk(entry));
    }

    [Fact]
    public void RiskEngine_ApprovesNormalizedConfidence()
    {
        var engine = new RiskEngine(new PositionSizingService());
        var rules = new[]
        {
            CreateRule(RiskRuleKeys.MaxRiskPerTradePercent, "0.5"),
            CreateRule(RiskRuleKeys.MaxDailyLossPercent, "2"),
            CreateRule(RiskRuleKeys.MaxWeeklyLossPercent, "5"),
            CreateRule(RiskRuleKeys.MaxOpenPositions, "2"),
            CreateRule(RiskRuleKeys.MaxExposurePerSymbolPercent, "25"),
            CreateRule(RiskRuleKeys.MaxTotalExposurePercent, "50"),
            CreateRule(RiskRuleKeys.MaxCorrelatedExposurePercent, "50"),
            CreateRule(RiskRuleKeys.MaxConsecutiveLosses, "3"),
            CreateRule(RiskRuleKeys.MinConfidenceScore, "50"),
            CreateRule(RiskRuleKeys.MaxSpreadPercent, "0.05"),
            CreateRule(RiskRuleKeys.MaxAtrPercent, "2.5"),
            CreateRule(RiskRuleKeys.EmergencyStopEnabled, "false"),
            CreateRule(RiskRuleKeys.RequireStopLoss, "true"),
            CreateRule(RiskRuleKeys.MinRewardRiskRatio, "1.2")
        };

        var normalized = ConfidenceScoreNormalizer.Normalize(0.75m);
        var result = engine.Evaluate(new RiskContext
        {
            SymbolId = 1,
            Symbol = "BNBUSDT",
            Direction = TradeDirection.Long,
            EntryPrice = 650m,
            SuggestedStopLoss = 645m,
            SuggestedTakeProfit = 660m,
            ConfidenceScore = normalized,
            RawConfidenceScore = 0.75m,
            EffectiveMinConfidenceScore = 50m,
            ConfidenceSource = "Strategy",
            AccountBalance = 10000m,
            DailyPnl = 0m,
            WeeklyPnl = 0m,
            OpenPositionCount = 0,
            OpenSymbolExposure = 0m,
            TotalExposure = 0m,
            ConsecutiveLosses = 0,
            SpreadPercent = 0.01m,
            AtrPercent = 1.2m,
            EmergencyStopEnabled = false,
            Rules = rules,
            EvaluationTimeUtc = DateTime.UtcNow
        });

        Assert.True(result.Approved);
    }

    [Fact]
    public void RiskEngine_RejectsNormalizedLowConfidenceWithFormattedReason()
    {
        var engine = new RiskEngine(new PositionSizingService());
        var rules = new[]
        {
            CreateRule(RiskRuleKeys.MinConfidenceScore, "50"),
            CreateRule(RiskRuleKeys.RequireStopLoss, "false")
        };

        var normalized = ConfidenceScoreNormalizer.Normalize(0.4m);
        var result = engine.Evaluate(new RiskContext
        {
            SymbolId = 1,
            Symbol = "BNBUSDT",
            Direction = TradeDirection.Long,
            EntryPrice = 650m,
            SuggestedStopLoss = 645m,
            ConfidenceScore = normalized,
            RawConfidenceScore = 0.4m,
            EffectiveMinConfidenceScore = 50m,
            ConfidenceSource = "Strategy",
            AccountBalance = 10000m,
            DailyPnl = 0m,
            WeeklyPnl = 0m,
            OpenPositionCount = 0,
            OpenSymbolExposure = 0m,
            TotalExposure = 0m,
            ConsecutiveLosses = 0,
            EmergencyStopEnabled = false,
            Rules = rules,
            EvaluationTimeUtc = DateTime.UtcNow
        });

        Assert.False(result.Approved);
        Assert.Equal(RiskRuleKeys.MinConfidenceScore, result.RejectedRuleKey);
        Assert.Contains("40.00", result.Reason);
        Assert.Contains("50.00", result.Reason);
    }

    private static RiskRule CreateRule(string key, string value) => new()
    {
        RuleKey = key,
        RuleValue = value,
        ValueType = SettingValueType.Decimal,
        IsEnabled = true,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };
}
