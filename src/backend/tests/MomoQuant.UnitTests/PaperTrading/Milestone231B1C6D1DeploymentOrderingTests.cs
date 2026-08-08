using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Audit;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Common;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.LiveMarket.Dtos;
using MomoQuant.Application.PaperTrading;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Sessions;

namespace MomoQuant.UnitTests.PaperTrading;

public sealed class Milestone231B1C6D1DeploymentOrderingTests
{
    [Fact]
    public async Task Start_CommitsDurableEvidenceBeforeRuntimeBootstrapSubscriptionAndLink()
    {
        var fixture = CreateFixture(bootstrapSucceeds: true);

        var result = await fixture.Service.StartAsync(fixture.Session.Id);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(PaperSessionStatus.Running, fixture.Session.Status);
        Assert.Equal(TradingSessionStatus.Running, fixture.TradingSession.Status);
        Assert.Equal(1, fixture.Coordinator.TransactionCalls);
        Assert.Equal(
            [RequiredAuditActions.PaperDeploymentQualificationVerified, RequiredAuditActions.PaperSessionStarted],
            fixture.RequiredActions);
        fixture.StateStore.Verify(store => store.Set(fixture.Session.Id, fixture.State), Times.Once);
        fixture.Live.Verify(manager => manager.SubscribeAsync(
            It.IsAny<LiveMarketSubscribeRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Live.Verify(manager => manager.LinkSession(fixture.Session.Id, 404, Timeframe.M15), Times.Once);
    }

    [Fact]
    public async Task Start_PostCommitBootstrapFailure_RemovesRuntimeAndCompensatesBothRowsWithRequiredEvidence()
    {
        var fixture = CreateFixture(bootstrapSucceeds: false);

        var result = await fixture.Service.StartAsync(fixture.Session.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(PaperDeploymentQualificationCodes.RuntimeActivationFailed, result.ErrorField);
        Assert.Equal(PaperSessionStatus.Failed, fixture.Session.Status);
        Assert.Equal(TradingSessionStatus.Failed, fixture.TradingSession.Status);
        Assert.Equal(PaperDeploymentQualificationCodes.RuntimeActivationFailed, fixture.Session.ErrorMessage);
        Assert.Equal(2, fixture.Coordinator.TransactionCalls);
        Assert.Equal(
            [
                RequiredAuditActions.PaperDeploymentQualificationVerified,
                RequiredAuditActions.PaperSessionStarted,
                RequiredAuditActions.PaperSessionFailed
            ],
            fixture.RequiredActions);
        fixture.StateStore.Verify(store => store.Remove(fixture.Session.Id), Times.Once);
        fixture.Live.Verify(manager => manager.UnlinkSession(fixture.Session.Id), Times.Once);
        fixture.Live.Verify(manager => manager.SubscribeAsync(
            It.IsAny<LiveMarketSubscribeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static OrderingFixture CreateFixture(bool bootstrapSucceeds)
    {
        var session = new PaperTradingSession
        {
            Id = 1,
            Name = "Deployment",
            PaperAccountId = 2,
            TradingSessionId = 3,
            Status = PaperSessionStatus.Created,
            Mode = PaperTradingMode.LivePaper,
            UseClass = PaperSessionUseClass.DeploymentSimulation,
            ParameterSetId = 101,
            BoundStrategyId = 303,
            BoundSymbolId = 404,
            BoundTimeframe = "15m",
            QualificationSourceExperimentId = 201,
            QualificationSourceTrialId = 202,
            QualificationParameterFingerprint = "ABCDEF0123456789",
            QualificationEvidenceVersion = ValidationParameterSetPublicationService.EvidenceVersion,
            QualificationVerifiedAtUtc = DateTime.UtcNow,
            ExchangeId = 5,
            RiskProfileId = 6,
            CreatedAtUtc = DateTime.UtcNow
        };
        var tradingSession = new TradingSession
        {
            Id = 3,
            Name = "Deployment",
            Status = TradingSessionStatus.Created,
            Mode = TradingMode.Paper,
            ExchangeId = 5,
            StartedByUserId = 7,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var state = new PaperSessionState
        {
            Session = session,
            Account = new PaperAccount { Id = 2, Name = "Paper", CurrentBalance = 100, CurrentEquity = 100 },
            Settings = new PaperSessionSettings
            {
                MakerFeeRate = 0,
                TakerFeeRate = 0,
                OrderExpiryCandles = 2,
                UseAiScoring = false,
                MinConfidenceScore = 80,
                SlippagePercent = 0,
                ExecutionMode = ExecutionMode.MarketFill,
                StrategyIds = [303],
                SymbolIds = [404],
                Timeframes = [Timeframe.M15],
                ParameterSetId = 101
            },
            Context = null!,
            Dataset = null!,
            Strategies = Array.Empty<PreparedStrategy>(),
            RiskRules = Array.Empty<RiskRule>()
        };
        var coordinator = new RecordingCoordinator(session);
        var repository = new Mock<IPaperTradingSessionRepository>();
        repository.Setup(repo => repo.GetByIdAsync(session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        repository.Setup(repo => repo.UpdateAsync(session, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(repo => repo.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => Assert.True(coordinator.InTransaction))
            .Returns(Task.CompletedTask);
        var tradingRepository = new Mock<ITradingSessionRepository>();
        tradingRepository.Setup(repo => repo.GetByIdAsync(tradingSession.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tradingSession);
        tradingRepository.Setup(repo => repo.UpdateAsync(tradingSession, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var stateStore = new Mock<IPaperStateStore>();
        stateStore.Setup(store => store.TryGet(session.Id, out state)).Returns(true);
        stateStore.Setup(store => store.Set(session.Id, state)).Callback(() => Assert.False(coordinator.InTransaction));

        var verifier = new Mock<IPaperDeploymentQualificationVerifier>();
        verifier.Setup(item => item.VerifyAsync(
                101, 303, 404, "15m", It.IsAny<PaperDeploymentStoredBinding>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaperDeploymentQualificationResult
            {
                Succeeded = true,
                ParameterSetId = 101,
                StrategyId = 303,
                SymbolId = 404,
                Timeframe = "15m",
                SourceExperimentId = 201,
                SourceTrialId = 202,
                ParameterFingerprint = "ABCDEF0123456789",
                EvidenceVersion = ValidationParameterSetPublicationService.EvidenceVersion,
                VerifiedAtUtc = DateTime.UtcNow,
                FrozenParameters = new Dictionary<string, string> { ["lookback"] = "20" }
            });
        var actions = new List<string>();
        var required = new Mock<IRequiredAuditWriter>();
        required.Setup(writer => writer.AttachRequired(
                It.IsAny<RequiredAuditRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RequiredAuditRequest, CancellationToken>((request, _) =>
            {
                Assert.True(coordinator.InTransaction);
                actions.Add(request.Action);
            });
        var bootstrap = new Mock<ILiveMarketBootstrapService>();
        bootstrap.Setup(service => service.EnsureWarmupAsync(
                5, 404, Timeframe.M15, It.IsAny<CancellationToken>()))
            .Callback(() => Assert.False(coordinator.InTransaction))
            .ReturnsAsync(bootstrapSucceeds
                ? ServiceResult<LiveBootstrapResult>.Ok(new LiveBootstrapResult
                {
                    DataSource = "StoredHistorical",
                    CandleCountUsed = 600,
                    IndicatorsAvailable = true
                })
                : ServiceResult<LiveBootstrapResult>.Fail("Provider bootstrap failed.", "provider"));
        var live = new Mock<ILiveMarketConnectionManager>();
        live.SetupGet(manager => manager.IsAvailable).Returns(true);
        live.SetupGet(manager => manager.IsConnected).Returns(true);
        live.Setup(manager => manager.IsSubscribed(404, Timeframe.M15)).Returns(false);
        live.Setup(manager => manager.SubscribeAsync(
                It.IsAny<LiveMarketSubscribeRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => Assert.False(coordinator.InTransaction))
            .ReturnsAsync(ServiceResult<LiveMarketStatusDto>.Ok(new LiveMarketStatusDto
            {
                Provider = "test",
                Connected = true,
                Subscriptions = Array.Empty<LiveMarketSubscriptionDto>()
            }));
        live.Setup(manager => manager.LinkSession(session.Id, 404, Timeframe.M15))
            .Callback(() => Assert.False(coordinator.InTransaction));
        live.Setup(manager => manager.GetDiagnostics()).Returns(new LiveMarketDiagnosticsDto
        {
            Provider = "test",
            Connected = false,
            Subscriptions = Array.Empty<LiveMarketSubscriptionDiagnosticsDto>()
        });
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(service => service.UserId).Returns(7);

        var service = new PaperSessionControlService(
            repository.Object,
            tradingRepository.Object,
            stateStore.Object,
            Mock.Of<IPaperTradingEngine>(),
            Mock.Of<IPaperPersistenceService>(),
            live.Object,
            bootstrap.Object,
            currentUser.Object,
            Mock.Of<IAuditService>(),
            verifier.Object,
            coordinator,
            required.Object);
        return new OrderingFixture(
            service, session, tradingSession, state, stateStore, live, coordinator, actions);
    }

    private sealed record OrderingFixture(
        PaperSessionControlService Service,
        PaperTradingSession Session,
        TradingSession TradingSession,
        PaperSessionState State,
        Mock<IPaperStateStore> StateStore,
        Mock<ILiveMarketConnectionManager> Live,
        RecordingCoordinator Coordinator,
        List<string> RequiredActions);

    private sealed class RecordingCoordinator(PaperTradingSession session) : IPaperSessionRelationalCoordinator
    {
        public bool InTransaction { get; private set; }
        public int TransactionCalls { get; private set; }

        public Task<T> ExecuteCreationAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default) => action(cancellationToken);

        public async Task<T> ExecuteSerializedAsync<T>(
            long paperSessionId,
            Func<PaperTradingSession?, CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default)
        {
            TransactionCalls++;
            InTransaction = true;
            try
            {
                return await action(session, cancellationToken);
            }
            finally
            {
                InTransaction = false;
            }
        }
    }
}
