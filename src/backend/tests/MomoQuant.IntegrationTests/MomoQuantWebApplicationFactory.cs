using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace MomoQuant.IntegrationTests;

public sealed class MomoQuantWebApplicationFactory : WebApplicationFactory<Program>
{
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
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            var mysql = Environment.GetEnvironmentVariable("MOMO_INTEGRATION_MYSQL")
                ?? "Server=localhost;Port=3306;Database=momo_quant_test;User=momo_user;Password=IntegrationTest_DbPassword_NotForProd!";
            var redis = Environment.GetEnvironmentVariable("MOMO_INTEGRATION_REDIS")
                ?? "127.0.0.1:6379,password=IntegrationTest_RedisPassword_NotForProd!";

            // Highest-priority in-memory overrides for the test host.
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = mysql,
                ["ConnectionStrings:Redis"] = redis,
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
    }

    private static void TryLoadLocalEnvFile()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "integration.local.env")),
            Path.Combine(Directory.GetCurrentDirectory(), "tests", "MomoQuant.IntegrationTests", "integration.local.env"),
            Path.Combine(Directory.GetCurrentDirectory(), "integration.local.env")
        };

        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null)
        {
            return;
        }

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || !line.Contains('='))
            {
                continue;
            }

            var idx = line.IndexOf('=');
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
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
