using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace MomoQuant.Persistence;

public static class DatabaseMigrationExtensions
{
    public static async Task ApplyMigrationsAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<MomoQuantDbContext>>();
        var options = scope.ServiceProvider
            .GetService<IOptions<DatabaseMigrationOptions>>()?.Value
            ?? new DatabaseMigrationOptions();

        if (!ShouldApplyMigrations(options))
        {
            if (options.LogPendingMigrationsWhenDisabled)
            {
                var disabledPendingMigrations =
                    (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                logger.LogInformation(
                    "Automatic database migrations are disabled. {MigrationCount} pending migration(s): {Migrations}",
                    disabledPendingMigrations.Count,
                    string.Join(", ", disabledPendingMigrations));
            }

            return;
        }

        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        ValidateTargetForMigration(
            configuration.GetConnectionString("DefaultConnection"),
            options);

        var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pendingMigrations.Count == 0)
        {
            logger.LogInformation("Database schema is up to date. No pending migrations.");
            return;
        }

        logger.LogInformation(
            "Applying {MigrationCount} pending migration(s): {Migrations}",
            pendingMigrations.Count,
            string.Join(", ", pendingMigrations));

        await dbContext.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("Database migrations applied successfully.");
    }

    public static bool ShouldApplyMigrations(DatabaseMigrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.ApplyOnStartup;
    }

    public static void ValidateTargetForMigration(
        string? connectionString,
        DatabaseMigrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.ApplyOnStartup || !options.RequireTestSuffixWhenApplying)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Database migration target is not configured; a database ending with '_test' is required.");
        }

        string databaseName;
        try
        {
            databaseName = new MySqlConnectionStringBuilder(connectionString).Database?.Trim() ?? string.Empty;
        }
        catch
        {
            throw new InvalidOperationException(
                "Database migration target is invalid; a database ending with '_test' is required.");
        }

        if (string.IsNullOrWhiteSpace(databaseName)
            || !databaseName.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Database migrations are restricted to databases ending with '_test'.");
        }
    }
}
