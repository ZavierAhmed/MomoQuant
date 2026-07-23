using Microsoft.EntityFrameworkCore;
using MomoQuant.Persistence;

namespace MomoQuant.UnitTests.Persistence;

public class MomoQuantDbContextTests
{
    [Fact]
    public void Model_ContainsMvpTables()
    {
        var options = new DbContextOptionsBuilder<MomoQuantDbContext>()
            .UseMySql(
                "Server=localhost;Port=3306;Database=momo_quant_test;User=test;Password=test;",
                ServerVersion.Parse(PersistenceConstants.MySqlServerVersion))
            .Options;

        using var context = new MomoQuantDbContext(options);
        var entityTypes = context.Model.GetEntityTypes().Select(type => type.GetTableName()).ToList();

        Assert.Contains("Users", entityTypes);
        Assert.Contains("Roles", entityTypes);
        Assert.Contains("Candles", entityTypes);
        Assert.Contains("StrategySignals", entityTypes);
        Assert.Contains("RiskDecisions", entityTypes);
        Assert.Contains("Trades", entityTypes);
        Assert.Contains("BacktestRuns", entityTypes);
        Assert.Contains("PaperAccounts", entityTypes);
        Assert.Contains("AuditLogs", entityTypes);
        Assert.Contains("AppSettings", entityTypes);
    }

    [Fact]
    public void Candle_HasUniqueCompositeIndex()
    {
        var options = new DbContextOptionsBuilder<MomoQuantDbContext>()
            .UseMySql(
                "Server=localhost;Port=3306;Database=momo_quant_test;User=test;Password=test;",
                ServerVersion.Parse(PersistenceConstants.MySqlServerVersion))
            .Options;

        using var context = new MomoQuantDbContext(options);
        var candleEntity = context.Model.FindEntityType(typeof(MomoQuant.Domain.MarketData.Candle));
        Assert.NotNull(candleEntity);

        var uniqueIndex = candleEntity.GetIndexes()
            .FirstOrDefault(index => index.IsUnique
                && index.Properties.Select(property => property.Name)
                    .OrderBy(name => name)
                    .SequenceEqual(new[] { "ExchangeId", "OpenTimeUtc", "SymbolId", "Timeframe" }.OrderBy(name => name)));

        Assert.NotNull(uniqueIndex);
    }
}
