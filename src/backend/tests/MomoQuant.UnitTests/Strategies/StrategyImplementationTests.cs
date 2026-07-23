using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

public class EmaPullbackStrategyTests
{
    private readonly EmaPullbackStrategy _strategy = new();

    [Fact]
    public void Evaluate_ReturnsLongSignalInBullishTrendConditions()
    {
        var context = BuildContext(
            MarketRegime.Trending,
            CreateBullishCandle(100.2m, 99.8m, 101m, 99m, 120m),
            CreateIndicators(ema20: 100m, ema50: 95m, ema200: 90m, volumeSma20: 80m, atr14: 2m));

        var result = _strategy.Evaluate(context);

        Assert.Equal(SignalType.Entry, result.SignalType);
        Assert.Equal(TradeDirection.Long, result.Direction);
        Assert.Contains("Bullish EMA alignment", result.Reason);
    }

    [Fact]
    public void Evaluate_ReturnsShortSignalInBearishTrendConditions()
    {
        var context = BuildContext(
            MarketRegime.Trending,
            CreateBearishCandle(99.8m, 100.2m, 100.5m, 99m, 120m),
            CreateIndicators(ema20: 100m, ema50: 105m, ema200: 110m, volumeSma20: 80m, atr14: 2m));

        var result = _strategy.Evaluate(context);

        Assert.Equal(SignalType.Entry, result.SignalType);
        Assert.Equal(TradeDirection.Short, result.Direction);
    }

    [Fact]
    public void Evaluate_ReturnsNoTradeInUnsupportedRegime()
    {
        var context = BuildContext(
            MarketRegime.Ranging,
            CreateBullishCandle(100.2m, 99.8m, 101m, 99m, 120m),
            CreateIndicators(ema20: 100m, ema50: 95m, ema200: 90m, volumeSma20: 80m, atr14: 2m));

        var result = _strategy.Evaluate(context);

        Assert.Equal(SignalType.NoTrade, result.SignalType);
    }

    [Fact]
    public void Evaluate_ReturnsNoTradeWhenEmaAlignmentIsInvalid()
    {
        var context = BuildContext(
            MarketRegime.Trending,
            CreateBullishCandle(100.2m, 99.8m, 101m, 99m, 120m),
            CreateIndicators(ema20: 100m, ema50: 100m, ema200: 90m, volumeSma20: 80m, atr14: 2m));

        var result = _strategy.Evaluate(context);

        Assert.Equal(SignalType.NoTrade, result.SignalType);
        Assert.Contains("EMA alignment is invalid", result.Reason);
    }

    private static StrategyContext BuildContext(
        MarketRegime regime,
        Candle candle,
        IndicatorSnapshot indicators) => new()
    {
        SymbolId = 1,
        Symbol = "BTCUSDT",
        Timeframe = Timeframe.M3,
        HigherTimeframe = Timeframe.M15,
        MarketRegime = regime,
        Candles = [candle],
        IndicatorSnapshot = indicators,
        EvaluatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static Candle CreateBullishCandle(decimal close, decimal open, decimal high, decimal low, decimal volume) => new()
    {
        Id = 1,
        SymbolId = 1,
        Open = open,
        Close = close,
        High = high,
        Low = low,
        Volume = volume,
        OpenTimeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static Candle CreateBearishCandle(decimal close, decimal open, decimal high, decimal low, decimal volume) => new()
    {
        Id = 1,
        SymbolId = 1,
        Open = open,
        Close = close,
        High = high,
        Low = low,
        Volume = volume,
        OpenTimeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static IndicatorSnapshot CreateIndicators(
        decimal ema20,
        decimal ema50,
        decimal? ema200,
        decimal? volumeSma20,
        decimal? atr14) => new()
    {
        SymbolId = 1,
        Timeframe = Timeframe.M3,
        CandleId = 1,
        CalculatedAtUtc = DateTime.UtcNow,
        Ema20 = ema20,
        Ema50 = ema50,
        Ema200 = ema200,
        VolumeSma20 = volumeSma20,
        Atr14 = atr14,
        CreatedAtUtc = DateTime.UtcNow
    };
}

public class VwapMeanReversionStrategyTests
{
    private readonly VwapMeanReversionStrategy _strategy = new();

    [Fact]
    public void Evaluate_ReturnsLongSignalWhenPriceBelowVwapAndRsiOversold()
    {
        var context = BuildContext(
            MarketRegime.Ranging,
            new Candle { Open = 99m, Close = 100m, High = 100.5m, Low = 97m, Volume = 100m },
            new IndicatorSnapshot
            {
                Vwap = 102m,
                Rsi14 = 25m,
                Atr14 = 1m,
                Timeframe = Timeframe.M3,
                CandleId = 1,
                SymbolId = 1,
                CalculatedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            });

        var result = _strategy.Evaluate(context);

        Assert.Equal(SignalType.Entry, result.SignalType);
        Assert.Equal(TradeDirection.Long, result.Direction);
    }

    [Fact]
    public void Evaluate_ReturnsShortSignalWhenPriceAboveVwapAndRsiOverbought()
    {
        var context = BuildContext(
            MarketRegime.Ranging,
            new Candle { Open = 104m, Close = 103m, High = 105m, Low = 102.5m, Volume = 100m },
            new IndicatorSnapshot
            {
                Vwap = 100m,
                Rsi14 = 75m,
                Atr14 = 1m,
                Timeframe = Timeframe.M3,
                CandleId = 1,
                SymbolId = 1,
                CalculatedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            });

        var result = _strategy.Evaluate(context);

        Assert.Equal(SignalType.Entry, result.SignalType);
        Assert.Equal(TradeDirection.Short, result.Direction);
    }

    [Fact]
    public void Evaluate_ReturnsNoTradeInTrendingRegime()
    {
        var context = BuildContext(
            MarketRegime.Trending,
            new Candle { Open = 99m, Close = 100m, High = 100.5m, Low = 97m, Volume = 100m },
            new IndicatorSnapshot
            {
                Vwap = 102m,
                Rsi14 = 25m,
                Atr14 = 1m,
                Timeframe = Timeframe.M3,
                CandleId = 1,
                SymbolId = 1,
                CalculatedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            });

        var result = _strategy.Evaluate(context);

        Assert.Equal(SignalType.NoTrade, result.SignalType);
    }

    [Fact]
    public void Evaluate_ReturnsNoTradeWhenRsiConditionIsNotMet()
    {
        var context = BuildContext(
            MarketRegime.Ranging,
            new Candle { Open = 99m, Close = 100m, High = 100.5m, Low = 97m, Volume = 100m },
            new IndicatorSnapshot
            {
                Vwap = 102m,
                Rsi14 = 45m,
                Atr14 = 1m,
                Timeframe = Timeframe.M3,
                CandleId = 1,
                SymbolId = 1,
                CalculatedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            });

        var result = _strategy.Evaluate(context);

        Assert.Equal(SignalType.NoTrade, result.SignalType);
    }

    private static StrategyContext BuildContext(MarketRegime regime, Candle candle, IndicatorSnapshot indicators) => new()
    {
        SymbolId = 1,
        Timeframe = Timeframe.M3,
        HigherTimeframe = Timeframe.M15,
        MarketRegime = regime,
        Candles = [candle],
        IndicatorSnapshot = indicators,
        EvaluatedAtUtc = DateTime.UtcNow
    };
}

public class LiquiditySweepStrategyTests
{
    private readonly LiquiditySweepStrategy _strategy = new();

    [Fact]
    public void Evaluate_ReturnsLongSignalAfterSweepBelowSwingLowAndReclaim()
    {
        var candles = new List<Candle>
        {
            new() { Open = 105m, Close = 104m, High = 106m, Low = 104m, Volume = 80m },
            new() { Open = 104m, Close = 103m, High = 105m, Low = 103m, Volume = 80m },
            new() { Open = 103m, Close = 102m, High = 104m, Low = 98m, Volume = 80m },
            new() { Open = 102m, Close = 101m, High = 103m, Low = 99m, Volume = 80m },
            new() { Open = 101m, Close = 100.5m, High = 102m, Low = 100m, Volume = 80m },
            new() { Open = 101m, Close = 102m, High = 103m, Low = 96m, Volume = 150m }
        };

        var context = BuildContext(candles, volumeSma20: 100m, new Dictionary<string, string>
        {
            ["MinWickPercent"] = "30",
            ["RequireVolumeSpike"] = "false"
        });
        var result = _strategy.Evaluate(context);

        Assert.Equal(SignalType.Entry, result.SignalType);
        Assert.Equal(TradeDirection.Long, result.Direction);
    }

    [Fact]
    public void Evaluate_ReturnsShortSignalAfterSweepAboveSwingHighAndReject()
    {
        var candles = new List<Candle>
        {
            new() { Open = 95m, Close = 96m, High = 97m, Low = 94m, Volume = 80m },
            new() { Open = 96m, Close = 97m, High = 98m, Low = 95m, Volume = 80m },
            new() { Open = 97m, Close = 98m, High = 102m, Low = 96m, Volume = 80m },
            new() { Open = 98m, Close = 99m, High = 101m, Low = 97m, Volume = 80m },
            new() { Open = 99m, Close = 100m, High = 101m, Low = 98m, Volume = 80m },
            new() { Open = 101m, Close = 99.5m, High = 104m, Low = 99m, Volume = 150m }
        };

        var context = BuildContext(candles, volumeSma20: 100m, new Dictionary<string, string>
        {
            ["MinWickPercent"] = "30",
            ["RequireVolumeSpike"] = "false"
        });
        var result = _strategy.Evaluate(context);

        Assert.Equal(SignalType.Entry, result.SignalType);
        Assert.Equal(TradeDirection.Short, result.Direction);
    }

    [Fact]
    public void Evaluate_ReturnsNoTradeWhenInsufficientCandles()
    {
        var context = BuildContext([new Candle { Open = 100m, Close = 101m, High = 102m, Low = 99m, Volume = 100m }], 80m);
        var result = _strategy.Evaluate(context);

        Assert.Equal(SignalType.NoTrade, result.SignalType);
    }

    private static StrategyContext BuildContext(
        IReadOnlyList<Candle> candles,
        decimal volumeSma20,
        IReadOnlyDictionary<string, string>? parameters = null) => new()
    {
        SymbolId = 1,
        Timeframe = Timeframe.M3,
        HigherTimeframe = Timeframe.M15,
        MarketRegime = MarketRegime.Reversal,
        Candles = candles,
        StrategyParameters = parameters ?? new Dictionary<string, string>(),
        IndicatorSnapshot = new IndicatorSnapshot
        {
            VolumeSma20 = volumeSma20,
            Atr14 = 2m,
            Timeframe = Timeframe.M3,
            CandleId = 1,
            SymbolId = 1,
            CalculatedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        },
        EvaluatedAtUtc = DateTime.UtcNow
    };
}

public class StrategyEngineTests
{
    [Fact]
    public async Task EvaluateAsync_DoesNotPlaceOrdersAndReturnsReasons()
    {
        var engine = new MomoQuant.Application.Strategies.StrategyEngine();
        var strategy = new EmaPullbackStrategy();
        var context = new StrategyContext
        {
            SymbolId = 1,
            Timeframe = Timeframe.M3,
            HigherTimeframe = Timeframe.M15,
            MarketRegime = MarketRegime.Ranging,
            Candles = [],
            IndicatorSnapshot = null,
            EvaluatedAtUtc = DateTime.UtcNow
        };

        var results = await engine.EvaluateAsync([strategy], context);

        Assert.Single(results);
        Assert.False(string.IsNullOrWhiteSpace(results[0].Reason));
        Assert.Equal(SignalType.NoTrade, results[0].SignalType);
    }
}
