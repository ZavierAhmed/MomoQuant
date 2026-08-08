using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Audit;
using MomoQuant.Application.Common;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Audit;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

[Collection("Integration")]
public sealed class Milestone231B1C6D1AuditTrustBoundaryIntegrationTests
    : IClassFixture<DisposableIntegrationDatabaseFixture>
{
    private const long RequiredEntityId = 9_231_601;
    private const long TelemetryEntityId = 9_231_602;
    private const string TelemetryAction = "D1_TELEMETRY_ISOLATION";
    private static readonly DateTime EvidenceTime =
        new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

    private readonly DisposableIntegrationDatabaseFixture _fixture;

    public Milestone231B1C6D1AuditTrustBoundaryIntegrationTests(
        DisposableIntegrationDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task RequiredWriter_SharesCallerTransactionAndRollsBackWithIt()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<IRequiredAuditWriter>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        writer.AttachRequired(PublicationRequest());
        Assert.Single(db.ChangeTracker.Entries<AuditLog>());
        await db.SaveChangesAsync();

        await using (var independentScope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var independent = independentScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            Assert.False(await independent.AuditLogs.AnyAsync(row => row.EntityId == RequiredEntityId));
        }

        await transaction.RollbackAsync();
        db.ChangeTracker.Clear();

        await using var verificationScope = _fixture.Factory.Services.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        Assert.False(await verification.AuditLogs.AnyAsync(row => row.EntityId == RequiredEntityId));
    }

    [Fact]
    public async Task Coordinator_FailedResultRollsBackChangesAlreadySavedInTransaction()
    {
        const long entityId = RequiredEntityId + 100;
        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            var writer = scope.ServiceProvider.GetRequiredService<IRequiredAuditWriter>();
            var coordinator = scope.ServiceProvider.GetRequiredService<IPaperSessionRelationalCoordinator>();

            var result = await coordinator.ExecuteCreationAsync(async token =>
            {
                var request = PublicationRequest();
                writer.AttachRequired(
                    request with
                    {
                        EntityId = entityId,
                        Metadata = ((ParameterSetPublicationAuditMetadata)request.Metadata) with
                        {
                            ParameterSetId = entityId
                        }
                    },
                    token);
                await db.SaveChangesAsync(token);
                return ServiceResult<bool>.Fail("Deliberate transactional failure.", "D1_EXPECTED_FAILURE");
            });

            Assert.False(result.Succeeded);
        }

        await using var verificationScope = _fixture.Factory.Services.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        Assert.False(await verification.AuditLogs.AnyAsync(row => row.EntityId == entityId));
    }

    [Fact]
    public async Task TelemetryWriter_UsesIndependentContextAndCannotSaveCallerMutation()
    {
        string originalName;
        long strategyId;
        await using (var callerScope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var caller = callerScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            var strategy = await caller.Strategies.OrderBy(row => row.Id).FirstAsync();
            strategyId = strategy.Id;
            originalName = strategy.Name;
            strategy.Name = "UNCOMMITTED_D1_CALLER_MUTATION";

            var telemetry = _fixture.Factory.Services.GetRequiredService<IAuditTelemetryWriter>();
            await telemetry.WriteTelemetryAsync(new AuditTelemetryRequest(
                TelemetryAction,
                "Strategy",
                TelemetryEntityId,
                null,
                null,
                "{\"cookie\":\"unsafe-cookie\",\"state\":\"Observed\"}",
                null,
                null));
        }

        await using var verificationScope = _fixture.Factory.Services.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        Assert.Equal(originalName, (await verification.Strategies.SingleAsync(row => row.Id == strategyId)).Name);
        var audit = await verification.AuditLogs.SingleAsync(row =>
            row.Action == TelemetryAction && row.EntityId == TelemetryEntityId);
        Assert.Contains("[REDACTED]", audit.NewValueJson, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-cookie", audit.NewValueJson, StringComparison.Ordinal);

        verification.AuditLogs.Remove(audit);
        await verification.SaveChangesAsync();
    }

    [Theory]
    [InlineData("CleanBaselinePreviewed")]
    [InlineData("CleanBaselineFailed")]
    [InlineData("CleanBaselineExecuted")]
    [InlineData("FakeMarketDataCleanupPreviewed")]
    [InlineData("FakeMarketDataCleanupFailed")]
    [InlineData("FakeMarketDataCleanupExecuted")]
    public async Task LegacyTelemetry_PreservesPascalCaseCleanupActionsAndSanitizesPayload(string action)
    {
        var entityId = TelemetryEntityId + action.Length;
        await using (var scope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var telemetry = scope.ServiceProvider.GetRequiredService<IAuditTelemetryWriter>();
            await telemetry.WriteTelemetryAsync(new AuditTelemetryRequest(
                action,
                "Cleanup",
                entityId,
                null,
                null,
                "{\"password\":\"should-not-persist\",\"state\":\"Observed\"}",
                null,
                null));
        }

        await using var verificationScope = _fixture.Factory.Services.CreateAsyncScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var audit = await db.AuditLogs.SingleAsync(row =>
            row.Action == action && row.EntityId == entityId);
        Assert.Equal(action, audit.Action);
        Assert.Contains("[REDACTED]", audit.NewValueJson, StringComparison.Ordinal);
        Assert.DoesNotContain("should-not-persist", audit.NewValueJson, StringComparison.Ordinal);
        db.AuditLogs.Remove(audit);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task TelemetryInsertFailure_IsolatedFromCallerMutationAndDoesNotPersistTelemetry()
    {
        const string action = "D1_TELEMETRY_FAILURE";
        string originalName;
        long strategyId;
        try
        {
            await InstallTelemetryFailureConstraintAsync();
            await using (var callerScope = _fixture.Factory.Services.CreateAsyncScope())
            {
                var caller = callerScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                var strategy = await caller.Strategies.OrderBy(row => row.Id).FirstAsync();
                strategyId = strategy.Id;
                originalName = strategy.Name;
                strategy.Name = "UNCOMMITTED_D1_TELEMETRY_MUTATION";

                var telemetry = callerScope.ServiceProvider.GetRequiredService<IAuditTelemetryWriter>();
                await telemetry.WriteTelemetryAsync(new AuditTelemetryRequest(
                    action,
                    "Strategy",
                    TelemetryEntityId + 100,
                    null,
                    null,
                    "{\"state\":\"Rejected\"}",
                    null,
                    null));
            }

            await using var verificationScope = _fixture.Factory.Services.CreateAsyncScope();
            var verification = verificationScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            Assert.Equal(originalName, (await verification.Strategies.SingleAsync(row => row.Id == strategyId)).Name);
            Assert.False(await verification.AuditLogs.AnyAsync(row =>
                row.Action == action && row.EntityId == TelemetryEntityId + 100));
        }
        finally
        {
            await RemoveTelemetryFailureConstraintAsync();
        }
    }

    [Fact]
    public async Task Publication_AuditInsertFailureRollsBackEveryMutationAndRetrySucceedsOnce()
    {
        var publicationFixture = new Milestone231B1C6BPublicationIntegrationTests(_fixture);
        var seeded = await publicationFixture.SeedQualifiedExperimentAsync("d1-audit-failure");
        long auditWatermark;
        await using (var watermarkScope = _fixture.Factory.Services.CreateAsyncScope())
        {
            var watermarkDb = watermarkScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            auditWatermark = await watermarkDb.AuditLogs.Select(row => (long?)row.Id).MaxAsync() ?? 0;
        }

        try
        {
            await InstallAuditFailureTriggerAsync(RequiredAuditActions.ParameterSetDeploymentQualified);
            await using (var failureScope = _fixture.Factory.Services.CreateAsyncScope())
            {
                var result = await failureScope.ServiceProvider
                    .GetRequiredService<IValidationParameterSetPublicationService>()
                    .PublishAsync(seeded.ExperimentId, new PublishValidationParameterSetRequest());
                Assert.False(result.Succeeded);
                Assert.Equal(AuditEvidenceCodes.Unavailable, result.ErrorField);
                Assert.DoesNotContain("SQL", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            }

            await using (var verificationScope = _fixture.Factory.Services.CreateAsyncScope())
            {
                var db = verificationScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
                Assert.False(await db.StrategyParameterSets.AnyAsync(row =>
                    row.QualificationSourceExperimentId == seeded.ExperimentId));
                Assert.False((await db.ValidationExperiments.SingleAsync(row => row.Id == seeded.ExperimentId)).IsCanonical);
                var strategy = await db.Strategies.SingleAsync(row =>
                    row.Code == StrategyCode.PriceStructureBreakoutRetest);
                Assert.Null(strategy.CanonicalValidationExperimentId);
                Assert.False(strategy.DeploymentQualificationEligible);
                Assert.False(await db.AuditLogs.AnyAsync(row =>
                    row.Id > auditWatermark
                    && row.Action == RequiredAuditActions.ParameterSetDeploymentQualified
                    && row.NewValueJson != null
                    && row.NewValueJson.Contains($"\"experimentId\":{seeded.ExperimentId}")));
            }

            await RemoveAuditFailureTriggerAsync();
            await using (var retryScope = _fixture.Factory.Services.CreateAsyncScope())
            {
                var retry = await retryScope.ServiceProvider
                    .GetRequiredService<IValidationParameterSetPublicationService>()
                    .PublishAsync(seeded.ExperimentId, new PublishValidationParameterSetRequest());
                Assert.True(retry.Succeeded, retry.ErrorMessage);
            }

            await using var retryVerificationScope = _fixture.Factory.Services.CreateAsyncScope();
            var retryVerification = retryVerificationScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            Assert.Single(await retryVerification.StrategyParameterSets
                .Where(row => row.QualificationSourceExperimentId == seeded.ExperimentId)
                .ToListAsync());
            Assert.Single(await retryVerification.AuditLogs
                .Where(row => row.Id > auditWatermark)
                .Where(row => row.Action == RequiredAuditActions.ParameterSetDeploymentQualified)
                .Where(row => row.NewValueJson != null && row.NewValueJson.Contains($"\"experimentId\":{seeded.ExperimentId}"))
                .ToListAsync());
        }
        finally
        {
            await RemoveAuditFailureTriggerAsync();
            await publicationFixture.CleanupAsync([seeded]);
        }
    }

    private static RequiredAuditRequest PublicationRequest() => new(
        RequiredAuditActions.ParameterSetDeploymentQualified,
        "StrategyParameterSet",
        RequiredEntityId,
        null,
        null,
        LogSeverity.Info,
        new ParameterSetPublicationAuditMetadata(
            RequiredEntityId,
            "MOMO_ADAPTIVE_MTF_TREND_BREAKOUT",
            9_231_611,
            9_231_612,
            "ABCDEF0123456789",
            "v1",
            EvidenceTime),
        EvidenceTime);

    private async Task InstallAuditFailureTriggerAsync(string action)
    {
        if (action != RequiredAuditActions.ParameterSetDeploymentQualified)
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        await RemoveAuditFailureConstraintAsync(db);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE `AuditLogs`
            ADD CONSTRAINT `CK_D1_RejectRequiredAudit`
            CHECK (`Action` <> 'PARAMETER_SET_DEPLOYMENT_QUALIFIED')
            """);
    }

    private async Task RemoveAuditFailureTriggerAsync()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        await RemoveAuditFailureConstraintAsync(db);
    }

    private async Task InstallTelemetryFailureConstraintAsync()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        await RemoveTelemetryFailureConstraintAsync(db);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE `AuditLogs`
            ADD CONSTRAINT `CK_D1_RejectTelemetryAction`
            CHECK (`Action` <> 'D1_TELEMETRY_FAILURE')
            """);
    }

    private async Task RemoveTelemetryFailureConstraintAsync()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        await RemoveTelemetryFailureConstraintAsync(
            scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>());
    }

    private static async Task RemoveTelemetryFailureConstraintAsync(MomoQuantDbContext db)
    {
        var exists = await db.Database.SqlQuery<int>($"""
            SELECT COUNT(*) AS `Value`
            FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
            WHERE `CONSTRAINT_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'AuditLogs'
              AND `CONSTRAINT_NAME` = 'CK_D1_RejectTelemetryAction'
            """).SingleAsync();
        if (exists == 1)
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE `AuditLogs` DROP CHECK `CK_D1_RejectTelemetryAction`");
        }
    }

    private static async Task RemoveAuditFailureConstraintAsync(MomoQuantDbContext db)
    {
        var exists = await db.Database.SqlQuery<int>($"""
            SELECT COUNT(*) AS `Value`
            FROM `INFORMATION_SCHEMA`.`TABLE_CONSTRAINTS`
            WHERE `CONSTRAINT_SCHEMA` = DATABASE()
              AND `TABLE_NAME` = 'AuditLogs'
              AND `CONSTRAINT_NAME` = 'CK_D1_RejectRequiredAudit'
            """).SingleAsync();
        if (exists == 1)
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE `AuditLogs` DROP CHECK `CK_D1_RejectRequiredAudit`");
        }
    }
}
