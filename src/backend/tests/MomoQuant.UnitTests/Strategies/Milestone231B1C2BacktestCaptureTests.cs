using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>Milestone 23.1B1C2 — complete Backtest StrategyContext capture (no inference).</summary>
public sealed class Milestone231B1C2BacktestCaptureTests
{
    private static readonly DateTime EvalTime = new(2026, 2, 1, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Capture_RecordsCompleteContextFromActualStrategyContext()
    {
        var candles = new List<Candle> { BuildCandle(10), BuildCandle(11), BuildCandle(12) };
        var htf = new List<Candle> { BuildCandle(100, timeframe: Timeframe.H1) };
        var snapshot = new IndicatorSnapshot
        {
            CandleId = 12,
            SymbolId = 7,
            Timeframe = Timeframe.M5,
            Ema20 = 1.1m,
            Ema50 = 1.2m,
            Ema200 = 1.3m,
            Atr14 = 0.4m
        };
        var parameters = new Dictionary<string, string>
        {
            ["alpha"] = "1",
            ["__seenFingerprints"] = "[]"
        };
        var context = new StrategyContext
        {
            ExchangeId = 42,
            SymbolId = 7,
            Symbol = "BTCUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            HigherTimeframeCandles = htf,
            MarketRegime = MarketRegime.Trending,
            Candles = candles,
            IndicatorSnapshot = snapshot,
            StrategyParameters = parameters,
            EvaluatedAtUtc = EvalTime,
            CurrentCandleIndex = 99
        };

        var recording = new StrategyEvaluationCaptureRecording();
        recording.Capture(context, new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy());

        var record = Assert.Single(recording.Records);
        Assert.Equal(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout, record.StrategyCode);
        Assert.Equal(42, record.ExchangeId);
        Assert.Equal(7, record.SymbolId);
        Assert.Equal("BTCUSDT", record.Symbol);
        Assert.Equal(Timeframe.M5, record.ExecutionTimeframe);
        Assert.Equal(Timeframe.H1, record.HigherTimeframe);
        Assert.Equal(MarketRegime.Trending, record.MarketRegime);
        Assert.Equal(EvalTime, record.EvaluatedAtUtc);
        Assert.Equal(99, record.CurrentCandleIndex);
        Assert.NotEqual(candles.Count - 1, record.CurrentCandleIndex);
        Assert.Equal(new long[] { 10, 11, 12 }, record.Candles.Select(c => c.Id).ToArray());
        Assert.Equal(new long[] { 100 }, record.HigherTimeframeCandles.Select(c => c.Id).ToArray());
        Assert.Same(snapshot, record.IndicatorSnapshot);
        Assert.Equal(2, record.StrategyParameters.Count);
        Assert.Equal("1", record.StrategyParameters["alpha"]);

        parameters["alpha"] = "mutated";
        Assert.Equal("1", record.StrategyParameters["alpha"]);
    }

    [Fact]
    public void Capture_PreservesNullExchangeAndSymbolWithoutSubstitution()
    {
        var context = new StrategyContext
        {
            ExchangeId = null,
            SymbolId = 7,
            Symbol = null,
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            MarketRegime = MarketRegime.Ranging,
            Candles = [BuildCandle(1)],
            IndicatorSnapshot = null,
            StrategyParameters = new Dictionary<string, string>(),
            EvaluatedAtUtc = EvalTime,
            CurrentCandleIndex = 0
        };

        var recording = new StrategyEvaluationCaptureRecording();
        recording.Capture(context, new MomoVolatilityRangeReversionStrategy());

        var record = Assert.Single(recording.Records);
        Assert.Null(record.ExchangeId);
        Assert.Null(record.Symbol);
        Assert.Null(record.IndicatorSnapshot);
        Assert.Equal(0, record.CurrentCandleIndex);
    }

    [Fact]
    public void Capture_DoesNotInferIndexAsLastCandle()
    {
        var candles = new List<Candle> { BuildCandle(1), BuildCandle(2), BuildCandle(3) };
        var context = new StrategyContext
        {
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = "ETHUSDT",
            Timeframe = Timeframe.M5,
            HigherTimeframe = Timeframe.H1,
            MarketRegime = MarketRegime.Breakout,
            Candles = candles,
            IndicatorSnapshot = null,
            StrategyParameters = new Dictionary<string, string>(),
            EvaluatedAtUtc = EvalTime,
            CurrentCandleIndex = 1
        };

        var recording = new StrategyEvaluationCaptureRecording();
        recording.Capture(context, new PriceStructureBreakoutRetestStrategy());

        Assert.Equal(1, Assert.Single(recording.Records).CurrentCandleIndex);
    }

    private static Candle BuildCandle(long id, Timeframe timeframe = Timeframe.M5) =>
        new()
        {
            Id = id,
            SymbolId = 7,
            ExchangeId = 42,
            Timeframe = timeframe,
            OpenTimeUtc = EvalTime.AddMinutes(-(int)id),
            CloseTimeUtc = EvalTime.AddMinutes(-(int)id).AddMinutes(5),
            Open = 100m,
            High = 101m,
            Low = 99m,
            Close = 100.5m,
            Volume = 10m,
            IsClosed = true,
            CreatedAtUtc = EvalTime
        };
}
