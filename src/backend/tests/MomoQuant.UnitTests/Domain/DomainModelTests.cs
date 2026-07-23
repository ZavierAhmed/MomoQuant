using System.Reflection;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Trades;

namespace MomoQuant.UnitTests.Domain;

public class DomainModelTests
{
    [Theory]
    [InlineData(typeof(Candle), nameof(Candle.Open))]
    [InlineData(typeof(Candle), nameof(Candle.High))]
    [InlineData(typeof(Candle), nameof(Candle.Low))]
    [InlineData(typeof(Candle), nameof(Candle.Close))]
    [InlineData(typeof(Candle), nameof(Candle.Volume))]
    [InlineData(typeof(Trade), nameof(Trade.EntryPrice))]
    [InlineData(typeof(Trade), nameof(Trade.NetPnl))]
    [InlineData(typeof(Order), nameof(Order.Price))]
    [InlineData(typeof(OrderFill), nameof(OrderFill.Fee))]
    [InlineData(typeof(Position), nameof(Position.UnrealizedPnl))]
    public void FinancialProperties_UseDecimal(Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        Assert.Equal(typeof(decimal), property.PropertyType);
    }

    [Fact]
    public void DomainAssembly_HasNoEntityFrameworkDependency()
    {
        var domainAssembly = typeof(Candle).Assembly;
        var references = domainAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToList();

        Assert.DoesNotContain(references, name => name != null && name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(MarketRegime.Trending)]
    [InlineData(MarketRegime.Ranging)]
    [InlineData(MarketRegime.Choppy)]
    [InlineData(MarketRegime.Unknown)]
    public void MarketRegime_ContainsDocumentedValues(MarketRegime regime)
    {
        Assert.True(Enum.IsDefined(regime));
    }

    [Theory]
    [InlineData(RiskDecisionType.Approved)]
    [InlineData(RiskDecisionType.Rejected)]
    [InlineData(RiskDecisionType.Adjusted)]
    [InlineData(RiskDecisionType.EmergencyBlocked)]
    public void RiskDecisionType_MatchesDocumentation(RiskDecisionType decision)
    {
        Assert.True(Enum.IsDefined(decision));
    }

    [Theory]
    [InlineData(TradingMode.Backtest)]
    [InlineData(TradingMode.Replay)]
    [InlineData(TradingMode.Paper)]
    [InlineData(TradingMode.Live)]
    public void TradingMode_MatchesDocumentation(TradingMode mode)
    {
        Assert.True(Enum.IsDefined(mode));
    }

    [Theory]
    [InlineData(StrategyCode.LiquiditySweep, "LIQUIDITY_SWEEP_RECLAIM")]
    [InlineData(StrategyCode.VwapMeanReversion, "VWAP_MEAN_REVERSION")]
    [InlineData(StrategyCode.EmaPullback, "EMA_PULLBACK")]
    [InlineData(StrategyCode.FourHourRangeReEntry, "FOUR_HOUR_RANGE_REENTRY")]
    [InlineData(StrategyCode.BbLiquiditySweepCisd, "BB_LIQUIDITY_SWEEP_CISD")]
    [InlineData(StrategyCode.BbLiquiditySweepCisdRsiPrimed, "BB_LIQUIDITY_SWEEP_CISD_RSI_PRIMED")]
    public void StrategyCodeExtensions_MapToDocumentedCodes(StrategyCode code, string expected)
    {
        Assert.Equal(expected, code.ToCode());
        Assert.Equal(code, StrategyCodeExtensions.FromCode(expected));
    }

    [Fact]
    public void StrategyCodeExtensions_AcceptsLegacyLiquiditySweepCode()
    {
        Assert.Equal(StrategyCode.LiquiditySweep, StrategyCodeExtensions.FromCode("LIQUIDITY_SWEEP"));
    }
}
