using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MomoQuant.Application.Options;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;
using MomoQuant.Persistence;
using MomoQuant.Persistence.Seeding;

namespace MomoQuant.UnitTests.Persistence;

public class StrategyDataSeederTests
{
    private static IOptions<StrategyCatalogSettings> SeedEnabled() =>
        Options.Create(new StrategyCatalogSettings { SeedDefaultStrategies = true });

    [Fact]
    public async Task SeedAsync_WhenSeedingDisabled_StillSeedsStrategyLabStrategiesOnly()
    {
        var options = new DbContextOptionsBuilder<MomoQuantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new MomoQuantDbContext(options);

        var seeder = new StrategyDataSeeder(
            context,
            Options.Create(new StrategyCatalogSettings { SeedDefaultStrategies = false }),
            NullLogger<StrategyDataSeeder>.Instance);

        await seeder.SeedAsync();

        var strategies = await context.Strategies.ToListAsync();
        Assert.Equal(2, strategies.Count);
        Assert.Contains(strategies, s => s.Code == StrategyCode.PriceStructureBreakoutRetest && s.IsEnabled);
        Assert.Contains(strategies, s => s.Code == StrategyCode.PriceStructureLiquiditySweepReclaim && s.IsEnabled);
        Assert.DoesNotContain(strategies, s => s.Code == StrategyCode.EmaPullback);
    }

    [Fact]
    public async Task SeedAsync_WhenLabStrategyAlreadyExists_DoesNotDuplicate()
    {
        var options = new DbContextOptionsBuilder<MomoQuantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new MomoQuantDbContext(options);
        context.Strategies.Add(new Strategy
        {
            Code = StrategyCode.PriceStructureBreakoutRetest,
            Name = "Price Structure Breakout + Retest",
            Description = "Detects confirmed swing structure levels, breakout closes, retests, and confirmation using OHLC candles only.",
            IsEnabled = true,
            Version = "1.0.0",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var seeder = new StrategyDataSeeder(
            context,
            Options.Create(new StrategyCatalogSettings { SeedDefaultStrategies = false }),
            NullLogger<StrategyDataSeeder>.Instance);

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        Assert.Equal(1, await context.Strategies.CountAsync(s => s.Code == StrategyCode.PriceStructureBreakoutRetest));
        Assert.Equal(1, await context.Strategies.CountAsync(s => s.Code == StrategyCode.PriceStructureLiquiditySweepReclaim));
    }

    [Fact]
    public async Task SeedAsync_WhenStrategyExists_UpdatesCanonicalName()
    {
        var options = new DbContextOptionsBuilder<MomoQuantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new MomoQuantDbContext(options);
        context.Strategies.Add(new Strategy
        {
            Code = StrategyCode.LiquiditySweep,
            Name = "Liquidity Sweep",
            Description = "Old name.",
            IsEnabled = true,
            Version = "1.0.0",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var seeder = new StrategyDataSeeder(context, SeedEnabled(), NullLogger<StrategyDataSeeder>.Instance);

        await seeder.SeedAsync();

        var liquiditySweep = await context.Strategies.SingleAsync(item => item.Code == StrategyCode.LiquiditySweep);
        Assert.Equal("Liquidity Sweep Reclaim", liquiditySweep.Name);
    }

    [Fact]
    public async Task SeedAsync_WhenDuplicateCodeExists_DisablesLegacyDuplicateRow()
    {
        var options = new DbContextOptionsBuilder<MomoQuantDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new MomoQuantDbContext(options);
        context.Strategies.AddRange(
            new Strategy
            {
                Code = StrategyCode.LiquiditySweep,
                Name = "Liquidity Sweep Reclaim",
                Description = "Canonical.",
                IsEnabled = true,
                Version = "2.0.0",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            },
            new Strategy
            {
                Code = StrategyCode.LiquiditySweep,
                Name = "Liquidity Sweep",
                Description = "Duplicate.",
                IsEnabled = true,
                Version = "2.0.0",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        await context.SaveChangesAsync();

        var seeder = new StrategyDataSeeder(context, SeedEnabled(), NullLogger<StrategyDataSeeder>.Instance);

        await seeder.SeedAsync();

        var rows = await context.Strategies
            .Where(item => item.Code == StrategyCode.LiquiditySweep)
            .OrderBy(item => item.Id)
            .ToListAsync();

        Assert.True(rows.Count >= 2);
        Assert.Equal("Liquidity Sweep Reclaim", rows.First().Name);
        Assert.Contains(rows.Skip(1), item => !item.IsEnabled && item.Name.Contains("Legacy Duplicate", StringComparison.OrdinalIgnoreCase));
    }
}
