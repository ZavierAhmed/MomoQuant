using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Strategies;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;
using MomoQuant.Persistence;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.1A — Canonical Strategy Portfolio Integration Tests.
/// Verifies seeder behavior, archived strategy rejection in orchestration paths, and canonical-only execution.
/// </summary>
[Collection("Integration")]
public sealed class Milestone231APortfolioOrchestrationTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly MomoQuantWebApplicationFactory _factory;

    public Milestone231APortfolioOrchestrationTests(MomoQuantWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seeder_CleanDatabase_CreatesExactlyThreeCanonicalRows()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        var strategies = await db.Strategies.ToListAsync();
        var enabled = strategies.Where(s => s.IsEnabled).ToList();

        Assert.True(enabled.Count >= 3, $"Expected at least 3 enabled strategies, found {enabled.Count}");
        Assert.Contains(enabled, s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        Assert.Contains(enabled, s => s.Code == StrategyCode.PriceStructureBreakoutRetest);
        Assert.Contains(enabled, s => s.Code == StrategyCode.MomoVolatilityRangeReversion);

        var canonical = enabled.Where(s =>
            s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout ||
            s.Code == StrategyCode.PriceStructureBreakoutRetest ||
            s.Code == StrategyCode.MomoVolatilityRangeReversion).ToList();

        Assert.Equal(3, canonical.Count);
        Assert.All(canonical, s => Assert.True(s.IsEnabled, $"Strategy {s.Code.ToCode()} should be enabled"));
    }

    [Fact]
    public async Task Seeder_ExistingDatabase_DisablesLegacyRowsWithoutDeletingThem()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        var volatilityGated = await db.Strategies.FirstOrDefaultAsync(
            s => s.Code == StrategyCode.VolatilityGatedSupertrendMomentum);
        if (volatilityGated is null)
        {
            volatilityGated = new Strategy
            {
                Code = StrategyCode.VolatilityGatedSupertrendMomentum,
                Name = "Volatility-Gated SuperTrend Momentum",
                Description = "Legacy test strategy",
                IsEnabled = true,
                Version = "1.0.0",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            await db.Strategies.AddAsync(volatilityGated);
            await db.SaveChangesAsync();
        }
        else
        {
            volatilityGated.IsEnabled = true;
            await db.SaveChangesAsync();
        }

        var seeder = scope.ServiceProvider.GetRequiredService<Persistence.Seeding.IStrategyDataSeeder>();
        await seeder.SeedAsync();

        var reloaded = await db.Strategies.AsNoTracking().FirstOrDefaultAsync(
            s => s.Code == StrategyCode.VolatilityGatedSupertrendMomentum);

        Assert.NotNull(reloaded);
        Assert.False(reloaded!.IsEnabled, "Legacy strategy should be disabled");
    }

    [Fact]
    public async Task Seeder_PreservesHistoricalResearchFields()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        var existing = await db.Strategies.FirstOrDefaultAsync(s => s.Code == StrategyCode.VwapMeanReversion);
        if (existing is null)
        {
            existing = new Strategy
            {
                Code = StrategyCode.VwapMeanReversion,
                Name = "VWAP Mean Reversion",
                Description = "Legacy strategy with research notes",
                IsEnabled = false,
                Version = "1.0.0",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                ResearchStatus = StrategyResearchStatus.Failed
            };
            await db.Strategies.AddAsync(existing);
            await db.SaveChangesAsync();
        }
        else
        {
            existing.ResearchStatus = StrategyResearchStatus.Failed;
            await db.SaveChangesAsync();
        }

        var seeder = scope.ServiceProvider.GetRequiredService<Persistence.Seeding.IStrategyDataSeeder>();
        await seeder.SeedAsync();

        var reloaded = await db.Strategies.FirstOrDefaultAsync(s => s.Code == StrategyCode.VwapMeanReversion);
        Assert.NotNull(reloaded);
        Assert.Equal(StrategyResearchStatus.Failed, reloaded!.ResearchStatus);
    }

    [Fact]
    public async Task Seeder_IsIdempotent()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<Persistence.Seeding.IStrategyDataSeeder>();

        await seeder.SeedAsync();
        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var strategies = await db.Strategies.ToListAsync();

        var mtfCount = strategies.Count(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        var priceStructureCount = strategies.Count(s => s.Code == StrategyCode.PriceStructureBreakoutRetest);
        var rangeCount = strategies.Count(s => s.Code == StrategyCode.MomoVolatilityRangeReversion);

        Assert.Equal(1, mtfCount);
        Assert.Equal(1, priceStructureCount);
        Assert.Equal(1, rangeCount);
    }

    [Fact]
    public async Task Seeder_PriceStructureVersionIs110()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        var priceStructure = await db.Strategies.FirstOrDefaultAsync(
            s => s.Code == StrategyCode.PriceStructureBreakoutRetest && s.IsEnabled);

        Assert.NotNull(priceStructure);
        Assert.Equal("1.1.0", priceStructure!.Version);
    }

    [Fact]
    public async Task Seeder_CreatesCanonicalParametersWithoutDuplicates()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<Persistence.Seeding.IStrategyDataSeeder>();
        await seeder.SeedAsync();

        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        var mtfStrategy = await db.Strategies.FirstOrDefaultAsync(
            s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout && s.IsEnabled);
        Assert.NotNull(mtfStrategy);

        var mtfParams = await db.StrategyParameters
            .Where(p => p.StrategyId == mtfStrategy!.Id)
            .ToListAsync();

        Assert.NotEmpty(mtfParams);

        var duplicateKeys = mtfParams
            .GroupBy(p => new { p.ParameterKey, p.Timeframe })
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.ParameterKey)
            .ToList();
        Assert.Empty(duplicateKeys);
    }

    [Fact]
    public async Task Backtest_ArchivedStrategyRejected()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        var archived = await db.Strategies.FirstOrDefaultAsync(s => s.Code == StrategyCode.VolatilityGatedSupertrendMomentum);
        if (archived is null)
        {
            archived = new Strategy
            {
                Code = StrategyCode.VolatilityGatedSupertrendMomentum,
                Name = "Volatility-Gated SuperTrend Momentum",
                Description = "Archived strategy",
                IsEnabled = false,
                Version = "1.0.0",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            await db.Strategies.AddAsync(archived);
            await db.SaveChangesAsync();
        }

        Assert.False(CanonicalStrategyPortfolio.CanCreateNewRun(archived.Code));
        Assert.False(CanonicalStrategyPortfolio.CanExecute(archived.Code));
    }

    [Fact]
    public async Task Replay_ArchivedStrategyRejected()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();

        var archived = await db.Strategies.FirstOrDefaultAsync(s => s.Code == StrategyCode.FourHourRangeReEntry);
        if (archived is not null)
        {
            Assert.False(CanonicalStrategyPortfolio.CanCreateNewRun(archived.Code));
            Assert.False(CanonicalStrategyPortfolio.CanExecute(archived.Code));
        }
    }

    [Fact]
    public async Task Paper_ArchivedStrategyRejected()
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var archivedCode = StrategyCode.VwapMeanReversion;
        Assert.False(CanonicalStrategyPortfolio.CanCreateNewRun(archivedCode));
        Assert.False(CanonicalStrategyPortfolio.CanExecute(archivedCode));
        Assert.False(CanonicalStrategyPortfolio.CanEnable(archivedCode));
    }

    [Fact]
    public async Task Benchmark_ArchivedStrategyRejectedEvenWithIncludeDisabled()
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var archivedCode = StrategyCode.SupportResistanceBreakoutRetest;
        Assert.False(CanonicalStrategyPortfolio.CanCreateNewRun(archivedCode));
        Assert.False(CanonicalStrategyPortfolio.CanExecute(archivedCode));
    }

    [Fact]
    public async Task ManualEvaluation_ArchivedStrategyRejected()
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var archivedCode = StrategyCode.SupertrendContinuation;
        Assert.False(CanonicalStrategyPortfolio.CanExecute(archivedCode));
    }

    [Fact]
    public async Task Optimization_ArchivedStrategyRejected()
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var archivedCode = StrategyCode.MacdMomentumContinuation;
        Assert.False(CanonicalStrategyPortfolio.CanCreateNewRun(archivedCode));
    }

    [Fact]
    public async Task ValidationCreate_ArchivedStrategyRejected()
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var archivedCode = StrategyCode.BbLiquiditySweepCisd;
        Assert.False(CanonicalStrategyPortfolio.CanCreateNewRun(archivedCode));
    }

    [Fact]
    public async Task QueuedBenchmark_ArchivedStrategyCannotExecute()
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var archivedCode = StrategyCode.DonchianBreakout;
        Assert.False(CanonicalStrategyPortfolio.CanExecute(archivedCode));
    }

    [Fact]
    public async Task Preflight_CanonicalStrategy_Passes()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IStrategyRegistry>();

        var mtfStrategy = registry.GetByCode(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        Assert.NotNull(mtfStrategy);
        Assert.Equal(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout, mtfStrategy.Code);
        Assert.True(CanonicalStrategyPortfolio.IsCanonicalActive(mtfStrategy.Code));
    }

    [Fact]
    public async Task StrategyRegistry_RejectsArchivedLookup()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IStrategyRegistry>();

        Assert.Null(registry.GetByCode(StrategyCode.VolatilityGatedSupertrendMomentum));
    }

    [Fact]
    public async Task CanonicalPortfolio_AllThreeCodesActive()
    {
        Assert.True(CanonicalStrategyPortfolio.IsCanonicalActive(StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout));
        Assert.True(CanonicalStrategyPortfolio.IsCanonicalActive(StrategyCodes.PriceStructureBreakoutRetest));
        Assert.True(CanonicalStrategyPortfolio.IsCanonicalActive(StrategyCodes.MomoVolatilityRangeReversion));

        Assert.False(CanonicalStrategyPortfolio.IsCanonicalActive(StrategyCodes.VolatilityGatedSupertrendMomentum));
        Assert.False(CanonicalStrategyPortfolio.IsCanonicalActive(StrategyCodes.VwapMeanReversion));
        Assert.False(CanonicalStrategyPortfolio.IsCanonicalActive(StrategyCodes.FourHourRangeReEntry));
    }

    [Fact]
    public void ArchivedMessage_ContainsStrategyCode()
    {
        var message = CanonicalStrategyPortfolio.ArchivedCannotUseMessage(StrategyCodes.VolatilityGatedSupertrendMomentum);
        Assert.Contains(StrategyCodes.VolatilityGatedSupertrendMomentum, message);
        Assert.Contains("archived", message, StringComparison.OrdinalIgnoreCase);
    }
}
