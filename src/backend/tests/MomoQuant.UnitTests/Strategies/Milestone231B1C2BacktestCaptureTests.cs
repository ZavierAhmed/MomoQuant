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
            Id = 99,
            CandleId = 12,
            SymbolId = 7,
            Timeframe = Timeframe.M5,
            CalculatedAtUtc = EvalTime.AddSeconds(-1),
            Ema20 = 1.1m, Ema50 = 1.2m, Ema200 = 1.3m, Vwap = 1.4m,
            Rsi14 = 1.5m, Atr14 = 1.6m, VolumeSma20 = 1.7m, SwingHigh = 1.8m,
            SwingLow = 1.9m, MarketStructure = MarketStructure.HigherHighsHigherLows,
            BollingerMiddle20 = 2.0m, BollingerUpper20 = 2.1m, BollingerLower20 = 2.2m,
            BollingerBandwidth20 = 2.3m, DonchianHigh20 = 2.4m, DonchianLow20 = 2.5m,
            MacdLine = 2.6m, MacdSignal = 2.7m, MacdHistogram = 2.8m, Supertrend = 2.9m,
            SupertrendDirection = 1, SupportLevel = 3.0m, ResistanceLevel = 3.1m,
            CreatedAtUtc = EvalTime.AddSeconds(-2)
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

        var expectedLtf = candles.Select(StrategyEvaluationCandleSnapshot.Capture).ToArray();
        var expectedHtf = htf.Select(StrategyEvaluationCandleSnapshot.Capture).ToArray();
        var expectedIndicator = StrategyEvaluationIndicatorSnapshot.Capture(snapshot);
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
        Assert.Equal(expectedLtf, record.Candles);
        Assert.Equal(expectedHtf, record.HigherTimeframeCandles);
        Assert.Equal(expectedIndicator, record.IndicatorSnapshot);
        Assert.Equal(2, record.StrategyParameters.Count);
        Assert.Equal("1", record.StrategyParameters["alpha"]);

        foreach (var candle in candles.Concat(htf))
        {
            candle.Id += 1_000; candle.ExchangeId += 1_000; candle.SymbolId += 1_000;
            candle.Timeframe = Timeframe.H4; candle.OpenTimeUtc = candle.OpenTimeUtc.AddDays(1);
            candle.CloseTimeUtc = candle.CloseTimeUtc.AddDays(1); candle.Open += 1m; candle.High += 1m;
            candle.Low += 1m; candle.Close += 1m; candle.Volume += 1m; candle.QuoteVolume += 1m;
            candle.TradeCount += 1; candle.IsClosed = !candle.IsClosed; candle.CreatedAtUtc = candle.CreatedAtUtc.AddDays(1);
        }
        snapshot.Id += 1; snapshot.SymbolId += 1; snapshot.Timeframe = Timeframe.H4; snapshot.CandleId += 1;
        snapshot.CalculatedAtUtc = snapshot.CalculatedAtUtc.AddDays(1); snapshot.Ema20 = null; snapshot.Ema50 = null;
        snapshot.Ema200 = null; snapshot.Vwap = null; snapshot.Rsi14 = null; snapshot.Atr14 = null;
        snapshot.VolumeSma20 = null; snapshot.SwingHigh = null; snapshot.SwingLow = null; snapshot.MarketStructure = MarketStructure.Bearish;
        snapshot.BollingerMiddle20 = null; snapshot.BollingerUpper20 = null; snapshot.BollingerLower20 = null;
        snapshot.BollingerBandwidth20 = null; snapshot.DonchianHigh20 = null; snapshot.DonchianLow20 = null;
        snapshot.MacdLine = null; snapshot.MacdSignal = null; snapshot.MacdHistogram = null; snapshot.Supertrend = null;
        snapshot.SupertrendDirection = null; snapshot.SupportLevel = null; snapshot.ResistanceLevel = null;
        snapshot.CreatedAtUtc = snapshot.CreatedAtUtc.AddDays(1);
        parameters["alpha"] = "mutated";
        parameters["added"] = "mutated";

        Assert.Equal(expectedLtf, record.Candles);
        Assert.Equal(expectedHtf, record.HigherTimeframeCandles);
        Assert.Equal(expectedIndicator, record.IndicatorSnapshot);
        Assert.Equal("1", record.StrategyParameters["alpha"]);
        Assert.False(record.StrategyParameters.ContainsKey("added"));
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
            QuoteVolume = 11m,
            TradeCount = 12,
            IsClosed = true,
            CreatedAtUtc = EvalTime
        };
}
