using MomoQuant.Application.Strategies;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.StrategyLab;

public class StrategyLabIntegrationRegistrationTests
{
    [Theory]
    [InlineData(StrategyCodes.PriceStructureBreakoutRetest)]
    [InlineData(StrategyCodes.PriceStructureLiquiditySweepReclaim)]
    public void StrategyCodeExtensions_RoundTrip(string code)
    {
        var parsed = StrategyCodeExtensions.FromCode(code);
        Assert.Equal(code, parsed.ToCode());
    }

    [Fact]
    public void Registry_CanResolveBothPriceStructureStrategies()
    {
        ITradingStrategy[] plugins =
        [
            new Application.Strategies.Implementations.PriceStructureBreakoutRetestStrategy(),
            new Application.Strategies.Implementations.PriceStructureLiquiditySweepReclaimStrategy()
        ];
        var registry = new StrategyRegistry(plugins);

        Assert.NotNull(registry.GetByCode(StrategyCode.PriceStructureBreakoutRetest));
        Assert.NotNull(registry.GetByCode(StrategyCode.PriceStructureLiquiditySweepReclaim));
    }

    [Fact]
    public void CatalogMapper_MarksSupportsStrategyLab()
    {
        var strategy = new Strategy
        {
            Id = 1,
            Code = StrategyCode.PriceStructureBreakoutRetest,
            Name = "Price Structure Breakout + Retest",
            Description = "test",
            IsEnabled = true,
            Version = "1.0.0"
        };

        var dto = StrategyCatalogMapper.MapToCatalogDto(strategy, null, true);
        Assert.True(dto.SupportsStrategyLab);
        Assert.Equal("Price Action / Market Structure", dto.Category);
    }

    [Fact]
    public void SyntheticCatalog_HasScenariosForBothStrategies()
    {
        Assert.NotEmpty(Application.StrategyLab.Synthetic.SyntheticScenarioCatalog.ForStrategy(StrategyCodes.PriceStructureBreakoutRetest));
        Assert.NotEmpty(Application.StrategyLab.Synthetic.SyntheticScenarioCatalog.ForStrategy(StrategyCodes.PriceStructureLiquiditySweepReclaim));
    }

    [Fact]
    public void LabStrategyCodes_AreStable()
    {
        Assert.Equal("PRICE_STRUCTURE_BREAKOUT_RETEST", StrategyCodes.PriceStructureBreakoutRetest);
        Assert.Equal("PRICE_STRUCTURE_LIQUIDITY_SWEEP_RECLAIM", StrategyCodes.PriceStructureLiquiditySweepReclaim);
    }
}
