using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Exchanges;
using MomoQuant.Application.Exchanges.Dtos;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

public class StrategyCatalogMetadataTests
{
    [Fact]
    public async Task GetAllRequirements_VolatilityGatedStrategy_IncludesAllowedTimeframesAndOptimization()
    {
        var strategyRepository = new Mock<IStrategyRepository>();
        strategyRepository.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Strategy>
            {
                new()
                {
                    Id = 14,
                    Code = StrategyCode.VolatilityGatedSupertrendMomentum,
                    Name = "Volatility Gated SuperTrend Momentum",
                    Description = "test",
                    IsEnabled = true
                }
            });

        var service = new StrategyDataRequirementService(strategyRepository.Object, Mock.Of<ISymbolRepository>());
        var result = await service.GetAllAsync();

        Assert.True(result.Succeeded);
        var requirement = Assert.Single(result.Data!);
        Assert.Equal("VOLATILITY_GATED_SUPERTREND_MOMENTUM", requirement.StrategyCode);
        Assert.Equal("15m", requirement.PreferredExecutionTimeframe);
        Assert.Contains("3m", requirement.AllowedExecutionTimeframes);
        Assert.Contains("4h", requirement.AllowedExecutionTimeframes);
        Assert.Equal(["5m", "15m", "1h"], requirement.PreferredTimeframes);
        Assert.True(requirement.SupportsOptimization);
        Assert.True(requirement.SupportsValidation);
    }

    [Fact]
    public async Task GetAllRequirements_BbLiquiditySweep_Uses3mExecutionAndMultiDataTimeframes()
    {
        var strategyRepository = new Mock<IStrategyRepository>();
        strategyRepository.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Strategy>
            {
                new()
                {
                    Id = 12,
                    Code = StrategyCode.BbLiquiditySweepCisd,
                    Name = "BB Liquidity Sweep CISD",
                    Description = "test",
                    IsEnabled = true
                }
            });

        var service = new StrategyDataRequirementService(strategyRepository.Object, Mock.Of<ISymbolRepository>());
        var result = await service.GetAllAsync();

        Assert.True(result.Succeeded);
        var requirement = Assert.Single(result.Data!);
        Assert.Equal("BB_LIQUIDITY_SWEEP_CISD", requirement.StrategyCode);
        Assert.Equal(["3m"], requirement.AllowedExecutionTimeframes);
        Assert.Equal(["1m", "3m", "5m"], requirement.RequiredDataTimeframes);
        Assert.False(requirement.SupportsOptimization);
        Assert.True(requirement.SupportsValidation);
    }

    [Fact]
    public void StrategyCatalogMapper_MapsRequirementMetadataOntoDto()
    {
        var strategy = new Strategy
        {
            Id = 14,
            Code = StrategyCode.VolatilityGatedSupertrendMomentum,
            Name = "Volatility Gated SuperTrend Momentum",
            Description = "test",
            IsEnabled = true,
            Version = "1.0"
        };

        var requirement = new StrategyDataRequirementDto
        {
            StrategyId = 14,
            StrategyCode = "VOLATILITY_GATED_SUPERTREND_MOMENTUM",
            StrategyName = strategy.Name,
            PreferredExecutionTimeframe = "15m",
            AllowedExecutionTimeframes = ["3m", "5m", "15m", "30m", "1h", "4h"],
            RequiredDataTimeframes = ["15m"],
            OptionalDataTimeframes = [],
            AnchorTimeframes = [],
            HigherTimeframeFilters = [],
            RequiredIndicators = ["SuperTrend"],
            RequiredIndicatorTimeframes = ["15m"],
            PreferredTimeframes = ["5m", "15m", "1h"],
            SupportsOptimization = true,
            SupportsValidation = true,
            SupportsBenchmark = true,
            Warnings = []
        };

        var dto = StrategyCatalogMapper.MapToCatalogDto(strategy, requirement, parameterDefinitionsAvailable: true);

        Assert.Equal("15m", dto.PreferredTimeframe);
        Assert.Contains("4h", dto.AllowedTimeframes);
        Assert.True(dto.SupportsOptimization);
        Assert.True(dto.ParameterDefinitionsAvailable);
    }
}

public class ExchangeEnabledSymbolsServiceTests
{
    [Fact]
    public async Task GetEnabledSymbolsAsync_ReturnsOnlyActiveSymbolsForExchange()
    {
        var exchangeRepository = new Mock<IExchangeRepository>();
        exchangeRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Exchange { Id = 1, Name = "Binance Futures", Code = "BINANCE_FUTURES", BaseUrl = "https://fapi.binance.com", WebSocketUrl = "wss://fstream.binance.com", IsActive = true });

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetEnabledByExchangeIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Symbol>
            {
                new() { Id = 1, ExchangeId = 1, SymbolName = "BTCUSDT", IsActive = true },
                new() { Id = 2, ExchangeId = 1, SymbolName = "ETHUSDT", IsActive = true }
            });

        var service = new ExchangeService(
            exchangeRepository.Object,
            symbolRepository.Object,
            Mock.Of<IExchangeConnectivityProvider>(),
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>());

        var result = await service.GetEnabledSymbolsAsync(1);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
        Assert.All(result.Data, item => Assert.True(item.IsEnabled));
        Assert.Contains(result.Data, item => item.Symbol == "BTCUSDT" && item.DisplayName.Contains("Binance Futures"));
    }
}
