using Moq;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

/// <summary>
/// Milestone 23.1A1E — Adaptive LTF future/open pollution filtered by BacktestEngine at fixed T.
/// </summary>
public sealed class AdaptiveProductionLtfPollutionTests
{
    private static readonly DateTime Start = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task BacktestEngine_FixedT_OpenAndFutureLtfPollution_DoesNotReachAdaptive()
    {
        var (cleanLtf, htf) = AdaptiveDefaultFixtures.BuildValidLong(Start);
        for (var i = 0; i < cleanLtf.Count; i++)
        {
            cleanLtf[i].Id = i + 1;
        }

        var evaluationIndex = cleanLtf.Count - 1;
        var evaluationCandle = cleanLtf[evaluationIndex];
        var evaluationTimeUtc = evaluationCandle.CloseTimeUtc;
        var slicedHtf = htf.Where(c => c.IsClosed && c.CloseTimeUtc <= evaluationTimeUtc).ToList();
        for (var i = 0; i < slicedHtf.Count; i++)
        {
            slicedHtf[i].Id = 10_000 + i;
        }

        var pollutedLtf = cleanLtf.Select(CloneExact).ToList();
        Assert.Equal(cleanLtf.Count, pollutedLtf.Count);
        pollutedLtf.Add(new Candle
        {
            Id = 90_001,
            SymbolId = evaluationCandle.SymbolId,
            ExchangeId = evaluationCandle.ExchangeId,
            Timeframe = evaluationCandle.Timeframe,
            OpenTimeUtc = evaluationTimeUtc,
            CloseTimeUtc = evaluationTimeUtc.AddMinutes(5),
            Open = evaluationCandle.Close + 500m,
            High = evaluationCandle.Close + 800m,
            Low = evaluationCandle.Close + 400m,
            Close = evaluationCandle.Close + 700m,
            Volume = evaluationCandle.Volume,
            IsClosed = false,
            CreatedAtUtc = evaluationTimeUtc
        });
        pollutedLtf.Add(new Candle
        {
            Id = 90_002,
            SymbolId = evaluationCandle.SymbolId,
            ExchangeId = evaluationCandle.ExchangeId,
            Timeframe = evaluationCandle.Timeframe,
            OpenTimeUtc = evaluationTimeUtc.AddMinutes(5),
            CloseTimeUtc = evaluationTimeUtc.AddMinutes(10),
            Open = 1m,
            High = 2m,
            Low = 0.5m,
            Close = 1.5m,
            Volume = 1m,
            IsClosed = true,
            CreatedAtUtc = evaluationTimeUtc.AddMinutes(5)
        });

        Assert.True(pollutedLtf.Count > cleanLtf.Count);
        Assert.Contains(pollutedLtf, c => c.Id == 90_001 && !c.IsClosed);
        Assert.Contains(pollutedLtf, c => c.Id == 90_002 && c.IsClosed && c.CloseTimeUtc > evaluationTimeUtc);

        var prepared = new PreparedStrategy
        {
            Strategy = new Strategy
            {
                Id = 42,
                Code = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
                Name = "Adaptive",
                IsEnabled = true,
                Version = "1.0.0"
            },
            Plugin = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy()
        };

        var cleanRecording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var cleanEngine = ClosedHtfCaptureHarness.CreateBacktestEngine(cleanRecording);
        await cleanEngine.ProcessCandleAtIndexAsync(
            ClosedHtfCaptureHarness.CreateBacktestContext(),
            CreateDataset(cleanLtf, slicedHtf, evaluationIndex),
            [prepared],
            evaluationIndex: 0);

        var pollutedRecording = new ClosedHtfCaptureHarness.RecordingStrategyEngine(new StrategyEvaluationCaptureRecording());
        var pollutedEngine = ClosedHtfCaptureHarness.CreateBacktestEngine(pollutedRecording);
        await pollutedEngine.ProcessCandleAtIndexAsync(
            ClosedHtfCaptureHarness.CreateBacktestContext(),
            CreateDataset(pollutedLtf, slicedHtf, evaluationIndex),
            [prepared],
            evaluationIndex: 0);

        var cleanCapture = Assert.Single(cleanRecording.Capture.Records);
        var pollutedCapture = Assert.Single(pollutedRecording.Capture.Records);
        var cleanResult = Assert.Single(cleanRecording.Results);
        var pollutedResult = Assert.Single(pollutedRecording.Results);

        Assert.Equal(evaluationTimeUtc, cleanCapture.EvaluatedAtUtc);
        Assert.Equal(evaluationTimeUtc, pollutedCapture.EvaluatedAtUtc);
        Assert.All(cleanCapture.Candles, c => Assert.True(c.IsClosed));
        Assert.All(cleanCapture.Candles, c => Assert.True(c.CloseTimeUtc <= evaluationTimeUtc));
        Assert.All(pollutedCapture.Candles, c => Assert.True(c.IsClosed));
        Assert.All(pollutedCapture.Candles, c => Assert.True(c.CloseTimeUtc <= evaluationTimeUtc));
        Assert.DoesNotContain(pollutedCapture.Candles, c => c.Id is 90_001 or 90_002);

        Assert.Equal(cleanCapture.Candles.Count, pollutedCapture.Candles.Count);
        Assert.Equal(
            cleanCapture.Candles.Select(c => (c.Id, c.CloseTimeUtc, c.Open, c.High, c.Low, c.Close)).ToArray(),
            pollutedCapture.Candles.Select(c => (c.Id, c.CloseTimeUtc, c.Open, c.High, c.Low, c.Close)).ToArray());
        Assert.Equal(cleanResult.Reason, pollutedResult.Reason);
        Assert.Equal(cleanResult.Direction, pollutedResult.Direction);
        Assert.Equal(cleanResult.EntryPrice, pollutedResult.EntryPrice);
        Assert.Equal(cleanResult.SuggestedStopLoss, pollutedResult.SuggestedStopLoss);
        Assert.Equal(cleanResult.SuggestedTakeProfit, pollutedResult.SuggestedTakeProfit);
        Assert.Equal(cleanResult.Strength, pollutedResult.Strength);
        Assert.Equal(cleanResult.RawDataJson, pollutedResult.RawDataJson);
    }

    private static BacktestDataset CreateDataset(
        IReadOnlyList<Candle> ltf,
        IReadOnlyList<Candle> htf,
        int evaluationIndex)
    {
        var snapshots = new Dictionary<long, IndicatorSnapshot>();
        for (var i = 0; i < ltf.Count; i++)
        {
            var candle = ltf[i];
            if (!candle.IsClosed || candle.CloseTimeUtc > ltf[evaluationIndex].CloseTimeUtc)
            {
                continue;
            }

            snapshots[candle.Id] = new IndicatorSnapshot
            {
                CandleId = candle.Id,
                SymbolId = candle.SymbolId,
                Timeframe = candle.Timeframe,
                Ema20 = candle.Close + 2m,
                Ema50 = candle.Close + 1m,
                Ema200 = candle.Close,
                Atr14 = 10m,
                CalculatedAtUtc = candle.CloseTimeUtc,
                CreatedAtUtc = candle.CloseTimeUtc,
                MarketStructure = MarketStructure.Bullish
            };
        }

        return new BacktestDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = ltf,
            IndicatorSnapshots = snapshots,
            EvaluationIndices = [evaluationIndex],
            HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>
            {
                [Timeframe.H1] = htf
            }
        };
    }

    private static Candle CloneExact(Candle c) => new()
    {
        Id = c.Id,
        SymbolId = c.SymbolId,
        ExchangeId = c.ExchangeId,
        Timeframe = c.Timeframe,
        OpenTimeUtc = c.OpenTimeUtc,
        CloseTimeUtc = c.CloseTimeUtc,
        Open = c.Open,
        High = c.High,
        Low = c.Low,
        Close = c.Close,
        Volume = c.Volume,
        IsClosed = c.IsClosed,
        CreatedAtUtc = c.CreatedAtUtc
    };
}
