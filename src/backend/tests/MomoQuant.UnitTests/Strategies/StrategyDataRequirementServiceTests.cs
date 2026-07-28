using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

public class StrategyDataRequirementServiceTests
{
    [Fact]
    public async Task ResolveAsync_ArchivedStrategyRejected()
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

        Assert.False(result.Succeeded);
        Assert.Contains("archived", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveAsync_ForMomoAdaptiveMtf_IncludesMappedHigherTimeframeImport()
    {
        var strategyRepository = new Mock<IStrategyRepository>();
        strategyRepository.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Strategy>
            {
                new()
                {
                    Id = 17,
                    Code = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
                    Name = "MOMO Adaptive MTF",
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
                SymbolName = "BTCUSDT"
            });

        var service = new StrategyDataRequirementService(strategyRepository.Object, symbolRepository.Object);

        var result = await service.ResolveAsync(new ResolveStrategyRequirementsRequest
        {
            StrategyIds = [17],
            SymbolIds = [1],
            Mode = "Benchmark",
            ExecutionScope = "PreferredOnly"
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        var execution = Assert.Single(result.Data!.ExecutionPlan);
        Assert.Equal(StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout, execution.StrategyCode);
        Assert.Equal(["5m"], execution.ExecutionTimeframes);
        Assert.Contains("1h", execution.RequiredDataTimeframes);
        Assert.Contains("1h", execution.AnchorTimeframes);
        Assert.Contains(result.Data.ImportPlan, item =>
            item.Symbol == "BTCUSDT" &&
            item.Timeframe == "1h" &&
            item.Reason.Contains("anchor", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Data.ImportPlan, item => item.Timeframe == "4h");
    }

    [Fact]
    public async Task ResolveAsync_RejectsInvalidManualOverrideForMomoAdaptiveMtf()
    {
        var strategyRepository = new Mock<IStrategyRepository>();
        strategyRepository.Setup(repo => repo.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Strategy>
            {
                new()
                {
                    Id = 17,
                    Code = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
                    Name = "MOMO Adaptive MTF",
                    Description = "test",
                    IsEnabled = true
                }
            });

        var service = new StrategyDataRequirementService(strategyRepository.Object, Mock.Of<ISymbolRepository>());
        var result = await service.ResolveAsync(new ResolveStrategyRequirementsRequest
        {
            StrategyIds = [17],
            Mode = "Benchmark",
            ExecutionScope = "ManualOverride",
            ManualExecutionTimeframes = ["3m"]
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Contains(result.Data!.BlockingIssues, issue =>
            issue.Contains("does not support", StringComparison.OrdinalIgnoreCase)
            || issue.Contains("only supports", StringComparison.OrdinalIgnoreCase)
            || issue.Contains("3m", StringComparison.OrdinalIgnoreCase));
    }
}
