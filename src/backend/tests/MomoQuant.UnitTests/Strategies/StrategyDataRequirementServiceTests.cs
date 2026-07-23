using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

public class StrategyDataRequirementServiceTests
{
    [Fact]
    public async Task ResolveAsync_ForFourHourRangeReEntry_Uses5mExecutionAnd4hAnchorImport()
    {
        var strategyRepository = new Mock<IStrategyRepository>();
        strategyRepository.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Strategy>
            {
                new()
                {
                    Id = 11,
                    Code = StrategyCode.FourHourRangeReEntry,
                    Name = "Four Hour Range Re-Entry",
                    Description = "test",
                    IsEnabled = true
                }
            });

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol
            {
                Id = 1,
                ExchangeId = 1,
                SymbolName = "BNBUSDT"
            });

        var service = new StrategyDataRequirementService(strategyRepository.Object, symbolRepository.Object);

        var result = await service.ResolveAsync(new ResolveStrategyRequirementsRequest
        {
            StrategyIds = [11],
            SymbolIds = [1],
            Mode = "Benchmark",
            ExecutionScope = "PreferredOnly"
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        var execution = Assert.Single(result.Data!.ExecutionPlan);
        Assert.Equal("FOUR_HOUR_RANGE_REENTRY", execution.StrategyCode);
        Assert.Equal(["5m"], execution.ExecutionTimeframes);
        Assert.Equal(["5m", "4h"], execution.RequiredDataTimeframes);
        Assert.Equal(["4h"], execution.AnchorTimeframes);
        Assert.Empty(execution.RequiredIndicatorTimeframes);

        Assert.Contains(result.Data.ImportPlan, item =>
            item.Symbol == "BNBUSDT" &&
            item.Timeframe == "4h" &&
            item.Reason.Contains("anchor", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Data.ImportPlan, item =>
            item.Symbol == "BNBUSDT" &&
            item.Timeframe == "5m" &&
            item.Reason.Contains("execution", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResolveAsync_RejectsInvalidManualOverrideForFourHourRangeReEntry()
    {
        var strategyRepository = new Mock<IStrategyRepository>();
        strategyRepository.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Strategy>
            {
                new()
                {
                    Id = 11,
                    Code = StrategyCode.FourHourRangeReEntry,
                    Name = "Four Hour Range Re-Entry",
                    Description = "test",
                    IsEnabled = true
                }
            });

        var service = new StrategyDataRequirementService(strategyRepository.Object, Mock.Of<ISymbolRepository>());
        var result = await service.ResolveAsync(new ResolveStrategyRequirementsRequest
        {
            StrategyIds = [11],
            Mode = "Benchmark",
            ExecutionScope = "ManualOverride",
            ManualExecutionTimeframes = ["3m"]
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Contains(result.Data!.BlockingIssues, issue =>
            issue.Contains("only supports 5m execution", StringComparison.OrdinalIgnoreCase));
    }
}
