using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Application.Strategies.MomoRange;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;
using MomoQuant.Persistence;
using MomoQuant.Persistence.Seeding;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.1A1 — MySQL seeder reconciliation for canonical parameter contracts.
/// </summary>
[Collection("Integration")]
public sealed class Milestone231A1SeederParameterContractTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly MomoQuantWebApplicationFactory _factory;

    public Milestone231A1SeederParameterContractTests(MomoQuantWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(Timeframe.M5)]
    [InlineData(Timeframe.M15)]
    [InlineData(Timeframe.H1)]
    [InlineData(Timeframe.H4)]
    public async Task Seeder_Adaptive_ActiveKeysMatchContract(Timeframe timeframe)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<IStrategyDataSeeder>();
        await seeder.SeedAsync();

        var strategy = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        var contract = MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract();
        var active = await db.StrategyParameters
            .Where(p => p.StrategyId == strategy.Id && p.Timeframe == timeframe && p.SymbolId == null && p.IsActive)
            .ToListAsync();

        Assert.Equal(contract.Count, active.Count);
        foreach (var (key, expected) in contract)
        {
            var row = Assert.Single(active, p => string.Equals(p.ParameterKey, key, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(NormalizeDecimalish(expected), NormalizeDecimalish(row.ParameterValue));
        }

        var obsolete = await db.StrategyParameters
            .Where(p => p.StrategyId == strategy.Id && p.Timeframe == timeframe && p.SymbolId == null)
            .Where(p =>
                p.ParameterKey == "htfTrendTimeframe"
                || p.ParameterKey == "htfStructureTimeframe"
                || p.ParameterKey == "requireHtfTrendAlignment"
                || p.ParameterKey == "requireHtfStructureBreak"
                || p.ParameterKey == "minBreakoutStrength"
                || p.ParameterKey == "stopBufferPercent")
            .ToListAsync();

        Assert.All(obsolete, p => Assert.False(p.IsActive));
    }

    [Theory]
    [InlineData(Timeframe.M5)]
    [InlineData(Timeframe.M15)]
    [InlineData(Timeframe.M30)]
    [InlineData(Timeframe.H1)]
    public async Task Seeder_Range_ActiveKeysMatchContract(Timeframe timeframe)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<IStrategyDataSeeder>();
        await seeder.SeedAsync();

        var strategy = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoVolatilityRangeReversion);
        var contract = MomoVolatilityRangeReversionParameters.GetDefaultParameterContract();
        var active = await db.StrategyParameters
            .Where(p => p.StrategyId == strategy.Id && p.Timeframe == timeframe && p.SymbolId == null && p.IsActive)
            .ToListAsync();

        Assert.Equal(contract.Count, active.Count);
        foreach (var (key, expected) in contract)
        {
            var row = Assert.Single(active, p => string.Equals(p.ParameterKey, key, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(NormalizeDecimalish(expected), NormalizeDecimalish(row.ParameterValue));
        }

        var obsolete = await db.StrategyParameters
            .Where(p => p.StrategyId == strategy.Id && p.Timeframe == timeframe && p.SymbolId == null)
            .Where(p =>
                p.ParameterKey == "rangeLookbackBars"
                || p.ParameterKey == "maxVolatilityAtrPercent"
                || p.ParameterKey == "meanReversionZonePercent"
                || p.ParameterKey == "requireRangeConfirmation"
                || p.ParameterKey == "fixedRewardRisk"
                || p.ParameterKey == "stopBufferPercent")
            .ToListAsync();

        Assert.All(obsolete, p => Assert.False(p.IsActive));
    }

    [Fact]
    public async Task Seeder_UpgradesKnown231AAdaptiveRewardRiskSeedFrom20To250()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<IStrategyDataSeeder>();
        await seeder.SeedAsync();

        var strategy = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        var row = await db.StrategyParameters.SingleAsync(p =>
            p.StrategyId == strategy.Id
            && p.Timeframe == Timeframe.M5
            && p.SymbolId == null
            && p.ParameterKey == "fixedRewardRisk");

        row.ParameterValue = "2.0";
        row.IsActive = true;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await seeder.SeedAsync();

        await db.Entry(row).ReloadAsync();
        Assert.Equal(NormalizeDecimalish("2.50"), NormalizeDecimalish(row.ParameterValue));
        Assert.True(row.IsActive);
    }

    [Fact]
    public async Task Seeder_DoesNotBlindlyOverwriteNonSeedAdaptiveRewardRisk()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<IStrategyDataSeeder>();
        await seeder.SeedAsync();

        var strategy = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        var row = await db.StrategyParameters.SingleAsync(p =>
            p.StrategyId == strategy.Id
            && p.Timeframe == Timeframe.M5
            && p.SymbolId == null
            && p.ParameterKey == "fixedRewardRisk");

        row.ParameterValue = "3.25";
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await seeder.SeedAsync();
        await db.Entry(row).ReloadAsync();
        Assert.Equal("3.25", NormalizeDecimalish(row.ParameterValue));
    }

    private static string NormalizeDecimalish(string value)
    {
        if (bool.TryParse(value, out var boolean))
        {
            return boolean ? "true" : "false";
        }

        if (decimal.TryParse(value, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        return value;
    }
}
