using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>Milestone 23.1B1C1 — parity assertions must fail when Lab/capture evidence is wrong or missing.</summary>
public sealed class Milestone231B1CParityEvidenceTests
{
    private static readonly DateTime EvalTime = new(2026, 2, 1, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PositiveParity_EmptyCaptureCandles_Fails()
    {
        var direct = EntrySignal();
        var lab = EntryLabContextAndSignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles(Array.Empty<Candle>());

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                direct, lab.Context, lab.Signal, EntryLabCandidate(), backtest, BuildPositiveEvidence(capture)));
    }

    [Fact]
    public void PositiveParity_MismatchedExecutionCandleIds_Fails()
    {
        var candle = BuildCandle(1);
        var direct = EntrySignal();
        var lab = EntryLabContextAndSignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([candle]);
        var evidence = BuildPositiveEvidence(capture);
        evidence = new ParityAssertionHelper.PositiveThreePathEvidence
        {
            BacktestCapture = evidence.BacktestCapture,
            ExpectedRegime = evidence.ExpectedRegime,
            ExpectedExchangeId = evidence.ExpectedExchangeId,
            ExpectedSymbolId = evidence.ExpectedSymbolId,
            ExpectedSymbol = evidence.ExpectedSymbol,
            ExpectedTimeframe = evidence.ExpectedTimeframe,
            ExpectedHigherTimeframe = evidence.ExpectedHigherTimeframe,
            ExpectedEvaluationTimestamp = evidence.ExpectedEvaluationTimestamp,
            ExpectedCurrentCandleIndex = evidence.ExpectedCurrentCandleIndex,
            ExpectedExecutionCandleIds = [999],
            ExpectedHtfCandleIds = evidence.ExpectedHtfCandleIds,
            ExpectedParameters = evidence.ExpectedParameters,
            ExpectedIndicatorSnapshot = evidence.ExpectedIndicatorSnapshot
        };

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                direct, lab.Context, lab.Signal, EntryLabCandidate(), backtest, evidence));
    }

    [Fact]
    public void PositiveParity_WrongLabSignalType_Fails()
    {
        var direct = EntrySignal();
        var lab = EntryLabContextAndSignal();
        var wrongLabSignal = RejectionSignal("TREND_FILTER_FAILED");
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                direct, lab.Context, wrongLabSignal, EntryLabCandidate(), backtest, BuildPositiveEvidence(capture)));
    }

    [Fact]
    public void PositiveParity_WrongLabReason_Fails()
    {
        var direct = EntrySignal();
        var lab = EntryLabContextAndSignal();
        var wrongLabSignal = EntrySignal();
        wrongLabSignal = new StrategySignalResult
        {
            SignalType = wrongLabSignal.SignalType,
            Direction = wrongLabSignal.Direction,
            EntryPrice = wrongLabSignal.EntryPrice,
            SuggestedStopLoss = wrongLabSignal.SuggestedStopLoss,
            SuggestedTakeProfit = wrongLabSignal.SuggestedTakeProfit,
            Strength = wrongLabSignal.Strength,
            ConfidenceContribution = wrongLabSignal.ConfidenceContribution,
            Reason = "WRONG",
            RawDataJson = wrongLabSignal.RawDataJson
        };
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                direct, lab.Context, wrongLabSignal, EntryLabCandidate(), backtest, BuildPositiveEvidence(capture)));
    }

    [Fact]
    public void PositiveParity_WrongLabRegime_Fails()
    {
        var direct = EntrySignal();
        var labContext = BuildLabContext(MarketRegime.Ranging);
        var labSignal = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                direct, labContext, labSignal, EntryLabCandidate(), backtest, BuildPositiveEvidence(capture)));
    }

    [Fact]
    public void PositiveParity_WrongLabTimestamp_Fails()
    {
        var direct = EntrySignal();
        var labContext = BuildLabContext(MarketRegime.Trending, EvalTime.AddMinutes(5));
        var labSignal = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                direct, labContext, labSignal, EntryLabCandidate(), backtest, BuildPositiveEvidence(capture)));
    }

    [Fact]
    public void PositiveParity_WrongLabCandleIndex_Fails()
    {
        var direct = EntrySignal();
        var labContext = BuildLabContext(MarketRegime.Trending, currentCandleIndex: 99);
        var labSignal = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                direct, labContext, labSignal, EntryLabCandidate(), backtest, BuildPositiveEvidence(capture)));
    }

    [Fact]
    public void PositiveParity_WrongSymbolExchange_Fails()
    {
        var direct = EntrySignal();
        var labContext = BuildLabContext(MarketRegime.Trending, exchangeId: 99, symbolId: 88, symbol: "WRONG");
        var labSignal = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1, exchangeId: 99, symbolId: 88)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                direct, labContext, labSignal, EntryLabCandidate(), backtest, BuildPositiveEvidence(capture)));
    }

    [Fact]
    public void PositiveParity_WrongExecutionTimeframe_Fails()
    {
        var direct = EntrySignal();
        var labContext = BuildLabContext(MarketRegime.Trending, timeframe: Timeframe.H1);
        var labSignal = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1, timeframe: Timeframe.H1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                direct, labContext, labSignal, EntryLabCandidate(), backtest, BuildPositiveEvidence(capture)));
    }

    [Fact]
    public void PositiveParity_WrongHigherTimeframe_Fails()
    {
        var direct = EntrySignal();
        var labContext = BuildLabContext(MarketRegime.Trending, higherTimeframe: Timeframe.H4);
        var labSignal = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)], higherTimeframe: Timeframe.H4);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                direct, labContext, labSignal, EntryLabCandidate(), backtest, BuildPositiveEvidence(capture)));
    }

    [Fact]
    public void PositiveParity_WrongHtfCandleIds_Fails()
    {
        var direct = EntrySignal();
        var labContext = BuildLabContext(MarketRegime.Trending, htfCandles: [BuildCandle(999, timeframe: Timeframe.H1)]);
        var labSignal = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)], htfCandles: [BuildCandle(999, timeframe: Timeframe.H1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                direct, labContext, labSignal, EntryLabCandidate(), backtest, BuildPositiveEvidence(capture)));
    }

    [Fact]
    public void PositiveParity_WrongParameters_Fails()
    {
        var direct = EntrySignal();
        var labContext = BuildLabContext(MarketRegime.Trending, parameters: new Dictionary<string, string> { ["x"] = "wrong" });
        var labSignal = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                direct, labContext, labSignal, EntryLabCandidate(), backtest, BuildPositiveEvidence(capture)));
    }

    [Fact]
    public void PositiveParity_WrongIndicatorSnapshot_Fails()
    {
        var direct = EntrySignal();
        var snapshot = BuildSnapshot(1);
        var wrongSnapshot = BuildSnapshot(1);
        wrongSnapshot.Ema20 = 999m;
        var labContext = BuildLabContext(MarketRegime.Trending, indicatorSnapshot: wrongSnapshot);
        var labSignal = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                direct, labContext, labSignal, EntryLabCandidate(), backtest,
                new ParityAssertionHelper.PositiveThreePathEvidence
                {
                    BacktestCapture = capture,
                    ExpectedRegime = MarketRegime.Trending,
                    ExpectedExchangeId = 42,
                    ExpectedSymbolId = 7,
                    ExpectedSymbol = "BTCUSDT",
                    ExpectedTimeframe = Timeframe.M5,
                    ExpectedHigherTimeframe = Timeframe.H1,
                    ExpectedEvaluationTimestamp = EvalTime,
                    ExpectedCurrentCandleIndex = 0,
                    ExpectedExecutionCandleIds = capture.Candles.Select(c => c.Id).ToArray(),
                    ExpectedHtfCandleIds = [],
                    ExpectedParameters = DefaultParameters(),
                    ExpectedIndicatorSnapshot = snapshot
                }));
    }

    [Fact]
    public void PositiveParity_MissingFingerprint_Fails()
    {
        var direct = new StrategySignalResult
        {
            SignalType = SignalType.Entry,
            Direction = TradeDirection.Long,
            EntryPrice = 100m,
            SuggestedStopLoss = 99m,
            SuggestedTakeProfit = 102m,
            Strength = 0.75m,
            ConfidenceContribution = 0.5m,
            Reason = "ENTRY",
            RawDataJson = "{\"strength\":0.75,\"strengthBreakdown\":{\"total\":0.75}}"
        };
        var lab = EntryLabContextAndSignal();
        var backtest = EntryBacktest();
        backtest = new StrategyEvaluationResult
        {
            StrategyCode = backtest.StrategyCode,
            StrategyName = backtest.StrategyName,
            Evaluated = backtest.Evaluated,
            Skipped = backtest.Skipped,
            SignalType = backtest.SignalType,
            Direction = backtest.Direction,
            EntryPrice = backtest.EntryPrice,
            SuggestedStopLoss = backtest.SuggestedStopLoss,
            SuggestedTakeProfit = backtest.SuggestedTakeProfit,
            Strength = backtest.Strength,
            ConfidenceContribution = backtest.ConfidenceContribution,
            Reason = backtest.Reason,
            Regime = backtest.Regime,
            RawDataJson = direct.RawDataJson,
            IsValid = backtest.IsValid
        };
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                direct, lab.Context, lab.Signal, EntryLabCandidate(), backtest, BuildPositiveEvidence(capture)));
    }

    [Fact]
    public void PositiveParity_MissingStrengthBreakdown_Fails()
    {
        var rawWithoutBreakdown = "{\"setupFingerprint\":\"fp-1\",\"strength\":0.75}";
        var direct = new StrategySignalResult
        {
            SignalType = SignalType.Entry,
            Direction = TradeDirection.Long,
            EntryPrice = 100m,
            SuggestedStopLoss = 99m,
            SuggestedTakeProfit = 102m,
            Strength = 0.75m,
            ConfidenceContribution = 0.5m,
            Reason = "ENTRY",
            RawDataJson = rawWithoutBreakdown
        };
        var labContext = BuildLabContext(MarketRegime.Trending);
        var labSignal = new StrategySignalResult
        {
            SignalType = SignalType.Entry,
            Direction = TradeDirection.Long,
            EntryPrice = 100m,
            SuggestedStopLoss = 99m,
            SuggestedTakeProfit = 102m,
            Strength = 0.75m,
            ConfidenceContribution = 0.5m,
            Reason = "ENTRY",
            RawDataJson = rawWithoutBreakdown
        };
        var backtest = EntryBacktest();
        backtest = new StrategyEvaluationResult
        {
            StrategyCode = backtest.StrategyCode,
            StrategyName = backtest.StrategyName,
            Evaluated = backtest.Evaluated,
            Skipped = backtest.Skipped,
            SignalType = backtest.SignalType,
            Direction = backtest.Direction,
            EntryPrice = backtest.EntryPrice,
            SuggestedStopLoss = backtest.SuggestedStopLoss,
            SuggestedTakeProfit = backtest.SuggestedTakeProfit,
            Strength = backtest.Strength,
            ConfidenceContribution = backtest.ConfidenceContribution,
            Reason = backtest.Reason,
            Regime = backtest.Regime,
            RawDataJson = rawWithoutBreakdown,
            IsValid = backtest.IsValid
        };
        var candidate = EntryLabCandidate();
        candidate.StructureJson = "{\"strength\":0.75}";
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                direct, labContext, labSignal, candidate, backtest, BuildPositiveEvidence(capture)));
    }

    [Fact]
    public void RejectionParity_EmptyLabSummary_Fails()
    {
        var direct = RejectionSignal("TREND_FILTER_FAILED");
        var lab = RejectionLabContextAndSignal("TREND_FILTER_FAILED");
        var backtest = RejectionBacktest("TREND_FILTER_FAILED");
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertRejectionThreePathParity(
                direct, lab.Context, lab.Signal, backtest,
                new ParityAssertionHelper.RejectionThreePathEvidence
                {
                    BacktestCapture = capture,
                    ExpectedRegime = MarketRegime.Ranging,
                    ExpectedLabRejectionCode = "TREND_FILTER_FAILED",
                    LabResultSummaryJson = "",
                    ExpectedEvaluationTimestamp = EvalTime,
                    ExpectedCurrentCandleIndex = 0,
                    ExpectedExecutionCandleIds = [1],
                    ExpectedHtfCandleIds = [],
                    ExpectedParameters = DefaultParameters(),
                    ExpectedIndicatorSnapshot = BuildSnapshot(1)
                }));
    }

    [Fact]
    public void RejectionParity_MissingFunnelCode_Fails()
    {
        var direct = RejectionSignal("NOT_IN_FUNNEL");
        var lab = RejectionLabContextAndSignal("NOT_IN_FUNNEL");
        var backtest = RejectionBacktest("NOT_IN_FUNNEL");
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertRejectionThreePathParity(
                direct, lab.Context, lab.Signal, backtest,
                new ParityAssertionHelper.RejectionThreePathEvidence
                {
                    BacktestCapture = capture,
                    ExpectedRegime = MarketRegime.Ranging,
                    ExpectedLabRejectionCode = "NOT_IN_FUNNEL",
                    LabResultSummaryJson = "{\"rejectionFunnel\":{\"counts\":{\"OTHER_CODE\":1}}}",
                    ExpectedEvaluationTimestamp = EvalTime,
                    ExpectedCurrentCandleIndex = 0,
                    ExpectedExecutionCandleIds = [1],
                    ExpectedHtfCandleIds = [],
                    ExpectedParameters = DefaultParameters(),
                    ExpectedIndicatorSnapshot = BuildSnapshot(1)
                }));
    }

    [Fact]
    public void RejectionParity_WrongLabReason_Fails()
    {
        var direct = RejectionSignal("TREND_FILTER_FAILED");
        var lab = RejectionLabContextAndSignal("OTHER_REASON");
        var backtest = RejectionBacktest("TREND_FILTER_FAILED");
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertRejectionThreePathParity(
                direct, lab.Context, lab.Signal, backtest, BuildRejectionEvidence(capture, "TREND_FILTER_FAILED")));
    }

    [Fact]
    public void RejectionParity_AggregateFunnelWithoutMatchingCapture_Fails()
    {
        var direct = RejectionSignal("CAPTURED_REASON");
        var lab = RejectionLabContextAndSignal("CAPTURED_REASON");
        var backtest = RejectionBacktest("CAPTURED_REASON");
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertRejectionThreePathParity(
                direct, lab.Context, lab.Signal, backtest,
                BuildRejectionEvidence(capture, "DIFFERENT_FUNNEL_CODE")));
    }

    [Fact]
    public void RejectionParity_WrongLabSignalType_Fails()
    {
        var direct = RejectionSignal("TREND_FILTER_FAILED");
        var labContext = BuildLabContext(MarketRegime.Ranging);
        var entryLabSignal = EntrySignal();
        var backtest = RejectionBacktest("TREND_FILTER_FAILED");
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertRejectionThreePathParity(
                direct, labContext, entryLabSignal, backtest, BuildRejectionEvidence(capture, "TREND_FILTER_FAILED")));
    }

    private static ParityAssertionHelper.PositiveThreePathEvidence BuildPositiveEvidence(StrategyEvaluationCaptureRecord capture) =>
        new()
        {
            BacktestCapture = capture,
            ExpectedRegime = MarketRegime.Trending,
            ExpectedExchangeId = 42,
            ExpectedSymbolId = 7,
            ExpectedSymbol = "BTCUSDT",
            ExpectedTimeframe = Timeframe.M5,
            ExpectedHigherTimeframe = Timeframe.H1,
            ExpectedEvaluationTimestamp = EvalTime,
            ExpectedCurrentCandleIndex = 0,
            ExpectedExecutionCandleIds = capture.Candles.Select(c => c.Id).ToArray(),
            ExpectedHtfCandleIds = [],
            ExpectedParameters = DefaultParameters(),
            ExpectedIndicatorSnapshot = BuildSnapshot(capture.Candles.FirstOrDefault()?.Id ?? 1)
        };

    private static ParityAssertionHelper.RejectionThreePathEvidence BuildRejectionEvidence(
        StrategyEvaluationCaptureRecord capture,
        string rejectionCode) =>
        new()
        {
            BacktestCapture = capture,
            ExpectedRegime = MarketRegime.Ranging,
            ExpectedLabRejectionCode = rejectionCode,
            LabResultSummaryJson = $"{{\"rejectionFunnel\":{{\"counts\":{{\"{rejectionCode}\":1}}}}}}",
            ExpectedEvaluationTimestamp = EvalTime,
            ExpectedCurrentCandleIndex = 0,
            ExpectedExecutionCandleIds = capture.Candles.Select(c => c.Id).ToArray(),
            ExpectedHtfCandleIds = [],
            ExpectedParameters = DefaultParameters(),
            ExpectedIndicatorSnapshot = BuildSnapshot(capture.Candles.FirstOrDefault()?.Id ?? 1)
        };

    private static (StrategyContext Context, StrategySignalResult Signal) EntryLabContextAndSignal() =>
        (BuildLabContext(MarketRegime.Trending), EntrySignal());

    private static (StrategyContext Context, StrategySignalResult Signal) RejectionLabContextAndSignal(string reason) =>
        (BuildLabContext(MarketRegime.Ranging), RejectionSignal(reason));

    private static StrategyContext BuildLabContext(
        MarketRegime regime,
        DateTime? evaluatedAtUtc = null,
        int currentCandleIndex = 0,
        long exchangeId = 42,
        long symbolId = 7,
        string symbol = "BTCUSDT",
        Timeframe timeframe = Timeframe.M5,
        Timeframe higherTimeframe = Timeframe.H1,
        IReadOnlyList<Candle>? htfCandles = null,
        IReadOnlyDictionary<string, string>? parameters = null,
        IndicatorSnapshot? indicatorSnapshot = null) =>
        new()
        {
            ExchangeId = exchangeId,
            SymbolId = symbolId,
            Symbol = symbol,
            Timeframe = timeframe,
            HigherTimeframe = higherTimeframe,
            HigherTimeframeCandles = htfCandles ?? [],
            MarketRegime = regime,
            Candles = [BuildCandle(1, exchangeId, symbolId, timeframe)],
            IndicatorSnapshot = indicatorSnapshot ?? BuildSnapshot(1),
            StrategyParameters = parameters ?? DefaultParameters(),
            EvaluatedAtUtc = evaluatedAtUtc ?? EvalTime,
            CurrentCandleIndex = currentCandleIndex
        };

    private static IReadOnlyDictionary<string, string> DefaultParameters() =>
        new Dictionary<string, string> { ["__seenFingerprints"] = "[]" };

    private static StrategyEvaluationCaptureRecord CaptureWithCandles(
        IReadOnlyList<Candle> candles,
        Timeframe higherTimeframe = Timeframe.H1,
        IReadOnlyList<Candle>? htfCandles = null) =>
        new(
            StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
            EvalTime,
            Timeframe.M5,
            higherTimeframe,
            candles,
            htfCandles ?? Array.Empty<Candle>());

    private static Candle BuildCandle(
        long id,
        long exchangeId = 42,
        long symbolId = 7,
        Timeframe timeframe = Timeframe.M5) =>
        new()
        {
            Id = id,
            SymbolId = symbolId,
            ExchangeId = exchangeId,
            Timeframe = timeframe,
            OpenTimeUtc = EvalTime,
            CloseTimeUtc = EvalTime.AddMinutes(5),
            Open = 100m,
            High = 101m,
            Low = 99m,
            Close = 100m,
            Volume = 1m,
            IsClosed = true,
            CreatedAtUtc = EvalTime
        };

    private static IndicatorSnapshot BuildSnapshot(long candleId) =>
        new()
        {
            CandleId = candleId,
            SymbolId = 7,
            Timeframe = Timeframe.M5,
            Ema20 = 130m,
            Ema50 = 120m,
            Ema200 = 110m,
            Atr14 = 1m,
            CalculatedAtUtc = EvalTime,
            CreatedAtUtc = EvalTime
        };

    private static StrategySignalResult EntrySignal() => new()
    {
        SignalType = SignalType.Entry,
        Direction = TradeDirection.Long,
        EntryPrice = 100m,
        SuggestedStopLoss = 99m,
        SuggestedTakeProfit = 102m,
        Strength = 0.75m,
        ConfidenceContribution = 0.5m,
        Reason = "ENTRY",
        RawDataJson = "{\"setupFingerprint\":\"fp-1\",\"strength\":0.75,\"strengthBreakdown\":{\"total\":0.75}}"
    };

    private static StrategyResearchCandidate EntryLabCandidate() =>
        new()
        {
            StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
            Direction = TradeDirection.Long,
            ProposedEntryPrice = 100m,
            StopLoss = 99m,
            Target1 = 102m,
            SetupFingerprint = "fp-1",
            StrategyReason = "ENTRY",
            StructureJson = "{\"strength\":0.75,\"strengthBreakdown\":{\"total\":0.75}}"
        };

    private static StrategyEvaluationResult EntryBacktest() => new()
    {
        StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
        StrategyName = "Adaptive",
        Evaluated = true,
        Skipped = false,
        SignalType = SignalType.Entry,
        Direction = TradeDirection.Long,
        EntryPrice = 100m,
        SuggestedStopLoss = 99m,
        SuggestedTakeProfit = 102m,
        Strength = 0.75m,
        ConfidenceContribution = 0.5m,
        Reason = "ENTRY",
        Regime = MarketRegime.Trending.ToString(),
        RawDataJson = "{\"setupFingerprint\":\"fp-1\",\"strength\":0.75,\"strengthBreakdown\":{\"total\":0.75}}",
        IsValid = true
    };

    private static StrategySignalResult RejectionSignal(string reason) => new()
    {
        SignalType = SignalType.NoTrade,
        Direction = TradeDirection.None,
        Strength = 0m,
        ConfidenceContribution = 0m,
        Reason = reason,
        RawDataJson = "{\"setupFingerprint\":\"fp-rej\"}"
    };

    private static StrategyEvaluationResult RejectionBacktest(string reason) => new()
    {
        StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
        StrategyName = "Adaptive",
        Evaluated = true,
        Skipped = false,
        SignalType = SignalType.NoTrade,
        Direction = TradeDirection.None,
        Strength = 0m,
        ConfidenceContribution = 0m,
        Reason = reason,
        Regime = MarketRegime.Ranging.ToString(),
        RawDataJson = "{\"setupFingerprint\":\"fp-rej\"}",
        IsValid = true
    };
}
