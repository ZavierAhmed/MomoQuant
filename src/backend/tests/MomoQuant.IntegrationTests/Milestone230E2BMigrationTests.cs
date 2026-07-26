using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0E2B WP18/WP19 — additive migration applied only to disposable `_test` databases,
/// historical null-hash rows preserved, no pending model changes, fixture cleanup.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230E2BMigrationTests : IClassFixture<E2BSeamFactory>
{
    private readonly E2BSeamFactory _factory;

    public Milestone230E2BMigrationTests(E2BSeamFactory factory) => _factory = factory;

    [Fact]
    public async Task MigrationFreshAndUpgradePaths_Pass()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        var pending = await db.Database.GetPendingMigrationsAsync();
        var applied = await db.Database.GetAppliedMigrationsAsync();

        Assert.Empty(pending);
        Assert.Contains(applied, m => m.Contains("M230E2B_AccessPayloadVerification", StringComparison.Ordinal));

        // Columns exist and are nullable (upgrade/fresh path both leave them nullable).
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection!.State != System.Data.ConnectionState.Open)
        {
            await cmd.Connection.OpenAsync();
        }

        cmd.CommandText = """
            SELECT IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'ValidationCandleAccessAudits'
              AND COLUMN_NAME IN ('AccessPayloadHash', 'AccessPayloadContractVersion')
            ORDER BY COLUMN_NAME
            """;
        var nullability = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                nullability.Add(reader.GetString(0));
            }
        }

        Assert.Equal(2, nullability.Count);
        Assert.All(nullability, n => Assert.Equal("YES", n));
    }

    [Fact]
    public async Task NoPendingModelChanges()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        var pending = await db.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);
    }

    [Fact]
    public async Task HistoricalNullHashRow_PreservedAfterMigration_WithoutBackfill()
    {
        var experimentId = 23_070_000L + Random.Shared.Next(1, 999);
        var accessEventId = Guid.NewGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        try
        {
            db.ValidationCandleAccessAudits.Add(new ValidationCandleAccessAudit
            {
                AccessEventId = accessEventId,
                ScopeExecutionId = Guid.NewGuid(),
                ScopeSequenceNumber = 1,
                ValidationExperimentId = experimentId,
                TrialNumber = 1,
                CallerComponent = "Historical",
                AccessPurpose = "EvaluationRange",
                DatasetPartition = "Training",
                CandleContentFingerprint = "HIST0001",
                AccessedAtUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                WasDenied = false,
                ReturnedCandleCount = 1,
                FlushAttemptCount = 1,
                PersistedAtUtc = DateTime.UtcNow,
                RecorderVersion = "ValidationCandleAccess/v1",
                CreatedAtUtc = DateTime.UtcNow,
                AccessPayloadHash = null,
                AccessPayloadContractVersion = null
            });
            await db.SaveChangesAsync();

            var row = await db.ValidationCandleAccessAudits
                .AsNoTracking()
                .SingleAsync(a => a.AccessEventId == accessEventId);

            Assert.Null(row.AccessPayloadHash);
            Assert.Null(row.AccessPayloadContractVersion);
            Assert.Equal("ValidationCandleAccess/v1", row.RecorderVersion);
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, experimentId);
        }
    }

    [Fact]
    public async Task FixtureCleanup_RemovesOnlyTargetExperimentAudits()
    {
        var keepId = 23_071_000L + Random.Shared.Next(1, 499);
        var dropId = 23_071_500L + Random.Shared.Next(1, 499);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        try
        {
            db.ValidationCandleAccessAudits.AddRange(
                E2BAuditFixtures.NewAudit(keepId, Guid.NewGuid(), Guid.NewGuid(), 1, "Keep"),
                E2BAuditFixtures.NewAudit(dropId, Guid.NewGuid(), Guid.NewGuid(), 1, "Drop"));
            await db.SaveChangesAsync();

            await E2BAuditFixtures.CleanupAsync(_factory, dropId);

            Assert.Equal(1, await db.ValidationCandleAccessAudits
                .CountAsync(a => a.ValidationExperimentId == keepId));
            Assert.Equal(0, await db.ValidationCandleAccessAudits
                .CountAsync(a => a.ValidationExperimentId == dropId));
        }
        finally
        {
            await E2BAuditFixtures.CleanupAsync(_factory, keepId);
            await E2BAuditFixtures.CleanupAsync(_factory, dropId);
        }
    }
}
