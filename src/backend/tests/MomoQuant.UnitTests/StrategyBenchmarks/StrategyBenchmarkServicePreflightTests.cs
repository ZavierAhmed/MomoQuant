using Microsoft.Extensions.Options;
using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.MarketData.Dtos;
using MomoQuant.Application.Options;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Application.StrategyBenchmarks;
using MomoQuant.Application.StrategyBenchmarks.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.StrategyBenchmarks;

public class StrategyBenchmarkServicePreflightTests
{
    [Fact]
    public async Task Preflight_ForFourHourRangeReEntry_Includes4hImportAnd5mExecutionOnly()
    {
        var exchangeRepository = new Mock<IExchangeRepository>();
        exchangeRepository.Setup(repo => repo.GetByCodeAsync("BINANCE_FUTURES", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Exchange { Id = 1, Code = "BINANCE_FUTURES", Name = "Binance Futures" });

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByExchangeAndNameAsync(1, "BNBUSDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 10, ExchangeId = 1, SymbolName = "BNBUSDT" });

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
                    Version = "1.0.0",
                    IsEnabled = true
                }
            });

        var requirementService = new Mock<IStrategyDataRequirementService>();
        requirementService.Setup(service => service.ResolveAsync(
                It.IsAny<ResolveStrategyRequirementsRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MomoQuant.Application.Common.ServiceResult<ResolveStrategyRequirementsResponse>.Ok(
                new ResolveStrategyRequirementsResponse
                {
                    RequiredTimeframes = ["5m", "4h"],
                    ExecutionPlan =
                    [
                        new StrategyExecutionPlanItemDto
                        {
                            StrategyId = 11,
                            StrategyCode = "FOUR_HOUR_RANGE_REENTRY",
                            StrategyName = "Four Hour Range Re-Entry",
                            PreferredExecutionTimeframe = "5m",
                            ExecutionTimeframes = ["5m"],
                            RequiredDataTimeframes = ["5m", "4h"],
                            RequiredIndicatorTimeframes = [],
                            AnchorTimeframes = ["4h"]
                        }
                    ],
                    ImportPlan =
                    [
                        new StrategyImportPlanItemDto
                        {
                            SymbolId = 10,
                            Symbol = "BNBUSDT",
                            Timeframe = "5m",
                            Reason = "Required by FOUR_HOUR_RANGE_REENTRY execution"
                        },
                        new StrategyImportPlanItemDto
                        {
                            SymbolId = 10,
                            Symbol = "BNBUSDT",
                            Timeframe = "4h",
                            Reason = "Required by FOUR_HOUR_RANGE_REENTRY anchor range"
                        }
                    ],
                    Warnings = [],
                    BlockingIssues = []
                }));

        var marketDataService = new Mock<IMarketDataService>();
        marketDataService.Setup(service => service.GetDataQualityAsync(
                1,
                10,
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MomoQuant.Application.Common.ServiceResult<MarketDataQualityDto>.Ok(
                new MarketDataQualityDto
                {
                    ExchangeId = 1,
                    SymbolId = 10,
                    Timeframe = "5m",
                    FromUtc = new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc),
                    ToUtc = new DateTime(2026, 6, 10, 23, 59, 59, DateTimeKind.Utc),
                    TotalCandles = 1,
                    ExpectedCandles = 1,
                    MissingCandles = 0,
                    DuplicateCandles = 0,
                    CoveragePercent = 100m,
                    Gaps = []
                }));

        var service = new StrategyBenchmarkService(
            Mock.Of<IStrategyBenchmarkRunRepository>(),
            Mock.Of<IStrategyBenchmarkRunItemRepository>(),
            Mock.Of<IStrategyBenchmarkResultRepository>(),
            exchangeRepository.Object,
            symbolRepository.Object,
            strategyRepository.Object,
            Mock.Of<IRiskProfileRepository>(),
            Mock.Of<IStrategyBenchmarkQueue>(),
            Mock.Of<IStrategyBenchmarkReportService>(),
            requirementService.Object,
            marketDataService.Object,
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>(),
            Options.Create(new StrategyBenchmarkSettings()));

        var result = await service.PreflightAsync(new StrategyBenchmarkPreflightRequest
        {
            ExchangeCode = "BINANCE_FUTURES",
            Symbols = ["BNBUSDT"],
            StrategyIds = [11],
            BenchmarkFromDate = new DateOnly(2026, 6, 1),
            BenchmarkToDate = new DateOnly(2026, 6, 10),
            WarmupFromDate = new DateOnly(2026, 5, 25),
            ExecutionTimeframeMode = "AutoSelectByStrategy",
            StrategyExecutionScope = "PreferredOnly"
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(1, result.Data!.EstimatedTotalRuns);
        var executionRun = Assert.Single(result.Data.ResolvedExecutionRuns);
        Assert.Equal(["5m"], executionRun.ExecutionTimeframes);
        Assert.Equal(["5m", "4h"], executionRun.RequiredDataTimeframes);
        Assert.Empty(executionRun.RequiredIndicatorTimeframes);
        Assert.Contains(result.Data.RequiredImportTimeframes, item => item.Timeframe == "4h" && item.IsAnchorData);
        Assert.DoesNotContain(result.Data.ResolvedExecutionRuns.SelectMany(item => item.ExecutionTimeframes), tf => tf == "4h");
    }
}
