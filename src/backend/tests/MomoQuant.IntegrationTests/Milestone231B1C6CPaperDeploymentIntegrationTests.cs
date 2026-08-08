using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Audit;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Common;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.LiveMarket.Dtos;
using MomoQuant.Application.MarketSituation;
using MomoQuant.Application.PaperTrading;
using MomoQuant.Application.PaperTrading.Dtos;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Audit;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Domain.Sessions;
using MomoQuant.Persistence;
using MomoQuant.Shared.Contracts;
using MySqlConnector;

namespace MomoQuant.IntegrationTests;

[Collection("Integration")]
public sealed class Milestone231B1C6CPaperDeploymentIntegrationTests
    : IClassFixture<DisposableIntegrationDatabaseFixture>
{
    private const string PreviousMigration = "20260801185538_PublishQualifiedValidationLabParameterSets";
    private const string CurrentMigration = "20260801232441_GatePaperDeploymentSimulation";
    private readonly DisposableIntegrationDatabaseFixture _fixture;

    public Milestone231B1C6CPaperDeploymentIntegrationTests(DisposableIntegrationDatabaseFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task Controller_PreservesStableDeploymentFailureCode_AndIgnoresCallerBindingFields()
    {
        var authorized = await IntegrationDisposableAuth.CreateAuthorizedAdminClientAsync(
            _fixture.Factory,
            "b1c6c-controller");
        using var client = authorized.Client;
        try
        {
            using var response = await client.PostAsJsonAsync("/api/v1/paper/sessions", new
            {
                name = "Rejected deployment request",
                paperAccountId = 0,
                exchangeId = 0,
                symbolIds = new[] { 1L },
                timeframes = new[] { "15m" },
                mode = "HistoricalPaper",
                useClass = "DeploymentSimulation",
                riskProfileId = 0,
                strategyIds = new[] { 1L },
                parameterSetId = 1,
                boundStrategyId = 999,
                boundSymbolId = 999,
                boundTimeframe = "forged",
                qualificationSourceExperimentId = 999,
                qualificationSourceTrialId = 999,
                qualificationParameterFingerprint = "FORGED",
                qualificationEvidenceVersion = "forged/v9"
            });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var payload = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(IntegrationTestJson.Options);
            var error = Assert.Single(payload!.Errors!);
            Assert.Equal(PaperDeploymentQualificationCodes.LiveModeRequired, error.Field);
            Assert.DoesNotContain("forged", payload.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await IntegrationDisposableAuth.DeleteUsersAsync(_fixture.Factory, authorized.UserId);
        }
    }

    [Fact]
    public async Task Create_WithRealAuthoritativeEvaluator_PersistsExactBindingRuntimeAndSafeAudit()
    {
        var seeded = await SeedDeploymentSessionAsync("create-real-audit");
        try
        {
            await using var scope = _fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            var session = await db.PaperTradingSessions.AsNoTracking()
                .SingleAsync(row => row.Id == seeded.PaperSessionId);
            Assert.Equal(PaperSessionUseClass.DeploymentSimulation, session.UseClass);
            Assert.Equal(seeded.ParameterSetId, session.ParameterSetId);
            Assert.Equal(seeded.StrategyId, session.BoundStrategyId);
            Assert.Equal(seeded.SymbolId, session.BoundSymbolId);
            Assert.Equal(seeded.Timeframe, session.BoundTimeframe);
            Assert.Equal(seeded.Publication.ExperimentId, session.QualificationSourceExperimentId);
            Assert.Equal(seeded.Publication.TrialId, session.QualificationSourceTrialId);
            Assert.Equal(seeded.Publication.Fingerprint, session.QualificationParameterFingerprint);
            Assert.Equal(ValidationParameterSetPublicationService.EvidenceVersion, session.QualificationEvidenceVersion);
            Assert.NotNull(session.QualificationVerifiedAtUtc);
            Assert.Equal(PaperSessionStatus.Created, session.Status);

            var stateStore = scope.ServiceProvider.GetRequiredService<IPaperStateStore>();
            Assert.True(stateStore.TryGet(session.Id, out var state));
            Assert.Equal("20", state!.FrozenStrategyParameters![seeded.StrategyId]["lookback"]);
            Assert.Equal("0.5", state.FrozenStrategyParameters[seeded.StrategyId]["minimumStrength"]);

            var audit = await db.AuditLogs.AsNoTracking().SingleAsync(row =>
                row.Action == "PAPER_DEPLOYMENT_QUALIFICATION_VERIFIED"
                && row.EntityId == session.Id
                && row.NewValueJson!.Contains("\"phase\":\"Create\""));
            Assert.Contains(seeded.Publication.Fingerprint, audit.NewValueJson, StringComparison.Ordinal);
            Assert.DoesNotContain("minimumStrength", audit.NewValueJson, StringComparison.Ordinal);
            Assert.DoesNotContain("lookback", audit.NewValueJson, StringComparison.Ordinal);
        }
        finally
        {
            await CleanupDeploymentSessionAsync(seeded);
        }
    }

    [Theory]
    [InlineData(RequiredAuditActions.PaperDeploymentQualificationVerified)]
    [InlineData(RequiredAuditActions.PaperSessionCreated)]
    public async Task Create_RequiredAuditInsertFailure_RollsBackRowsAuditsRuntimeAndRetrySucceeds(string action)
    {
        var prepared = await PrepareDeploymentCreationAsync($"d1-create-{action}");
        var stateStore = new RecordingStateStore();
        var live = new DeterministicLiveMarketManager();
        try
        {
            await InstallAuditFailureConstraintAsync(action);
            await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
            {
                var service = BuildCreateService(
                    scope.ServiceProvider,
                    live,
                    new FixedCurrentUser(prepared.UserId),
                    stateStore: stateStore);
                var failure = await service.CreateAsync(prepared.Request);
                Assert.False(failure.Succeeded);
                Assert.Equal(AuditEvidenceCodes.Unavailable, failure.ErrorField);
                Assert.DoesNotContain("constraint", failure.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            }

            await using (var verificationScope = _fixture.Factory.Services.CreateAsyncScope())
            {
                var db = verificationScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                Assert.False(await db.PaperTradingSessions.AnyAsync(row =>
                    row.PaperAccountId == prepared.PaperAccountId));
                Assert.False(await db.TradingSessions.AnyAsync(row =>
                    row.Name == $"Paper: {prepared.Request.Name}"));
                Assert.False(await db.AuditLogs.AnyAsync(row =>
                    row.Action == RequiredAuditActions.PaperDeploymentQualificationVerified
                    || row.Action == RequiredAuditActions.PaperSessionCreated));
            }

            Assert.Equal(0, stateStore.SetCalls);
            Assert.Equal(0, live.SubscribeCalls);

            await RemoveAuditFailureConstraintAsync();
            await using (var retryScope = _fixture.Factory.Services.CreateAsyncScope())
            {
                var service = BuildCreateService(
                    retryScope.ServiceProvider,
                    live,
                    new FixedCurrentUser(prepared.UserId),
                    stateStore: stateStore);
                var retry = await service.CreateAsync(prepared.Request);
                Assert.True(retry.Succeeded, retry.ErrorMessage);
            }

            Assert.Equal(1, stateStore.SetCalls);
            await using var retryVerificationScope = _fixture.Factory.Services.CreateAsyncScope();
            var retryDb = retryVerificationScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            var session = await retryDb.PaperTradingSessions.SingleAsync(row =>
                row.PaperAccountId == prepared.PaperAccountId);
            Assert.Equal(2, await retryDb.AuditLogs.CountAsync(row =>
                row.EntityType == nameof(PaperTradingSession) && row.EntityId == session.Id));
        }
        finally
        {
            await RemoveAuditFailureConstraintAsync();
            await CleanupPreparedCreationAsync(prepared);
        }
    }

    [Theory]
    [InlineData(false, RequiredAuditActions.PaperDeploymentQualificationVerified)]
    [InlineData(false, RequiredAuditActions.PaperSessionStarted)]
    [InlineData(true, RequiredAuditActions.PaperDeploymentQualificationVerified)]
    [InlineData(true, RequiredAuditActions.PaperSessionResumed)]
    public async Task StartOrResume_RequiredAuditInsertFailure_RollsBackDurableStateAndHasNoRuntimeSideEffects(
        bool resume,
        string action)
    {
        var seeded = await SeedDeploymentSessionAsync($"d1-transition-{resume}-{action}");
        var live = new DeterministicLiveMarketManager();
        try
        {
            var expectedStatus = resume ? PaperSessionStatus.Paused : PaperSessionStatus.Created;
            DateTime? verificationTime;
            await using (var setupScope = _fixture.Factory.Services.CreateAsyncScope())
            {
                var db = setupScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                var paper = await db.PaperTradingSessions.SingleAsync(row => row.Id == seeded.PaperSessionId);
                var trading = await db.TradingSessions.SingleAsync(row => row.Id == paper.TradingSessionId);
                if (resume)
                {
                    paper.Status = PaperSessionStatus.Paused;
                    trading.Status = TradingSessionStatus.Paused;
                    await db.SaveChangesAsync();
                }

                verificationTime = paper.QualificationVerifiedAtUtc;
            }

            await InstallAuditFailureConstraintAsync(action, allowCreateQualification: true);
            await using (var controlScope = _fixture.Factory.Services.CreateAsyncScope())
            {
                var service = BuildControlService(
                    controlScope.ServiceProvider,
                    live,
                    new FixedCurrentUser(seeded.UserId));
                var failure = resume
                    ? await service.ResumeAsync(seeded.PaperSessionId)
                    : await service.StartAsync(seeded.PaperSessionId);
                Assert.False(failure.Succeeded);
                Assert.Equal(AuditEvidenceCodes.Unavailable, failure.ErrorField);
            }

            await using var verificationScope = _fixture.Factory.Services.CreateAsyncScope();
            var verification = verificationScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            var durablePaper = await verification.PaperTradingSessions.AsNoTracking()
                .SingleAsync(row => row.Id == seeded.PaperSessionId);
            var durableTrading = await verification.TradingSessions.AsNoTracking()
                .SingleAsync(row => row.Id == durablePaper.TradingSessionId);
            Assert.Equal(expectedStatus, durablePaper.Status);
            Assert.Equal(resume ? TradingSessionStatus.Paused : TradingSessionStatus.Created, durableTrading.Status);
            Assert.Equal(verificationTime, durablePaper.QualificationVerifiedAtUtc);
            Assert.False(await verification.AuditLogs.AnyAsync(row =>
                row.EntityId == seeded.PaperSessionId
                && (row.Action == RequiredAuditActions.PaperSessionStarted
                    || row.Action == RequiredAuditActions.PaperSessionResumed)));
            Assert.Equal(0, live.SubscribeCalls);
        }
        finally
        {
            await RemoveAuditFailureConstraintAsync();
            await CleanupDeploymentSessionAsync(seeded);
        }
    }

    [Fact]
    public async Task Start_PostCommitBootstrapFailure_CompensatesRowsAndAuditWithoutRawProviderDetails()
    {
        var seeded = await SeedDeploymentSessionAsync("d1-postcommit-failure");
        var live = new DeterministicLiveMarketManager();
        try
        {
            await using (var controlScope = _fixture.Factory.Services.CreateAsyncScope())
            {
                var service = BuildControlService(
                    controlScope.ServiceProvider,
                    live,
                    new FixedCurrentUser(seeded.UserId),
                    new FailingBootstrapService());
                var result = await service.StartAsync(seeded.PaperSessionId);
                Assert.False(result.Succeeded);
                Assert.Equal(PaperDeploymentQualificationCodes.RuntimeActivationFailed, result.ErrorField);
            }

            await using var verificationScope = _fixture.Factory.Services.CreateAsyncScope();
            var services = verificationScope.ServiceProvider;
            var db = services.GetRequiredService<MomoQuantDbContext>();
            var paper = await db.PaperTradingSessions.AsNoTracking()
                .SingleAsync(row => row.Id == seeded.PaperSessionId);
            var trading = await db.TradingSessions.AsNoTracking()
                .SingleAsync(row => row.Id == paper.TradingSessionId);
            Assert.Equal(PaperSessionStatus.Failed, paper.Status);
            Assert.Equal(TradingSessionStatus.Failed, trading.Status);
            Assert.Equal(PaperDeploymentQualificationCodes.RuntimeActivationFailed, paper.ErrorMessage);
            var failureAudit = await db.AuditLogs.AsNoTracking().SingleAsync(row =>
                row.EntityId == paper.Id && row.Action == RequiredAuditActions.PaperSessionFailed);
            Assert.Contains(PaperDeploymentQualificationCodes.RuntimeActivationFailed, failureAudit.NewValueJson, StringComparison.Ordinal);
            Assert.DoesNotContain("PROVIDER_SECRET", failureAudit.NewValueJson, StringComparison.Ordinal);
            Assert.False(services.GetRequiredService<IPaperStateStore>().TryGet(paper.Id, out _));
            Assert.Equal(0, live.SubscribeCalls);
        }
        finally
        {
            await CleanupDeploymentSessionAsync(seeded);
        }
    }

    [Fact]
    public async Task Start_PostCommitReloadCancellation_CompensatesCommittedRowsAndRethrows()
    {
        var seeded = await SeedDeploymentSessionAsync("d1-postcommit-reload-cancel");
        var live = new DeterministicLiveMarketManager();
        var reloadGate = new ReloadCancellationGate();
        using var cancellation = new CancellationTokenSource();
        try
        {
            Task<ServiceResult<PaperSessionControlResponse>> startTask;
            await using (var controlScope = _fixture.Factory.Services.CreateAsyncScope())
            {
                var services = controlScope.ServiceProvider;
                var repository = new CancellationAfterCommitPaperSessionRepository(
                    services.GetRequiredService<IPaperTradingSessionRepository>(),
                    reloadGate);
                var service = BuildControlService(
                    services,
                    live,
                    new FixedCurrentUser(seeded.UserId),
                    paperSessionRepository: repository);
                startTask = service.StartAsync(seeded.PaperSessionId, cancellation.Token);
                await reloadGate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startTask);
            }

            await using var verificationScope = _fixture.Factory.Services.CreateAsyncScope();
            var servicesAfterCancellation = verificationScope.ServiceProvider;
            var db = servicesAfterCancellation.GetRequiredService<MomoQuantDbContext>();
            var paper = await db.PaperTradingSessions.AsNoTracking()
                .SingleAsync(row => row.Id == seeded.PaperSessionId);
            var trading = await db.TradingSessions.AsNoTracking()
                .SingleAsync(row => row.Id == paper.TradingSessionId);
            Assert.Equal(PaperSessionStatus.Failed, paper.Status);
            Assert.Equal(TradingSessionStatus.Failed, trading.Status);
            Assert.Equal(PaperDeploymentQualificationCodes.RuntimeActivationFailed, paper.ErrorMessage);
            Assert.Equal(1, await db.AuditLogs.CountAsync(row =>
                row.EntityId == paper.Id && row.Action == RequiredAuditActions.PaperSessionStarted));
            Assert.Equal(1, await db.AuditLogs.CountAsync(row =>
                row.EntityId == paper.Id
                && row.Action == RequiredAuditActions.PaperDeploymentQualificationVerified
                && row.NewValueJson != null
                && row.NewValueJson.Contains("\"phase\":\"Start\"")));
            var failureAudit = await db.AuditLogs.SingleAsync(row =>
                row.EntityId == paper.Id && row.Action == RequiredAuditActions.PaperSessionFailed);
            Assert.DoesNotContain("OperationCanceledException", failureAudit.NewValueJson, StringComparison.Ordinal);
            Assert.DoesNotContain("SQL", failureAudit.NewValueJson, StringComparison.OrdinalIgnoreCase);
            Assert.False(servicesAfterCancellation.GetRequiredService<IPaperStateStore>()
                .TryGet(paper.Id, out _));
            Assert.Equal(0, live.SubscribeCalls);
            Assert.Equal(0, live.LinkCalls);
            Assert.Equal(1, live.UnlinkCalls);
        }
        finally
        {
            await CleanupDeploymentSessionAsync(seeded);
        }
    }

    [Fact]
    public async Task Create_EvidenceInvalidatedAfterEarlyCheck_FailsAuthoritativeTransactionWithoutPartialState()
    {
        var prepared = await PrepareDeploymentCreationAsync("create-invalidated-before-transaction");
        try
        {
            await using var creationScope = _fixture.Factory.Services.CreateAsyncScope();
            var services = creationScope.ServiceProvider;
            var creationDb = services.GetRequiredService<MomoQuantDbContext>();
            var auditWatermark = await creationDb.AuditLogs
                .Select(row => (long?)row.Id)
                .MaxAsync() ?? 0;
            var earlyVerified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var continueCreation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var verifier = new CoordinatedVerifier(
                services.GetRequiredService<IPaperDeploymentQualificationVerifier>(),
                async (invocation, result) =>
                {
                    if (invocation == 1)
                    {
                        Assert.True(result.Succeeded, result.ErrorMessage);
                        earlyVerified.SetResult();
                        await continueCreation.Task;
                    }
                });
            var stateStore = new RecordingStateStore();
            var service = BuildCreateService(
                services,
                new DeterministicLiveMarketManager(),
                new FixedCurrentUser(prepared.UserId),
                verifier,
                stateStore);
            var creationTask = service.CreateAsync(prepared.Request);
            await earlyVerified.Task.WaitAsync(TimeSpan.FromSeconds(30));

            await using (var mutationScope = _fixture.Factory.Services.CreateAsyncScope())
            {
                var mutationDb = mutationScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                var changed = await mutationDb.ValidationExperiments
                    .Where(row => row.Id == prepared.Publication.ExperimentId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        row => row.Status,
                        ValidationExperimentStatus.Failed));
                Assert.Equal(1, changed);
            }

            continueCreation.SetResult();
            var result = await creationTask.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.False(result.Succeeded);
            Assert.Equal(PaperDeploymentQualificationCodes.ExperimentInvalid, result.ErrorField);
            Assert.Equal(2, verifier.InvocationCount);
            Assert.Equal(0, stateStore.SetCalls);
            await using var assertionScope = _fixture.Factory.Services.CreateAsyncScope();
            var db = assertionScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            Assert.False(await db.PaperTradingSessions.AnyAsync(row =>
                row.PaperAccountId == prepared.PaperAccountId));
            Assert.False(await db.TradingSessions.AnyAsync(row =>
                row.Name == $"Paper: {prepared.Request.Name}"));
            Assert.False(await db.AuditLogs.AnyAsync(row =>
                row.Id > auditWatermark
                && (row.Action == "PAPER_DEPLOYMENT_QUALIFICATION_VERIFIED"
                    || row.Action == "PAPER_SESSION_CREATED")));
        }
        finally
        {
            await CleanupPreparedCreationAsync(prepared);
        }
    }

    [Fact]
    public async Task Create_AuthoritativeReadLocksEvidenceUntilCommit_AndPersistsTransactionalSnapshot()
    {
        var prepared = await PrepareDeploymentCreationAsync("create-locks-authoritative-evidence");
        try
        {
            await using var creationScope = _fixture.Factory.Services.CreateAsyncScope();
            var services = creationScope.ServiceProvider;
            var authoritativeRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var continueCreation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var verifier = new CoordinatedVerifier(
                services.GetRequiredService<IPaperDeploymentQualificationVerifier>(),
                async (invocation, result) =>
                {
                    if (invocation == 2)
                    {
                        Assert.True(result.Succeeded, result.ErrorMessage);
                        authoritativeRead.SetResult();
                        await continueCreation.Task;
                    }
                });
            var stateStore = new RecordingStateStore();
            var service = BuildCreateService(
                services,
                new DeterministicLiveMarketManager(),
                new FixedCurrentUser(prepared.UserId),
                verifier,
                stateStore);
            var creationTask = service.CreateAsync(prepared.Request);
            await authoritativeRead.Task.WaitAsync(TimeSpan.FromSeconds(30));

            var mutationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var mutationTask = Task.Run(async () =>
            {
                await using var mutationScope = _fixture.Factory.Services.CreateAsyncScope();
                var mutationDb = mutationScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                mutationStarted.SetResult();
                return await mutationDb.StrategyParameterSets
                    .Where(row => row.Id == prepared.ParameterSetId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        row => row.ParametersJson,
                        "{\"lookback\":\"999\",\"minimumStrength\":\"0.9\"}"));
            });
            await mutationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(500);
            Assert.False(mutationTask.IsCompleted);

            continueCreation.SetResult();
            var result = await creationTask.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(result.Succeeded, $"{result.ErrorField}: {result.ErrorMessage}");
            Assert.Equal(1, await mutationTask.WaitAsync(TimeSpan.FromSeconds(30)));

            await using var assertionScope = _fixture.Factory.Services.CreateAsyncScope();
            var db = assertionScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            var session = await db.PaperTradingSessions.AsNoTracking()
                .SingleAsync(row => row.Id == result.Data!.Id);
            Assert.Equal(prepared.Publication.Fingerprint, session.QualificationParameterFingerprint);
            Assert.Equal(prepared.Publication.ExperimentId, session.QualificationSourceExperimentId);
            Assert.Equal(prepared.Publication.TrialId, session.QualificationSourceTrialId);
            Assert.Equal(1, stateStore.SetCalls);
            Assert.NotNull(stateStore.LastState);
            Assert.Equal("20", stateStore.LastState!.FrozenStrategyParameters![prepared.StrategyId]["lookback"]);
            Assert.Equal("0.5", stateStore.LastState.FrozenStrategyParameters[prepared.StrategyId]["minimumStrength"]);
            var audits = await db.AuditLogs.AsNoTracking()
                .Where(row => row.EntityType == nameof(PaperTradingSession)
                    && row.EntityId == session.Id
                    && (row.Action == "PAPER_DEPLOYMENT_QUALIFICATION_VERIFIED"
                        || row.Action == "PAPER_SESSION_CREATED"))
                .ToListAsync();
            Assert.Equal(2, audits.Count);
        }
        finally
        {
            var paperSessionId = await FindPaperSessionIdAsync(prepared.PaperAccountId);
            if (paperSessionId is long id)
            {
                await CleanupDeploymentSessionAsync(new SeededDeploymentSession(
                    prepared.Publication,
                    prepared.ParameterSetId,
                    prepared.StrategyId,
                    prepared.SymbolId,
                    prepared.Timeframe,
                    id,
                    prepared.PaperAccountId,
                    prepared.UserId));
            }
            else
            {
                await CleanupPreparedCreationAsync(prepared);
            }
        }
    }

    [Fact]
    public async Task Create_ResearchSession_RemainsOutsideDeploymentQualificationGate()
    {
        long accountId = 0;
        long paperSessionId = 0;
        long symbolId = 0;
        try
        {
            await using var scope = _fixture.Factory.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<MomoQuantDbContext>();
            var account = CreatePaperAccount("research-unchanged");
            db.PaperAccounts.Add(account);
            await db.SaveChangesAsync();
            accountId = account.Id;
            var exchange = await db.Exchanges.AsNoTracking().FirstAsync();
            var symbol = new Symbol
            {
                ExchangeId = exchange.Id,
                SymbolName = $"RSH{Guid.NewGuid():N}"[..15],
                BaseAsset = "B1C6C",
                QuoteAsset = "USDT",
                ContractType = ContractType.Perpetual,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Symbols.Add(symbol);
            await db.SaveChangesAsync();
            symbolId = symbol.Id;
            var risk = await db.RiskProfiles.AsNoTracking().FirstAsync();
            var userId = await db.Users.AsNoTracking().Select(row => row.Id).FirstAsync();
            var strategy = await db.Strategies.AsNoTracking().SingleAsync(row =>
                row.Code == StrategyCode.PriceStructureBreakoutRetest);
            var verifier = new CoordinatedVerifier(
                services.GetRequiredService<IPaperDeploymentQualificationVerifier>(),
                (_, _) => Task.CompletedTask);
            var service = BuildCreateService(
                services,
                new DeterministicLiveMarketManager(),
                new FixedCurrentUser(userId),
                verifier);

            var result = await service.CreateAsync(new CreatePaperSessionRequest
            {
                Name = $"B1C6C research unchanged {Guid.NewGuid():N}",
                PaperAccountId = account.Id,
                ExchangeId = exchange.Id,
                SymbolIds = [symbol.Id],
                Timeframes = ["15m"],
                Mode = "LivePaper",
                RiskProfileId = risk.Id,
                StrategyIds = [strategy.Id],
                AllowAbnormalMarketPaperTrading = true
            });

            Assert.True(result.Succeeded, result.ErrorMessage);
            paperSessionId = result.Data!.Id;
            Assert.Equal(0, verifier.InvocationCount);
            var session = await db.PaperTradingSessions.AsNoTracking()
                .SingleAsync(row => row.Id == paperSessionId);
            Assert.Equal(PaperSessionUseClass.Research, session.UseClass);
            Assert.Null(session.BoundStrategyId);
            Assert.Null(session.QualificationParameterFingerprint);
            Assert.False(await db.AuditLogs.AnyAsync(row =>
                row.EntityType == nameof(PaperTradingSession)
                && row.EntityId == paperSessionId
                && row.Action == "PAPER_DEPLOYMENT_QUALIFICATION_VERIFIED"));
            Assert.True(await db.AuditLogs.AnyAsync(row =>
                row.EntityType == nameof(PaperTradingSession)
                && row.EntityId == paperSessionId
                && row.Action == "PAPER_SESSION_CREATED"));
        }
        finally
        {
            if (paperSessionId > 0)
            {
                await CleanupResearchSessionAsync(paperSessionId, accountId, symbolId);
            }
            else if (accountId > 0)
            {
                await using var scope = _fixture.Factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                await db.PaperAccounts.Where(row => row.Id == accountId).ExecuteDeleteAsync();
                if (symbolId > 0)
                {
                    await db.Symbols.Where(row => row.Id == symbolId).ExecuteDeleteAsync();
                }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartOrResume_ValidDurableEvidence_ReverifiesRefreshesRuntimeAndAudits(bool resume)
    {
        var seeded = await SeedDeploymentSessionAsync(resume ? "resume-valid" : "start-valid");
        try
        {
            if (resume)
            {
                await using var pauseScope = _fixture.Factory.Services.CreateAsyncScope();
                var pauseDb = pauseScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                var paused = await pauseDb.PaperTradingSessions.SingleAsync(row => row.Id == seeded.PaperSessionId);
                paused.Status = PaperSessionStatus.Paused;
                await pauseDb.SaveChangesAsync();
            }

            await using var scope = _fixture.Factory.Services.CreateAsyncScope();
            var live = new DeterministicLiveMarketManager();
            var control = BuildControlService(scope.ServiceProvider, live, new FixedCurrentUser(seeded.UserId));
            var result = resume
                ? await control.ResumeAsync(seeded.PaperSessionId)
                : await control.StartAsync(seeded.PaperSessionId);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(PaperSessionStatus.Running.ToString(), result.Data!.Status);
            Assert.Equal(1, live.SubscribeCalls);
            var stateStore = scope.ServiceProvider.GetRequiredService<IPaperStateStore>();
            Assert.True(stateStore.TryGet(seeded.PaperSessionId, out var state));
            Assert.Equal("20", state!.FrozenStrategyParameters![seeded.StrategyId]["lookback"]);

            var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            db.ChangeTracker.Clear();
            var session = await db.PaperTradingSessions.AsNoTracking()
                .SingleAsync(row => row.Id == seeded.PaperSessionId);
            Assert.Equal(PaperSessionStatus.Running, session.Status);
            Assert.Equal(1, await db.AuditLogs.CountAsync(row =>
                row.Action == "PAPER_DEPLOYMENT_QUALIFICATION_VERIFIED"
                && row.EntityId == session.Id
                && row.NewValueJson!.Contains(resume ? "\"phase\":\"Resume\"" : "\"phase\":\"Start\"")));
        }
        finally
        {
            await CleanupDeploymentSessionAsync(seeded);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartOrResume_ReloadsDurableEvidence_AndMutationPreservesNonRunningState(bool resume)
    {
        var seeded = await SeedDeploymentSessionAsync(resume ? "resume-mutation" : "start-mutation");
        try
        {
            await using (var mutationScope = _fixture.Factory.Services.CreateAsyncScope())
            {
                var mutationDb = mutationScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                var parameterSet = await mutationDb.StrategyParameterSets.SingleAsync(row => row.Id == seeded.ParameterSetId);
                parameterSet.ParametersJson = "{\"lookback\":\"21\",\"minimumStrength\":\"0.5\"}";
                var session = await mutationDb.PaperTradingSessions.SingleAsync(row => row.Id == seeded.PaperSessionId);
                if (resume)
                {
                    session.Status = PaperSessionStatus.Paused;
                }

                await mutationDb.SaveChangesAsync();
            }

            await using var scope = _fixture.Factory.Services.CreateAsyncScope();
            var live = new DeterministicLiveMarketManager();
            var control = BuildControlService(scope.ServiceProvider, live, new FixedCurrentUser(seeded.UserId));
            var result = resume
                ? await control.ResumeAsync(seeded.PaperSessionId)
                : await control.StartAsync(seeded.PaperSessionId);
            Assert.False(result.Succeeded);
            Assert.Equal(PaperDeploymentQualificationCodes.FingerprintMismatch, result.ErrorField);
            Assert.Equal(0, live.SubscribeCalls);

            var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            db.ChangeTracker.Clear();
            var stored = await db.PaperTradingSessions.AsNoTracking()
                .SingleAsync(row => row.Id == seeded.PaperSessionId);
            Assert.Equal(resume ? PaperSessionStatus.Paused : PaperSessionStatus.Created, stored.Status);
            var trading = await db.TradingSessions.AsNoTracking()
                .SingleAsync(row => row.Id == stored.TradingSessionId);
            Assert.NotEqual(TradingSessionStatus.Running, trading.Status);
            Assert.Equal(0, await db.AuditLogs.CountAsync(row =>
                row.Action == "PAPER_DEPLOYMENT_QUALIFICATION_VERIFIED"
                && row.EntityId == stored.Id
                && row.NewValueJson!.Contains(resume ? "\"phase\":\"Resume\"" : "\"phase\":\"Start\"")));
        }
        finally
        {
            await CleanupDeploymentSessionAsync(seeded);
        }
    }

    [Fact]
    public async Task ConcurrentStarts_SerializeDurableBinding_AndWriteOneRunningTransitionAndAudit()
    {
        var seeded = await SeedDeploymentSessionAsync("concurrent-start");
        try
        {
            var live = new DeterministicLiveMarketManager();
            await using var firstScope = _fixture.Factory.Services.CreateAsyncScope();
            await using var secondScope = _fixture.Factory.Services.CreateAsyncScope();
            var currentUser = new FixedCurrentUser(seeded.UserId);
            var first = BuildControlService(firstScope.ServiceProvider, live, currentUser);
            var second = BuildControlService(secondScope.ServiceProvider, live, currentUser);

            var results = await Task.WhenAll(
                first.StartAsync(seeded.PaperSessionId),
                second.StartAsync(seeded.PaperSessionId));

            Assert.Single(results.Where(result => result.Succeeded));
            Assert.Single(results.Where(result => !result.Succeeded));
            Assert.Equal(1, live.SubscribeCalls);
            await using var verifyScope = _fixture.Factory.Services.CreateAsyncScope();
            var db = verifyScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            var session = await db.PaperTradingSessions.AsNoTracking()
                .SingleAsync(row => row.Id == seeded.PaperSessionId);
            Assert.Equal(PaperSessionStatus.Running, session.Status);
            Assert.Equal(1, await db.AuditLogs.CountAsync(row =>
                row.Action == "PAPER_DEPLOYMENT_QUALIFICATION_VERIFIED"
                && row.EntityId == session.Id
                && row.NewValueJson!.Contains("\"phase\":\"Start\"")));
            Assert.Equal(1, await db.AuditLogs.CountAsync(row =>
                row.Action == "PAPER_SESSION_STARTED" && row.EntityId == session.Id));
        }
        finally
        {
            await CleanupDeploymentSessionAsync(seeded);
        }
    }

    [Fact]
    public async Task Migration_DefaultsHistoricalRowsToResearch_RejectsIncompleteDeployment_AndRollsBackWithoutStatusLoss()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var migrator = db.GetService<IMigrator>();
        var account = CreatePaperAccount("migration");
        db.PaperAccounts.Add(account);
        var exchange = await db.Exchanges.AsNoTracking().FirstAsync();
        var risk = await db.RiskProfiles.AsNoTracking().FirstAsync();
        var userId = await db.Users.AsNoTracking().Select(row => row.Id).FirstAsync();
        var trading = new TradingSession
        {
            Name = "B1C6C historical migration",
            Mode = TradingMode.Paper,
            Status = TradingSessionStatus.Paused,
            ExchangeId = exchange.Id,
            StartedByUserId = userId,
            InitialBalance = account.CurrentBalance,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.TradingSessions.Add(trading);
        await db.SaveChangesAsync();
        long paperSessionId = 0;

        await migrator.MigrateAsync(PreviousMigration);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO PaperTradingSessions
                    (Name, PaperAccountId, TradingSessionId, Status, Mode, ExchangeId, RiskProfileId,
                     ExecutionMode, UseAiScoring, MinConfidenceScore, CurrentCandleIndex, TotalCandles,
                     ConfigJson, CreatedAt, UpdatedAt)
                VALUES
                    ({"B1C6C historical row"}, {account.Id}, {trading.Id}, {"Paused"}, {"HistoricalPaper"},
                     {exchange.Id}, {risk.Id}, {"MarketFill"}, {false}, {0m}, {-1}, {0},
                     {"{\"legacy\":true}"}, {DateTime.UtcNow}, {DateTime.UtcNow})
                """);
            paperSessionId = await ScalarLongAsync(
                db,
                $"SELECT Id FROM PaperTradingSessions WHERE TradingSessionId = {trading.Id}");

            await migrator.MigrateAsync(CurrentMigration);
            db.ChangeTracker.Clear();
            var migrated = await db.PaperTradingSessions.AsNoTracking().SingleAsync(row => row.Id == paperSessionId);
            Assert.Equal(PaperSessionUseClass.Research, migrated.UseClass);
            Assert.Equal(PaperSessionStatus.Paused, migrated.Status);
            Assert.Null(migrated.ParameterSetId);
            Assert.Null(migrated.BoundStrategyId);
            Assert.Null(migrated.QualificationVerifiedAtUtc);

            const string deploymentUseClass = "DeploymentSimulation";
            var constraint = await Assert.ThrowsAsync<MySqlException>(() =>
                db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE PaperTradingSessions SET UseClass = {deploymentUseClass} WHERE Id = {paperSessionId}"));
            Assert.Equal(3819, constraint.Number);

            await migrator.MigrateAsync(PreviousMigration);
            Assert.Equal(1, await ScalarLongAsync(db,
                $"SELECT COUNT(*) FROM PaperTradingSessions WHERE Id = {paperSessionId} AND Status = 'Paused'"));
            Assert.Equal(0, await ScalarLongAsync(db, """
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'PaperTradingSessions' AND COLUMN_NAME = 'UseClass'
                """));
        }
        finally
        {
            await migrator.MigrateAsync(CurrentMigration);
            await db.PaperTradingSessions
                .Where(row => row.TradingSessionId == trading.Id)
                .ExecuteDeleteAsync();

            await db.TradingSessions.Where(row => row.Id == trading.Id).ExecuteDeleteAsync();
            await db.PaperAccounts.Where(row => row.Id == account.Id).ExecuteDeleteAsync();
        }
    }

    private async Task<SeededDeploymentSession> SeedDeploymentSessionAsync(string suffix)
    {
        var prepared = await PrepareDeploymentCreationAsync(suffix);
        try
        {
            await using var scope = _fixture.Factory.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var service = BuildCreateService(
                services,
                new DeterministicLiveMarketManager(),
                new FixedCurrentUser(prepared.UserId));
            var result = await service.CreateAsync(prepared.Request);
            Assert.True(result.Succeeded, $"{result.ErrorField}: {result.ErrorMessage}");
            Assert.True(result.Data!.IsDeploymentSimulation);
            return new SeededDeploymentSession(
                prepared.Publication,
                prepared.ParameterSetId,
                prepared.StrategyId,
                prepared.SymbolId,
                prepared.Timeframe,
                result.Data.Id,
                prepared.PaperAccountId,
                prepared.UserId);
        }
        catch
        {
            await CleanupPreparedCreationAsync(prepared);
            throw;
        }
    }

    private async Task<PreparedDeploymentCreation> PrepareDeploymentCreationAsync(string suffix)
    {
        var publicationFixture = new Milestone231B1C6BPublicationIntegrationTests(_fixture);
        var publication = await publicationFixture.SeedQualifiedExperimentAsync($"b1c6c-{suffix}");
        long accountId = 0;
        try
        {
            await using var scope = _fixture.Factory.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var publish = await services.GetRequiredService<IValidationParameterSetPublicationService>()
                .PublishAsync(publication.ExperimentId, new());
            Assert.True(publish.Succeeded, publish.ErrorMessage);
            var db = services.GetRequiredService<MomoQuantDbContext>();
            var exchange = await db.Exchanges.SingleAsync(row => row.Id == 1);
            var symbol = await db.Symbols.SingleOrDefaultAsync(row => row.Id == publication.SymbolId);
            if (symbol is null)
            {
                symbol = new Symbol
                {
                    Id = publication.SymbolId,
                    ExchangeId = exchange.Id,
                    SymbolName = "BTCUSDT",
                    BaseAsset = "BTC",
                    QuoteAsset = "USDT",
                    ContractType = ContractType.Perpetual,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Symbols.Add(symbol);
            }

            var account = CreatePaperAccount(suffix);
            db.PaperAccounts.Add(account);
            await db.SaveChangesAsync();
            accountId = account.Id;
            var risk = await db.RiskProfiles.AsNoTracking().FirstAsync();
            var userId = await db.Users.AsNoTracking().Select(row => row.Id).FirstAsync();
            var strategy = await db.Strategies.AsNoTracking().SingleAsync(row =>
                row.Code == StrategyCode.PriceStructureBreakoutRetest);
            var request = new CreatePaperSessionRequest
            {
                Name = $"B1C6C {suffix} {Guid.NewGuid():N}",
                PaperAccountId = account.Id,
                ExchangeId = exchange.Id,
                SymbolIds = [symbol.Id],
                Timeframes = [publication.Timeframe],
                Mode = "LivePaper",
                UseClass = "DeploymentSimulation",
                RiskProfileId = risk.Id,
                StrategyIds = [strategy.Id],
                ParameterSetId = publish.Data!.Id,
                AllowAbnormalMarketPaperTrading = true
            };
            return new PreparedDeploymentCreation(
                publication,
                publish.Data.Id,
                strategy.Id,
                symbol.Id,
                publication.Timeframe,
                account.Id,
                userId,
                request);
        }
        catch
        {
            if (accountId > 0)
            {
                await using var cleanupScope = _fixture.Factory.Services.CreateAsyncScope();
                var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                await cleanupDb.PaperAccounts.Where(row => row.Id == accountId).ExecuteDeleteAsync();
            }

            await publicationFixture.CleanupAsync([publication]);
            throw;
        }
    }

    private static PaperSessionService BuildCreateService(
        IServiceProvider services,
        ILiveMarketConnectionManager live,
        ICurrentUserService currentUser,
        IPaperDeploymentQualificationVerifier? verifier = null,
        IPaperStateStore? stateStore = null) => new(
        services.GetRequiredService<IPaperTradingSessionRepository>(),
        services.GetRequiredService<IPaperAccountRepository>(),
        services.GetRequiredService<ITradingSessionRepository>(),
        services.GetRequiredService<IExchangeRepository>(),
        services.GetRequiredService<ISymbolRepository>(),
        services.GetRequiredService<IRiskProfileRepository>(),
        services.GetRequiredService<IStrategyRepository>(),
        services.GetRequiredService<IStrategyRegistry>(),
        services.GetRequiredService<IStrategyParameterSetRepository>(),
        services.GetRequiredService<IStrategyParameterProvider>(),
        services.GetRequiredService<IRiskRuleRepository>(),
        services.GetRequiredService<IBacktestDataLoader>(),
        stateStore ?? services.GetRequiredService<IPaperStateStore>(),
        live,
        services.GetRequiredService<IMarketSituationService>(),
        currentUser,
        services.GetRequiredService<IAuditService>(),
        services.GetRequiredService<IHigherTimeframeDatasetEnricher>(),
        verifier ?? services.GetRequiredService<IPaperDeploymentQualificationVerifier>(),
        services.GetRequiredService<IPaperSessionRelationalCoordinator>(),
        services.GetRequiredService<IRequiredAuditWriter>());

    private static PaperSessionControlService BuildControlService(
        IServiceProvider services,
        ILiveMarketConnectionManager live,
        ICurrentUserService currentUser,
        ILiveMarketBootstrapService? bootstrap = null,
        IPaperTradingSessionRepository? paperSessionRepository = null) => new(
        paperSessionRepository ?? services.GetRequiredService<IPaperTradingSessionRepository>(),
        services.GetRequiredService<ITradingSessionRepository>(),
        services.GetRequiredService<IPaperStateStore>(),
        services.GetRequiredService<IPaperTradingEngine>(),
        services.GetRequiredService<IPaperPersistenceService>(),
        live,
        bootstrap ?? new SuccessfulBootstrapService(),
        currentUser,
        services.GetRequiredService<IAuditService>(),
        services.GetRequiredService<IPaperDeploymentQualificationVerifier>(),
        services.GetRequiredService<IPaperSessionRelationalCoordinator>(),
        services.GetRequiredService<IRequiredAuditWriter>());

    private async Task InstallAuditFailureConstraintAsync(
        string action,
        bool allowCreateQualification = false)
    {
        var predicate = action switch
        {
            RequiredAuditActions.PaperDeploymentQualificationVerified =>
                allowCreateQualification
                    ? "`Action` <> 'PAPER_DEPLOYMENT_QUALIFICATION_VERIFIED' OR `NewValueJson` LIKE '%\"phase\":\"Create\"%'"
                    : "`Action` <> 'PAPER_DEPLOYMENT_QUALIFICATION_VERIFIED'",
            RequiredAuditActions.PaperSessionCreated => "`Action` <> 'PAPER_SESSION_CREATED'",
            RequiredAuditActions.PaperSessionStarted => "`Action` <> 'PAPER_SESSION_STARTED'",
            RequiredAuditActions.PaperSessionResumed => "`Action` <> 'PAPER_SESSION_RESUMED'",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        await RemoveAuditFailureConstraintAsync(db);
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"""
            ALTER TABLE `AuditLogs`
            ADD CONSTRAINT `CK_D1_RejectPaperAudit`
            CHECK ({predicate})
            """);
#pragma warning restore EF1002
    }

    private async Task RemoveAuditFailureConstraintAsync()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        await RemoveAuditFailureConstraintAsync(
            scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>());
    }

    private static async Task RemoveAuditFailureConstraintAsync(MomoQuantDbContext db)
    {
        var exists = await db.Database.SqlQuery<int>($"""
            SELECT COUNT(*) AS `Value`
            FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
            WHERE `CONSTRAINT_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'AuditLogs'
              AND `CONSTRAINT_NAME` = 'CK_D1_RejectPaperAudit'
            """).SingleAsync();
        if (exists == 1)
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE `AuditLogs` DROP CHECK `CK_D1_RejectPaperAudit`");
        }
    }

    private async Task CleanupDeploymentSessionAsync(SeededDeploymentSession seeded)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<IPaperStateStore>().Remove(seeded.PaperSessionId);
        var db = services.GetRequiredService<MomoQuantDbContext>();
        var tradingSessionId = await db.PaperTradingSessions
            .Where(row => row.Id == seeded.PaperSessionId)
            .Select(row => row.TradingSessionId)
            .SingleOrDefaultAsync();
        await db.AuditLogs.Where(row => row.EntityType == nameof(PaperTradingSession)
            && row.EntityId == seeded.PaperSessionId).ExecuteDeleteAsync();
        await db.PaperTradingSessions.Where(row => row.Id == seeded.PaperSessionId).ExecuteDeleteAsync();
        if (tradingSessionId > 0)
        {
            await db.TradingSessions.Where(row => row.Id == tradingSessionId).ExecuteDeleteAsync();
        }
        await db.PaperAccounts.Where(row => row.Id == seeded.PaperAccountId).ExecuteDeleteAsync();

        var publicationFixture = new Milestone231B1C6BPublicationIntegrationTests(_fixture);
        await publicationFixture.CleanupAsync([seeded.Publication]);
    }

    private async Task CleanupPreparedCreationAsync(PreparedDeploymentCreation prepared)
    {
        var paperSessionId = await FindPaperSessionIdAsync(prepared.PaperAccountId);
        if (paperSessionId is long id)
        {
            await CleanupDeploymentSessionAsync(new SeededDeploymentSession(
                prepared.Publication,
                prepared.ParameterSetId,
                prepared.StrategyId,
                prepared.SymbolId,
                prepared.Timeframe,
                id,
                prepared.PaperAccountId,
                prepared.UserId));
            return;
        }

        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            var tradingIds = await db.TradingSessions
                .Where(row => row.Name == $"Paper: {prepared.Request.Name}")
                .Select(row => row.Id)
                .ToListAsync();
            if (tradingIds.Count > 0)
            {
                await db.TradingSessions.Where(row => tradingIds.Contains(row.Id)).ExecuteDeleteAsync();
            }

            await db.PaperAccounts.Where(row => row.Id == prepared.PaperAccountId).ExecuteDeleteAsync();
        }

        var publicationFixture = new Milestone231B1C6BPublicationIntegrationTests(_fixture);
        await publicationFixture.CleanupAsync([prepared.Publication]);
    }

    private async Task<long?> FindPaperSessionIdAsync(long paperAccountId)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        return await db.PaperTradingSessions
            .Where(row => row.PaperAccountId == paperAccountId)
            .Select(row => (long?)row.Id)
            .SingleOrDefaultAsync();
    }

    private async Task CleanupResearchSessionAsync(long paperSessionId, long paperAccountId, long symbolId)
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<IPaperStateStore>().Remove(paperSessionId);
        var db = services.GetRequiredService<MomoQuantDbContext>();
        var tradingSessionId = await db.PaperTradingSessions
            .Where(row => row.Id == paperSessionId)
            .Select(row => row.TradingSessionId)
            .SingleAsync();
        await db.AuditLogs.Where(row => row.EntityType == nameof(PaperTradingSession)
            && row.EntityId == paperSessionId).ExecuteDeleteAsync();
        await db.PaperTradingSessions.Where(row => row.Id == paperSessionId).ExecuteDeleteAsync();
        await db.TradingSessions.Where(row => row.Id == tradingSessionId).ExecuteDeleteAsync();
        await db.PaperAccounts.Where(row => row.Id == paperAccountId).ExecuteDeleteAsync();
        await db.Symbols.Where(row => row.Id == symbolId).ExecuteDeleteAsync();
    }

    private static PaperAccount CreatePaperAccount(string suffix) => new()
    {
        Name = $"B1C6C {suffix} {Guid.NewGuid():N}",
        InitialBalance = 10_000m,
        CurrentBalance = 10_000m,
        CurrentEquity = 10_000m,
        Currency = "USDT",
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static async Task<long> ScalarLongAsync(MomoQuantDbContext db, string sql)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync();
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private sealed record SeededDeploymentSession(
        Milestone231B1C6BPublicationIntegrationTests.SeededPublication Publication,
        long ParameterSetId,
        long StrategyId,
        long SymbolId,
        string Timeframe,
        long PaperSessionId,
        long PaperAccountId,
        long UserId);

    private sealed record PreparedDeploymentCreation(
        Milestone231B1C6BPublicationIntegrationTests.SeededPublication Publication,
        long ParameterSetId,
        long StrategyId,
        long SymbolId,
        string Timeframe,
        long PaperAccountId,
        long UserId,
        CreatePaperSessionRequest Request);

    private sealed class CoordinatedVerifier(
        IPaperDeploymentQualificationVerifier inner,
        Func<int, PaperDeploymentQualificationResult, Task> afterVerification)
        : IPaperDeploymentQualificationVerifier
    {
        private int _invocationCount;
        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public async Task<PaperDeploymentQualificationResult> VerifyAsync(
            long parameterSetId,
            long strategyId,
            long symbolId,
            string timeframe,
            PaperDeploymentStoredBinding? storedBinding = null,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.VerifyAsync(
                parameterSetId,
                strategyId,
                symbolId,
                timeframe,
                storedBinding,
                cancellationToken);
            var invocation = Interlocked.Increment(ref _invocationCount);
            await afterVerification(invocation, result);
            return result;
        }
    }

    private sealed class RecordingStateStore : IPaperStateStore
    {
        private int _setCalls;
        public int SetCalls => Volatile.Read(ref _setCalls);
        public PaperSessionState? LastState { get; private set; }

        public bool TryGet(long sessionId, out PaperSessionState? state)
        {
            state = LastState?.Session.Id == sessionId ? LastState : null;
            return state is not null;
        }

        public void Set(long sessionId, PaperSessionState state)
        {
            Assert.Equal(sessionId, state.Session.Id);
            LastState = state;
            Interlocked.Increment(ref _setCalls);
        }

        public void Remove(long sessionId)
        {
            if (LastState?.Session.Id == sessionId)
            {
                LastState = null;
            }
        }
    }

    private sealed class FixedCurrentUser(long userId) : ICurrentUserService
    {
        public long? UserId => userId;
        public string? Email => "b1c6c@example.test";
        public UserRole? Role => UserRole.Admin;
        public bool IsAuthenticated => true;
    }

    private sealed class SuccessfulBootstrapService : ILiveMarketBootstrapService
    {
        public Task<ServiceResult<LiveBootstrapResult>> EnsureWarmupAsync(
            long exchangeId,
            long symbolId,
            Timeframe timeframe,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<LiveBootstrapResult>.Ok(new LiveBootstrapResult
            {
                DataSource = "B1C6C deterministic integration bootstrap",
                CandleCountUsed = 600,
                IndicatorsAvailable = true
            }));
    }

    private sealed class FailingBootstrapService : ILiveMarketBootstrapService
    {
        public Task<ServiceResult<LiveBootstrapResult>> EnsureWarmupAsync(
            long exchangeId,
            long symbolId,
            Timeframe timeframe,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<LiveBootstrapResult>.Fail(
                "PROVIDER_SECRET raw failure must not persist.",
                "provider"));
    }

    private sealed class ReloadCancellationGate
    {
        public TaskCompletionSource Reached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class CancellationAfterCommitPaperSessionRepository(
        IPaperTradingSessionRepository inner,
        ReloadCancellationGate gate) : IPaperTradingSessionRepository
    {
        private int _readCount;

        public async Task<PaperTradingSession?> GetByIdAsync(
            long id,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _readCount) == 2)
            {
                gate.Reached.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return await inner.GetByIdAsync(id, cancellationToken);
        }

        public Task<(IReadOnlyList<PaperTradingSession> Items, int TotalCount)> GetPagedAsync(
            PagedRequest request,
            CancellationToken cancellationToken = default) =>
            inner.GetPagedAsync(request, cancellationToken);

        public Task AddAsync(PaperTradingSession session, CancellationToken cancellationToken = default) =>
            inner.AddAsync(session, cancellationToken);

        public Task UpdateAsync(PaperTradingSession session, CancellationToken cancellationToken = default) =>
            inner.UpdateAsync(session, cancellationToken);

        public Task<IReadOnlyList<long>> GetRunningSessionIdsAsync(CancellationToken cancellationToken = default) =>
            inner.GetRunningSessionIdsAsync(cancellationToken);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            inner.SaveChangesAsync(cancellationToken);
    }

    private sealed class DeterministicLiveMarketManager : ILiveMarketConnectionManager
    {
        private int _subscribeCalls;
        private int _linkCalls;
        private int _unlinkCalls;
        public int SubscribeCalls => Volatile.Read(ref _subscribeCalls);
        public int LinkCalls => Volatile.Read(ref _linkCalls);
        public int UnlinkCalls => Volatile.Read(ref _unlinkCalls);
        public bool IsAvailable => true;
        public bool IsConnected => true;
        public event Action<LiveCandleUpdate>? CandleUpdated { add { } remove { } }
        public event Action<LiveCandleUpdate>? CandleClosed { add { } remove { } }
        public event Action<LiveMarketConnectionStatus>? ConnectionStatusChanged { add { } remove { } }

        public LiveMarketConnectionStatus GetStatus() => new()
        {
            Provider = "B1C6C deterministic",
            Connected = true,
            Subscriptions = []
        };

        public LiveMarketDiagnosticsDto GetDiagnostics() => new()
        {
            Provider = "B1C6C deterministic",
            Connected = true,
            Subscriptions = []
        };

        public bool IsSubscribed(long symbolId, Timeframe timeframe) => SubscribeCalls > 0;
        public void LinkSession(long sessionId, long symbolId, Timeframe timeframe) =>
            Interlocked.Increment(ref _linkCalls);

        public void UnlinkSession(long sessionId) => Interlocked.Increment(ref _unlinkCalls);

        public Task<ServiceResult<LiveMarketStatusDto>> SubscribeAsync(
            LiveMarketSubscribeRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _subscribeCalls);
            return Task.FromResult(OkStatus());
        }

        public Task<ServiceResult<LiveMarketStatusDto>> UnsubscribeAsync(
            LiveMarketSubscribeRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(OkStatus());

        public Task<ServiceResult<LiveMarketStatusDto>> ReconnectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OkStatus());

        private static ServiceResult<LiveMarketStatusDto> OkStatus() =>
            ServiceResult<LiveMarketStatusDto>.Ok(new LiveMarketStatusDto
            {
                Provider = "B1C6C deterministic",
                Connected = true,
                Subscriptions = []
            });
    }
}
