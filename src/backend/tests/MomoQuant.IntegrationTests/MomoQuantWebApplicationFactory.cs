using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

public class MomoQuantWebApplicationFactory : WebApplicationFactory<Program>
{
    public static string? LastResolvedDatabaseName { get; private set; }
    public static string? LastConnectionSource { get; private set; }

    public IIntegrationDatabaseInitializationObserver InitializationObserver { get; set; } =
        NoOpIntegrationDatabaseInitializationObserver.Instance;

    static MomoQuantWebApplicationFactory()
    {
        // Optional gitignored local overrides (never commit). Loaded once for the test host process.
        TryLoadLocalEnvFile();

        // Environment variables override user-secrets loaded in Development.
        Environment.SetEnvironmentVariable("MOMO_SKIP_SECRETS_VALIDATION", "true");
        Environment.SetEnvironmentVariable("Jwt__Secret", "IntegrationTest_JwtSecret_Key_AtLeast_32_Chars_Long!");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "MomoQuant");
        Environment.SetEnvironmentVariable("Jwt__Audience", "MomoQuant");
        Environment.SetEnvironmentVariable("Seed__AdminPassword", "Admin123!");
        Environment.SetEnvironmentVariable("Seed__AdminEmail", "admin@momoquant.local");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        var target = IntegrationDatabaseInitialization.ResolveTarget(InitializationObserver);
        LastResolvedDatabaseName = target.NormalizedDatabaseName;
        LastConnectionSource = target.ConnectionSource;

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            var redis = Environment.GetEnvironmentVariable("MOMO_INTEGRATION_REDIS")
                ?? "127.0.0.1:6379,password=IntegrationTest_RedisPassword_NotForProd!";

            // Highest-priority in-memory overrides for the test host.
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = target.ConnectionString,
                ["ConnectionStrings:Redis"] = redis,
                ["DatabaseMigrations:ApplyOnStartup"] = "true",
                ["DatabaseMigrations:RequireTestSuffixWhenApplying"] = "true",
                ["DatabaseMigrations:LogPendingMigrationsWhenDisabled"] = "true",
                ["Jwt:Secret"] = "IntegrationTest_JwtSecret_Key_AtLeast_32_Chars_Long!",
                ["Jwt:Issuer"] = "MomoQuant",
                ["Jwt:Audience"] = "MomoQuant",
                ["Seed:AdminPassword"] = "Admin123!",
                ["Seed:AdminEmail"] = "admin@momoquant.local",
                ["MarketData:HistoricalProvider"] = "Fake",
                ["StrategyCatalog:SeedDefaultStrategies"] = "true",
                ["AiService:BaseUrl"] = "http://127.0.0.1:59999",
                ["AiService:TimeoutSeconds"] = "2",
                ["AiService:EnableFallback"] = "true"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<MomoQuantDbContext>>();
            services.AddDbContext<MomoQuantDbContext>(options =>
                options.UseMySql(
                    target.ConnectionString,
                    ServerVersion.Parse(PersistenceConstants.MySqlServerVersion)));
        });
    }

    private static void TryLoadLocalEnvFile()
    {
        var path = IntegrationDatabaseConnectionResolver
            .GetLocalEnvCandidatePaths()
            .FirstOrDefault(File.Exists);
        if (path is null)
        {
            return;
        }

        foreach (var raw in File.ReadAllLines(path, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith('#') || !line.Contains('='))
            {
                continue;
            }

            var idx = line.IndexOf('=');
            var key = line[..idx].Trim().TrimStart('\uFEFF');
            var value = line[(idx + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (key.Equals(
                    IntegrationDatabaseConnectionResolver.EnvironmentVariableName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
