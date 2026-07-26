using Microsoft.Extensions.Configuration;
using MomoQuant.Persistence;

namespace MomoQuant.UnitTests;

public sealed class DatabaseMigrationOptionsTests
{
    [Fact]
    public void MigrationApplyOnStartup_DefaultsFalse()
    {
        var options = new DatabaseMigrationOptions();

        Assert.False(options.ApplyOnStartup);
        Assert.False(options.RequireTestSuffixWhenApplying);
        Assert.True(options.LogPendingMigrationsWhenDisabled);
        Assert.False(DatabaseMigrationExtensions.ShouldApplyMigrations(options));
    }

    [Fact]
    public void ConfigurationBinding_BindsMigrationOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseMigrations:ApplyOnStartup"] = "true",
                ["DatabaseMigrations:RequireTestSuffixWhenApplying"] = "true",
                ["DatabaseMigrations:LogPendingMigrationsWhenDisabled"] = "false"
            })
            .Build();

        var options = configuration
            .GetSection(DatabaseMigrationOptions.SectionName)
            .Get<DatabaseMigrationOptions>();

        Assert.NotNull(options);
        Assert.True(options.ApplyOnStartup);
        Assert.True(options.RequireTestSuffixWhenApplying);
        Assert.False(options.LogPendingMigrationsWhenDisabled);
        Assert.True(DatabaseMigrationExtensions.ShouldApplyMigrations(options));
    }

    [Fact]
    public void ApplyOnStartupFalse_DoesNotRequireMigrationTarget()
    {
        var options = new DatabaseMigrationOptions
        {
            ApplyOnStartup = false,
            RequireTestSuffixWhenApplying = true
        };

        var exception = Record.Exception(() =>
            DatabaseMigrationExtensions.ValidateTargetForMigration(null, options));

        Assert.Null(exception);
        Assert.False(DatabaseMigrationExtensions.ShouldApplyMigrations(options));
    }

    [Fact]
    public void RequireTestSuffix_AcceptsTestDatabase()
    {
        var options = RequiredTestTargetOptions();

        var exception = Record.Exception(() =>
            DatabaseMigrationExtensions.ValidateTargetForMigration(
                "Server=localhost;Database=momo_quant_test;User=test;Password=not-logged;",
                options));

        Assert.Null(exception);
    }

    [Fact]
    public void RequireTestSuffix_RejectsNonTestDatabaseSafely()
    {
        const string password = "SensitiveMigrationPassword";
        var options = RequiredTestTargetOptions();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseMigrationExtensions.ValidateTargetForMigration(
                $"Server=localhost;Database=momo_quant;User=test;Password={password};",
                options));

        Assert.Contains("_test", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(password, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequireTestSuffix_RejectsInvalidConnectionSafely()
    {
        const string password = "SensitiveMigrationPassword";
        var options = RequiredTestTargetOptions();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseMigrationExtensions.ValidateTargetForMigration(
                $"invalid;Password={password}",
                options));

        Assert.DoesNotContain(password, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static DatabaseMigrationOptions RequiredTestTargetOptions() =>
        new()
        {
            ApplyOnStartup = true,
            RequireTestSuffixWhenApplying = true
        };
}
