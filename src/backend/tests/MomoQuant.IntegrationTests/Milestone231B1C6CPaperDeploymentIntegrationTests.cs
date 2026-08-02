using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
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
        var publicationFixture = new Milestone231B1C6BPublicationIntegrationTests(_fixture);
        var publication = await publicationFixture.SeedQualifiedExperimentAsync($"b1c6c-{suffix}");
        try
        {
            await using var scope = _fixture.Factory.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var publish = await services.GetRequiredService<IValidationParameterSetPublicationService>()
                .PublishAsync(publication.ExperimentId, new());
            Assert.True(publish.Succeeded, publish.ErrorMessage);
            var parameterSetId = publish.Data!.Id;
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
            var risk = await db.RiskProfiles.AsNoTracking().FirstAsync();
            var userId = await db.Users.AsNoTracking().Select(row => row.Id).FirstAsync();
            var strategy = await db.Strategies.AsNoTracking().SingleAsync(row =>
                row.Code == StrategyCode.PriceStructureBreakoutRetest);
            var service = BuildCreateService(
                services,
                new DeterministicLiveMarketManager(),
                new FixedCurrentUser(userId));
            var result = await service.CreateAsync(new CreatePaperSessionRequest
            {
                Name = $"B1C6C {suffix}",
                PaperAccountId = account.Id,
                ExchangeId = exchange.Id,
                SymbolIds = [symbol.Id],
                Timeframes = [publication.Timeframe],
                Mode = "LivePaper",
                UseClass = "DeploymentSimulation",
                RiskProfileId = risk.Id,
                StrategyIds = [strategy.Id],
                ParameterSetId = parameterSetId,
                AllowAbnormalMarketPaperTrading = true
            });
            Assert.True(result.Succeeded, $"{result.ErrorField}: {result.ErrorMessage}");
            Assert.True(result.Data!.IsDeploymentSimulation);
            return new SeededDeploymentSession(
                publication,
                parameterSetId,
                strategy.Id,
                symbol.Id,
                publication.Timeframe,
                result.Data.Id,
                account.Id,
                userId);
        }
        catch
        {
            await publicationFixture.CleanupAsync([publication]);
            throw;
        }
    }

    private static PaperSessionService BuildCreateService(
        IServiceProvider services,
        ILiveMarketConnectionManager live,
        ICurrentUserService currentUser) => new(
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
        services.GetRequiredService<IPaperStateStore>(),
        live,
        services.GetRequiredService<IMarketSituationService>(),
        currentUser,
        services.GetRequiredService<IAuditService>(),
        services.GetRequiredService<IHigherTimeframeDatasetEnricher>(),
        services.GetRequiredService<IPaperDeploymentQualificationVerifier>(),
        services.GetRequiredService<IPaperSessionRelationalCoordinator>());

    private static PaperSessionControlService BuildControlService(
        IServiceProvider services,
        ILiveMarketConnectionManager live,
        ICurrentUserService currentUser) => new(
        services.GetRequiredService<IPaperTradingSessionRepository>(),
        services.GetRequiredService<ITradingSessionRepository>(),
        services.GetRequiredService<IPaperStateStore>(),
        services.GetRequiredService<IPaperTradingEngine>(),
        services.GetRequiredService<IPaperPersistenceService>(),
        live,
        new SuccessfulBootstrapService(),
        currentUser,
        services.GetRequiredService<IAuditService>(),
        services.GetRequiredService<IPaperDeploymentQualificationVerifier>(),
        services.GetRequiredService<IPaperSessionRelationalCoordinator>());

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

    private sealed class DeterministicLiveMarketManager : ILiveMarketConnectionManager
    {
        private int _subscribeCalls;
        public int SubscribeCalls => Volatile.Read(ref _subscribeCalls);
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
        public void LinkSession(long sessionId, long symbolId, Timeframe timeframe) { }
        public void UnlinkSession(long sessionId) { }

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
