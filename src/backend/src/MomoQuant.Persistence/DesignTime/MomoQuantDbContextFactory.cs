using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MomoQuant.Persistence.DesignTime;

public class MomoQuantDbContextFactory : IDesignTimeDbContextFactory<MomoQuantDbContext>
{
    public MomoQuantDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../MomoQuant.Api"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

        var optionsBuilder = new DbContextOptionsBuilder<MomoQuantDbContext>();
        optionsBuilder.UseMySql(
            connectionString,
            ServerVersion.Parse(PersistenceConstants.MySqlServerVersion));

        return new MomoQuantDbContext(optionsBuilder.Options);
    }
}
