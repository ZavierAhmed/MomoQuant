using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.MarketSituation;
using MomoQuant.Application.PaperTrading;
using MomoQuant.Application.PaperTrading.Dtos;
using MomoQuant.Application.Strategies;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Sessions;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

public sealed class Milestone231B1C6APaperResearchTests
{
    [Fact]
    public async Task LivePaper_AcceptsResearchApprovedResearchOnlyParameterSet()
    {
        const long strategyId = 10;
        const long parameterSetId = 20;
        var strategy = new Strategy
        {
            Id = strategyId,
            Code = StrategyCode.PriceStructureBreakoutRetest,
            Name = "PSBR",
            Version = "1.1",
            IsEnabled = true
        };
        var parameterSet = new StrategyParameterSet
        {
            Id = parameterSetId,
            Name = "Research approved",
            StrategyCode = strategy.Code.ToCode(),
            Timeframe = "15m",
            ParametersJson = "{\"breakoutLookback\":\"20\"}",
            IsApproved = true,
            QualificationStatus = ParameterSetQualificationStatus.ResearchOnly,
            CreatedAtUtc = DateTime.UtcNow
        };

        var sessions = new Mock<IPaperTradingSessionRepository>();
        sessions.Setup(repository => repository.AddAsync(It.IsAny<PaperTradingSession>(), It.IsAny<CancellationToken>()))
            .Callback<PaperTradingSession, CancellationToken>((session, _) => session.Id = 31)
            .Returns(Task.CompletedTask);
        sessions.Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var tradingSessions = new Mock<ITradingSessionRepository>();
        tradingSessions.Setup(repository => repository.AddAsync(It.IsAny<TradingSession>(), It.IsAny<CancellationToken>()))
            .Callback<TradingSession, CancellationToken>((session, _) => session.Id = 30)
            .Returns(Task.CompletedTask);
        tradingSessions.Setup(repository => repository.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var accounts = new Mock<IPaperAccountRepository>();
        accounts.Setup(repository => repository.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaperAccount
            {
                Id = 1,
                Name = "Research paper",
                CurrentBalance = 10_000m,
                CurrentEquity = 10_000m,
                IsActive = true
            });

        var exchanges = new Mock<IExchangeRepository>();
        exchanges.Setup(repository => repository.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Exchange { Id = 2, Code = "BINANCE", Name = "Binance" });

        var symbols = new Mock<ISymbolRepository>();
        symbols.Setup(repository => repository.GetByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 3, ExchangeId = 2, SymbolName = "BTCUSDT" });

        var riskProfiles = new Mock<IRiskProfileRepository>();
        riskProfiles.Setup(repository => repository.GetByIdAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskProfile { Id = 4, Name = "Research" });

        var strategies = new Mock<IStrategyRepository>();
        strategies.Setup(repository => repository.GetByIdAsync(strategyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategy);
        strategies.Setup(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([strategy]);

        var plugin = new Mock<ITradingStrategy>();
        plugin.SetupGet(item => item.Code).Returns(strategy.Code);
        var registry = new Mock<IStrategyRegistry>();
        registry.Setup(item => item.GetByCode(strategy.Code)).Returns(plugin.Object);

        var parameterSets = new Mock<IStrategyParameterSetRepository>();
        parameterSets.Setup(repository => repository.GetByIdAsync(parameterSetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parameterSet);
        var parameterProvider = new Mock<IStrategyParameterProvider>();
        parameterProvider.Setup(provider => provider.GetParametersFromSetAsync(parameterSetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["breakoutLookback"] = "20" });

        var riskRules = new Mock<IRiskRuleRepository>();
        riskRules.Setup(repository => repository.GetByProfileIdAsync(4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RiskRule>());

        var dataLoader = new Mock<IBacktestDataLoader>();
        dataLoader.Setup(loader => loader.LoadSymbolTimeframeAsync(
                2, 3, Timeframe.M15, It.IsAny<DateTime>(), It.IsAny<DateTime>(), 600, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BacktestDataset?)null);

        var live = new Mock<ILiveMarketConnectionManager>();
        live.SetupGet(manager => manager.IsAvailable).Returns(true);

        var enricher = new Mock<IHigherTimeframeDatasetEnricher>();
        enricher.Setup(service => service.EnrichForStrategiesAsync(
                It.IsAny<BacktestDataset>(), It.IsAny<IReadOnlyList<PreparedStrategy>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BacktestDataset dataset, IReadOnlyList<PreparedStrategy> _, CancellationToken _) => dataset);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns(5);

        var service = new PaperSessionService(
            sessions.Object,
            accounts.Object,
            tradingSessions.Object,
            exchanges.Object,
            symbols.Object,
            riskProfiles.Object,
            strategies.Object,
            registry.Object,
            parameterSets.Object,
            parameterProvider.Object,
            riskRules.Object,
            dataLoader.Object,
            Mock.Of<IPaperStateStore>(),
            live.Object,
            Mock.Of<IMarketSituationService>(),
            currentUser.Object,
            Mock.Of<IAuditService>(),
            enricher.Object);

        var result = await service.CreateAsync(new CreatePaperSessionRequest
        {
            Name = "Research-only simulation",
            PaperAccountId = 1,
            ExchangeId = 2,
            SymbolIds = [3],
            Timeframes = ["15m"],
            Mode = "LivePaper",
            RiskProfileId = 4,
            StrategyIds = [strategyId],
            ParameterSetId = parameterSetId,
            AllowAbnormalMarketPaperTrading = true
        });

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal("LivePaper", result.Data!.Mode);
        parameterSets.Verify(repository => repository.GetByIdAsync(parameterSetId, It.IsAny<CancellationToken>()), Times.Once);
        parameterProvider.Verify(provider => provider.GetParametersFromSetAsync(parameterSetId, It.IsAny<CancellationToken>()), Times.Once);
        sessions.Verify(repository => repository.AddAsync(It.IsAny<PaperTradingSession>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
