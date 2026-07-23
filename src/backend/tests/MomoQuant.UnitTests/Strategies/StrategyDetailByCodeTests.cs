using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Optimization;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

public class StrategyDetailByCodeTests
{
    [Fact]
    public async Task GetStrategyByCodeAsync_VgStrategy_ReturnsDetailMetadata()
    {
        var strategy = new Strategy
        {
            Id = 14,
            Code = StrategyCode.VolatilityGatedSupertrendMomentum,
            Name = "Volatility Gated SuperTrend Momentum",
            Description = "Momentum strategy",
            IsEnabled = true,
            Version = "1.0"
        };

        var strategyRepository = new Mock<IStrategyRepository>();
        strategyRepository.Setup(repo => repo.GetByCodeAsync(StrategyCode.VolatilityGatedSupertrendMomentum, It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategy);
        strategyRepository.Setup(repo => repo.GetByIdAsync(14, It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategy);
        strategyRepository.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Strategy> { strategy });

        var requirementService = new StrategyDataRequirementService(strategyRepository.Object, Mock.Of<ISymbolRepository>());
        var service = BuildService(strategyRepository, requirementService);

        var result = await service.GetStrategyByCodeAsync("VOLATILITY_GATED_SUPERTREND_MOMENTUM");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal("15m", result.Data!.PreferredTimeframe);
        Assert.Contains("4h", result.Data.AllowedTimeframes);
        Assert.Contains("SuperTrend", result.Data.RequiredIndicators);
        Assert.True(result.Data.SupportsOptimization);
        Assert.True(result.Data.SupportsValidation);
        Assert.False(string.IsNullOrWhiteSpace(result.Data.HowItWorks));
        Assert.NotEmpty(result.Data.ParameterDefinitions);
    }

    [Fact]
    public async Task GetStrategyByCodeAsync_UnknownCode_ReturnsNotFound()
    {
        var strategyRepository = new Mock<IStrategyRepository>();
        strategyRepository.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Strategy>());
        var requirementService = new StrategyDataRequirementService(strategyRepository.Object, Mock.Of<ISymbolRepository>());
        var service = BuildService(strategyRepository, requirementService);

        var result = await service.GetStrategyByCodeAsync("NOT_A_REAL_STRATEGY");

        Assert.False(result.Succeeded);
        Assert.Equal("Strategy was not found.", result.ErrorMessage);
    }

    private static StrategyService BuildService(
        Mock<IStrategyRepository> strategyRepository,
        StrategyDataRequirementService requirementService)
    {
        return new StrategyService(
            strategyRepository.Object,
            Mock.Of<IStrategyParameterRepository>(),
            Mock.Of<IStrategyRegistry>(),
            Mock.Of<IStrategyEngine>(),
            Mock.Of<IStrategyParameterProvider>(),
            requirementService,
            new StrategyParameterDefinitionProvider(),
            Mock.Of<ICandleRepository>(),
            Mock.Of<IIndicatorSnapshotRepository>(),
            Mock.Of<ISymbolRepository>(),
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>());
    }
}
