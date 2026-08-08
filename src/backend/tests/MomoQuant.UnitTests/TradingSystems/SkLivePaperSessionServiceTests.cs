using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.LiveMarket.Dtos;
using MomoQuant.Application.TradingSystems;
using MomoQuant.Application.TradingSystems.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.TradingSystems;

namespace MomoQuant.UnitTests.TradingSystems;

public class SkLivePaperSessionServiceTests
{
    private readonly Mock<ISkLivePaperSessionRepository> _sessionRepository = new();
    private readonly Mock<ISkLivePaperTradeRepository> _tradeRepository = new();
    private readonly Mock<ISkLivePaperCandidateRepository> _candidateRepository = new();
    private readonly Mock<ISkLivePaperEventRepository> _eventRepository = new();
    private readonly Mock<ISymbolRepository> _symbolRepository = new();
    private readonly Mock<IExchangeRepository> _exchangeRepository = new();
    private readonly Mock<ILiveMarketConnectionManager> _liveMarket = new();
    private readonly Mock<ISkSystemAnalysisService> _skAnalysisService = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();
    private readonly Mock<IAuditService> _auditService = new();

    private SkLivePaperSessionService BuildService() => new(
        _sessionRepository.Object,
        _tradeRepository.Object,
        _candidateRepository.Object,
        _eventRepository.Object,
        _symbolRepository.Object,
        _exchangeRepository.Object,
        _liveMarket.Object,
        _skAnalysisService.Object,
        new SkLivePaperDiagnosticsStore(),
        _currentUserService.Object,
        _auditService.Object);

    [Fact]
    public async Task CreateSessionAsync_InvalidTimeframePair_IsRejected()
    {
        _symbolRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 1, ExchangeId = 1, SymbolName = "BTCUSDT" });
        _exchangeRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Exchange { Id = 1, Name = "Binance Futures", Code = "BINANCE_FUTURES" });

        var result = await BuildService().CreateSessionAsync(new CreateSkLivePaperSessionRequest
        {
            SessionName = "Test",
            ExchangeId = 1,
            SymbolId = 1,
            HigherTimeframe = "15m",
            PrimaryTimeframe = "1h"
        });

        Assert.False(result.Succeeded);
        Assert.Equal("timeframe", result.ErrorField);
    }

    [Fact]
    public async Task StartAsync_InvalidTimeframePair_IsRejected()
    {
        _sessionRepository.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SkLivePaperSession
            {
                Id = 1,
                ExchangeId = 1,
                SymbolId = 1,
                Symbol = "BTCUSDT",
                HigherTimeframe = "1h",
                PrimaryTimeframe = "1h",
                Status = SkLivePaperSessionStatus.Created
            });

        var result = await BuildService().StartAsync(1);

        Assert.False(result.Succeeded);
        Assert.Equal("timeframe", result.ErrorField);
        _liveMarket.Verify(
            m => m.SubscribeAsync(It.IsAny<LiveMarketSubscribeRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Milestone231B1C6D1Limit_ListSessionsAsync_NormalizesBeforeForwardingAndPreservesCancellation()
    {
        var cancellation = new CancellationTokenSource();
        _sessionRepository.Setup(r => r.GetRecentAsync(200, cancellation.Token))
            .ReturnsAsync([new SkLivePaperSession { Id = 17 }]);
        _tradeRepository.Setup(r => r.GetBySessionAsync(17, cancellation.Token))
            .ReturnsAsync([]);

        var result = await BuildService().ListSessionsAsync(int.MaxValue, cancellation.Token);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        _sessionRepository.Verify(r => r.GetRecentAsync(200, cancellation.Token), Times.Once);
        _sessionRepository.Verify(r => r.GetRecentAsync(int.MaxValue, It.IsAny<CancellationToken>()), Times.Never);

        _sessionRepository.Setup(r => r.GetRecentAsync(50, cancellation.Token)).ReturnsAsync([]);
        await BuildService().ListSessionsAsync(0, cancellation.Token);
        _sessionRepository.Verify(r => r.GetRecentAsync(50, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task Milestone231B1C6D1Limit_GetCandidatesAsync_NormalizesBeforeForwardingAndPreservesSessionId()
    {
        var cancellation = new CancellationTokenSource();
        _candidateRepository.Setup(r => r.GetBySessionAsync(23, 500, cancellation.Token))
            .ReturnsAsync([new SkLivePaperCandidate { Id = 1, SessionId = 23 }]);

        var result = await BuildService().GetCandidatesAsync(23, int.MaxValue, cancellation.Token);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        _candidateRepository.Verify(r => r.GetBySessionAsync(23, 500, cancellation.Token), Times.Once);
        _candidateRepository.Verify(r => r.GetBySessionAsync(23, int.MaxValue, It.IsAny<CancellationToken>()), Times.Never);

        _candidateRepository.Setup(r => r.GetBySessionAsync(23, 100, cancellation.Token)).ReturnsAsync([]);
        await BuildService().GetCandidatesAsync(23, -1, cancellation.Token);
        _candidateRepository.Verify(r => r.GetBySessionAsync(23, 100, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task Milestone231B1C6D1Limit_GetEventsAsync_NormalizesBeforeForwardingAndPreservesSessionId()
    {
        var cancellation = new CancellationTokenSource();
        _eventRepository.Setup(r => r.GetBySessionAsync(29, 1000, cancellation.Token))
            .ReturnsAsync([new SkLivePaperEvent { Id = 1, SessionId = 29 }]);

        var result = await BuildService().GetEventsAsync(29, int.MaxValue, cancellation.Token);

        Assert.True(result.Succeeded);
        Assert.Single(result.Data!);
        _eventRepository.Verify(r => r.GetBySessionAsync(29, 1000, cancellation.Token), Times.Once);
        _eventRepository.Verify(r => r.GetBySessionAsync(29, int.MaxValue, It.IsAny<CancellationToken>()), Times.Never);

        _eventRepository.Setup(r => r.GetBySessionAsync(29, 200, cancellation.Token)).ReturnsAsync([]);
        await BuildService().GetEventsAsync(29, 0, cancellation.Token);
        _eventRepository.Verify(r => r.GetBySessionAsync(29, 200, cancellation.Token), Times.Once);
    }
}
