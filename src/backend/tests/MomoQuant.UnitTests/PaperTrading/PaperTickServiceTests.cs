using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.PaperTrading;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.PaperTrading;

namespace MomoQuant.UnitTests.PaperTrading;

public class PaperTickServiceTests
{
    [Fact]
    public async Task TickAsync_RejectsWhenSessionNotRunning()
    {
        var session = CreateSession(PaperSessionStatus.Paused);
        var repository = new Mock<IPaperTradingSessionRepository>();
        repository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var service = CreateControlService(repository.Object);
        var result = await service.TickAsync(1);

        Assert.False(result.Succeeded);
        Assert.Contains("not running", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static PaperSessionControlService CreateControlService(IPaperTradingSessionRepository repository) => new(
        repository,
        new Mock<ITradingSessionRepository>().Object,
        new Mock<IPaperStateStore>().Object,
        new Mock<IPaperTradingEngine>().Object,
        new Mock<IPaperPersistenceService>().Object,
        new Mock<ILiveMarketConnectionManager>().Object,
        new Mock<ILiveMarketBootstrapService>().Object,
        new Mock<ICurrentUserService>().Object,
        new Mock<IAuditService>().Object);

    private static PaperTradingSession CreateSession(PaperSessionStatus status) => new()
    {
        Id = 1,
        Name = "Test",
        PaperAccountId = 1,
        TradingSessionId = 1,
        Status = status,
        Mode = PaperTradingMode.HistoricalPaper,
        ExchangeId = 1,
        RiskProfileId = 1,
        ExecutionMode = ExecutionMode.MakerOnly,
        TotalCandles = 10,
        CreatedAtUtc = DateTime.UtcNow
    };
}
