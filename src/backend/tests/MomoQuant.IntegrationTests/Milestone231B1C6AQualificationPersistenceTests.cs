using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Optimization.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Persistence;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.IntegrationTests;

[Collection("Integration")]
public sealed class Milestone231B1C6AQualificationPersistenceTests
    : IClassFixture<DisposableIntegrationDatabaseFixture>
{
    private const string PreviousMigration = "20260726193537_M230E2C1_DurableAuditExecutions";
    private const string CurrentMigration = "20260801174916_AddParameterSetQualificationStatus";
    private readonly DisposableIntegrationDatabaseFixture _fixture;

    public Milestone231B1C6AQualificationPersistenceTests(DisposableIntegrationDatabaseFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task Migration_ApprovedAndUnapprovedHistoricalRowsBecomeNotEvaluated_AndRollbackPreservesApproval()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigration);
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO StrategyParameterSets
                    (Name, StrategyCode, SymbolId, Timeframe, MarketRegime, ParametersJson, Source,
                     OptimizationRunId, TrainingRangeJson, ValidationRangeJson, TrainingMetricsJson,
                     ValidationMetricsJson, RobustnessScore, IsApproved, IsDefaultForStrategy,
                     IsDefaultForSymbolTimeframe, CreatedAtUtc, ApprovedAtUtc)
                VALUES
                    ('B1C6A historical approved', 'MOMO_ADAPTIVE_MTF_TREND_BREAKOUT', NULL, '15m', NULL,
                     '{{}}', 'Manual', NULL, NULL, NULL, NULL, NULL, NULL, 1, 0, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
                    ('B1C6A historical unapproved', 'MOMO_ADAPTIVE_MTF_TREND_BREAKOUT', NULL, '15m', NULL,
                     '{{}}', 'Manual', NULL, NULL, NULL, NULL, NULL, NULL, 0, 0, 0, UTC_TIMESTAMP(6), NULL)
                """);

            await migrator.MigrateAsync(CurrentMigration);
            db.ChangeTracker.Clear();

            var upgraded = await db.StrategyParameterSets
                .AsNoTracking()
                .Where(row => row.Name.StartsWith("B1C6A historical"))
                .OrderBy(row => row.Name)
                .ToListAsync();

            Assert.Equal(2, upgraded.Count);
            Assert.All(upgraded, row =>
                Assert.Equal(ParameterSetQualificationStatus.HistoricalNotEvaluated, row.QualificationStatus));
            Assert.True(upgraded.Single(row => row.Name == "B1C6A historical approved").IsApproved);
            Assert.False(upgraded.Single(row => row.Name == "B1C6A historical unapproved").IsApproved);

            await migrator.MigrateAsync(PreviousMigration);

            Assert.Equal(0, await ScalarLongAsync(db, """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'StrategyParameterSets'
                  AND COLUMN_NAME = 'QualificationStatus'
                """));
            Assert.Equal(1, await ScalarLongAsync(db, """
                SELECT COUNT(*) FROM StrategyParameterSets
                WHERE Name = 'B1C6A historical approved' AND IsApproved = 1
                """));
            Assert.Equal(1, await ScalarLongAsync(db, """
                SELECT COUNT(*) FROM StrategyParameterSets
                WHERE Name = 'B1C6A historical unapproved' AND IsApproved = 0
                """));
        }
        finally
        {
            await migrator.MigrateAsync(CurrentMigration);
        }
    }

    [Fact]
    public async Task ForgedQualificationFields_CannotPersistDeploymentQualifiedThroughHttp()
    {
        var (client, userId) = await IntegrationDisposableAuth.CreateAuthorizedAdminClientAsync(
            _fixture.Factory,
            "b1c6a-qualification");
        long? parameterSetId = null;

        try
        {
            using var response = await client.PostAsJsonAsync("/api/v1/strategy-research/parameter-sets", new
            {
                name = "B1C6A forged qualification",
                strategyCode = "MOMO_ADAPTIVE_MTF_TREND_BREAKOUT",
                timeframe = "15m",
                parameters = new Dictionary<string, string> { ["minimumStrength"] = "0.5" },
                approve = true,
                validationStatus = "Passed",
                validationTradeCount = 10,
                qualificationStatus = "DeploymentQualified",
                isDeploymentQualified = true
            });

            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var payload = await response.Content.ReadFromJsonAsync<ApiResponse<StrategyParameterSetDto>>(
                IntegrationTestJson.Options);
            var dto = Assert.IsType<StrategyParameterSetDto>(payload!.Data);
            parameterSetId = dto.Id;

            Assert.True(dto.IsApproved);
            Assert.Equal("ResearchOnly", dto.QualificationStatus);
            Assert.Equal("Research", dto.ApprovalScope);
            Assert.False(dto.IsDeploymentQualified);
            Assert.Equal(["PARAMETER_SET_RESEARCH_ONLY"], dto.QualificationBlockingReasons);

            await using var scope = _fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            var stored = await db.StrategyParameterSets.AsNoTracking().SingleAsync(row => row.Id == dto.Id);
            Assert.True(stored.IsApproved);
            Assert.Equal(ParameterSetQualificationStatus.ResearchOnly, stored.QualificationStatus);
        }
        finally
        {
            await using var cleanupScope = _fixture.Factory.Services.CreateAsyncScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            if (parameterSetId.HasValue)
            {
                await cleanupDb.StrategyParameterSets.Where(row => row.Id == parameterSetId.Value).ExecuteDeleteAsync();
            }
            await IntegrationDisposableAuth.DeleteUsersAsync(_fixture.Factory, userId);
            client.Dispose();
        }
    }

    [Fact]
    public async Task CleanDatabase_HasRequiredStringColumnAndNoPendingMigration()
    {
        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(1, await ScalarLongAsync(db, """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'StrategyParameterSets'
              AND COLUMN_NAME = 'QualificationStatus'
              AND IS_NULLABLE = 'NO'
              AND CHARACTER_MAXIMUM_LENGTH = 32
            """));
    }

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
}
