using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Persistence;
using MomoQuant.Persistence.Seeding;
using MySqlConnector;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Owns one uniquely named MySQL database for a stateful integration-test fixture.
/// The configured shared integration database is used only as a server-connection template.
/// </summary>
public sealed class DisposableIntegrationDatabaseFixture : IAsyncLifetime
{
    private const int MySqlIdentifierLimit = 64;
    private readonly string _prefix;
    private IntegrationDatabaseTarget? _sharedTarget;
    private IntegrationDatabaseTarget? _disposableTarget;
    private MomoQuantWebApplicationFactory? _factory;

    public DisposableIntegrationDatabaseFixture()
        : this("momo_231a1_seeder")
    {
    }

    internal DisposableIntegrationDatabaseFixture(string prefix)
    {
        _prefix = prefix;
    }

    public string DatabaseName => _disposableTarget?.NormalizedDatabaseName
        ?? throw new InvalidOperationException("Disposable database has not been initialized.");

    public MomoQuantWebApplicationFactory Factory => _factory
        ?? throw new InvalidOperationException("Disposable database has not been initialized.");

    public async Task InitializeAsync()
    {
        _sharedTarget = IntegrationDatabaseConnectionResolver.Resolve();
        var databaseName = CreateDatabaseName(_prefix);
        _disposableTarget = CreateTarget(_sharedTarget, databaseName);

        await CreateDatabaseAsync(_sharedTarget, _disposableTarget);
        try
        {
            _factory = new MomoQuantWebApplicationFactory
            {
                DatabaseTargetOverride = _disposableTarget
            };

            // Force normal application startup: migrations and ordinary seeding now target only this database.
            _ = _factory.Services;
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            await db.Database.CanConnectAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
            _factory = null;
        }

        if (_sharedTarget is not null && _disposableTarget is not null)
        {
            await DropDatabaseAsync(_sharedTarget, _disposableTarget);
        }

        _disposableTarget = null;
        _sharedTarget = null;
    }

    public async Task<SeederContractTestScope> CreateTestScopeAsync()
    {
        var scope = Factory.Services.CreateAsyncScope();
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            var transaction = await db.Database.BeginTransactionAsync();
            var seeder = scope.ServiceProvider.GetRequiredService<IStrategyDataSeeder>();
            return new SeederContractTestScope(scope, db, seeder, transaction);
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    internal static string CreateDatabaseName(string prefix)
    {
        var sanitizedPrefix = new string(prefix
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray())
            .Trim('_');
        if (string.IsNullOrWhiteSpace(sanitizedPrefix))
        {
            sanitizedPrefix = "momo_integration";
        }

        const string suffix = "_test";
        var nonce = Guid.NewGuid().ToString("N");
        var availablePrefixLength = MySqlIdentifierLimit - suffix.Length - nonce.Length - 1;
        sanitizedPrefix = sanitizedPrefix[..Math.Min(sanitizedPrefix.Length, availablePrefixLength)];
        return $"{sanitizedPrefix}_{nonce}{suffix}";
    }

    private static IntegrationDatabaseTarget CreateTarget(
        IntegrationDatabaseTarget sharedTarget,
        string databaseName)
    {
        ValidateGeneratedTarget(sharedTarget, databaseName);
        var builder = new MySqlConnectionStringBuilder(sharedTarget.ConnectionString)
        {
            Database = databaseName
        };
        return IntegrationDatabaseSafetyGuard.Validate(builder.ConnectionString, "disposable fixture");
    }

    private static async Task CreateDatabaseAsync(
        IntegrationDatabaseTarget sharedTarget,
        IntegrationDatabaseTarget disposableTarget)
    {
        ValidateGeneratedTarget(sharedTarget, disposableTarget.NormalizedDatabaseName);
        var serverBuilder = new MySqlConnectionStringBuilder(sharedTarget.ConnectionString)
        {
            Database = string.Empty
        };
        await using var connection = new MySqlConnection(serverBuilder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE `{disposableTarget.NormalizedDatabaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(
        IntegrationDatabaseTarget sharedTarget,
        IntegrationDatabaseTarget disposableTarget)
    {
        ValidateGeneratedTarget(sharedTarget, disposableTarget.NormalizedDatabaseName);
        var serverBuilder = new MySqlConnectionStringBuilder(sharedTarget.ConnectionString)
        {
            Database = string.Empty
        };
        await using var connection = new MySqlConnection(serverBuilder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE `{disposableTarget.NormalizedDatabaseName}`;";
        await command.ExecuteNonQueryAsync();
    }

    private static void ValidateGeneratedTarget(IntegrationDatabaseTarget sharedTarget, string databaseName)
    {
        var validated = IntegrationDatabaseSafetyGuard.Validate(
            new MySqlConnectionStringBuilder(sharedTarget.ConnectionString) { Database = databaseName }.ConnectionString,
            "disposable fixture validation");
        if (validated.NormalizedDatabaseName.Length > MySqlIdentifierLimit
            || string.Equals(validated.NormalizedDatabaseName, sharedTarget.NormalizedDatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnsafeIntegrationDatabaseTargetException(
                IntegrationDatabaseErrorCodes.ReservedName,
                "Disposable integration database target is invalid.",
                "disposable fixture validation",
                validated.NormalizedDatabaseName);
        }
    }
}

public sealed class SeederContractTestScope : IAsyncDisposable
{
    private readonly AsyncServiceScope _scope;
    private readonly IDbContextTransaction _transaction;

    internal SeederContractTestScope(
        AsyncServiceScope scope,
        MomoQuantDbContext db,
        IStrategyDataSeeder seeder,
        IDbContextTransaction transaction)
    {
        _scope = scope;
        Db = db;
        Seeder = seeder;
        _transaction = transaction;
    }

    public MomoQuantDbContext Db { get; }
    public IStrategyDataSeeder Seeder { get; }

    public async ValueTask DisposeAsync()
    {
        await _transaction.RollbackAsync();
        await _transaction.DisposeAsync();
        await _scope.DisposeAsync();
    }
}
