using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

public static class StrategyTestHelpers
{
    public static StrategyContext BuildContext(
        MarketRegime regime,
        IReadOnlyList<Candle> candles,
        IndicatorSnapshot? indicators,
        IReadOnlyList<IndicatorSnapshot>? recentSnapshots = null,
        IReadOnlyDictionary<string, string>? parameters = null) => new()
    {
        SymbolId = 1,
        Symbol = "BTCUSDT",
        Timeframe = Timeframe.M3,
        HigherTimeframe = Timeframe.M15,
        MarketRegime = regime,
        Candles = candles,
        IndicatorSnapshot = indicators,
        RecentIndicatorSnapshots = recentSnapshots ?? (indicators is null ? [] : [indicators]),
        StrategyParameters = parameters ?? new Dictionary<string, string>(),
        EvaluatedAtUtc = DateTime.UtcNow
    };

    public static Candle Candle(decimal open, decimal close, decimal high, decimal low, decimal volume = 100m) => new()
    {
        Id = 1,
        SymbolId = 1,
        Open = open,
        Close = close,
        High = high,
        Low = low,
        Volume = volume,
        OpenTimeUtc = DateTime.UtcNow
    };

    public static IndicatorSnapshot Indicators(Action<IndicatorSnapshot>? configure = null)
    {
        var snapshot = new IndicatorSnapshot
        {
            SymbolId = 1,
            Timeframe = Timeframe.M3,
            CandleId = 1,
            CalculatedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            MarketStructure = MarketStructure.Neutral
        };
        configure?.Invoke(snapshot);
        return snapshot;
    }

    public static void AssertStrengthInRange(StrategySignalResult result)
    {
        Assert.InRange(result.Strength, 0m, 100m);
        Assert.InRange(result.ConfidenceContribution, 0m, 100m);
    }

    public static void AssertNoOrdersOrRisk(StrategySignalResult result)
    {
        Assert.NotEqual(SignalType.Exit, result.SignalType);
        _ = result;
    }
}

public class StrategyCatalogV2Tests
{
    [Theory]
    [InlineData(typeof(EmaPullbackStrategy))]
    [InlineData(typeof(VwapMeanReversionStrategy))]
    [InlineData(typeof(LiquiditySweepStrategy))]
    [InlineData(typeof(BollingerSqueezeBreakoutStrategy))]
    [InlineData(typeof(DonchianBreakoutStrategy))]
    [InlineData(typeof(RsiDivergenceReversalStrategy))]
    [InlineData(typeof(MacdMomentumContinuationStrategy))]
    [InlineData(typeof(AtrVolatilityBreakoutStrategy))]
    [InlineData(typeof(SupportResistanceBreakoutRetestStrategy))]
    [InlineData(typeof(SupertrendContinuationStrategy))]
    [InlineData(typeof(FourHourRangeReEntryStrategy))]
    [InlineData(typeof(BbLiquiditySweepCisdStrategy))]
    [InlineData(typeof(BbLiquiditySweepCisdRsiPrimedStrategy))]
    [InlineData(typeof(VolatilityGatedSuperTrendMomentumStrategy))]
    public void Strategy_RegistersCodeAndMetadata(Type strategyType)
    {
        var strategy = (StrategyBase)Activator.CreateInstance(strategyType)!;
        Assert.False(string.IsNullOrWhiteSpace(strategy.Code.ToCode()));
        Assert.False(string.IsNullOrWhiteSpace(strategy.Name));
        Assert.False(string.IsNullOrWhiteSpace(strategy.Description));
        Assert.NotEmpty(strategy.SupportedRegimes);
        Assert.NotEmpty(strategy.SupportedTimeframes);
    }

    [Fact]
    public void DonchianBreakout_ReturnsLongOnValidBreakout()
    {
        var strategy = new DonchianBreakoutStrategy();
        var candle = StrategyTestHelpers.Candle(101m, 102m, 103m, 100.5m);
        var indicators = StrategyTestHelpers.Indicators(snapshot =>
        {
            snapshot.DonchianHigh20 = 100m;
            snapshot.DonchianLow20 = 90m;
            snapshot.Atr14 = 2m;
        });

        var result = strategy.Evaluate(StrategyTestHelpers.BuildContext(MarketRegime.Trending, [candle], indicators));
        Assert.Equal(SignalType.Entry, result.SignalType);
        Assert.Equal(TradeDirection.Long, result.Direction);
        StrategyTestHelpers.AssertStrengthInRange(result);
    }

    [Fact]
    public void DonchianBreakout_ReturnsShortOnValidBreakdown()
    {
        var strategy = new DonchianBreakoutStrategy();
        var candle = StrategyTestHelpers.Candle(89m, 88m, 90m, 87m);
        var indicators = StrategyTestHelpers.Indicators(snapshot =>
        {
            snapshot.DonchianHigh20 = 100m;
            snapshot.DonchianLow20 = 90m;
            snapshot.Atr14 = 2m;
        });

        var result = strategy.Evaluate(StrategyTestHelpers.BuildContext(MarketRegime.Breakout, [candle], indicators));
        Assert.Equal(SignalType.Entry, result.SignalType);
        Assert.Equal(TradeDirection.Short, result.Direction);
    }

    [Fact]
    public void DonchianBreakout_ReturnsNoTradeInUnsupportedRegime()
    {
        var strategy = new DonchianBreakoutStrategy();
        var candle = StrategyTestHelpers.Candle(101m, 102m, 103m, 100.5m);
        var indicators = StrategyTestHelpers.Indicators(snapshot =>
        {
            snapshot.DonchianHigh20 = 100m;
            snapshot.DonchianLow20 = 90m;
        });

        var result = strategy.Evaluate(StrategyTestHelpers.BuildContext(MarketRegime.Ranging, [candle], indicators));
        Assert.Equal(SignalType.NoTrade, result.SignalType);
        Assert.Contains("not supported", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DonchianBreakout_ReturnsNoTradeWhenIndicatorsMissing()
    {
        var strategy = new DonchianBreakoutStrategy();
        var result = strategy.Evaluate(StrategyTestHelpers.BuildContext(
            MarketRegime.Trending,
            [StrategyTestHelpers.Candle(100m, 101m, 102m, 99m)],
            StrategyTestHelpers.Indicators()));

        Assert.Equal(SignalType.NoTrade, result.SignalType);
        Assert.Contains("Donchian", result.Reason);
    }

    [Fact]
    public void BollingerSqueezeBreakout_ReturnsLongOnBreakout()
    {
        var strategy = new BollingerSqueezeBreakoutStrategy();
        var candle = StrategyTestHelpers.Candle(100m, 105m, 106m, 99m, 200m);
        var squeezeSnapshot = StrategyTestHelpers.Indicators(snapshot => snapshot.BollingerBandwidth20 = 0.8m);
        var indicators = StrategyTestHelpers.Indicators(snapshot =>
        {
            snapshot.BollingerUpper20 = 104m;
            snapshot.BollingerLower20 = 96m;
            snapshot.BollingerBandwidth20 = 2m;
            snapshot.Atr14 = 1m;
            snapshot.VolumeSma20 = 100m;
        });

        var result = strategy.Evaluate(StrategyTestHelpers.BuildContext(
            MarketRegime.Breakout,
            [candle],
            indicators,
            [squeezeSnapshot, indicators]));

        Assert.Equal(SignalType.Entry, result.SignalType);
        Assert.Equal(TradeDirection.Long, result.Direction);
    }

    [Fact]
    public void MacdMomentumContinuation_ReturnsLongWhenConditionsMet()
    {
        var strategy = new MacdMomentumContinuationStrategy();
        var candle = StrategyTestHelpers.Candle(100m, 102m, 103m, 99m);
        var previous = StrategyTestHelpers.Indicators(snapshot =>
        {
            snapshot.MacdHistogram = 0.1m;
        });
        var indicators = StrategyTestHelpers.Indicators(snapshot =>
        {
            snapshot.MacdLine = 1.2m;
            snapshot.MacdSignal = 0.8m;
            snapshot.MacdHistogram = 0.4m;
            snapshot.Ema20 = 101m;
            snapshot.Ema50 = 99m;
            snapshot.Atr14 = 1m;
        });

        var result = strategy.Evaluate(StrategyTestHelpers.BuildContext(
            MarketRegime.Trending,
            [candle],
            indicators,
            [previous, indicators]));

        Assert.Equal(SignalType.Entry, result.SignalType);
        Assert.Equal(TradeDirection.Long, result.Direction);
    }

    [Fact]
    public void SupertrendContinuation_ReturnsLongOnBullishPullback()
    {
        var strategy = new SupertrendContinuationStrategy();
        var candle = StrategyTestHelpers.Candle(100.1m, 100.3m, 100.5m, 99.8m);
        var indicators = StrategyTestHelpers.Indicators(snapshot =>
        {
            snapshot.Supertrend = 100m;
            snapshot.SupertrendDirection = 1;
            snapshot.Ema20 = 100.2m;
            snapshot.Atr14 = 1m;
        });

        var result = strategy.Evaluate(StrategyTestHelpers.BuildContext(MarketRegime.Trending, [candle], indicators));
        Assert.Equal(SignalType.Entry, result.SignalType);
        Assert.Equal(TradeDirection.Long, result.Direction);
    }

    [Fact]
    public void SupertrendContinuation_ReturnsNoTradeWhenSupertrendMissing()
    {
        var strategy = new SupertrendContinuationStrategy();
        var result = strategy.Evaluate(StrategyTestHelpers.BuildContext(
            MarketRegime.Trending,
            [StrategyTestHelpers.Candle(100m, 101m, 102m, 99m)],
            StrategyTestHelpers.Indicators()));

        Assert.Equal(SignalType.NoTrade, result.SignalType);
        Assert.Contains("Supertrend", result.Reason);
    }

    [Fact]
    public async Task StrategyEngine_MapsDiagnosticsFields()
    {
        var engine = new StrategyEngine();
        var strategy = new EmaPullbackStrategy();
        var context = StrategyTestHelpers.BuildContext(
            MarketRegime.Ranging,
            [StrategyTestHelpers.Candle(100m, 101m, 102m, 99m)],
            StrategyTestHelpers.Indicators(snapshot =>
            {
                snapshot.Ema20 = 100m;
                snapshot.Ema50 = 95m;
            }));

        var results = await engine.EvaluateAsync([strategy], context);
        Assert.Single(results);
        Assert.True(results[0].Evaluated);
        Assert.True(results[0].Skipped);
        Assert.False(string.IsNullOrWhiteSpace(results[0].SkipReason));
        Assert.Equal("Ranging", results[0].Regime);
        Assert.Equal("3m", results[0].Timeframe);
    }
}
