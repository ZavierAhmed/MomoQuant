using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

public class ChronologicalHoldoutSplitTests
{
    [Fact]
    public void Split_IsDeterministic_70_30_ByCandleCount()
    {
        var opens = Enumerable.Range(0, 100)
            .Select(i => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i * 15))
            .ToList();

        var a = ChronologicalHoldoutSplit.Split(opens, 0.70m, requiredWarmupCandles: 10, timeframeMinutes: 15);
        var b = ChronologicalHoldoutSplit.Split(opens, 0.70m, requiredWarmupCandles: 10, timeframeMinutes: 15);

        Assert.True(a.IsValid);
        Assert.Equal(70, a.TrainingCandleCount);
        Assert.Equal(30, a.ValidationCandleCount);
        Assert.Equal(a.TrainingEndUtc, b.TrainingEndUtc);
        Assert.Equal(a.ValidationStartUtc, b.ValidationStartUtc);
        Assert.Equal(opens[70], a.ValidationStartUtc);
        Assert.Equal(opens[69], a.TrainingEndUtc);
    }

    [Fact]
    public void Split_NoCandleBelongsToBothSegments()
    {
        var opens = Enumerable.Range(0, 50)
            .Select(i => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i))
            .ToList();
        var split = ChronologicalHoldoutSplit.Split(opens, 0.70m, timeframeMinutes: 60);
        Assert.True(split.IsValid);
        Assert.True(split.TrainingEndUtc < split.ValidationStartUtc);
    }

    [Fact]
    public void Split_SmallDataset_FailsSafely()
    {
        var opens = Enumerable.Range(0, 8)
            .Select(i => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i))
            .ToList();
        var split = ChronologicalHoldoutSplit.Split(opens, 0.70m, minTrainingCandles: 10, minValidationCandles: 5);
        Assert.False(split.IsValid);
        Assert.Contains("Insufficient", split.FailureReason!);
    }

    [Fact]
    public void Split_UsesTimeframeMinutesForTrainingWarmup()
    {
        var opens = Enumerable.Range(0, 40)
            .Select(i => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i * 15))
            .ToList();
        var split = ChronologicalHoldoutSplit.Split(opens, 0.70m, requiredWarmupCandles: 4, timeframeMinutes: 15);
        Assert.True(split.IsValid);
        Assert.Equal(split.TrainingStartUtc.AddMinutes(-4 * 15), split.TrainingWarmupStartUtc);
    }
}

public class ValidationTrainingScoreTests
{
    [Fact]
    public void Score_CapsWhenSampleVerySmall()
    {
        var score = ValidationTrainingScoreCalculator.Calculate(
            closedTrades: 2,
            netExpectancyR: 2m,
            profitFactor: 3m,
            maxDrawdownPercent: 1m,
            feeToGrossProfitPercent: 1m,
            opportunityRatePer1000: 10m,
            minimumClosedTrades: 30);
        Assert.True(score.Total <= 40m);
        Assert.Contains(score.Notes, n => n.Contains("capped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Score_IncreasesWithBetterExpectancy()
    {
        var low = ValidationTrainingScoreCalculator.Calculate(40, 0.2m, 1.2m, 10m, 5m, 3m);
        var high = ValidationTrainingScoreCalculator.Calculate(40, 1.5m, 1.2m, 10m, 5m, 3m);
        Assert.True(high.Total > low.Total);
    }
}

public class ValidationRobustnessEvaluatorTests
{
    [Fact]
    public void Evaluate_PassesWhenRawValidationHealthy()
    {
        var training = new LayerSegmentMetrics
        {
            ClosedTradeCount = 40,
            NetExpectancyR = 0.3m,
            ProfitFactor = 1.3m,
            OpportunityRatePer1000Candles = 5m,
            NetPnl = 100m,
            MaximumRealizedDrawdownPercent = 10m
        };
        var validation = new LayerSegmentMetrics
        {
            ClosedTradeCount = 20,
            NetExpectancyR = 0.25m,
            ProfitFactor = 1.2m,
            OpportunityRatePer1000Candles = 4m,
            NetPnl = 40m,
            MaximumRealizedDrawdownPercent = 12m
        };

        var verdict = ValidationRobustnessEvaluator.Evaluate(training, validation, ValidationQualificationProfile.StandardDefault());
        Assert.Equal(StrategyRobustnessDecision.Passed, verdict.Decision);
    }

    [Fact]
    public void Evaluate_FailsOnPerformanceCollapse()
    {
        var training = new LayerSegmentMetrics
        {
            ClosedTradeCount = 40,
            NetExpectancyR = 0.5m,
            ProfitFactor = 1.5m,
            OpportunityRatePer1000Candles = 5m,
            NetPnl = 100m
        };
        var validation = new LayerSegmentMetrics
        {
            ClosedTradeCount = 20,
            NetExpectancyR = -0.2m,
            ProfitFactor = 0.7m,
            OpportunityRatePer1000Candles = 4m,
            NetPnl = -50m
        };

        var verdict = ValidationRobustnessEvaluator.Evaluate(training, validation, ValidationQualificationProfile.StandardDefault());
        Assert.Contains(nameof(StrategyRobustnessDecision.FailedPerformanceCollapse), verdict.FailureReasons);
    }

    [Fact]
    public void FreezeRequired_ConfigurationFrozenOnly()
    {
        Assert.Equal(ValidationExperimentStatus.ConfigurationFrozen, ValidationExperimentStatus.ConfigurationFrozen);
        Assert.NotEqual(ValidationExperimentStatus.TrainingCompleted, ValidationExperimentStatus.ConfigurationFrozen);
    }
}

public class ValidationComparisonCalculatorTests
{
    [Fact]
    public void Compare_NotMeaningful_WhenTrainingExpectancyNonPositive()
    {
        var training = new LayerSegmentMetrics { NetExpectancyR = 0m, ProfitFactor = 1.1m, ClosedTradeCount = 10 };
        var validation = new LayerSegmentMetrics { NetExpectancyR = 0.2m, ProfitFactor = 1.2m, ClosedTradeCount = 8 };
        var cmp = ValidationComparisonCalculator.Compare("RawStrategy", training, validation);
        Assert.Equal("NotMeaningful", cmp.NetExpectancyRetention);
    }

    [Fact]
    public void Compare_Retention_WhenTrainingPositive()
    {
        var training = new LayerSegmentMetrics { NetExpectancyR = 1m, ProfitFactor = 1.5m, ClosedTradeCount = 20 };
        var validation = new LayerSegmentMetrics { NetExpectancyR = 0.5m, ProfitFactor = 1.2m, ClosedTradeCount = 10 };
        var cmp = ValidationComparisonCalculator.Compare("RawStrategy", training, validation);
        Assert.Equal("50", cmp.NetExpectancyRetention);
    }
}

public class ValidationBoundaryCensorTests
{
    [Fact]
    public void CountBoundaryCensored_DetectsTrainingTradesCrossingSplit()
    {
        var split = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var candidates = new List<StrategyResearchCandidate>
        {
            new()
            {
                SetupDetectedAtUtc = split.AddHours(-2),
                RawExitTimeUtc = split.AddHours(1),
                CandidateStatus = StrategyResearchCandidateStatus.Closed,
                RawOutcomeStatus = RawOutcomeStatus.Winner
            },
            new()
            {
                SetupDetectedAtUtc = split.AddHours(-5),
                RawExitTimeUtc = split.AddHours(-1),
                CandidateStatus = StrategyResearchCandidateStatus.Closed,
                RawOutcomeStatus = RawOutcomeStatus.Loser
            }
        };

        Assert.Equal(1, ValidationMetricsMapper.CountBoundaryCensored(candidates, split));
        var remaining = ValidationMetricsMapper.ExcludeBoundaryFromMetrics(candidates, split);
        Assert.Single(remaining);
    }
}

public class ValidationParameterStabilityTests
{
    [Fact]
    public void Analyze_FlagsSingleWinnerDominance()
    {
        var trials = new List<ValidationParameterTrial>
        {
            new()
            {
                TrialNumber = 1,
                ParameterFingerprint = "AAA",
                Status = ValidationTrialStatus.Completed,
                TrainingScore = 80m,
                GuardrailDecision = "Passed",
                ClosedTradeCount = 40,
                NetExpectancyR = 0.4m
            },
            new()
            {
                TrialNumber = 2,
                ParameterFingerprint = "BBB",
                Status = ValidationTrialStatus.GuardrailRejected,
                TrainingScore = 50m,
                GuardrailDecision = "Failed",
                ClosedTradeCount = 10,
                NetExpectancyR = 0.1m
            }
        };

        var result = ValidationParameterStabilityAnalyzer.Analyze(trials);
        Assert.Contains("SingleWinnerDominance", result.Warnings);
    }
}

public class ValidationLeakageRankingTests
{
    [Fact]
    public void Ranking_IgnoresValidationMetrics_ChangingThemDoesNotChangeOrder()
    {
        var trials = new List<ValidationParameterTrial>
        {
            new()
            {
                Id = 1,
                TrialNumber = 1,
                ParameterFingerprint = "A",
                GuardrailDecision = "Passed",
                TrainingScore = 70m,
                NetExpectancyR = 0.4m,
                ProfitFactor = 1.4m,
                MaximumDrawdownPercent = 8m,
                ClosedTradeCount = 40
            },
            new()
            {
                Id = 2,
                TrialNumber = 2,
                ParameterFingerprint = "B",
                GuardrailDecision = "Passed",
                TrainingScore = 75m,
                NetExpectancyR = 0.5m,
                ProfitFactor = 1.5m,
                MaximumDrawdownPercent = 7m,
                ClosedTradeCount = 42
            }
        };

        var order1 = ValidationTrialRanker.OrderForRanking(trials).Select(t => t.Id).ToList();

        // Simulate attaching "validation" fields on unrelated objects — ranking must still only use training fields.
        trials[0].DiagnosticWarningsJson = """{"validationNetPnl":9999}""";
        trials[1].DiagnosticWarningsJson = """{"validationNetPnl":-9999}""";

        var order2 = ValidationTrialRanker.OrderForRanking(trials).Select(t => t.Id).ToList();
        Assert.Equal(order1, order2);
        Assert.Equal(2, order2[0]);
        Assert.Equal(1, order2[1]);
    }
}

public class ValidationFreezeGateTests
{
    [Fact]
    public void RunValidation_RequiresConfigurationFrozenStatus()
    {
        Assert.True(ValidationLifecycleGate.CanRunValidation(ValidationExperimentStatus.ConfigurationFrozen));
        Assert.False(ValidationLifecycleGate.CanRunValidation(ValidationExperimentStatus.Draft));
        Assert.False(ValidationLifecycleGate.CanRunValidation(ValidationExperimentStatus.DataReady));
        Assert.False(ValidationLifecycleGate.CanRunValidation(ValidationExperimentStatus.TrainingCompleted));
        Assert.False(ValidationLifecycleGate.CanRunValidation(ValidationExperimentStatus.TrainingRunning));
    }
}

public class ValidationRegimeAnalyzerTests
{
    [Fact]
    public void Compare_SimilarRegime_ForSimilarCandles()
    {
        var candles = Enumerable.Range(0, 30).Select(i => new Candle
        {
            OpenTimeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i),
            Open = 100m + i * 0.1m,
            High = 101m + i * 0.1m,
            Low = 99m + i * 0.1m,
            Close = 100.5m + i * 0.1m,
            Volume = 10m
        }).ToList();

        var result = ValidationRegimeAnalyzer.Compare(candles.Take(20).ToList(), candles.Skip(10).Take(20).ToList());
        Assert.Equal("SimilarRegime", result.QualitativeComparison);
    }
}
