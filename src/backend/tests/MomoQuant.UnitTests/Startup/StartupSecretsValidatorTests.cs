using MomoQuant.Application.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace MomoQuant.UnitTests.Startup;

public class StartupSecretsValidatorTests
{
    [Fact]
    public void ValidateOrThrow_Skips_WhenEnvironmentIsTesting()
    {
        var configuration = new ConfigurationBuilder().Build();
        var environment = new FakeHostEnvironment { EnvironmentName = "Testing" };

        StartupSecretsValidator.ValidateOrThrow(configuration, environment);
    }

    [Fact]
    public void ValidateOrThrow_Rejects_PlaceholderJwtSecret()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=localhost;Port=3306;Database=momo_quant;User=momo_user;Password=ValidLocalDbPassword_NotPlaceholder!",
                ["Jwt:Secret"] = "CHANGE_ME_REPLACE_WITH_USER_SECRETS_OR_ENV",
                ["Seed:AdminPassword"] = "ValidLocalAdminPassword_NotPlaceholder!"
            })
            .Build();
        var environment = new FakeHostEnvironment { EnvironmentName = "Development" };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            StartupSecretsValidator.ValidateOrThrow(configuration, environment));

        Assert.Contains("Jwt:Secret", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "MomoQuant.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
