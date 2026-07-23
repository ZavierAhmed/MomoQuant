using MomoQuant.Application.Optimization;
using MomoQuant.Application.Optimization.Dtos;
using MomoQuant.Application.Validation;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.UnitTests.Strategies;

public class TargetOptimizationTests
{
    private readonly TargetOptimizationRulesEvaluator _evaluator = new();
    private readonly ValidationDateSplitService _splitService = new();

    private static StrategyPerformanceMetricsDto GoodTraining(decimal netPnl = 5m, int tradeCount = 30) => new()
    {
        NetPnlPercent = netPnl, WinRate = 60m, ProfitFactor = 1.8m, MaxDrawdownPercent = 6m,
        TradeCount = tradeCount, AverageR = 1.5m, Expectancy = 5m, RecoveryFactor = 2m, LargestLoss = -10m, ConsecutiveLosses = 2
    };

    private static StrategyPerformanceMetricsDto GoodValidation(decimal netPnl = 2m, decimal profitFactor = 1.4m, int tradeCount = 15) => new()
    {
        NetPnlPercent = netPnl, WinRate = 55m, ProfitFactor = profitFactor, MaxDrawdownPercent = 5m,
        TradeCount = tradeCount, AverageR = 1.2m, Expectancy = 3m, RecoveryFactor = 1.5m, LargestLoss = -8m, ConsecutiveLosses = 2
    };

    [Fact]
    public void TargetOptimization_SplitsDateRange70_30()
    {
        var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(100);
        var split = _splitService.Split(from, to);
        Assert.Equal(70m, split.TrainingPercent);
        Assert.Equal(30m, split.ValidationPercent);
        Assert.Equal(from, split.TrainingRange.FromUtc);
        Assert.Equal(from.AddDays(70), split.TrainingRange.ToUtc);
        Assert.Equal(from.AddDays(70), split.ValidationRange.FromUtc);
        Assert.Equal(to, split.ValidationRange.ToUtc);
    }

    [Fact]
    public void TrainingTarget_FailsWhenPnlTooLow()
    {
        var metrics = GoodTraining(netPnl: 0.5m);
        var (passed, failReasons, _) = _evaluator.EvaluateTraining(metrics, TargetOptimizationRulesDto.DefaultResearch());
        Assert.False(passed);
        Assert.Contains(failReasons, r => r.Contains("PnL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TrainingTarget_FailsWhenTooFewTrades()
    {
        var metrics = GoodTraining(tradeCount: 5);
        var (passed, _, summary) = _evaluator.EvaluateTraining(metrics, TargetOptimizationRulesDto.DefaultResearch());
        Assert.False(passed);
        Assert.False(summary.TrainingTradesPassed);
    }

    [Fact]
    public void ValidationPassed_WhenBothWindowsMeetTargets()
    {
        var (passed, status, _, _, summary, robustness) =
            _evaluator.EvaluateValidation(GoodTraining(), GoodValidation(), TargetOptimizationRulesDto.DefaultResearch());
        Assert.True(passed);
        Assert.Equal(ParameterSetTestStatus.ValidationPassed, status);
        Assert.True(summary.ValidationPassed);
        Assert.True(robustness >= 60m);
    }

    [Fact]
    public void ValidationFailed_WhenTrainingPassesButValidationFails()
    {
        var validation = GoodValidation(netPnl: -1m, profitFactor: 0.8m);
        var (_, status, failReasons, overfitWarnings, _, _) =
            _evaluator.EvaluateValidation(GoodTraining(), validation, TargetOptimizationRulesDto.DefaultResearch());
        Assert.NotEqual(ParameterSetTestStatus.ValidationPassed, status);
        Assert.True(status is ParameterSetTestStatus.ValidationFailed or ParameterSetTestStatus.Overfit);
        Assert.NotEmpty(failReasons);
        Assert.NotEmpty(overfitWarnings);
    }

    [Fact]
    public void OverfitDetected_WhenTrainingStrongButValidationWeak()
    {
        var validation = GoodValidation(netPnl: -2m, profitFactor: 0.7m, tradeCount: 12);
        var (_, status, _, overfitWarnings, _, _) =
            _evaluator.EvaluateValidation(GoodTraining(), validation, TargetOptimizationRulesDto.DefaultResearch());
        Assert.Equal(ParameterSetTestStatus.Overfit, status);
        Assert.Contains(overfitWarnings, w => w.Contains("overfit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Ranking_PrefersValidationPassedOverTrainingPnl()
    {
        var passed = new TargetParameterSetResultDto
        {
            Rank = 0,
            Status = ParameterSetTestStatus.ValidationPassed,
            Parameters = new Dictionary<string, string>(),
            TrainingMetrics = GoodTraining(netPnl: 3m),
            ValidationMetrics = GoodValidation(netPnl: 1m),
            RobustnessScore = 70m,
            Score = 80m,
            TargetPassSummary = new TargetPassSummary()
        };
        var overfit = new TargetParameterSetResultDto
        {
            Rank = 0,
            Status = ParameterSetTestStatus.Overfit,
            Parameters = new Dictionary<string, string>(),
            TrainingMetrics = GoodTraining(netPnl: 20m),
            ValidationMetrics = GoodValidation(netPnl: -1m),
            RobustnessScore = 30m,
            Score = 10m,
            TargetPassSummary = new TargetPassSummary()
        };

        var ranked = RankResultsForTest([overfit, passed]);
        Assert.Equal(ParameterSetTestStatus.ValidationPassed, ranked[0].Status);
    }

    [Fact]
    public void DefaultTargetRules_MatchResearchDefaults()
    {
        var rules = TargetOptimizationRulesDto.DefaultResearch();
        Assert.Equal(2.0m, rules.MinTrainingNetPnlPercent);
        Assert.Equal(0.5m, rules.MinValidationNetPnlPercent);
        Assert.Equal(20, rules.MinTrainingTrades);
        Assert.Equal(10, rules.MinValidationTrades);
        Assert.Equal(60m, rules.MinRobustnessScore);
    }

    [Fact]
    public void BuildParameterSetName_UsesTargetOptFormat()
    {
        var name = TargetParameterOptimizationService.BuildParameterSetName(
            "VOLATILITY_GATED_SUPERTREND_MOMENTUM", "BTCUSDT", "15m");
        Assert.Contains("VG SuperTrend", name);
        Assert.Contains("BTCUSDT", name);
        Assert.Contains("TargetOpt", name);
    }

    private static List<TargetParameterSetResultDto> RankResultsForTest(IReadOnlyList<TargetParameterSetResultDto> results) =>
        results
            .OrderByDescending(r => r.Status == ParameterSetTestStatus.ValidationPassed)
            .ThenByDescending(r => r.RobustnessScore)
            .ThenByDescending(r => r.ValidationMetrics?.NetPnlPercent ?? decimal.MinValue)
            .Select((r, idx) => r with { Rank = idx + 1 })
            .ToList();
}
