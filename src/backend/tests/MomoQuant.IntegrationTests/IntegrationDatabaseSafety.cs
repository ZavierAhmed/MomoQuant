using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Persistence;
using MySqlConnector;

namespace MomoQuant.IntegrationTests;

public static class IntegrationDatabaseErrorCodes
{
    public const string MustEndWithTest = "INTEGRATION_DATABASE_MUST_END_WITH_TEST";
    public const string ConnectionNotConfigured = "INTEGRATION_DATABASE_CONNECTION_NOT_CONFIGURED";
    public const string DatabaseNameMissing = "INTEGRATION_DATABASE_NAME_MISSING";
    public const string ConnectionInvalid = "INTEGRATION_DATABASE_CONNECTION_INVALID";
    public const string ReservedName = "INTEGRATION_DATABASE_RESERVED_NAME";
}

public sealed class UnsafeIntegrationDatabaseTargetException : Exception
{
    public UnsafeIntegrationDatabaseTargetException(
        string safeErrorCode,
        string safeMessage,
        string connectionSource,
        string? resolvedDatabaseName = null)
        : base(safeMessage)
    {
        SafeErrorCode = safeErrorCode;
        SafeMessage = safeMessage;
        ConnectionSource = connectionSource;
        ResolvedDatabaseName = resolvedDatabaseName;
    }

    public string SafeErrorCode { get; }
    public string? ResolvedDatabaseName { get; }
    public string ConnectionSource { get; }
    public string SafeMessage { get; }
}

public sealed record IntegrationDatabaseTarget
{
    internal IntegrationDatabaseTarget(
        string normalizedDatabaseName,
        string connectionSource,
        string connectionString,
        string? server)
    {
        NormalizedDatabaseName = normalizedDatabaseName;
        ConnectionSource = connectionSource;
        ConnectionString = connectionString;
        Server = server;
    }

    public string NormalizedDatabaseName { get; }
    public string ConnectionSource { get; }
    internal string ConnectionString { get; }
    public string? Server { get; }
}

public static class IntegrationDatabaseConnectionResolver
{
    public const string EnvironmentVariableName = "MOMO_INTEGRATION_MYSQL";

    public static IntegrationDatabaseTarget Resolve()
    {
        var environmentValue = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return Resolve(environmentValue, ReadFromFirstExistingLocalEnvFile);
    }

    public static IntegrationDatabaseTarget Resolve(
        string? environmentValue,
        Func<string?>? localFileReader)
    {
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return IntegrationDatabaseSafetyGuard.Validate(
                environmentValue,
                EnvironmentVariableName);
        }

        var fileValue = localFileReader?.Invoke();
        if (!string.IsNullOrWhiteSpace(fileValue))
        {
            return IntegrationDatabaseSafetyGuard.Validate(fileValue, "integration.local.env");
        }

        throw new UnsafeIntegrationDatabaseTargetException(
            IntegrationDatabaseErrorCodes.ConnectionNotConfigured,
            "Integration database connection is not configured. Set MOMO_INTEGRATION_MYSQL or provide integration.local.env.",
            "unconfigured");
    }

    internal static IReadOnlyList<string> GetLocalEnvCandidatePaths() =>
    [
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "integration.local.env")),
        Path.Combine(Directory.GetCurrentDirectory(), "tests", "MomoQuant.IntegrationTests", "integration.local.env"),
        Path.Combine(Directory.GetCurrentDirectory(), "integration.local.env")
    ];

    private static string? ReadFromFirstExistingLocalEnvFile()
    {
        var path = GetLocalEnvCandidatePaths().FirstOrDefault(File.Exists);
        if (path is null)
        {
            return null;
        }

        // utf-8-sig strips a BOM so the first key is not "\uFEFFMOMO_INTEGRATION_MYSQL".
        foreach (var rawLine in File.ReadAllLines(path, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            var line = rawLine.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim().TrimStart('\uFEFF');
            if (key.Equals(EnvironmentVariableName, StringComparison.Ordinal))
            {
                var value = line[(separator + 1)..].Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }
}

public static class IntegrationDatabaseSafetyGuard
{
    private static readonly HashSet<string> ReservedDatabaseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "mysql",
        "information_schema",
        "performance_schema",
        "sys"
    };

    public static IntegrationDatabaseTarget Validate(
        string? connectionString,
        string connectionSource)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new UnsafeIntegrationDatabaseTargetException(
                IntegrationDatabaseErrorCodes.ConnectionNotConfigured,
                "Integration database connection is not configured.",
                connectionSource);
        }

        MySqlConnectionStringBuilder builder;
        try
        {
            builder = new MySqlConnectionStringBuilder(connectionString);
        }
        catch
        {
            throw new UnsafeIntegrationDatabaseTargetException(
                IntegrationDatabaseErrorCodes.ConnectionInvalid,
                "Integration database connection string is invalid.",
                connectionSource);
        }

        if (string.IsNullOrWhiteSpace(builder.Server))
        {
            throw new UnsafeIntegrationDatabaseTargetException(
                IntegrationDatabaseErrorCodes.ConnectionInvalid,
                "Integration database connection must specify a server.",
                connectionSource);
        }

        var databaseName = builder.Database?.Trim();
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new UnsafeIntegrationDatabaseTargetException(
                IntegrationDatabaseErrorCodes.DatabaseNameMissing,
                "Integration database connection must specify a database name.",
                connectionSource);
        }

        if (ReservedDatabaseNames.Contains(databaseName))
        {
            throw new UnsafeIntegrationDatabaseTargetException(
                IntegrationDatabaseErrorCodes.ReservedName,
                "Integration tests refuse to target a reserved database name.",
                connectionSource,
                databaseName);
        }

        if (!databaseName.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnsafeIntegrationDatabaseTargetException(
                IntegrationDatabaseErrorCodes.MustEndWithTest,
                "Integration database name must end with '_test' (case-insensitive).",
                connectionSource,
                databaseName);
        }

        return new IntegrationDatabaseTarget(
            databaseName,
            connectionSource,
            connectionString,
            builder.Server.Trim());
    }
}

public static class IntegrationDatabaseSafety
{
    public static void AssertDisposableTestDatabase(string? connectionString) =>
        IntegrationDatabaseSafetyGuard.Validate(connectionString, "caller");
}

public static partial class ConnectionStringRedaction
{
    [GeneratedRegex(@"(?i)(^|;)\s*(Password|Pwd)\s*=\s*[^;]*", RegexOptions.CultureInvariant)]
    private static partial Regex PasswordSegmentRegex();

    public static string? Redact(string? connectionString)
    {
        if (connectionString is null)
        {
            return null;
        }

        return PasswordSegmentRegex().Replace(
            connectionString,
            match => $"{match.Groups[1].Value}{match.Groups[2].Value}=***");
    }
}

public interface IIntegrationDatabaseInitializationObserver
{
    void OnDbContextCreating();
    void OnMigrating();
    void OnSeeding();
}

public sealed class NoOpIntegrationDatabaseInitializationObserver
    : IIntegrationDatabaseInitializationObserver
{
    public static NoOpIntegrationDatabaseInitializationObserver Instance { get; } = new();

    private NoOpIntegrationDatabaseInitializationObserver()
    {
    }

    public void OnDbContextCreating()
    {
    }

    public void OnMigrating()
    {
    }

    public void OnSeeding()
    {
    }
}

public static class IntegrationDatabaseInitialization
{
    public static IntegrationDatabaseTarget ResolveTarget(
        IIntegrationDatabaseInitializationObserver observer,
        string? environmentValue = null,
        Func<string?>? localFileReader = null,
        bool useProcessConfiguration = true)
    {
        ArgumentNullException.ThrowIfNull(observer);

        var target = useProcessConfiguration
            ? IntegrationDatabaseConnectionResolver.Resolve()
            : IntegrationDatabaseConnectionResolver.Resolve(environmentValue, localFileReader);
        observer.OnDbContextCreating();
        return target;
    }
}

public static class IntegrationDatabaseMigrator
{
    public static async Task ApplyPendingMigrationsAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        IntegrationDatabaseSafetyGuard.Validate(connectionString, "ConnectionStrings:DefaultConnection");

        scope.ServiceProvider
            .GetService<IIntegrationDatabaseInitializationObserver>()
            ?.OnMigrating();

        await scope.ServiceProvider.ApplyMigrationsAsync(cancellationToken);
    }
}
