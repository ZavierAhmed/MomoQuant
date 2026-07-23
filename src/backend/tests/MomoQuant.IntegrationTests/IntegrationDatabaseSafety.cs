namespace MomoQuant.IntegrationTests;

/// <summary>
/// Guards integration tests from accidentally targeting a non-disposable database.
/// Never logs or returns the full connection string (may contain credentials).
/// </summary>
public static class IntegrationDatabaseSafety
{
    public static void AssertDisposableTestDatabase(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Integration tests require a MySQL connection string with a Database name ending in '_test'.");
        }

        var databaseName = TryParseDatabaseName(connectionString);
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException(
                "Integration tests require a Database name in the MySQL connection string.");
        }

        if (!databaseName.EndsWith("_test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Integration tests refuse to start: the MySQL database name must end with '_test' (case-insensitive).");
        }
    }

    internal static string? TryParseDatabaseName(string connectionString)
    {
        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = segment.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = segment[..separator].Trim();
            if (key.Equals("Database", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase))
            {
                var value = segment[(separator + 1)..].Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }
}
