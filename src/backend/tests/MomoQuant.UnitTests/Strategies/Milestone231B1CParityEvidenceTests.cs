using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>Milestone 23.1B1C2 — parity assertions must fail when Lab/capture evidence is wrong or missing.</summary>
public sealed class Milestone231B1CParityEvidenceTests
{
    private static readonly DateTime EvalTime = new(2026, 2, 1, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PositiveParity_EmptyCaptureCandles_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var lab = EntryLabEvaluation();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles(Array.Empty<Candle>());

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, lab, backtest, capture, EntryLabCandidate()));
    }

    [Fact]
    public void PositiveParity_MultipleLabEvaluations_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var lab = EntryLabEvaluation();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                directContext,
                direct,
                backtest,
                new ParityAssertionHelper.PositiveThreePathEvidence
                {
                    LabEvaluations = [lab, lab],
                    BacktestCaptures = [capture],
                    LabCandidates = [EntryLabCandidate()],
                    ExpectedStrategyCode = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
                    ExpectedStrategyVersion = "1.0.0",
                    ExpectedStrategyLabRunId = 100,
                    ExpectedRegime = MarketRegime.Trending,
                    ExpectedExchangeId = 42,
                    ExpectedSymbolId = 7,
                    ExpectedSymbol = "BTCUSDT",
                    ExpectedTimeframe = Timeframe.M5,
                    ExpectedTimeframeApi = "5m",
                    ExpectedHigherTimeframe = Timeframe.H1,
                    ExpectedEvaluationTimestamp = EvalTime,
                    ExpectedCurrentCandleIndex = 0,
                    ExpectedExecutionCandleIds = [1],
                    ExpectedHtfCandleIds = [],
                    ExpectedParameters = DefaultParameters(),
                    ExpectedIndicatorSnapshot = BuildSnapshot(1),
                    Fingerprint = new ParityAssertionHelper.FingerprintContract.RequiredPresent("fp-1"),
                    RequiredRawDataJsonProperties = ["setupFingerprint", "strength", "strengthBreakdown"],
                    RequiredStructureJsonProperties = ["strength", "strengthBreakdown"]
                }));
    }

    [Fact]
    public void PositiveParity_MismatchedExecutionCandleIds_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var lab = EntryLabEvaluation();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);
        var evidence = BuildPositiveEvidence(capture);
        evidence = CopyPositiveEvidence(evidence, expectedExecutionCandleIds: [999]);

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, lab, backtest, capture, EntryLabCandidate(), evidence));
    }

    [Fact]
    public void PositiveParity_WrongLabSignalType_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var lab = EntryLabEvaluation();
        var wrongLab = (lab.Context, RejectionSignal("TREND_FILTER_FAILED"));
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, wrongLab, backtest, capture, EntryLabCandidate()));
    }

    [Fact]
    public void PositiveParity_WrongLabReason_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var lab = EntryLabEvaluation();
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
            AssertPositive(directContext, direct, (lab.Context, wrongLabSignal), backtest, capture, EntryLabCandidate()));
    }

    [Fact]
    public void PositiveParity_WrongLabRegime_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var labContext = BuildLabContext(MarketRegime.Ranging);
        var labSignal = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, (labContext, labSignal), backtest, capture, EntryLabCandidate()));
    }

    [Fact]
    public void PositiveParity_WrongLabTimestamp_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var labContext = BuildLabContext(MarketRegime.Trending, EvalTime.AddMinutes(5));
        var labSignal = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, (labContext, labSignal), backtest, capture, EntryLabCandidate()));
    }

    [Fact]
    public void PositiveParity_WrongLabCandleIndex_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var labContext = BuildLabContext(MarketRegime.Trending, currentCandleIndex: 99);
        var labSignal = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, (labContext, labSignal), backtest, capture, EntryLabCandidate()));
    }

    [Fact]
    public void PositiveParity_WrongSymbolExchange_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var labContext = BuildLabContext(MarketRegime.Trending, exchangeId: 99, symbolId: 88, symbol: "WRONG");
        var labSignal = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1, exchangeId: 99, symbolId: 88)]);

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, (labContext, labSignal), backtest, capture, EntryLabCandidate()));
    }

    [Fact]
    public void PositiveParity_WrongExecutionTimeframe_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var labContext = BuildLabContext(MarketRegime.Trending, timeframe: Timeframe.H1);
        var labSignal = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1, timeframe: Timeframe.H1)], executionTimeframe: Timeframe.H1);

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, (labContext, labSignal), backtest, capture, EntryLabCandidate()));
    }

    [Fact]
    public void PositiveParity_WrongHigherTimeframe_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var labContext = BuildLabContext(MarketRegime.Trending, higherTimeframe: Timeframe.H4);
        var labSignal = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)], higherTimeframe: Timeframe.H4);

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, (labContext, labSignal), backtest, capture, EntryLabCandidate()));
    }

    [Fact]
    public void PositiveParity_WrongHtfCandleIds_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var labContext = BuildLabContext(MarketRegime.Trending, htfCandles: [BuildCandle(999, timeframe: Timeframe.H1)]);
        var labSignal = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)], htfCandles: [BuildCandle(999, timeframe: Timeframe.H1)]);

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, (labContext, labSignal), backtest, capture, EntryLabCandidate()));
    }

    [Fact]
    public void PositiveParity_WrongParameters_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var labContext = BuildLabContext(MarketRegime.Trending, parameters: new Dictionary<string, string> { ["x"] = "wrong" });
        var labSignal = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, (labContext, labSignal), backtest, capture, EntryLabCandidate()));
    }

    [Fact]
    public void PositiveParity_WrongIndicatorSnapshot_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var snapshot = BuildSnapshot(1);
        var wrongSnapshot = BuildSnapshot(1);
        wrongSnapshot.Ema20 = 999m;
        var labContext = BuildLabContext(MarketRegime.Trending, indicatorSnapshot: wrongSnapshot);
        var labSignal = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(
                directContext,
                direct,
                (labContext, labSignal),
                backtest,
                capture,
                EntryLabCandidate(),
                CopyPositiveEvidence(BuildPositiveEvidence(capture), expectedIndicatorSnapshot: snapshot)));
    }

    [Fact]
    public void PositiveParity_MissingFingerprint_Fails()
    {
        var directContext = BuildDirectContext();
        var rawWithoutFingerprint = "{\"strength\":0.75,\"strengthBreakdown\":{\"total\":0.75}}";
        var direct = EntrySignal(rawWithoutFingerprint);
        var lab = EntryLabEvaluation(rawWithoutFingerprint);
        var backtest = EntryBacktest(rawWithoutFingerprint);
        var capture = CaptureWithCandles([BuildCandle(1)]);
        var evidence = BuildPositiveEvidence(capture);
        evidence = CopyPositiveEvidence(
            evidence,
            fingerprint: new ParityAssertionHelper.FingerprintContract.RequiredPresent("fp-1"));

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, lab, backtest, capture, EntryLabCandidate(), evidence));
    }

    [Fact]
    public void PositiveParity_MissingStrengthBreakdown_Fails()
    {
        var rawWithoutBreakdown = "{\"setupFingerprint\":\"fp-1\",\"strength\":0.75}";
        var directContext = BuildDirectContext();
        var direct = EntrySignal(rawWithoutBreakdown);
        var lab = EntryLabEvaluation(rawWithoutBreakdown);
        var backtest = EntryBacktest(rawWithoutBreakdown);
        var candidate = EntryLabCandidate();
        candidate.StructureJson = "{\"strength\":0.75}";
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, lab, backtest, capture, candidate));
    }

    [Fact]
    public void PositiveParity_WrongDirectContextExchange_Fails()
    {
        var directContext = BuildDirectContext(exchangeId: 99);
        var direct = EntrySignal();
        var lab = EntryLabEvaluation();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, lab, backtest, capture, EntryLabCandidate()));
    }

    [Fact]
    public void PositiveParity_WrongCandidateStrategyLabRunId_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var lab = EntryLabEvaluation();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);
        var candidate = EntryLabCandidate();
        candidate.StrategyLabRunId = 999;

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, lab, backtest, capture, candidate));
    }

    [Fact]
    public void RejectionParity_EmptyLabSummary_Fails()
    {
        var directContext = BuildDirectContext(MarketRegime.Ranging);
        var direct = RejectionSignal("TREND_FILTER_FAILED");
        var lab = RejectionLabEvaluation("TREND_FILTER_FAILED");
        var backtest = RejectionBacktest("TREND_FILTER_FAILED");
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertRejectionThreePathParity(
                directContext,
                direct,
                backtest,
                new ParityAssertionHelper.RejectionThreePathEvidence
                {
                    LabEvaluations = [lab],
                    BacktestCaptures = [capture],
                    LabCandidates = [],
                    ExpectedStrategyCode = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
                    ExpectedStrategyLabRunId = 100,
                    ExpectedRegime = MarketRegime.Ranging,
                    ExpectedLabRejectionCode = "TREND_FILTER_FAILED",
                    LabResultSummaryJson = "",
                    ExpectedEvaluationTimestamp = EvalTime,
                    ExpectedCurrentCandleIndex = 0,
                    ExpectedExecutionCandleIds = [1],
                    ExpectedHtfCandleIds = [],
                    ExpectedExchangeId = 42,
                    ExpectedSymbolId = 7,
                    ExpectedSymbol = "BTCUSDT",
                    ExpectedTimeframe = Timeframe.M5,
                    ExpectedHigherTimeframe = Timeframe.H1,
                    ExpectedParameters = DefaultParameters(),
                    ExpectedIndicatorSnapshot = BuildSnapshot(1),
                    Fingerprint = new ParityAssertionHelper.FingerprintContract.RequiredPresent("fp-rej"),
                    RequiredRawDataJsonProperties = ["setupFingerprint"]
                }));
    }

    [Fact]
    public void RejectionParity_MissingFunnelCode_Fails()
    {
        var directContext = BuildDirectContext(MarketRegime.Ranging);
        var direct = RejectionSignal("NOT_IN_FUNNEL");
        var lab = RejectionLabEvaluation("NOT_IN_FUNNEL");
        var backtest = RejectionBacktest("NOT_IN_FUNNEL");
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertRejectionThreePathParity(
                directContext,
                direct,
                backtest,
                new ParityAssertionHelper.RejectionThreePathEvidence
                {
                    LabEvaluations = [lab],
                    BacktestCaptures = [capture],
                    LabCandidates = [],
                    ExpectedStrategyCode = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
                    ExpectedStrategyLabRunId = 100,
                    ExpectedRegime = MarketRegime.Ranging,
                    ExpectedLabRejectionCode = "NOT_IN_FUNNEL",
                    LabResultSummaryJson = "{\"rejectionFunnel\":{\"counts\":{\"OTHER_CODE\":1},\"evaluations\":1,\"entryConfirmed\":0}}",
                    ExpectedEvaluationTimestamp = EvalTime,
                    ExpectedCurrentCandleIndex = 0,
                    ExpectedExecutionCandleIds = [1],
                    ExpectedHtfCandleIds = [],
                    ExpectedExchangeId = 42,
                    ExpectedSymbolId = 7,
                    ExpectedSymbol = "BTCUSDT",
                    ExpectedTimeframe = Timeframe.M5,
                    ExpectedHigherTimeframe = Timeframe.H1,
                    ExpectedParameters = DefaultParameters(),
                    ExpectedIndicatorSnapshot = BuildSnapshot(1),
                    Fingerprint = new ParityAssertionHelper.FingerprintContract.RequiredPresent("fp-rej"),
                    RequiredRawDataJsonProperties = ["setupFingerprint"]
                }));
    }

    [Fact]
    public void RejectionParity_WrongLabReason_Fails()
    {
        var directContext = BuildDirectContext(MarketRegime.Ranging);
        var direct = RejectionSignal("TREND_FILTER_FAILED");
        var lab = RejectionLabEvaluation("OTHER_REASON");
        var backtest = RejectionBacktest("TREND_FILTER_FAILED");
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            AssertRejection(directContext, direct, lab, backtest, capture, "TREND_FILTER_FAILED"));
    }

    [Fact]
    public void RejectionParity_AggregateFunnelWithoutMatchingCapture_Fails()
    {
        var directContext = BuildDirectContext(MarketRegime.Ranging);
        var direct = RejectionSignal("CAPTURED_REASON");
        var lab = RejectionLabEvaluation("CAPTURED_REASON");
        var backtest = RejectionBacktest("CAPTURED_REASON");
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            AssertRejection(directContext, direct, lab, backtest, capture, "DIFFERENT_FUNNEL_CODE"));
    }

    [Fact]
    public void RejectionParity_WrongLabSignalType_Fails()
    {
        var directContext = BuildDirectContext(MarketRegime.Ranging);
        var direct = RejectionSignal("TREND_FILTER_FAILED");
        var labContext = BuildLabContext(MarketRegime.Ranging);
        var entryLabSignal = EntrySignal();
        var backtest = RejectionBacktest("TREND_FILTER_FAILED");
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            AssertRejection(directContext, direct, (labContext, entryLabSignal), backtest, capture, "TREND_FILTER_FAILED"));
    }

    [Fact]
    public void RejectionParity_NonEmptyCandidates_Fails()
    {
        var directContext = BuildDirectContext(MarketRegime.Ranging);
        var direct = RejectionSignal("TREND_FILTER_FAILED");
        var lab = RejectionLabEvaluation("TREND_FILTER_FAILED");
        var backtest = RejectionBacktest("TREND_FILTER_FAILED");
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            AssertRejection(directContext, direct, lab, backtest, capture, "TREND_FILTER_FAILED", [EntryLabCandidate()]));
    }

    [Fact]
    public void RejectionParity_RequiredAbsentFingerprintWhenPresent_Fails()
    {
        var directContext = BuildDirectContext(MarketRegime.Ranging);
        var direct = RejectionSignal("TREND_FILTER_FAILED");
        var lab = RejectionLabEvaluation("TREND_FILTER_FAILED");
        var backtest = RejectionBacktest("TREND_FILTER_FAILED");
        var capture = CaptureWithCandles([BuildCandle(1)]);
        var evidence = CopyRejectionEvidence(
            BuildRejectionEvidence(capture, "TREND_FILTER_FAILED"),
            fingerprint: new ParityAssertionHelper.FingerprintContract.RequiredAbsent());

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertRejectionThreePathParity(
                directContext,
                direct,
                backtest,
                new ParityAssertionHelper.RejectionThreePathEvidence
                {
                    LabEvaluations = [lab],
                    BacktestCaptures = [capture],
                    LabCandidates = [],
                    ExpectedStrategyCode = evidence.ExpectedStrategyCode,
                    ExpectedStrategyLabRunId = evidence.ExpectedStrategyLabRunId,
                    ExpectedRegime = evidence.ExpectedRegime,
                    ExpectedLabRejectionCode = evidence.ExpectedLabRejectionCode,
                    LabResultSummaryJson = evidence.LabResultSummaryJson,
                    ExpectedEvaluationTimestamp = evidence.ExpectedEvaluationTimestamp,
                    ExpectedCurrentCandleIndex = evidence.ExpectedCurrentCandleIndex,
                    ExpectedExecutionCandleIds = evidence.ExpectedExecutionCandleIds,
                    ExpectedHtfCandleIds = evidence.ExpectedHtfCandleIds,
                    ExpectedExchangeId = evidence.ExpectedExchangeId,
                    ExpectedSymbolId = evidence.ExpectedSymbolId,
                    ExpectedSymbol = evidence.ExpectedSymbol,
                    ExpectedTimeframe = evidence.ExpectedTimeframe,
                    ExpectedHigherTimeframe = evidence.ExpectedHigherTimeframe,
                    ExpectedParameters = evidence.ExpectedParameters,
                    ExpectedIndicatorSnapshot = evidence.ExpectedIndicatorSnapshot,
                    Fingerprint = evidence.Fingerprint,
                    RequiredRawDataJsonProperties = evidence.RequiredRawDataJsonProperties
                }));
    }

    [Fact]
    public void PositiveParity_MissingLabCapture_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);
        var evidence = BuildPositiveEvidence(capture);
        evidence = CopyPositiveEvidence(evidence, labEvaluations: []);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                directContext,
                direct,
                backtest,
                new ParityAssertionHelper.PositiveThreePathEvidence
                {
                    LabEvaluations = [],
                    BacktestCaptures = [capture],
                    LabCandidates = [EntryLabCandidate()],
                    ExpectedStrategyCode = evidence.ExpectedStrategyCode,
                    ExpectedStrategyVersion = evidence.ExpectedStrategyVersion,
                    ExpectedStrategyLabRunId = evidence.ExpectedStrategyLabRunId,
                    ExpectedRegime = evidence.ExpectedRegime,
                    ExpectedExchangeId = evidence.ExpectedExchangeId,
                    ExpectedSymbolId = evidence.ExpectedSymbolId,
                    ExpectedSymbol = evidence.ExpectedSymbol,
                    ExpectedTimeframe = evidence.ExpectedTimeframe,
                    ExpectedTimeframeApi = evidence.ExpectedTimeframeApi,
                    ExpectedHigherTimeframe = evidence.ExpectedHigherTimeframe,
                    ExpectedEvaluationTimestamp = evidence.ExpectedEvaluationTimestamp,
                    ExpectedCurrentCandleIndex = evidence.ExpectedCurrentCandleIndex,
                    ExpectedExecutionCandleIds = evidence.ExpectedExecutionCandleIds,
                    ExpectedHtfCandleIds = evidence.ExpectedHtfCandleIds,
                    ExpectedParameters = evidence.ExpectedParameters,
                    ExpectedIndicatorSnapshot = evidence.ExpectedIndicatorSnapshot,
                    Fingerprint = evidence.Fingerprint,
                    RequiredRawDataJsonProperties = evidence.RequiredRawDataJsonProperties,
                    RequiredStructureJsonProperties = evidence.RequiredStructureJsonProperties
                }));
    }

    [Fact]
    public void PositiveParity_MissingBacktestCapture_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var lab = EntryLabEvaluation();
        var backtest = EntryBacktest();

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                directContext,
                direct,
                backtest,
                new ParityAssertionHelper.PositiveThreePathEvidence
                {
                    LabEvaluations = [lab],
                    BacktestCaptures = [],
                    LabCandidates = [EntryLabCandidate()],
                    ExpectedStrategyCode = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
                    ExpectedStrategyVersion = "1.0.0",
                    ExpectedStrategyLabRunId = 100,
                    ExpectedRegime = MarketRegime.Trending,
                    ExpectedExchangeId = 42,
                    ExpectedSymbolId = 7,
                    ExpectedSymbol = "BTCUSDT",
                    ExpectedTimeframe = Timeframe.M5,
                    ExpectedTimeframeApi = "5m",
                    ExpectedHigherTimeframe = Timeframe.H1,
                    ExpectedEvaluationTimestamp = EvalTime,
                    ExpectedCurrentCandleIndex = 0,
                    ExpectedExecutionCandleIds = [1],
                    ExpectedHtfCandleIds = [],
                    ExpectedParameters = DefaultParameters(),
                    ExpectedIndicatorSnapshot = BuildSnapshot(1),
                    Fingerprint = new ParityAssertionHelper.FingerprintContract.RequiredPresent("fp-1"),
                    RequiredRawDataJsonProperties = ["setupFingerprint", "strength", "strengthBreakdown"],
                    RequiredStructureJsonProperties = ["strength", "strengthBreakdown"]
                }));
    }

    [Fact]
    public void PositiveParity_MultipleBacktestCaptures_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var lab = EntryLabEvaluation();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                directContext,
                direct,
                backtest,
                new ParityAssertionHelper.PositiveThreePathEvidence
                {
                    LabEvaluations = [lab],
                    BacktestCaptures = [capture, capture],
                    LabCandidates = [EntryLabCandidate()],
                    ExpectedStrategyCode = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
                    ExpectedStrategyVersion = "1.0.0",
                    ExpectedStrategyLabRunId = 100,
                    ExpectedRegime = MarketRegime.Trending,
                    ExpectedExchangeId = 42,
                    ExpectedSymbolId = 7,
                    ExpectedSymbol = "BTCUSDT",
                    ExpectedTimeframe = Timeframe.M5,
                    ExpectedTimeframeApi = "5m",
                    ExpectedHigherTimeframe = Timeframe.H1,
                    ExpectedEvaluationTimestamp = EvalTime,
                    ExpectedCurrentCandleIndex = 0,
                    ExpectedExecutionCandleIds = [1],
                    ExpectedHtfCandleIds = [],
                    ExpectedParameters = DefaultParameters(),
                    ExpectedIndicatorSnapshot = BuildSnapshot(1),
                    Fingerprint = new ParityAssertionHelper.FingerprintContract.RequiredPresent("fp-1"),
                    RequiredRawDataJsonProperties = ["setupFingerprint", "strength", "strengthBreakdown"],
                    RequiredStructureJsonProperties = ["strength", "strengthBreakdown"]
                }));
    }

    [Fact]
    public void PositiveParity_WrongStrategyCode_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var lab = EntryLabEvaluation();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);
        capture = capture with { StrategyCode = StrategyCode.MomoVolatilityRangeReversion };

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, lab, backtest, capture, EntryLabCandidate()));
    }

    [Fact]
    public void PositiveParity_FingerprintAbsentFromOnePath_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var lab = EntryLabEvaluation("{\"strength\":0.75,\"strengthBreakdown\":{\"total\":0.75}}");
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, lab, backtest, capture, EntryLabCandidate()));
    }

    [Fact]
    public void PositiveParity_FingerprintEmptyOnOnePath_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var lab = EntryLabEvaluation(
            "{\"setupFingerprint\":\"\",\"strength\":0.75,\"strengthBreakdown\":{\"total\":0.75}}");
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, lab, backtest, capture, EntryLabCandidate()));
    }

    [Fact]
    public void PositiveParity_DifferentFingerprintOnOnePath_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var lab = EntryLabEvaluation(
            "{\"setupFingerprint\":\"fp-OTHER\",\"strength\":0.75,\"strengthBreakdown\":{\"total\":0.75}}");
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, lab, backtest, capture, EntryLabCandidate()));
    }

    [Fact]
    public void PositiveParity_WrongFunnelCount_Fails()
    {
        var directContext = BuildDirectContext(MarketRegime.Ranging);
        var direct = RejectionSignal("TREND_FILTER_FAILED");
        var lab = RejectionLabEvaluation("TREND_FILTER_FAILED");
        var backtest = RejectionBacktest("TREND_FILTER_FAILED");
        var capture = CaptureWithCandles([BuildCandle(1)]);
        var evidence = BuildRejectionEvidence(capture, "TREND_FILTER_FAILED");

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertRejectionThreePathParity(
                directContext,
                direct,
                backtest,
                new ParityAssertionHelper.RejectionThreePathEvidence
                {
                    LabEvaluations = [lab],
                    BacktestCaptures = [capture],
                    LabCandidates = [],
                    ExpectedStrategyCode = evidence.ExpectedStrategyCode,
                    ExpectedStrategyLabRunId = evidence.ExpectedStrategyLabRunId,
                    ExpectedRegime = evidence.ExpectedRegime,
                    ExpectedLabRejectionCode = "TREND_FILTER_FAILED",
                    ExpectedFunnelCount = 99,
                    LabResultSummaryJson = evidence.LabResultSummaryJson,
                    ExpectedEvaluationTimestamp = evidence.ExpectedEvaluationTimestamp,
                    ExpectedCurrentCandleIndex = evidence.ExpectedCurrentCandleIndex,
                    ExpectedExecutionCandleIds = evidence.ExpectedExecutionCandleIds,
                    ExpectedHtfCandleIds = evidence.ExpectedHtfCandleIds,
                    ExpectedExchangeId = evidence.ExpectedExchangeId,
                    ExpectedSymbolId = evidence.ExpectedSymbolId,
                    ExpectedSymbol = evidence.ExpectedSymbol,
                    ExpectedTimeframe = evidence.ExpectedTimeframe,
                    ExpectedHigherTimeframe = evidence.ExpectedHigherTimeframe,
                    ExpectedParameters = evidence.ExpectedParameters,
                    ExpectedIndicatorSnapshot = evidence.ExpectedIndicatorSnapshot,
                    Fingerprint = evidence.Fingerprint,
                    RequiredRawDataJsonProperties = evidence.RequiredRawDataJsonProperties
                }));
    }

    [Fact]
    public void PositiveParity_MissingLabCandidate_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var lab = EntryLabEvaluation();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                directContext,
                direct,
                backtest,
                new ParityAssertionHelper.PositiveThreePathEvidence
                {
                    LabEvaluations = [lab],
                    BacktestCaptures = [capture],
                    LabCandidates = [],
                    ExpectedStrategyCode = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
                    ExpectedStrategyVersion = "1.0.0",
                    ExpectedStrategyLabRunId = 100,
                    ExpectedRegime = MarketRegime.Trending,
                    ExpectedExchangeId = 42,
                    ExpectedSymbolId = 7,
                    ExpectedSymbol = "BTCUSDT",
                    ExpectedTimeframe = Timeframe.M5,
                    ExpectedTimeframeApi = "5m",
                    ExpectedHigherTimeframe = Timeframe.H1,
                    ExpectedEvaluationTimestamp = EvalTime,
                    ExpectedCurrentCandleIndex = 0,
                    ExpectedExecutionCandleIds = [1],
                    ExpectedHtfCandleIds = [],
                    ExpectedParameters = DefaultParameters(),
                    ExpectedIndicatorSnapshot = BuildSnapshot(1),
                    Fingerprint = new ParityAssertionHelper.FingerprintContract.RequiredPresent("fp-1"),
                    RequiredRawDataJsonProperties = ["setupFingerprint", "strength", "strengthBreakdown"],
                    RequiredStructureJsonProperties = ["strength", "strengthBreakdown"]
                }));
    }

    [Fact]
    public void PositiveParity_MultipleLabCandidates_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var lab = EntryLabEvaluation();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveThreePathParity(
                directContext,
                direct,
                backtest,
                new ParityAssertionHelper.PositiveThreePathEvidence
                {
                    LabEvaluations = [lab],
                    BacktestCaptures = [capture],
                    LabCandidates = [EntryLabCandidate(), EntryLabCandidate()],
                    ExpectedStrategyCode = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
                    ExpectedStrategyVersion = "1.0.0",
                    ExpectedStrategyLabRunId = 100,
                    ExpectedRegime = MarketRegime.Trending,
                    ExpectedExchangeId = 42,
                    ExpectedSymbolId = 7,
                    ExpectedSymbol = "BTCUSDT",
                    ExpectedTimeframe = Timeframe.M5,
                    ExpectedTimeframeApi = "5m",
                    ExpectedHigherTimeframe = Timeframe.H1,
                    ExpectedEvaluationTimestamp = EvalTime,
                    ExpectedCurrentCandleIndex = 0,
                    ExpectedExecutionCandleIds = [1],
                    ExpectedHtfCandleIds = [],
                    ExpectedParameters = DefaultParameters(),
                    ExpectedIndicatorSnapshot = BuildSnapshot(1),
                    Fingerprint = new ParityAssertionHelper.FingerprintContract.RequiredPresent("fp-1"),
                    RequiredRawDataJsonProperties = ["setupFingerprint", "strength", "strengthBreakdown"],
                    RequiredStructureJsonProperties = ["strength", "strengthBreakdown"]
                }));
    }

    [Fact]
    public void PositiveParity_WrongCandidateStrategyCode_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var lab = EntryLabEvaluation();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);
        var candidate = EntryLabCandidate();
        candidate.StrategyCode = StrategyCodes.MomoVolatilityRangeReversion;

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, lab, backtest, capture, candidate));
    }

    [Fact]
    public void PositiveParity_AdditionalParameter_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var labContext = BuildLabContext(
            MarketRegime.Trending,
            parameters: new Dictionary<string, string>
            {
                ["__seenFingerprints"] = "[]",
                ["extra"] = "1"
            });
        var lab = (labContext, EntrySignal());
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, lab, backtest, capture, EntryLabCandidate()));
    }

    [Fact]
    public void PositiveParity_WrongLtfCandleContent_Fails()
    {
        var directContext = BuildDirectContext();
        var direct = EntrySignal();
        var lab = EntryLabEvaluation();
        var backtest = EntryBacktest();
        var wrongCandle = BuildCandle(1);
        wrongCandle.Close = 999m;
        var capture = CaptureWithCandles([wrongCandle]);

        Assert.ThrowsAny<Exception>(() =>
            AssertPositive(directContext, direct, lab, backtest, capture, EntryLabCandidate()));
    }

    [Fact]
    public void RejectionParity_FunnelEvaluationsMismatch_Fails()
    {
        var directContext = BuildDirectContext(MarketRegime.Ranging);
        var direct = RejectionSignal("TREND_FILTER_FAILED");
        var lab = RejectionLabEvaluation("TREND_FILTER_FAILED");
        var backtest = RejectionBacktest("TREND_FILTER_FAILED");
        var capture = CaptureWithCandles([BuildCandle(1)]);
        var evidence = BuildRejectionEvidence(capture, "TREND_FILTER_FAILED");

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertRejectionThreePathParity(
                directContext,
                direct,
                backtest,
                new ParityAssertionHelper.RejectionThreePathEvidence
                {
                    LabEvaluations = [lab],
                    BacktestCaptures = [capture],
                    LabCandidates = [],
                    ExpectedStrategyCode = evidence.ExpectedStrategyCode,
                    ExpectedStrategyLabRunId = evidence.ExpectedStrategyLabRunId,
                    ExpectedRegime = evidence.ExpectedRegime,
                    ExpectedLabRejectionCode = "TREND_FILTER_FAILED",
                    LabResultSummaryJson =
                        "{\"rejectionFunnel\":{\"counts\":{\"TREND_FILTER_FAILED\":1},\"evaluations\":5,\"entryConfirmed\":0}}",
                    ExpectedEvaluationTimestamp = evidence.ExpectedEvaluationTimestamp,
                    ExpectedCurrentCandleIndex = evidence.ExpectedCurrentCandleIndex,
                    ExpectedExecutionCandleIds = evidence.ExpectedExecutionCandleIds,
                    ExpectedHtfCandleIds = evidence.ExpectedHtfCandleIds,
                    ExpectedExchangeId = evidence.ExpectedExchangeId,
                    ExpectedSymbolId = evidence.ExpectedSymbolId,
                    ExpectedSymbol = evidence.ExpectedSymbol,
                    ExpectedTimeframe = evidence.ExpectedTimeframe,
                    ExpectedHigherTimeframe = evidence.ExpectedHigherTimeframe,
                    ExpectedParameters = evidence.ExpectedParameters,
                    ExpectedIndicatorSnapshot = evidence.ExpectedIndicatorSnapshot,
                    Fingerprint = evidence.Fingerprint,
                    RequiredRawDataJsonProperties = evidence.RequiredRawDataJsonProperties
                }));
    }

    private static void AssertPositive(
        StrategyContext directContext,
        StrategySignalResult direct,
        (StrategyContext Context, StrategySignalResult Result) lab,
        StrategyEvaluationResult backtest,
        StrategyEvaluationCaptureRecord capture,
        StrategyResearchCandidate candidate,
        ParityAssertionHelper.PositiveThreePathEvidence? evidenceOverride = null)
    {
        var baseEvidence = evidenceOverride ?? BuildPositiveEvidence(capture);
        var evidence = new ParityAssertionHelper.PositiveThreePathEvidence
        {
            LabEvaluations = [lab],
            BacktestCaptures = [capture],
            LabCandidates = [candidate],
            ExpectedStrategyCode = baseEvidence.ExpectedStrategyCode,
            ExpectedStrategyVersion = baseEvidence.ExpectedStrategyVersion,
            ExpectedStrategyLabRunId = baseEvidence.ExpectedStrategyLabRunId,
            ExpectedRegime = baseEvidence.ExpectedRegime,
            ExpectedExchangeId = baseEvidence.ExpectedExchangeId,
            ExpectedSymbolId = baseEvidence.ExpectedSymbolId,
            ExpectedSymbol = baseEvidence.ExpectedSymbol,
            ExpectedTimeframe = baseEvidence.ExpectedTimeframe,
            ExpectedTimeframeApi = baseEvidence.ExpectedTimeframeApi,
            ExpectedHigherTimeframe = baseEvidence.ExpectedHigherTimeframe,
            ExpectedEvaluationTimestamp = baseEvidence.ExpectedEvaluationTimestamp,
            ExpectedCurrentCandleIndex = baseEvidence.ExpectedCurrentCandleIndex,
            ExpectedExecutionCandleIds = baseEvidence.ExpectedExecutionCandleIds,
            ExpectedHtfCandleIds = baseEvidence.ExpectedHtfCandleIds,
            ExpectedParameters = baseEvidence.ExpectedParameters,
            ExpectedIndicatorSnapshot = baseEvidence.ExpectedIndicatorSnapshot,
            Fingerprint = baseEvidence.Fingerprint,
            RequiredRawDataJsonProperties = baseEvidence.RequiredRawDataJsonProperties,
            RequiredStructureJsonProperties = baseEvidence.RequiredStructureJsonProperties,
            ExpectedCandidateStatus = baseEvidence.ExpectedCandidateStatus
        };
        ParityAssertionHelper.AssertPositiveThreePathParity(directContext, direct, backtest, evidence);
    }

    private static void AssertRejection(
        StrategyContext directContext,
        StrategySignalResult direct,
        (StrategyContext Context, StrategySignalResult Result) lab,
        StrategyEvaluationResult backtest,
        StrategyEvaluationCaptureRecord capture,
        string rejectionCode,
        IReadOnlyList<StrategyResearchCandidate>? labCandidates = null)
    {
        var baseEvidence = BuildRejectionEvidence(capture, rejectionCode);
        ParityAssertionHelper.AssertRejectionThreePathParity(
            directContext,
            direct,
            backtest,
            new ParityAssertionHelper.RejectionThreePathEvidence
            {
                LabEvaluations = [lab],
                BacktestCaptures = [capture],
                LabCandidates = labCandidates ?? [],
                ExpectedStrategyCode = baseEvidence.ExpectedStrategyCode,
                ExpectedStrategyLabRunId = baseEvidence.ExpectedStrategyLabRunId,
                ExpectedRegime = baseEvidence.ExpectedRegime,
                ExpectedLabRejectionCode = baseEvidence.ExpectedLabRejectionCode,
                LabResultSummaryJson = baseEvidence.LabResultSummaryJson,
                ExpectedEvaluationTimestamp = baseEvidence.ExpectedEvaluationTimestamp,
                ExpectedCurrentCandleIndex = baseEvidence.ExpectedCurrentCandleIndex,
                ExpectedExecutionCandleIds = baseEvidence.ExpectedExecutionCandleIds,
                ExpectedHtfCandleIds = baseEvidence.ExpectedHtfCandleIds,
                ExpectedExchangeId = baseEvidence.ExpectedExchangeId,
                ExpectedSymbolId = baseEvidence.ExpectedSymbolId,
                ExpectedSymbol = baseEvidence.ExpectedSymbol,
                ExpectedTimeframe = baseEvidence.ExpectedTimeframe,
                ExpectedHigherTimeframe = baseEvidence.ExpectedHigherTimeframe,
                ExpectedParameters = baseEvidence.ExpectedParameters,
                ExpectedIndicatorSnapshot = baseEvidence.ExpectedIndicatorSnapshot,
                Fingerprint = baseEvidence.Fingerprint,
                RequiredRawDataJsonProperties = baseEvidence.RequiredRawDataJsonProperties
            });
    }

    private static ParityAssertionHelper.PositiveThreePathEvidence BuildPositiveEvidence(StrategyEvaluationCaptureRecord capture) =>
        new()
        {
            LabEvaluations = [],
            BacktestCaptures = [],
            LabCandidates = [],
            ExpectedStrategyCode = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
            ExpectedStrategyVersion = "1.0.0",
            ExpectedStrategyLabRunId = 100,
            ExpectedRegime = MarketRegime.Trending,
            ExpectedExchangeId = 42,
            ExpectedSymbolId = 7,
            ExpectedSymbol = "BTCUSDT",
            ExpectedTimeframe = Timeframe.M5,
            ExpectedTimeframeApi = "5m",
            ExpectedHigherTimeframe = Timeframe.H1,
            ExpectedEvaluationTimestamp = EvalTime,
            ExpectedCurrentCandleIndex = 0,
            ExpectedExecutionCandleIds = capture.Candles.Select(c => c.Id).ToArray(),
            ExpectedHtfCandleIds = [],
            ExpectedParameters = DefaultParameters(),
            ExpectedIndicatorSnapshot = BuildSnapshot(capture.Candles.FirstOrDefault()?.Id ?? 1),
            Fingerprint = new ParityAssertionHelper.FingerprintContract.RequiredPresent("fp-1"),
            RequiredRawDataJsonProperties = ["setupFingerprint", "strength", "strengthBreakdown"],
            RequiredStructureJsonProperties = ["strength", "strengthBreakdown"]
        };

    private static ParityAssertionHelper.RejectionThreePathEvidence BuildRejectionEvidence(
        StrategyEvaluationCaptureRecord capture,
        string rejectionCode) =>
        new()
        {
            LabEvaluations = [],
            BacktestCaptures = [],
            LabCandidates = [],
            ExpectedStrategyCode = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
            ExpectedStrategyLabRunId = 100,
            ExpectedRegime = MarketRegime.Ranging,
            ExpectedLabRejectionCode = rejectionCode,
            LabResultSummaryJson = $"{{\"rejectionFunnel\":{{\"counts\":{{\"{rejectionCode}\":1}},\"evaluations\":1,\"entryConfirmed\":0}}}}",
            ExpectedEvaluationTimestamp = EvalTime,
            ExpectedCurrentCandleIndex = 0,
            ExpectedExecutionCandleIds = capture.Candles.Select(c => c.Id).ToArray(),
            ExpectedHtfCandleIds = [],
            ExpectedExchangeId = 42,
            ExpectedSymbolId = 7,
            ExpectedSymbol = "BTCUSDT",
            ExpectedTimeframe = Timeframe.M5,
            ExpectedHigherTimeframe = Timeframe.H1,
            ExpectedParameters = DefaultParameters(),
            ExpectedIndicatorSnapshot = BuildSnapshot(capture.Candles.FirstOrDefault()?.Id ?? 1),
            Fingerprint = new ParityAssertionHelper.FingerprintContract.RequiredPresent("fp-rej"),
            RequiredRawDataJsonProperties = ["setupFingerprint"]
        };

    private static ParityAssertionHelper.PositiveThreePathEvidence CopyPositiveEvidence(
        ParityAssertionHelper.PositiveThreePathEvidence source,
        long[]? expectedExecutionCandleIds = null,
        IndicatorSnapshot? expectedIndicatorSnapshot = null,
        ParityAssertionHelper.FingerprintContract? fingerprint = null,
        IReadOnlyList<(StrategyContext Context, StrategySignalResult Result)>? labEvaluations = null) =>
        new()
        {
            LabEvaluations = labEvaluations ?? source.LabEvaluations,
            BacktestCaptures = source.BacktestCaptures,
            LabCandidates = source.LabCandidates,
            ExpectedStrategyCode = source.ExpectedStrategyCode,
            ExpectedStrategyVersion = source.ExpectedStrategyVersion,
            ExpectedStrategyLabRunId = source.ExpectedStrategyLabRunId,
            ExpectedRegime = source.ExpectedRegime,
            ExpectedExchangeId = source.ExpectedExchangeId,
            ExpectedSymbolId = source.ExpectedSymbolId,
            ExpectedSymbol = source.ExpectedSymbol,
            ExpectedTimeframe = source.ExpectedTimeframe,
            ExpectedTimeframeApi = source.ExpectedTimeframeApi,
            ExpectedHigherTimeframe = source.ExpectedHigherTimeframe,
            ExpectedEvaluationTimestamp = source.ExpectedEvaluationTimestamp,
            ExpectedCurrentCandleIndex = source.ExpectedCurrentCandleIndex,
            ExpectedExecutionCandleIds = expectedExecutionCandleIds ?? source.ExpectedExecutionCandleIds,
            ExpectedHtfCandleIds = source.ExpectedHtfCandleIds,
            ExpectedParameters = source.ExpectedParameters,
            ExpectedIndicatorSnapshot = expectedIndicatorSnapshot ?? source.ExpectedIndicatorSnapshot,
            Fingerprint = fingerprint ?? source.Fingerprint,
            RequiredRawDataJsonProperties = source.RequiredRawDataJsonProperties,
            RequiredStructureJsonProperties = source.RequiredStructureJsonProperties
        };

    private static ParityAssertionHelper.RejectionThreePathEvidence CopyRejectionEvidence(
        ParityAssertionHelper.RejectionThreePathEvidence source,
        ParityAssertionHelper.FingerprintContract? fingerprint = null) =>
        new()
        {
            LabEvaluations = source.LabEvaluations,
            BacktestCaptures = source.BacktestCaptures,
            LabCandidates = source.LabCandidates,
            ExpectedStrategyCode = source.ExpectedStrategyCode,
            ExpectedStrategyLabRunId = source.ExpectedStrategyLabRunId,
            ExpectedRegime = source.ExpectedRegime,
            ExpectedLabRejectionCode = source.ExpectedLabRejectionCode,
            LabResultSummaryJson = source.LabResultSummaryJson,
            ExpectedEvaluationTimestamp = source.ExpectedEvaluationTimestamp,
            ExpectedCurrentCandleIndex = source.ExpectedCurrentCandleIndex,
            ExpectedExecutionCandleIds = source.ExpectedExecutionCandleIds,
            ExpectedHtfCandleIds = source.ExpectedHtfCandleIds,
            ExpectedExchangeId = source.ExpectedExchangeId,
            ExpectedSymbolId = source.ExpectedSymbolId,
            ExpectedSymbol = source.ExpectedSymbol,
            ExpectedTimeframe = source.ExpectedTimeframe,
            ExpectedHigherTimeframe = source.ExpectedHigherTimeframe,
            ExpectedParameters = source.ExpectedParameters,
            ExpectedIndicatorSnapshot = source.ExpectedIndicatorSnapshot,
            Fingerprint = fingerprint ?? source.Fingerprint,
            RequiredRawDataJsonProperties = source.RequiredRawDataJsonProperties
        };

    private static StrategyContext BuildDirectContext(
        MarketRegime regime = MarketRegime.Trending,
        long exchangeId = 42) =>
        BuildLabContext(regime, exchangeId: exchangeId);

    private static (StrategyContext Context, StrategySignalResult Result) EntryLabEvaluation(string? rawDataJson = null) =>
        (BuildLabContext(MarketRegime.Trending), EntrySignal(rawDataJson));

    private static (StrategyContext Context, StrategySignalResult Result) RejectionLabEvaluation(string reason) =>
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
        Timeframe executionTimeframe = Timeframe.M5,
        IReadOnlyList<Candle>? htfCandles = null,
        MarketRegime regime = MarketRegime.Trending) =>
        new(
            StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
            EvalTime,
            executionTimeframe,
            higherTimeframe,
            candles,
            htfCandles ?? Array.Empty<Candle>(),
            ExchangeId: candles.FirstOrDefault()?.ExchangeId ?? 42,
            SymbolId: candles.FirstOrDefault()?.SymbolId ?? 7,
            Symbol: "BTCUSDT",
            MarketRegime: regime,
            CurrentCandleIndex: 0,
            IndicatorSnapshot: BuildSnapshot(candles.FirstOrDefault()?.Id ?? 1),
            StrategyParameters: new Dictionary<string, string>(DefaultParameters()));

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

    private static StrategySignalResult EntrySignal(string? rawDataJson = null) => new()
    {
        SignalType = SignalType.Entry,
        Direction = TradeDirection.Long,
        EntryPrice = 100m,
        SuggestedStopLoss = 99m,
        SuggestedTakeProfit = 102m,
        Strength = 0.75m,
        ConfidenceContribution = 0.5m,
        Reason = "ENTRY",
        RawDataJson = rawDataJson
            ?? "{\"setupFingerprint\":\"fp-1\",\"strength\":0.75,\"strengthBreakdown\":{\"total\":0.75}}"
    };

    private static StrategyResearchCandidate EntryLabCandidate() =>
        new()
        {
            StrategyLabRunId = 100,
            StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
            StrategyVersion = "1.0.0",
            ExchangeId = 42,
            SymbolId = 7,
            Symbol = "BTCUSDT",
            Timeframe = "5m",
            Direction = TradeDirection.Long,
            SetupDetectedAtUtc = EvalTime,
            ProposedEntryTimeUtc = EvalTime,
            ProposedEntryPrice = 100m,
            StopLoss = 99m,
            Target1 = 102m,
            RewardRisk = 2m,
            CandidateStatus = StrategyResearchCandidateStatus.Detected,
            StrategyReason = "ENTRY",
            SetupFingerprint = "fp-1",
            ParametersJson = "{\"__seenFingerprints\":\"[]\"}",
            StructureJson = "{\"strength\":0.75,\"strengthBreakdown\":{\"total\":0.75}}"
        };

    private static StrategyEvaluationResult EntryBacktest(string? rawDataJson = null) => new()
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
        RawDataJson = rawDataJson
            ?? "{\"setupFingerprint\":\"fp-1\",\"strength\":0.75,\"strengthBreakdown\":{\"total\":0.75}}",
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
