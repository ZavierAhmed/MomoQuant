using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application;
using MomoQuant.Application.Strategies;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A — Canonical Strategy Portfolio Unit Tests.
/// Verifies exactly three strategies are active, registry resolution, and code round-tripping.
/// </summary>
public sealed class Milestone231ACanonicalPortfolioTests
{
    [Fact]
    public void CanonicalPortfolio_ContainsExactlyThreeCodes()
    {
        var activeCodes = CanonicalStrategyPortfolio.ActiveCodes;
        Assert.Equal(3, activeCodes.Count);
        Assert.Contains(StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout, activeCodes);
        Assert.Contains(StrategyCodes.PriceStructureBreakoutRetest, activeCodes);
        Assert.Contains(StrategyCodes.MomoVolatilityRangeReversion, activeCodes);

        var activeStrategyCodes = CanonicalStrategyPortfolio.ActiveStrategyCodes;
        Assert.Equal(3, activeStrategyCodes.Count);
        Assert.Contains(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout, activeStrategyCodes);
        Assert.Contains(StrategyCode.PriceStructureBreakoutRetest, activeStrategyCodes);
        Assert.Contains(StrategyCode.MomoVolatilityRangeReversion, activeStrategyCodes);
    }

    [Fact]
    public void StrategyRegistry_ResolvesExactlyThreeCanonicalPlugins()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IStrategyRegistry>();
        var allPlugins = registry.GetAll();

        Assert.Equal(3, allPlugins.Count);

        var mtf = registry.GetByCode(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        Assert.NotNull(mtf);
        Assert.Equal(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout, mtf.Code);

        var priceStructure = registry.GetByCode(StrategyCode.PriceStructureBreakoutRetest);
        Assert.NotNull(priceStructure);
        Assert.Equal(StrategyCode.PriceStructureBreakoutRetest, priceStructure.Code);

        var rangeReversion = registry.GetByCode(StrategyCode.MomoVolatilityRangeReversion);
        Assert.NotNull(rangeReversion);
        Assert.Equal(StrategyCode.MomoVolatilityRangeReversion, rangeReversion.Code);
    }

    [Fact]
    public void StrategyRegistry_DoesNotResolveArchivedPlugins()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IStrategyRegistry>();

        Assert.Null(registry.GetByCode(StrategyCode.VolatilityGatedSupertrendMomentum));
        Assert.Null(registry.GetByCode(StrategyCode.FourHourRangeReEntry));
        Assert.Null(registry.GetByCode(StrategyCode.VwapMeanReversion));
    }

    [Fact]
    public void StrategyCodeExtensions_RoundTripNewCodes()
    {
        var mtfCode = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout;
        var mtfString = mtfCode.ToCode();
        Assert.Equal(StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout, mtfString);
        var mtfBackToEnum = StrategyCodeExtensions.FromCode(mtfString);
        Assert.Equal(mtfCode, mtfBackToEnum);

        var priceStructureCode = StrategyCode.PriceStructureBreakoutRetest;
        var priceStructureString = priceStructureCode.ToCode();
        Assert.Equal(StrategyCodes.PriceStructureBreakoutRetest, priceStructureString);
        var priceStructureBackToEnum = StrategyCodeExtensions.FromCode(priceStructureString);
        Assert.Equal(priceStructureCode, priceStructureBackToEnum);

        var rangeCode = StrategyCode.MomoVolatilityRangeReversion;
        var rangeString = rangeCode.ToCode();
        Assert.Equal(StrategyCodes.MomoVolatilityRangeReversion, rangeString);
        var rangeBackToEnum = StrategyCodeExtensions.FromCode(rangeString);
        Assert.Equal(rangeCode, rangeBackToEnum);
    }

    [Fact]
    public void LegacyStrategyCodes_RemainParseable()
    {
        var volatilityGated = StrategyCodeExtensions.FromCode(StrategyCodes.VolatilityGatedSupertrendMomentum);
        Assert.Equal(StrategyCode.VolatilityGatedSupertrendMomentum, volatilityGated);
        Assert.False(CanonicalStrategyPortfolio.IsCanonicalActive(volatilityGated));

        var vwap = StrategyCodeExtensions.FromCode(StrategyCodes.VwapMeanReversion);
        Assert.Equal(StrategyCode.VwapMeanReversion, vwap);
        Assert.False(CanonicalStrategyPortfolio.IsCanonicalActive(vwap));

        var fourHour = StrategyCodeExtensions.FromCode(StrategyCodes.FourHourRangeReEntry);
        Assert.Equal(StrategyCode.FourHourRangeReEntry, fourHour);
        Assert.False(CanonicalStrategyPortfolio.IsCanonicalActive(fourHour));

        var supportResistance = StrategyCodeExtensions.FromCode(StrategyCodes.SupportResistanceBreakoutRetest);
        Assert.Equal(StrategyCode.SupportResistanceBreakoutRetest, supportResistance);
        Assert.False(CanonicalStrategyPortfolio.IsCanonicalActive(supportResistance));
    }

    [Fact]
    public void CanonicalActive_ValidatesCorrectly()
    {
        Assert.True(CanonicalStrategyPortfolio.IsCanonicalActive(StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout));
        Assert.True(CanonicalStrategyPortfolio.IsCanonicalActive(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout));

        Assert.True(CanonicalStrategyPortfolio.IsCanonicalActive(StrategyCodes.PriceStructureBreakoutRetest));
        Assert.True(CanonicalStrategyPortfolio.IsCanonicalActive(StrategyCode.PriceStructureBreakoutRetest));

        Assert.True(CanonicalStrategyPortfolio.IsCanonicalActive(StrategyCodes.MomoVolatilityRangeReversion));
        Assert.True(CanonicalStrategyPortfolio.IsCanonicalActive(StrategyCode.MomoVolatilityRangeReversion));

        Assert.False(CanonicalStrategyPortfolio.IsCanonicalActive(StrategyCodes.VolatilityGatedSupertrendMomentum));
        Assert.False(CanonicalStrategyPortfolio.IsCanonicalActive(StrategyCode.VolatilityGatedSupertrendMomentum));

        Assert.False(CanonicalStrategyPortfolio.IsCanonicalActive(StrategyCodes.VwapMeanReversion));
        Assert.False(CanonicalStrategyPortfolio.IsCanonicalActive(StrategyCode.VwapMeanReversion));

        Assert.False(CanonicalStrategyPortfolio.IsCanonicalActive((string?)null));
        Assert.False(CanonicalStrategyPortfolio.IsCanonicalActive(string.Empty));
        Assert.False(CanonicalStrategyPortfolio.IsCanonicalActive("UNKNOWN_STRATEGY"));
    }

    [Fact]
    public void CanCreateNewRun_RespectsCanonicalPortfolio()
    {
        Assert.True(CanonicalStrategyPortfolio.CanCreateNewRun(StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout));
        Assert.True(CanonicalStrategyPortfolio.CanCreateNewRun(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout));

        Assert.False(CanonicalStrategyPortfolio.CanCreateNewRun(StrategyCodes.VolatilityGatedSupertrendMomentum));
        Assert.False(CanonicalStrategyPortfolio.CanCreateNewRun(StrategyCode.VolatilityGatedSupertrendMomentum));
    }

    [Fact]
    public void CanExecute_RespectsCanonicalPortfolio()
    {
        Assert.True(CanonicalStrategyPortfolio.CanExecute(StrategyCodes.PriceStructureBreakoutRetest));
        Assert.True(CanonicalStrategyPortfolio.CanExecute(StrategyCode.PriceStructureBreakoutRetest));

        Assert.False(CanonicalStrategyPortfolio.CanExecute(StrategyCodes.FourHourRangeReEntry));
        Assert.False(CanonicalStrategyPortfolio.CanExecute(StrategyCode.FourHourRangeReEntry));
    }

    [Fact]
    public void CanEnable_RespectsCanonicalPortfolio()
    {
        Assert.True(CanonicalStrategyPortfolio.CanEnable(StrategyCodes.MomoVolatilityRangeReversion));
        Assert.True(CanonicalStrategyPortfolio.CanEnable(StrategyCode.MomoVolatilityRangeReversion));

        Assert.False(CanonicalStrategyPortfolio.CanEnable(StrategyCodes.VwapMeanReversion));
        Assert.False(CanonicalStrategyPortfolio.CanEnable(StrategyCode.VwapMeanReversion));
    }

    [Fact]
    public void TryParseCanonical_ParsesOnlyCanonicalCodes()
    {
        Assert.True(CanonicalStrategyPortfolio.TryParseCanonical(
            StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout, out var mtfCode));
        Assert.Equal(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout, mtfCode);

        Assert.True(CanonicalStrategyPortfolio.TryParseCanonical(
            StrategyCodes.PriceStructureBreakoutRetest, out var psCode));
        Assert.Equal(StrategyCode.PriceStructureBreakoutRetest, psCode);

        Assert.False(CanonicalStrategyPortfolio.TryParseCanonical(
            StrategyCodes.VolatilityGatedSupertrendMomentum, out var archivedCode));
        Assert.Equal(default, archivedCode);

        Assert.False(CanonicalStrategyPortfolio.TryParseCanonical((string?)null, out _));
        Assert.False(CanonicalStrategyPortfolio.TryParseCanonical(string.Empty, out _));
        Assert.False(CanonicalStrategyPortfolio.TryParseCanonical("UNKNOWN_CODE", out _));
    }
}
