using System.Text.Json;
using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Optimization;
using MomoQuant.Application.Optimization.Dtos;
using MomoQuant.Application.Strategies.Optimization;
using MomoQuant.Application.Validation;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Optimization;

namespace MomoQuant.UnitTests.Strategies;

public sealed class Milestone231B1C6AOptimizerQualificationTests
{
    [Fact]
    public async Task StandardOptimization_AutosaveProducesResearchOnly()
    {
        var parameterRepository = new InMemoryStrategyParameterSetRepository();
        var parameterService = new StrategyParameterSetService(parameterRepository);
        var runRepository = new Mock<IParameterOptimizationRunRepository>();
        runRepository
            .Setup(repository => repository.AddAsync(It.IsAny<ParameterOptimizationRun>(), It.IsAny<CancellationToken>()))
            .Callback<ParameterOptimizationRun, CancellationToken>((run, _) => run.Id = 71)
            .Returns(Task.CompletedTask);
        runRepository
            .Setup(repository => repository.UpdateAsync(It.IsAny<ParameterOptimizationRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        runRepository
            .Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var definitions = new Mock<IStrategyParameterDefinitionProvider>();
        definitions.Setup(provider => provider.EstimateGridCombinations(
                It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>()))
            .Returns(1);
        definitions.Setup(provider => provider.GenerateGridCombinations(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<string, string>?>()))
            .Returns([new Dictionary<string, string> { ["breakoutLookback"] = "20" }]);

        var split = Split();
        var splitService = new Mock<IValidationDateSplitService>();
        splitService.Setup(service => service.Split(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<decimal>()))
            .Returns(split);

        var executor = new Mock<IStrategyResearchBacktestExecutor>();
        executor.Setup(service => service.RunWindowAsync(
                It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<long>(), It.IsAny<decimal>(),
                It.IsAny<StrategyResearchExecutionOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StrategyResearchBacktestResult { Metrics = Metrics() });

        var evaluator = new Mock<IStrategyValidationEvaluator>();
        evaluator.Setup(service => service.Evaluate(
                It.IsAny<StrategyPerformanceMetricsDto>(), It.IsAny<StrategyPerformanceMetricsDto>(), It.IsAny<decimal>()))
            .Returns((ValidationStatus.Passed, Array.Empty<string>(), Array.Empty<string>(), 88m));

        var scorer = new Mock<IParameterOptimizationScorer>();
        scorer.Setup(service => service.Score(
                It.IsAny<StrategyPerformanceMetricsDto>(), It.IsAny<StrategyPerformanceMetricsDto>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(90m);

        var symbols = new Mock<ISymbolRepository>();
        symbols.Setup(repository => repository.GetByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 3, ExchangeId = 2, SymbolName = "BTCUSDT" });

        var service = new ParameterOptimizationService(
            runRepository.Object,
            definitions.Object,
            splitService.Object,
            evaluator.Object,
            scorer.Object,
            executor.Object,
            parameterService,
            symbols.Object);

        var result = await service.RunAsync(new RunParameterOptimizationRequest
        {
            StrategyCode = StrategyCodes.PriceStructureBreakoutRetest,
            ExchangeId = 2,
            SymbolId = 3,
            Timeframe = "15m",
            FromUtc = split.FullDateRange.FromUtc,
            ToUtc = split.FullDateRange.ToUtc,
            OptimizationMode = ParameterOptimizationMode.GridSearch,
            MaxCombinations = 1,
            MaxRuntimeMinutes = 1,
            RiskProfileId = 4,
            SaveBestParameterSet = true
        }, userId: 5);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var saved = Assert.Single(await parameterRepository.ListAsync(null, null, null));
        Assert.True(saved.IsApproved);
        Assert.Equal(ParameterSetQualificationStatus.ResearchOnly, saved.QualificationStatus);
    }

    [Fact]
    public async Task TargetOptimization_SaveBestProducesResearchOnly()
    {
        var (service, repository) = CreateTargetService();

        var result = await service.SaveBestAsync(81, new SaveTargetOptimizationBestRequest());

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.False(result.Data!.IsApproved);
        Assert.Equal("ResearchOnly", result.Data.QualificationStatus);
        Assert.Equal(ParameterSetQualificationStatus.ResearchOnly,
            Assert.Single(await repository.ListAsync(null, null, null)).QualificationStatus);
    }

    [Fact]
    public async Task TargetOptimization_ApproveBestRemainsResearchOnly()
    {
        var (service, repository) = CreateTargetService();

        var result = await service.ApproveBestAsync(81);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.True(result.Data!.IsApproved);
        Assert.Equal("ResearchOnly", result.Data.QualificationStatus);
        Assert.False(result.Data.IsDeploymentQualified);
        Assert.Equal(ParameterSetQualificationStatus.ResearchOnly,
            Assert.Single(await repository.ListAsync(null, null, null)).QualificationStatus);
    }

    private static (TargetParameterOptimizationService Service, InMemoryStrategyParameterSetRepository Repository)
        CreateTargetService()
    {
        var parameterRepository = new InMemoryStrategyParameterSetRepository();
        var parameterService = new StrategyParameterSetService(parameterRepository);
        var dto = TargetRun();
        var run = new TargetOptimizationRun
        {
            Id = dto.Id,
            StrategyCode = dto.StrategyCode,
            ExchangeId = 2,
            SymbolId = dto.SymbolId,
            Timeframe = dto.Timeframe,
            FromUtc = dto.DateRange.FromUtc,
            ToUtc = dto.DateRange.ToUtc,
            Status = TargetOptimizationStatus.Completed,
            ResultJson = JsonSerializer.Serialize(dto, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }),
            CreatedAtUtc = dto.CreatedAtUtc,
            CompletedAtUtc = dto.CompletedAtUtc
        };
        var runs = new Mock<ITargetOptimizationRunRepository>();
        runs.Setup(repository => repository.GetByIdAsync(81, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var service = new TargetParameterOptimizationService(
            runs.Object,
            Mock.Of<IStrategyParameterDefinitionProvider>(),
            Mock.Of<IValidationDateSplitService>(),
            Mock.Of<ITargetOptimizationRulesEvaluator>(),
            Mock.Of<IStrategyResearchBacktestExecutor>(),
            Mock.Of<IStrategyResearchCandleCoverageService>(),
            parameterService,
            Mock.Of<ISymbolRepository>(),
            Mock.Of<IExchangeRepository>());
        return (service, parameterRepository);
    }

    private static TargetOptimizationRunDto TargetRun()
    {
        var split = Split();
        var best = new TargetParameterSetResultDto
        {
            Rank = 1,
            Status = ParameterSetTestStatus.ValidationPassed,
            Parameters = new Dictionary<string, string> { ["breakoutLookback"] = "20" },
            TrainingMetrics = Metrics(),
            ValidationMetrics = Metrics(),
            RobustnessScore = 85m,
            Score = 90m,
            TargetPassSummary = new TargetPassSummary
            {
                TrainingPnlPassed = true,
                ValidationPnlPassed = true,
                TrainingProfitFactorPassed = true,
                ValidationProfitFactorPassed = true,
                TrainingDrawdownPassed = true,
                ValidationDrawdownPassed = true,
                TrainingTradesPassed = true,
                ValidationTradesPassed = true,
                RobustnessPassed = true
            }
        };
        return new TargetOptimizationRunDto
        {
            Id = 81,
            StrategyCode = StrategyCodes.PriceStructureBreakoutRetest,
            SymbolId = 3,
            Exchange = "Binance",
            Symbol = "BTCUSDT",
            Timeframe = "15m",
            DateRange = split.FullDateRange,
            TrainingRange = split.TrainingRange,
            ValidationRange = split.ValidationRange,
            Status = TargetOptimizationStatus.Completed,
            BestPassedParameterSet = best,
            Results = [best],
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            CompletedAtUtc = DateTime.UtcNow
        };
    }

    private static ValidationSplitDto Split()
    {
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var split = from.AddDays(7);
        var to = from.AddDays(10);
        return new ValidationSplitDto
        {
            FullDateRange = new DateRangeDto { FromUtc = from, ToUtc = to },
            TrainingRange = new DateRangeDto { FromUtc = from, ToUtc = split },
            ValidationRange = new DateRangeDto { FromUtc = split, ToUtc = to }
        };
    }

    private static StrategyPerformanceMetricsDto Metrics() => new()
    {
        NetPnlPercent = 5m,
        WinRate = 55m,
        ProfitFactor = 1.5m,
        MaxDrawdownPercent = 4m,
        TradeCount = 20,
        AverageR = 1m,
        Expectancy = 1m,
        RecoveryFactor = 1m,
        LargestLoss = -1m,
        ConsecutiveLosses = 1
    };
}
