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
}
