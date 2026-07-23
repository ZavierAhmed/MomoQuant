using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace MomoQuant.IntegrationTests;

public sealed class MomoQuantWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development loads appsettings.Development.json; skip secrets gate for the test host.
        Environment.SetEnvironmentVariable("MOMO_SKIP_SECRETS_VALIDATION", "true");
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Non-production test doubles only — never commit real environment secrets.
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=localhost;Port=3306;Database=momo_quant_test;User=momo_user;Password=IntegrationTest_DbPassword_NotForProd!",
                ["ConnectionStrings:Redis"] =
                    "127.0.0.1:6379,password=IntegrationTest_RedisPassword_NotForProd!",
                ["Jwt:Secret"] = "IntegrationTest_JwtSecret_Key_AtLeast_32_Chars_Long!",
                ["Jwt:Issuer"] = "MomoQuant",
                ["Jwt:Audience"] = "MomoQuant",
                ["Seed:AdminPassword"] = "IntegrationTest_AdminPassword_NotForProd!",
                ["MarketData:HistoricalProvider"] = "Fake",
                ["StrategyCatalog:SeedDefaultStrategies"] = "true",
                ["AiService:BaseUrl"] = "http://127.0.0.1:59999",
                ["AiService:TimeoutSeconds"] = "2",
                ["AiService:EnableFallback"] = "true"
            });
        });
    }
}
