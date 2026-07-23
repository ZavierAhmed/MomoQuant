using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Replay;
using MomoQuant.Application.Replay.Dtos;
using MomoQuant.Application.Strategies;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Replay;
using MomoQuant.Domain.Sessions;

namespace MomoQuant.UnitTests.Replay;

public class ReplayMapperTests
{
    [Theory]
    [InlineData("ManualStep", ReplaySpeed.ManualStep)]
    [InlineData("1x", ReplaySpeed.Speed1x)]
    [InlineData("10x", ReplaySpeed.Speed10x)]
    public void TryParseSpeed_ParsesKnownValues(string input, ReplaySpeed expected)
    {
        var parsed = ReplayMapper.TryParseSpeed(input, out var speed);

        Assert.True(parsed);
        Assert.Equal(expected, speed);
    }

    [Fact]
    public void TryParseSpeed_RejectsInvalidValue()
    {
        var parsed = ReplayMapper.TryParseSpeed("invalid", out _);

        Assert.False(parsed);
    }
}

public class ReplayControlServiceTests
{
    [Fact]
    public async Task StepForward_AtFinalFrame_CompletesSession()
    {
        var session = CreateSession(currentFrameIndex: -1, totalFrames: 1, status: ReplaySessionStatus.Running);
        var state = CreateRuntimeState(session);

        var replaySessionRepository = new Mock<IReplaySessionRepository>();
        replaySessionRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        replaySessionRepository.Setup(repo => repo.UpdateAsync(It.IsAny<ReplaySession>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        replaySessionRepository.Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var frameRepository = new Mock<IReplayFrameRepository>();
        frameRepository.Setup(repo => repo.GetTrackedBySessionAndIndexAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReplayFrame?)null);
        frameRepository.Setup(repo => repo.AddAsync(It.IsAny<ReplayFrame>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        frameRepository.Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var tradingSessionRepository = new Mock<ITradingSessionRepository>();
        tradingSessionRepository.Setup(repo => repo.GetByIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradingSession { Id = 1, Status = TradingSessionStatus.Running });
        tradingSessionRepository.Setup(repo => repo.UpdateAsync(It.IsAny<TradingSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        tradingSessionRepository.Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 1, SymbolName = "BTCUSDT" });

        var stateStore = new Mock<IReplayStateStore>();
        stateStore.Setup(store => store.TryGet(1, out state)).Returns(true);

        var replayEngine = new Mock<IReplayEngine>();
        replayEngine.Setup(engine => engine.ProcessFrameAsync(state!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplayStepResult
            {
                Candle = state!.Dataset.Candles[state.Dataset.EvaluationIndices[0]],
                StrategyResults = [],
                MarketRegime = MarketRegime.Unknown,
                Balance = 10_000m,
                Equity = 10_000m,
                Drawdown = 0m,
                DrawdownPercent = 0m,
                Explanation = "Processed frame."
            });

        var persistence = new Mock<IReplayPersistenceService>();
        persistence.Setup(service => service.PersistStepEntitiesAsync(
                It.IsAny<BacktestContext>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var auditService = new Mock<IAuditService>();
        auditService.Setup(service => service.LogAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long?>(),
                It.IsAny<long?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(service => service.UserId).Returns(1);

        var service = new ReplayControlService(
            replaySessionRepository.Object,
            frameRepository.Object,
            tradingSessionRepository.Object,
            symbolRepository.Object,
            stateStore.Object,
            replayEngine.Object,
            persistence.Object,
            currentUser.Object,
            auditService.Object);

        var result = await service.StepForwardAsync(1);

        Assert.True(result.Succeeded);
        Assert.Equal(nameof(ReplaySessionStatus.Completed), result.Data!.Status);
    }

    [Fact]
    public async Task PauseAsync_ChangesStatusToPaused()
    {
        var session = CreateSession(currentFrameIndex: 0, totalFrames: 10, status: ReplaySessionStatus.Running);
        var state = CreateRuntimeState(session);

        var replaySessionRepository = new Mock<IReplaySessionRepository>();
        replaySessionRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        replaySessionRepository.Setup(repo => repo.UpdateAsync(It.IsAny<ReplaySession>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        replaySessionRepository.Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var stateStore = new Mock<IReplayStateStore>();
        stateStore.Setup(store => store.TryGet(1, out state)).Returns(true);

        var auditService = new Mock<IAuditService>();
        auditService.Setup(service => service.LogAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long?>(),
                It.IsAny<long?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new ReplayControlService(
            replaySessionRepository.Object,
            new Mock<IReplayFrameRepository>().Object,
            new Mock<ITradingSessionRepository>().Object,
            new Mock<ISymbolRepository>().Object,
            stateStore.Object,
            new Mock<IReplayEngine>().Object,
            new Mock<IReplayPersistenceService>().Object,
            new Mock<ICurrentUserService>().Object,
            auditService.Object);

        var result = await service.PauseAsync(1);

        Assert.True(result.Succeeded);
        Assert.Equal(nameof(ReplaySessionStatus.Paused), result.Data!.Status);
    }

    [Fact]
    public async Task StepForward_WhenFrameAlreadyExists_UpdatesInsteadOfInserting()
    {
        var session = CreateSession(currentFrameIndex: 4, totalFrames: 10, status: ReplaySessionStatus.Running);
        var state = CreateRuntimeState(session, frameCount: 10);
        state.CurrentFrameIndex = session.CurrentFrameIndex;

        var existingFrame = new ReplayFrame
        {
            Id = 99,
            ReplaySessionId = 1,
            FrameIndex = 5,
            CandleId = 10,
            TimestampUtc = DateTime.UtcNow,
            Balance = 10_000m,
            Equity = 10_000m,
            CreatedAtUtc = DateTime.UtcNow
        };

        var replaySessionRepository = new Mock<IReplaySessionRepository>();
        replaySessionRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        replaySessionRepository.Setup(repo => repo.UpdateAsync(It.IsAny<ReplaySession>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        replaySessionRepository.Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var frameRepository = new Mock<IReplayFrameRepository>();
        frameRepository.Setup(repo => repo.GetTrackedBySessionAndIndexAsync(1, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingFrame);
        frameRepository.Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var tradingSessionRepository = new Mock<ITradingSessionRepository>();
        tradingSessionRepository.Setup(repo => repo.GetByIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradingSession { Id = 1, Status = TradingSessionStatus.Running });
        tradingSessionRepository.Setup(repo => repo.UpdateAsync(It.IsAny<TradingSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        tradingSessionRepository.Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 1, SymbolName = "BTCUSDT" });

        var stateStore = new Mock<IReplayStateStore>();
        stateStore.Setup(store => store.TryGet(1, out state)).Returns(true);

        var replayEngine = new Mock<IReplayEngine>();
        replayEngine.Setup(engine => engine.ProcessFrameAsync(state!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplayStepResult
            {
                Candle = state!.Dataset.Candles[state.Dataset.EvaluationIndices[5]],
                StrategyResults = [],
                MarketRegime = MarketRegime.Unknown,
                Balance = 9_500m,
                Equity = 9_500m,
                Drawdown = 500m,
                DrawdownPercent = 5m,
                Explanation = "Reprocessed frame."
            });

        var persistence = new Mock<IReplayPersistenceService>();
        persistence.Setup(service => service.PersistStepEntitiesAsync(
                It.IsAny<BacktestContext>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var auditService = new Mock<IAuditService>();
        auditService.Setup(service => service.LogAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long?>(),
                It.IsAny<long?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new ReplayControlService(
            replaySessionRepository.Object,
            frameRepository.Object,
            tradingSessionRepository.Object,
            symbolRepository.Object,
            stateStore.Object,
            replayEngine.Object,
            persistence.Object,
            new Mock<ICurrentUserService>().Object,
            auditService.Object);

        var result = await service.StepForwardAsync(1);

        Assert.True(result.Succeeded);
        Assert.Equal(5, result.Data!.CurrentFrameIndex);
        Assert.Equal(9_500m, existingFrame.Balance);
        Assert.Equal(9_500m, existingFrame.Equity);
        frameRepository.Verify(repo => repo.AddAsync(It.IsAny<ReplayFrame>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StepBackward_DeletesFramesAfterTargetIndex()
    {
        var session = CreateSession(currentFrameIndex: 5, totalFrames: 10, status: ReplaySessionStatus.Paused);
        var state = CreateRuntimeState(session, frameCount: 10);
        state.CurrentFrameIndex = session.CurrentFrameIndex;

        var replaySessionRepository = new Mock<IReplaySessionRepository>();
        replaySessionRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        replaySessionRepository.Setup(repo => repo.UpdateAsync(It.IsAny<ReplaySession>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        replaySessionRepository.Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var frameRepository = new Mock<IReplayFrameRepository>();
        frameRepository.Setup(repo => repo.DeleteAfterFrameIndexAsync(1, 4, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        frameRepository.Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        frameRepository.Setup(repo => repo.GetBySessionAndIndexAsync(1, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReplayFrame?)null);

        var symbolRepository = new Mock<ISymbolRepository>();
        symbolRepository.Setup(repo => repo.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Symbol { Id = 1, SymbolName = "BTCUSDT" });

        var stateStore = new Mock<IReplayStateStore>();
        stateStore.Setup(store => store.TryGet(1, out state)).Returns(true);

        var replayEngine = new Mock<IReplayEngine>();
        replayEngine.Setup(engine => engine.RebuildToFrameAsync(state!, 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReplayRuntimeState state, int _, CancellationToken _) =>
            {
                state.CurrentFrameIndex = 4;
                return state;
            });

        var service = new ReplayControlService(
            replaySessionRepository.Object,
            frameRepository.Object,
            new Mock<ITradingSessionRepository>().Object,
            symbolRepository.Object,
            stateStore.Object,
            replayEngine.Object,
            new Mock<IReplayPersistenceService>().Object,
            new Mock<ICurrentUserService>().Object,
            new Mock<IAuditService>().Object);

        var result = await service.StepBackwardAsync(1);

        Assert.True(result.Succeeded);
        Assert.Equal(4, result.Data!.CurrentFrameIndex);
        frameRepository.Verify(repo => repo.DeleteAfterFrameIndexAsync(1, 4, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ReplaySession CreateSession(int currentFrameIndex, int totalFrames, ReplaySessionStatus status) => new()
    {
        Id = 1,
        Name = "Test Replay",
        TradingSessionId = 1,
        ExchangeId = 1,
        SymbolId = 1,
        Timeframe = Timeframe.M3,
        FromUtc = DateTime.UtcNow.AddDays(-1),
        ToUtc = DateTime.UtcNow,
        InitialBalance = 10_000m,
        CurrentBalance = 10_000m,
        CurrentEquity = 10_000m,
        RiskProfileId = 1,
        ExecutionMode = ExecutionMode.MarketFill,
        Speed = ReplaySpeed.ManualStep,
        Status = status,
        CurrentFrameIndex = currentFrameIndex,
        TotalFrames = totalFrames,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static ReplayRuntimeState CreateRuntimeState(ReplaySession session, int frameCount = 1)
    {
        var openTime = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var candles = new List<Candle>();
        var evaluationIndices = new List<int>();
        var indicatorSnapshots = new Dictionary<long, IndicatorSnapshot>();

        for (var i = 0; i < frameCount; i++)
        {
            var candleOpen = openTime.AddMinutes(i * 3);
            var candle = new Candle
            {
                Id = 10 + i,
                SymbolId = 1,
                ExchangeId = 1,
                Timeframe = Timeframe.M3,
                Open = 100m + i,
                High = 101m + i,
                Low = 99m + i,
                Close = 100m + i,
                OpenTimeUtc = candleOpen,
                CloseTimeUtc = candleOpen.AddMinutes(3),
                CreatedAtUtc = DateTime.UtcNow
            };

            candles.Add(candle);
            evaluationIndices.Add(i);
            indicatorSnapshots[candle.Id] = new IndicatorSnapshot
            {
                SymbolId = 1,
                Timeframe = Timeframe.M3,
                CandleId = candle.Id,
                Ema20 = 99m,
                Ema50 = 98m,
                Ema200 = 97m,
                CalculatedAtUtc = candle.CloseTimeUtc,
                CreatedAtUtc = DateTime.UtcNow
            };
        }

        var dataset = new BacktestDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M3,
            Candles = candles,
            IndicatorSnapshots = indicatorSnapshots,
            EvaluationIndices = evaluationIndices
        };

        var settings = new ReplaySessionSettings
        {
            MakerFeeRate = 0.0002m,
            TakerFeeRate = 0.0005m,
            OrderExpiryCandles = 3,
            UseAiScoring = false,
            MinConfidenceScore = 80m,
            SlippagePercent = 0m,
            ExecutionMode = ExecutionMode.MarketFill,
            StrategyIds = [1]
        };

        return ReplayEngine.CreateRuntimeState(
            settings,
            session,
            dataset,
            [],
            [],
            new Symbol { Id = 1, ExchangeId = 1, SymbolName = "BTCUSDT" });
    }
}

public class ReplaySessionServiceValidationTests
{
    [Fact]
    public async Task CreateAsync_RejectsInvalidDateRange()
    {
        var service = CreateService();
        var request = new CreateReplaySessionRequest
        {
            Name = "Invalid",
            ExchangeId = 1,
            SymbolId = 1,
            Timeframe = "3m",
            FromUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            InitialBalance = 10_000m,
            RiskProfileId = 1,
            StrategyIds = [1]
        };

        var result = await service.CreateAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal("fromUtc", result.ErrorField);
    }

    private static ReplaySessionService CreateService()
    {
        return new ReplaySessionService(
            new Mock<IReplaySessionRepository>().Object,
            new Mock<ITradingSessionRepository>().Object,
            new Mock<IExchangeRepository>().Object,
            new Mock<ISymbolRepository>().Object,
            new Mock<IRiskProfileRepository>().Object,
            new Mock<IStrategyRepository>().Object,
            new Mock<IStrategyRegistry>().Object,
            new Mock<IRiskRuleRepository>().Object,
            new Mock<IReplayDataLoader>().Object,
            new ReplayStateStore(),
            new Mock<ICurrentUserService>().Object,
            new Mock<IAuditService>().Object);
    }
}
