using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>Milestone 23.1B1C — mandatory parity evidence must fail when Lab/capture fields are missing.</summary>
public sealed class Milestone231B1CParityEvidenceTests
{
    private static readonly DateTime EvalTime = new(2026, 2, 1, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PositiveParity_EmptyCaptureCandles_Fails()
    {
        var direct = EntrySignal();
        var lab = EntryLabCandidate();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles(Array.Empty<Candle>());

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveEntryParity(direct, lab, backtest, BuildPositiveEvidence(capture)));
    }

    [Fact]
    public void PositiveParity_MismatchedExecutionCandleIds_Fails()
    {
        var candle = BuildCandle(1);
        var direct = EntrySignal();
        var lab = EntryLabCandidate();
        var backtest = EntryBacktest();
        var capture = CaptureWithCandles([candle]);
        var evidence = BuildPositiveEvidence(capture);
        evidence = new ParityAssertionHelper.PositiveParityEvidence
        {
            Capture = evidence.Capture,
            ExpectedRegime = evidence.ExpectedRegime,
            ExpectedExchangeId = evidence.ExpectedExchangeId,
            ExpectedSymbolId = evidence.ExpectedSymbolId,
            ExpectedSymbol = evidence.ExpectedSymbol,
            ExpectedTimeframe = evidence.ExpectedTimeframe,
            ExpectedHigherTimeframe = evidence.ExpectedHigherTimeframe,
            ExpectedEvaluationTimestamp = evidence.ExpectedEvaluationTimestamp,
            ExpectedCurrentCandleIndex = evidence.ExpectedCurrentCandleIndex,
            ExpectedExecutionCandleIds = [999],
            ExpectedHtfCandleIds = evidence.ExpectedHtfCandleIds
        };

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertPositiveEntryParity(direct, lab, backtest, evidence));
    }

    [Fact]
    public void RejectionParity_EmptyLabSummary_Fails()
    {
        var direct = RejectionSignal("TREND_FILTER_FAILED");
        var backtest = RejectionBacktest("TREND_FILTER_FAILED");
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertRejectionParity(direct, backtest, new ParityAssertionHelper.RejectionParityEvidence
            {
                Capture = capture,
                ExpectedRegime = MarketRegime.Ranging,
                ExpectedLabRejectionCode = "TREND_FILTER_FAILED",
                LabResultSummaryJson = "",
                ExpectedEvaluationTimestamp = EvalTime,
                ExpectedCurrentCandleIndex = 0,
                ExpectedExecutionCandleIds = [1],
                ExpectedHtfCandleIds = []
            }));
    }

    [Fact]
    public void RejectionParity_MissingFunnelCode_Fails()
    {
        var direct = RejectionSignal("NOT_IN_FUNNEL");
        var backtest = RejectionBacktest("NOT_IN_FUNNEL");
        var capture = CaptureWithCandles([BuildCandle(1)]);

        Assert.ThrowsAny<Exception>(() =>
            ParityAssertionHelper.AssertRejectionParity(direct, backtest, new ParityAssertionHelper.RejectionParityEvidence
            {
                Capture = capture,
                ExpectedRegime = MarketRegime.Ranging,
                ExpectedLabRejectionCode = "NOT_IN_FUNNEL",
                LabResultSummaryJson = "{\"rejectionFunnel\":{\"counts\":{\"OTHER_CODE\":1}}}",
                ExpectedEvaluationTimestamp = EvalTime,
                ExpectedCurrentCandleIndex = 0,
                ExpectedExecutionCandleIds = [1],
                ExpectedHtfCandleIds = []
            }));
    }

    private static ParityAssertionHelper.PositiveParityEvidence BuildPositiveEvidence(StrategyEvaluationCaptureRecord capture) =>
        new()
        {
            Capture = capture,
            ExpectedRegime = MarketRegime.Trending,
            ExpectedExchangeId = 42,
            ExpectedSymbolId = 7,
            ExpectedSymbol = "BTCUSDT",
            ExpectedTimeframe = Timeframe.M5,
            ExpectedHigherTimeframe = Timeframe.H1,
            ExpectedEvaluationTimestamp = EvalTime,
            ExpectedCurrentCandleIndex = 0,
            ExpectedExecutionCandleIds = capture.Candles.Select(c => c.Id).ToArray(),
            ExpectedHtfCandleIds = []
        };

    private static StrategyEvaluationCaptureRecord CaptureWithCandles(IReadOnlyList<Candle> candles) =>
        new(
            StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
            EvalTime,
            Timeframe.M5,
            Timeframe.H1,
            candles,
            Array.Empty<Candle>());

    private static Candle BuildCandle(long id) =>
        new()
        {
            Id = id,
            SymbolId = 7,
            ExchangeId = 42,
            Timeframe = Timeframe.M5,
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
        RawDataJson = "{\"fingerprint\":\"fp-1\",\"strength\":0.75,\"strengthBreakdown\":{\"total\":0.75}}"
    };

    private static StrategyResearchCandidate EntryLabCandidate()
    {
        var candidate = new StrategyResearchCandidate
        {
            StrategyCode = StrategyCodes.MomoAdaptiveMultiTimeframeTrendBreakout,
            Direction = TradeDirection.Long,
            ProposedEntryPrice = 100m,
            StopLoss = 99m,
            Target1 = 102m,
            SetupFingerprint = "fp-1",
            StructureJson = "{\"strength\":0.75,\"strengthBreakdown\":{\"total\":0.75}}"
        };
        return candidate;
    }

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
        RawDataJson = "{\"fingerprint\":\"fp-1\",\"strength\":0.75,\"strengthBreakdown\":{\"total\":0.75}}",
        IsValid = true
    };

    private static StrategySignalResult RejectionSignal(string reason) => new()
    {
        SignalType = SignalType.NoTrade,
        Direction = TradeDirection.None,
        Strength = 0m,
        ConfidenceContribution = 0m,
        Reason = reason,
        RawDataJson = "{\"fingerprint\":\"fp-rej\"}"
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
        RawDataJson = "{\"fingerprint\":\"fp-rej\"}",
        IsValid = true
    };
}
