using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Ai;
using MomoQuant.Application.Ai.Dtos;
using MomoQuant.Domain.Ai;
using MomoQuant.Domain.Enums;

namespace MomoQuant.UnitTests.Ai;

public class AiDecisionServiceTests
{
    [Fact]
    public async Task PersistEvaluationAsync_PersistsAiDecision()
    {
        AiDecision? persisted = null;
        var repository = new Mock<IAiDecisionRepository>();
        repository.Setup(repo => repo.AddAsync(It.IsAny<AiDecision>(), It.IsAny<CancellationToken>()))
            .Callback<AiDecision, CancellationToken>((decision, _) => persisted = decision)
            .Returns(Task.CompletedTask);
        repository.Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var auditService = new Mock<IAuditService>();
        var currentUserService = new Mock<ICurrentUserService>();

        var service = new AiDecisionService(repository.Object, currentUserService.Object, auditService.Object);
        var result = await service.PersistEvaluationAsync(new PersistAiEvaluationRequest
        {
            TradingSessionId = 10,
            StrategySignalId = 20,
            SymbolId = 1,
            Timeframe = Timeframe.M3,
            CandleId = 30,
            StrategyCode = StrategyCode.EmaPullback,
            Regime = new DetectRegimeResponseDto
            {
                Regime = "Trending",
                Confidence = 75,
                Reasons = ["Trend detected"]
            },
            Confidence = new ScoreConfidenceResponseDto
            {
                ConfidenceScore = 82,
                Classification = "High",
                Reasons = ["Good alignment"],
                Warnings = []
            },
            Anomaly = new DetectAnomalyResponseDto
            {
                IsAnomalous = false,
                Severity = "None",
                Reasons = []
            },
            TradeAllowed = true,
            RegimeRequest = new DetectRegimeRequestDto { Symbol = "BTCUSDT", Timeframe = "3m" },
            ConfidenceRequest = new ScoreConfidenceRequestDto
            {
                Symbol = "BTCUSDT",
                Timeframe = "3m",
                StrategyCode = "EMA_PULLBACK",
                SignalDirection = "Long",
                MarketRegime = "Trending",
                StrategyStrength = 70m
            }
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(persisted);
        Assert.Equal(20, persisted.SignalId);
        Assert.Equal(MarketRegime.Trending, persisted.MarketRegime);
        Assert.Equal(82m, persisted.ConfidenceScore);
        Assert.True(persisted.TradeAllowed);
        Assert.NotNull(persisted.RawRequestJson);
        Assert.NotNull(persisted.RawResponseJson);

        auditService.Verify(
            audit => audit.LogAsync(
                "AI_DECISION_PERSISTED",
                nameof(AiDecision),
                It.IsAny<long?>(),
                It.IsAny<long?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PersistEvaluationAsync_DoesNotCreateOrdersOrTrades()
    {
        var repository = new Mock<IAiDecisionRepository>();
        repository.Setup(repo => repo.AddAsync(It.IsAny<AiDecision>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository.Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new AiDecisionService(
            repository.Object,
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IAuditService>());

        await service.PersistEvaluationAsync(CreatePersistRequest());

        repository.Verify(repo => repo.AddAsync(It.IsAny<AiDecision>(), It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static PersistAiEvaluationRequest CreatePersistRequest() => new()
    {
        StrategySignalId = 1,
        SymbolId = 1,
        Timeframe = Timeframe.M3,
        StrategyCode = StrategyCode.EmaPullback,
        Regime = AiFallbackFactory.CreateRegimeFallback(),
        Confidence = AiFallbackFactory.CreateConfidenceFallback(),
        TradeAllowed = false,
        RegimeRequest = new DetectRegimeRequestDto { Symbol = "BTCUSDT", Timeframe = "3m" },
        ConfidenceRequest = new ScoreConfidenceRequestDto
        {
            Symbol = "BTCUSDT",
            Timeframe = "3m",
            StrategyCode = "EMA_PULLBACK",
            SignalDirection = "Long",
            MarketRegime = "Unknown",
            StrategyStrength = 0m
        }
    };
}
