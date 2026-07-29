using System.Text.Json;
using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Ai;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Backtesting.Simulation;
using MomoQuant.Application.Risk;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.Models;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Shared fixture helpers for Milestone 23.1A1C closed-HTF production-path capture suites.
/// </summary>
internal static class ClosedHtfCaptureHarness
{
    public const long SymbolId = 1;
    public const string SymbolName = "BTCUSDT";
    public const Timeframe ExecutionTimeframe = Timeframe.M5;
    public const Timeframe HigherTimeframe = Timeframe.H1;

    public sealed class RecordingStrategyEngine : IStrategyEngine
    {
        private readonly StrategyEngine _inner;

        public RecordingStrategyEngine(StrategyEvaluationCaptureRecording capture)
        {
            Capture = capture;
            _inner = new StrategyEngine(capture);
        }

        public StrategyEvaluationCaptureRecording Capture { get; }

        public List<StrategyEvaluationResult> Results { get; } = [];

        public async Task<IReadOnlyList<StrategyEvaluationResult>> EvaluateAsync(
            IReadOnlyCollection<ITradingStrategy> strategies,
            StrategyContext context,
            CancellationToken cancellationToken = default)
        {
            var results = await _inner.EvaluateAsync(strategies, context, cancellationToken);
            Results.AddRange(results);
            return results;
        }

        public void Clear()
        {
            Capture.Clear();
            Results.Clear();
        }
    }

    public sealed class Fixture
    {
        public required List<Candle> LtfCandles { get; init; }
        public required List<Candle> CleanHtfCandles { get; init; }
        public required DateTime EvaluationTimeUtc { get; init; }
        public required int EvaluationCandleIndex { get; init; }
        public required Candle EvaluationCandle { get; init; }
        public required Dictionary<long, IndicatorSnapshot> IndicatorSnapshots { get; init; }
        public required PreparedStrategy Prepared { get; init; }
    }

    public static Fixture CreateAdaptiveFixture(int ltfCount = 220, int htfCount = 210)
    {
        var ltf = BuildCandles(ltfCount, ExecutionTimeframe, idOffset: 1);
        var evaluationIndex = ltf.Count - 1;
        var evaluationCandle = ltf[evaluationIndex];
        var evaluationTimeUtc = evaluationCandle.CloseTimeUtc;

        var htf = BuildCandles(htfCount, HigherTimeframe, idOffset: 10_000);
        for (var i = 0; i < htf.Count; i++)
        {
            htf[i].CloseTimeUtc = evaluationTimeUtc.AddHours(-(htf.Count - i));
            htf[i].OpenTimeUtc = htf[i].CloseTimeUtc.AddHours(-1);
            htf[i].IsClosed = true;
        }

        var snapshots = new Dictionary<long, IndicatorSnapshot>
        {
            [evaluationCandle.Id] = new IndicatorSnapshot
            {
                SymbolId = SymbolId,
                Timeframe = ExecutionTimeframe,
                CandleId = evaluationCandle.Id,
                Ema20 = evaluationCandle.Close + 2m,
                Ema50 = evaluationCandle.Close + 1m,
                Ema200 = evaluationCandle.Close,
                Atr14 = 1.5m,
                CalculatedAtUtc = evaluationTimeUtc,
                CreatedAtUtc = evaluationTimeUtc,
                MarketStructure = MarketStructure.Bullish
            }
        };

        return new Fixture
        {
            LtfCandles = ltf,
            CleanHtfCandles = htf,
            EvaluationTimeUtc = evaluationTimeUtc,
            EvaluationCandleIndex = evaluationIndex,
            EvaluationCandle = evaluationCandle,
            IndicatorSnapshots = snapshots,
            Prepared = CreateAdaptivePrepared()
        };
    }

    public static PreparedStrategy CreateAdaptivePrepared() => new()
    {
        Strategy = new Strategy
        {
            Id = 42,
            Code = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
            Name = "MOMO Adaptive Multi-Timeframe Trend Breakout",
            IsEnabled = true,
            Version = MomoAdaptiveMultiTimeframeTrendBreakoutStrategy.Version
        },
        Plugin = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy()
    };

    public static BacktestDataset CreateDataset(Fixture fixture, IReadOnlyList<Candle> htfSeries) => new()
    {
        SymbolId = SymbolId,
        SymbolName = SymbolName,
        Timeframe = ExecutionTimeframe,
        Candles = fixture.LtfCandles,
        IndicatorSnapshots = fixture.IndicatorSnapshots,
        EvaluationIndices = [fixture.EvaluationCandleIndex],
        HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>
        {
            [HigherTimeframe] = htfSeries
        }
    };

    public static List<Candle> PolluteHtf(IReadOnlyList<Candle> cleanHtf, DateTime evaluationTimeUtc)
    {
        var polluted = cleanHtf.ToList();
        polluted.Add(new Candle
        {
            Id = 90_001,
            SymbolId = SymbolId,
            Timeframe = HigherTimeframe,
            OpenTimeUtc = evaluationTimeUtc.AddMinutes(-10),
            CloseTimeUtc = evaluationTimeUtc.AddMinutes(50),
            Open = 88888m,
            High = 90000m,
            Low = 87000m,
            Close = 89000m,
            Volume = 8888m,
            IsClosed = false
        });
        polluted.Add(new Candle
        {
            Id = 90_002,
            SymbolId = SymbolId,
            Timeframe = HigherTimeframe,
            OpenTimeUtc = evaluationTimeUtc.AddHours(1),
            CloseTimeUtc = evaluationTimeUtc.AddHours(2),
            Open = 99999m,
            High = 100000m,
            Low = 99000m,
            Close = 99500m,
            Volume = 9999m,
            IsClosed = true
        });
        return polluted;
    }

    public static BacktestContext CreateBacktestContext() => new()
    {
        BacktestRunId = 1,
        TradingSessionId = 1,
        ExchangeId = 1,
        RiskProfileId = 1,
        Settings = new RunBacktestSettings
        {
            Name = "ClosedHtf Capture",
            SymbolIds = [SymbolId],
            Timeframes = [ExecutionTimeframe],
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            InitialBalance = 10_000m,
            StrategyIds = [42],
            ExecutionMode = ExecutionMode.MarketFill,
            MakerFeeRate = 0.0002m,
            TakerFeeRate = 0.0005m,
            OrderExpiryCandles = 3,
            UseAiScoring = false,
            MinConfidenceScore = 0m,
            SlippagePercent = 0m
        },
        RiskRules = [],
        Strategies = [],
        Symbols = new Dictionary<long, Symbol>
        {
            [SymbolId] = new Symbol
            {
                Id = SymbolId,
                ExchangeId = 1,
                SymbolName = SymbolName
            }
        },
        Balance = 10_000m,
        PeakEquity = 10_000m
    };

    public static IHigherTimeframeDatasetEnricher CreatePassthroughEnricher()
    {
        var enricher = new Mock<IHigherTimeframeDatasetEnricher>();
        enricher.Setup(e => e.EnrichForStrategiesAsync(
                It.IsAny<BacktestDataset>(),
                It.IsAny<IReadOnlyList<PreparedStrategy>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BacktestDataset dataset, IReadOnlyList<PreparedStrategy> _, CancellationToken _) => dataset);
        enricher.Setup(e => e.EnrichAsync(
                It.IsAny<BacktestDataset>(),
                It.IsAny<IReadOnlyCollection<Timeframe>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BacktestDataset dataset, IReadOnlyCollection<Timeframe> _, CancellationToken _) => dataset);
        return enricher.Object;
    }

    public static IStrategyParameterProvider CreateAdaptiveParameterProvider()
    {
        var provider = new Mock<IStrategyParameterProvider>();
        provider.Setup(p => p.GetParametersAsync(
                It.IsAny<long>(),
                It.IsAny<Timeframe>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract());
        return provider.Object;
    }

    public static IRiskEngine CreateApprovingRiskEngine()
    {
        var risk = new Mock<IRiskEngine>();
        risk.Setup(r => r.Evaluate(It.IsAny<RiskContext>()))
            .Returns(new RiskEvaluationResult
            {
                Decision = RiskDecisionType.Approved,
                Reason = "Approved",
                PositionSize = 1m,
                StopLoss = 95m,
                TakeProfit = 110m,
                ApprovedRiskPercent = 1m
            });
        return risk.Object;
    }

    public static ISimulatedExecutionProvider CreateNoopExecutionProvider()
    {
        var execution = new Mock<ISimulatedExecutionProvider>();
        execution.Setup(p => p.ProcessPendingMarketFills(
            It.IsAny<BacktestContext>(),
            It.IsAny<IReadOnlyList<Candle>>(),
            It.IsAny<int>()));
        execution.Setup(p => p.ProcessPendingMakerOrders(
            It.IsAny<BacktestContext>(),
            It.IsAny<Candle>(),
            It.IsAny<int>()));
        execution.Setup(p => p.UpdateOpenPositions(It.IsAny<BacktestContext>(), It.IsAny<Candle>()));
        execution.Setup(p => p.FinalizePendingOrders(
            It.IsAny<BacktestContext>(),
            It.IsAny<Candle>(),
            It.IsAny<int>()));
        return execution.Object;
    }

    public static BacktestEngine CreateBacktestEngine(RecordingStrategyEngine strategyEngine) =>
        new(
            strategyEngine,
            CreateAdaptiveParameterProvider(),
            CreateApprovingRiskEngine(),
            new Mock<IAiIntegrationService>().Object,
            CreateNoopExecutionProvider(),
            CreatePassthroughEnricher());

    public static void AssertCaptureClosedOnly(
        StrategyEvaluationCaptureRecord record,
        DateTime evaluationTimeUtc)
    {
        Assert.Equal(StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout, record.StrategyCode);
        Assert.Equal(ExecutionTimeframe, record.ExecutionTimeframe);
        Assert.Equal(HigherTimeframe, record.HigherTimeframe);
        Assert.Equal(evaluationTimeUtc, record.EvaluatedAtUtc);
        Assert.NotEmpty(record.HigherTimeframeCandles);
        // Adaptive HTF warmup: htfSlowEmaPeriod(200) + htfSlopeLookback(5) = 205
        Assert.True(record.HigherTimeframeCandles.Count >= 205);
        Assert.All(record.HigherTimeframeCandles, candle =>
        {
            Assert.True(candle.IsClosed);
            Assert.True(candle.CloseTimeUtc <= evaluationTimeUtc);
        });
        Assert.DoesNotContain(record.HigherTimeframeCandles, c => c.Id is 90_001 or 90_002);
        Assert.DoesNotContain(record.HigherTimeframeCandles, c => !c.IsClosed);
        Assert.DoesNotContain(record.HigherTimeframeCandles, c => c.CloseTimeUtc > evaluationTimeUtc);
    }

    public static void AssertIdenticalCaptures(
        StrategyEvaluationCaptureRecord clean,
        StrategyEvaluationCaptureRecord polluted)
    {
        Assert.Equal(clean.StrategyCode, polluted.StrategyCode);
        Assert.Equal(clean.EvaluatedAtUtc, polluted.EvaluatedAtUtc);
        Assert.Equal(clean.ExecutionTimeframe, polluted.ExecutionTimeframe);
        Assert.Equal(clean.HigherTimeframe, polluted.HigherTimeframe);
        Assert.Equal(clean.HigherTimeframeCandles.Count, polluted.HigherTimeframeCandles.Count);
        for (var i = 0; i < clean.HigherTimeframeCandles.Count; i++)
        {
            var a = clean.HigherTimeframeCandles[i];
            var b = polluted.HigherTimeframeCandles[i];
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.OpenTimeUtc, b.OpenTimeUtc);
            Assert.Equal(a.CloseTimeUtc, b.CloseTimeUtc);
            Assert.Equal(a.Open, b.Open);
            Assert.Equal(a.High, b.High);
            Assert.Equal(a.Low, b.Low);
            Assert.Equal(a.Close, b.Close);
            Assert.Equal(a.IsClosed, b.IsClosed);
        }
    }

    public static void AssertIdenticalEvaluationOutcomes(
        StrategyEvaluationResult clean,
        StrategyEvaluationResult polluted)
    {
        Assert.Equal(clean.SignalType, polluted.SignalType);
        Assert.Equal(clean.Skipped, polluted.Skipped);
        Assert.Equal(clean.SkipReason, polluted.SkipReason);
        Assert.Equal(clean.Reason, polluted.Reason);
        Assert.Equal(clean.Direction, polluted.Direction);
        Assert.Equal(clean.Strength, polluted.Strength);
        Assert.Equal(clean.EntryPrice, polluted.EntryPrice);
        Assert.Equal(clean.SuggestedStopLoss, polluted.SuggestedStopLoss);
        Assert.Equal(clean.SuggestedTakeProfit, polluted.SuggestedTakeProfit);
        Assert.Equal(clean.RawDataJson, polluted.RawDataJson);
        Assert.Equal(ExtractFingerprint(clean.RawDataJson), ExtractFingerprint(polluted.RawDataJson));
        Assert.Equal(ExtractStrengthBreakdown(clean.RawDataJson), ExtractStrengthBreakdown(polluted.RawDataJson));
    }

    public static void AssertMissingHtfCapture(StrategyEvaluationCaptureRecord record, DateTime evaluationTimeUtc)
    {
        Assert.Equal(evaluationTimeUtc, record.EvaluatedAtUtc);
        Assert.Empty(record.HigherTimeframeCandles);
    }

    public static void AssertMtfDataUnavailable(StrategyEvaluationResult result)
    {
        Assert.Contains(
            MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable,
            result.Reason ?? result.SkipReason ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    public static BacktestDataset CreateDatasetMissingHtf(Fixture fixture) => new()
    {
        SymbolId = SymbolId,
        SymbolName = SymbolName,
        Timeframe = ExecutionTimeframe,
        Candles = fixture.LtfCandles,
        IndicatorSnapshots = fixture.IndicatorSnapshots,
        EvaluationIndices = [fixture.EvaluationCandleIndex],
        HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>()
    };

    public static void AssertCleanVsPollutedRun(
        RecordingStrategyEngine engine,
        DateTime evaluationTimeUtc,
        StrategyEvaluationCaptureRecord cleanCapture,
        StrategyEvaluationResult cleanResult)
    {
        Assert.Single(engine.Capture.Records);
        Assert.Single(engine.Results);
        var pollutedCapture = engine.Capture.Records[0];
        AssertCaptureClosedOnly(pollutedCapture, evaluationTimeUtc);
        AssertIdenticalCaptures(cleanCapture, pollutedCapture);
        AssertIdenticalEvaluationOutcomes(cleanResult, engine.Results[0]);
    }

    private static string? ExtractFingerprint(string? rawDataJson)
    {
        if (string.IsNullOrWhiteSpace(rawDataJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(rawDataJson);
        return document.RootElement.TryGetProperty("setupFingerprint", out var fingerprint)
            ? fingerprint.GetString()
            : null;
    }

    private static string? ExtractStrengthBreakdown(string? rawDataJson)
    {
        if (string.IsNullOrWhiteSpace(rawDataJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(rawDataJson);
        return document.RootElement.TryGetProperty("strengthBreakdown", out var breakdown)
            ? breakdown.GetRawText()
            : null;
    }

    public static List<Candle> BuildCandles(int count, Timeframe timeframe, long idOffset)
    {
        var candles = new List<Candle>(count);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var minutes = timeframe switch
        {
            Timeframe.M5 => 5,
            Timeframe.M15 => 15,
            Timeframe.H1 => 60,
            Timeframe.H4 => 240,
            _ => 5
        };

        for (var i = 0; i < count; i++)
        {
            var open = start.AddMinutes(i * minutes);
            // Mild uptrend with enough ATR variation for Adaptive warm-up.
            var price = 100m + (i * 0.15m) + ((i % 7) * 0.05m);
            candles.Add(new Candle
            {
                Id = idOffset + i,
                SymbolId = SymbolId,
                ExchangeId = 1,
                Timeframe = timeframe,
                OpenTimeUtc = open,
                CloseTimeUtc = open.AddMinutes(minutes),
                Open = price,
                High = price + 0.8m,
                Low = price - 0.6m,
                Close = price + 0.25m,
                Volume = 10m + i,
                IsClosed = true,
                CreatedAtUtc = open
            });
        }

        return candles;
    }
}
