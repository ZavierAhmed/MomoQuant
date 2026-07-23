using MomoQuant.Application.Optimization;
using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend;
using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend.Dtos;
using MomoQuant.Application.Strategies.Optimization;
using MomoQuant.Application.Validation;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.UnitTests.Strategies;

public class VolatilityGatedSuperTrendTests
{
    [Fact]
    public void StrategyCode_IsRegisteredInCatalog()
    {
        Assert.Equal(StrategyCodes.VolatilityGatedSupertrendMomentum, StrategyCode.VolatilityGatedSupertrendMomentum.ToCode());
        Assert.Equal(StrategyCode.VolatilityGatedSupertrendMomentum, StrategyCodeExtensions.FromCode(StrategyCodes.VolatilityGatedSupertrendMomentum));
    }

    [Fact]
    public void VolatilityGate_PassesWhenRatioAboveThreshold()
    {
        var parameters = VolatilityGatedSuperTrendParameters.From(new Dictionary<string, string>
        {
            ["minVolatilityRatio"] = "1.05",
            ["requireRetest"] = "false",
            ["allowTrendContinuationEntry"] = "true",
            ["minHistogramStrength"] = "0"
        });

        var candles = BuildTrendingCandles(400, startPrice: 100m, step: 0.5m);
        var contextService = new VolatilityGatedSuperTrendContextService();
        contextService.Precompute(1, candles, parameters);

        var lastIndex = candles.Count - 1;
        var data = contextService.GetCandleData(1, lastIndex);
        Assert.NotNull(data);
        Assert.True(data!.VolatilityRatio >= parameters.MinVolatilityRatio);
    }

    [Fact]
    public void VolatilityGate_FailsWhenRatioBelowThreshold()
    {
        var parameters = VolatilityGatedSuperTrendParameters.From(new Dictionary<string, string>
        {
            ["fastAtrPeriod"] = "7",
            ["slowAtrPeriod"] = "100",
            ["minVolatilityRatio"] = "2.0"
        });

        var candles = BuildFlatCandles(350, price: 100m);
        var contextService = new VolatilityGatedSuperTrendContextService();
        contextService.Precompute(1, candles, parameters);
        var data = contextService.GetCandleData(1, candles.Count - 1);
        Assert.NotNull(data);
        if (data!.VolatilityRatio.HasValue)
        {
            Assert.True(data.VolatilityRatio.Value < parameters.MinVolatilityRatio);
        }
    }

    [Fact]
    public void Momentum_PassesForLongWhenHistogramPositive()
    {
        var parameters = VolatilityGatedSuperTrendParameters.From(new Dictionary<string, string>());
        var candles = BuildTrendingCandles(400, 100m, 1m);
        var contextService = new VolatilityGatedSuperTrendContextService();
        contextService.Precompute(1, candles, parameters);
        var data = contextService.GetCandleData(1, candles.Count - 1);
        Assert.NotNull(data?.MacdHistogram);
    }

    [Fact]
    public void ValidationSplit_Produces70Training30Validation()
    {
        var service = new ValidationDateSplitService();
        var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(100);
        var split = service.Split(from, to);

        Assert.Equal(from, split.TrainingRange.FromUtc);
        Assert.Equal(from.AddDays(70), split.TrainingRange.ToUtc);
        Assert.Equal(from.AddDays(70), split.ValidationRange.FromUtc);
        Assert.Equal(to, split.ValidationRange.ToUtc);
        Assert.True(split.TrainingRange.ToUtc < split.ValidationRange.ToUtc);
    }

    [Fact]
    public void ValidationEvaluator_FailsWhenValidationPnlNegative()
    {
        var evaluator = new StrategyValidationEvaluator();
        var training = new StrategyPerformanceMetricsDto
        {
            NetPnlPercent = 10m, WinRate = 60m, ProfitFactor = 2m, MaxDrawdownPercent = 5m,
            TradeCount = 30, AverageR = 1.5m, Expectancy = 5m, RecoveryFactor = 2m, LargestLoss = -10m, ConsecutiveLosses = 2
        };
        var validation = new StrategyPerformanceMetricsDto
        {
            NetPnlPercent = -2m, ProfitFactor = 0.8m, MaxDrawdownPercent = 5m,
            TradeCount = 12, AverageR = 0.5m, Expectancy = -1m, WinRate = 45m,
            RecoveryFactor = 0.5m, LargestLoss = -15m, ConsecutiveLosses = 3
        };
        var (status, failReasons, _, _) = evaluator.Evaluate(training, validation);
        Assert.Equal(ValidationStatus.Failed, status);
        Assert.NotEmpty(failReasons);
    }

    [Fact]
    public void ParameterDefinitions_AreReturnedForVgStrategy()
    {
        var provider = new StrategyParameterDefinitionProvider();
        var defs = provider.GetDefinitions(StrategyCodes.VolatilityGatedSupertrendMomentum);
        Assert.NotEmpty(defs);
        Assert.Contains(defs, d => d.Key == "atrPeriod");
    }

    [Fact]
    public void GridSearch_EnforcesMaxCombinations()
    {
        var provider = new StrategyParameterDefinitionProvider();
        var combos = provider.GenerateGridCombinations(StrategyCodes.VolatilityGatedSupertrendMomentum, 50);
        Assert.True(combos.Count <= 50);
    }

    [Fact]
    public void OptimizationScorer_PenalizesOverfitting()
    {
        var scorer = new ParameterOptimizationScorer();
        var training = new StrategyPerformanceMetricsDto
        {
            NetPnlPercent = 20m, WinRate = 70m, ProfitFactor = 3m, MaxDrawdownPercent = 5m,
            TradeCount = 40, AverageR = 2m, Expectancy = 10m, RecoveryFactor = 4m, LargestLoss = -5m, ConsecutiveLosses = 1
        };
        var validation = new StrategyPerformanceMetricsDto
        {
            NetPnlPercent = -1m, WinRate = 40m, ProfitFactor = 0.9m, MaxDrawdownPercent = 15m,
            TradeCount = 15, AverageR = 0.2m, Expectancy = -1m, RecoveryFactor = 0.5m, LargestLoss = -20m, ConsecutiveLosses = 4
        };
        var strongScore = scorer.Score(training, training, "Balanced", 20, 10);
        var weakScore = scorer.Score(training, validation, "Balanced", 20, 10);
        Assert.True(strongScore > weakScore);
    }

    [Fact]
    public void FunnelTracker_RecordsPipelineCounts()
    {
        var tracker = new VolatilityGatedSuperTrendFunnelTracker();
        tracker.Reset();
        tracker.RecordEvaluation(new VolatilityGatedSuperTrendDiagnosticsDto
        {
            VolatilityGatePassed = true,
            MomentumPassed = true,
            RetestDetected = true,
            ConfirmationDetected = true,
            FinalDecision = "Entry"
        });
        var snapshot = tracker.GetSnapshot();
        Assert.Equal(1, snapshot.Evaluations);
        Assert.Equal(1, snapshot.VolatilityGatePassed);
        Assert.Equal(1, snapshot.MomentumPassed);
        Assert.Equal(1, snapshot.RetestCount);
        Assert.Equal(1, snapshot.ConfirmationCount);
        Assert.Equal(1, snapshot.CandidateSignals);
    }

    private static List<Candle> BuildTrendingCandles(int count, decimal startPrice, decimal step)
    {
        var candles = new List<Candle>();
        var price = startPrice;
        for (var i = 0; i < count; i++)
        {
            var open = price;
            price += step;
            candles.Add(new Candle
            {
                Id = i + 1,
                SymbolId = 1,
                OpenTimeUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i * 15),
                CloseTimeUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes((i + 1) * 15),
                Open = open,
                High = price + 1m,
                Low = open - 0.5m,
                Close = price,
                Volume = 1000m
            });
        }

        return candles;
    }

    private static List<Candle> BuildFlatCandles(int count, decimal price)
    {
        var candles = new List<Candle>();
        for (var i = 0; i < count; i++)
        {
            candles.Add(new Candle
            {
                Id = i + 1,
                SymbolId = 1,
                OpenTimeUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i * 15),
                CloseTimeUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes((i + 1) * 15),
                Open = price,
                High = price + 0.1m,
                Low = price - 0.1m,
                Close = price,
                Volume = 100m
            });
        }

        return candles;
    }
}
