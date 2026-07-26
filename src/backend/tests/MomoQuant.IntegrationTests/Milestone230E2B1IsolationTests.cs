using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0E2B1 — integration database isolation, fail-closed targeting, and schema proof on *_test.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230E2B1IsolationTests : IClassFixture<E2BSeamFactory>
{
    private readonly E2BSeamFactory _factory;

    public Milestone230E2B1IsolationTests(E2BSeamFactory factory) => _factory = factory;

    private sealed class RecordingObserver : IIntegrationDatabaseInitializationObserver
    {
        public int DbContextCreatingCalls { get; private set; }
        public int MigratingCalls { get; private set; }
        public int SeedingCalls { get; private set; }

        public void OnDbContextCreating() => DbContextCreatingCalls++;
        public void OnMigrating() => MigratingCalls++;
        public void OnSeeding() => SeedingCalls++;
    }

    [Fact]
    public void IntegrationHost_MomoQuantTarget_FailsBeforeDatabaseInitialization()
    {
        var observer = new RecordingObserver();
        var unsafeCs =
            "Server=localhost;Port=3306;Database=momo_quant;User=u;Password=SuperSecretPasswordValue!";

        var ex = Assert.Throws<UnsafeIntegrationDatabaseTargetException>(() =>
            IntegrationDatabaseInitialization.ResolveTarget(
                observer,
                environmentValue: unsafeCs,
                localFileReader: () => null,
                useProcessConfiguration: false));

        Assert.Equal(IntegrationDatabaseErrorCodes.MustEndWithTest, ex.SafeErrorCode);
        Assert.Equal("momo_quant", ex.ResolvedDatabaseName);
        Assert.Equal(0, observer.DbContextCreatingCalls);
        Assert.Equal(0, observer.MigratingCalls);
        Assert.Equal(0, observer.SeedingCalls);
        Assert.DoesNotContain("SuperSecretPasswordValue", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IntegrationHost_MissingConnection_DoesNotFallBackToApplicationDatabase()
    {
        var ex = Assert.Throws<UnsafeIntegrationDatabaseTargetException>(() =>
            IntegrationDatabaseConnectionResolver.Resolve(null, () => null));

        Assert.Equal(IntegrationDatabaseErrorCodes.ConnectionNotConfigured, ex.SafeErrorCode);
        Assert.DoesNotContain("momo_quant;", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnvironmentVariable_TakesPrecedenceOverLocalEnv_ForUnsafeRejection()
    {
        var ex = Assert.Throws<UnsafeIntegrationDatabaseTargetException>(() =>
            IntegrationDatabaseConnectionResolver.Resolve(
                "Server=localhost;Database=momo_quant;User=u;Password=secret",
                () => "Server=localhost;Database=momo_quant_test;User=u;Password=secret"));

        Assert.Equal(IntegrationDatabaseErrorCodes.MustEndWithTest, ex.SafeErrorCode);
        Assert.Equal("MOMO_INTEGRATION_MYSQL", ex.ConnectionSource);
    }

    [Fact]
    public void IntegrationMigrationRunner_RejectsNonTestDatabase()
    {
        var options = new DatabaseMigrationOptions
        {
            ApplyOnStartup = true,
            RequireTestSuffixWhenApplying = true
        };

        Assert.Throws<InvalidOperationException>(() =>
            DatabaseMigrationExtensions.ValidateTargetForMigration(
                "Server=localhost;Database=momo_quant;User=u;Password=secret",
                options));
    }

    [Fact]
    public void ApiStartup_ApplyMigrationsFalse_DoesNotMutate()
    {
        var options = new DatabaseMigrationOptions();
        Assert.False(options.ApplyOnStartup);
        Assert.False(DatabaseMigrationExtensions.ShouldApplyMigrations(options));
    }

    [Fact]
    public void Factory_HasNoHardcodedMySqlFallback()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "MomoQuantWebApplicationFactory.cs");
        path = Path.GetFullPath(path);
        Assert.True(File.Exists(path), path);
        var text = File.ReadAllText(path);
        Assert.DoesNotContain(
            "?? \"Server=localhost;Port=3306;Database=momo_quant_test",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "IntegrationTest_DbPassword_NotForProd!",
            text,
            StringComparison.Ordinal);
        Assert.Contains("IntegrationDatabaseInitialization.ResolveTarget", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IntegrationHost_MomoQuantTestTarget_StartsSuccessfully_AndReportsSafeName()
    {
        // Force host creation
        _ = _factory.Services;
        Assert.False(string.IsNullOrWhiteSpace(MomoQuantWebApplicationFactory.LastResolvedDatabaseName));
        Assert.EndsWith("_test", MomoQuantWebApplicationFactory.LastResolvedDatabaseName!, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(MomoQuantWebApplicationFactory.LastConnectionSource));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var conn = db.Database.GetDbConnection();
        Assert.Contains("_test", conn.Database, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task E2BColumns_ExistOnMomoQuantTest()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection!.State != System.Data.ConnectionState.Open)
        {
            await cmd.Connection.OpenAsync();
        }

        cmd.CommandText = """
            SELECT COLUMN_NAME, IS_NULLABLE, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'ValidationCandleAccessAudits'
              AND COLUMN_NAME IN ('AccessPayloadHash', 'AccessPayloadContractVersion')
            ORDER BY COLUMN_NAME
            """;
        var found = new List<(string Name, string Nullable, string Type, long? Len)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                found.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetInt64(3)));
            }
        }

        Assert.Equal(2, found.Count);
        Assert.All(found, f => Assert.Equal("YES", f.Nullable));
        Assert.All(found, f => Assert.Equal("varchar", f.Type));
        Assert.All(found, f => Assert.Equal(64, f.Len));
        Assert.EndsWith("_test", MomoQuantWebApplicationFactory.LastResolvedDatabaseName!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MigrationHistory_ContainsE2B_AndNoPending()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var pending = await db.Database.GetPendingMigrationsAsync();
        var applied = await db.Database.GetAppliedMigrationsAsync();

        Assert.Empty(pending);
        Assert.Contains(applied, m => m.Contains("M230E2B_AccessPayloadVerification", StringComparison.Ordinal));
        Assert.Contains(applied, m => m.Contains("M230E1", StringComparison.Ordinal));
        Assert.EndsWith("_test", MomoQuantWebApplicationFactory.LastResolvedDatabaseName!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExistingE2BPayloadTests_RunAgainstDatabaseEndingWithTest()
    {
        _ = _factory.Services;
        Assert.EndsWith(
            "_test",
            MomoQuantWebApplicationFactory.LastResolvedDatabaseName!,
            StringComparison.OrdinalIgnoreCase);
    }
}
