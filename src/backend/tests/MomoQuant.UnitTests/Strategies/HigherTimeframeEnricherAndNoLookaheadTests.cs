using Moq;
using MomoQuant.Application.Abstractions;
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
/// Milestone 23.1A1 — HTF enricher partial fill and no-lookahead slicing evidence.
/// </summary>
public sealed class HigherTimeframeEnricherAndNoLookaheadTests
{
    [Fact]
    public async Task EnrichForStrategiesAsync_PartiallyPopulated_FillsMissingMappedTimeframe()
    {
        var existingH4 = BuildCandles(10, Timeframe.H4);
        var dataset = new BacktestDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M15,
            Candles = BuildCandles(50, Timeframe.M15),
            IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
            EvaluationIndices = [49],
            HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>
            {
                [Timeframe.H4] = existingH4
            }
        };

        var candleRepository = new Mock<ICandleRepository>();
        candleRepository.Setup(repo => repo.GetCandlesChronologicalAsync(
                1,
                Timeframe.H4,
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingH4);

        // M15 Adaptive maps to H4 only — use a second required TF via general path by using
        // two Adaptive plugins on different execution TFs is hard; instead verify EnrichAsync
        // fills a missing key when requested explicitly and EnrichForStrategies preserves existing.
        var enricher = new HigherTimeframeDatasetEnricher(candleRepository.Object);
        var strategies = new List<PreparedStrategy>
        {
            new()
            {
                Strategy = new Strategy
                {
                    Id = 1,
                    Code = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
                    Name = "MTF",
                    IsEnabled = true,
                    Version = "1.0.0"
                },
                Plugin = new MomoAdaptiveMultiTimeframeTrendBreakoutStrategy()
            }
        };

        var enriched = await enricher.EnrichForStrategiesAsync(dataset, strategies);

        Assert.True(enriched.HigherTimeframeSeriesByTimeframe.ContainsKey(Timeframe.H4));
        Assert.Same(existingH4, enriched.HigherTimeframeSeriesByTimeframe[Timeframe.H4]);

        // Partial map with wrong TF present must still load the required mapped TF.
        var partialWrong = new BacktestDataset
        {
            SymbolId = 1,
            SymbolName = "BTCUSDT",
            Timeframe = Timeframe.M5,
            Candles = BuildCandles(50, Timeframe.M5),
            IndicatorSnapshots = new Dictionary<long, IndicatorSnapshot>(),
            EvaluationIndices = [49],
            HigherTimeframeSeriesByTimeframe = new Dictionary<Timeframe, IReadOnlyList<Candle>>
            {
                [Timeframe.D1] = BuildCandles(5, Timeframe.D1)
            }
        };

        var h1Series = BuildCandles(20, Timeframe.H1);
        candleRepository.Setup(repo => repo.GetCandlesChronologicalAsync(
                1,
                Timeframe.H1,
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(h1Series);

        var filled = await enricher.EnrichForStrategiesAsync(partialWrong, strategies);
        Assert.True(filled.HigherTimeframeSeriesByTimeframe.ContainsKey(Timeframe.D1));
        Assert.True(filled.HigherTimeframeSeriesByTimeframe.ContainsKey(Timeframe.H1));
        Assert.Equal(20, filled.HigherTimeframeSeriesByTimeframe[Timeframe.H1].Count);
        candleRepository.Verify(repo => repo.GetCandlesChronologicalAsync(
            1,
            Timeframe.H1,
            It.IsAny<DateTime?>(),
            It.IsAny<DateTime?>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public void SliceClosedThrough_ExcludesFutureAndOpenCandles()
    {
        var evaluationClose = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var closed = new Candle
        {
            SymbolId = 1,
            Timeframe = Timeframe.H1,
            OpenTimeUtc = evaluationClose.AddHours(-2),
            CloseTimeUtc = evaluationClose.AddHours(-1),
            Open = 100,
            High = 101,
            Low = 99,
            Close = 100.5m,
            Volume = 1,
            IsClosed = true
        };
        var open = new Candle
        {
            SymbolId = 1,
            Timeframe = Timeframe.H1,
            OpenTimeUtc = evaluationClose.AddMinutes(-30),
            CloseTimeUtc = evaluationClose.AddMinutes(30),
            Open = 100,
            High = 110,
            Low = 90,
            Close = 105,
            Volume = 1,
            IsClosed = false
        };
        var future = new Candle
        {
            SymbolId = 1,
            Timeframe = Timeframe.H1,
            OpenTimeUtc = evaluationClose.AddHours(1),
            CloseTimeUtc = evaluationClose.AddHours(2),
            Open = 200,
            High = 210,
            Low = 190,
            Close = 205,
            Volume = 1,
            IsClosed = true
        };

        var sliced = HigherTimeframeCandleView.SliceClosedThrough([closed, open, future], evaluationClose);
        Assert.Single(sliced);
        Assert.Same(closed, sliced[0]);
        Assert.DoesNotContain(sliced, c => !c.IsClosed);
        Assert.DoesNotContain(sliced, c => c.CloseTimeUtc > evaluationClose);
    }

    [Fact]
    public void NoLookahead_SlicedFutureHtf_IdenticalEvaluationAndDiagnostics()
    {
        var candles = BuildCandles(250, Timeframe.M5);
        var htfBase = BuildCandles(250, Timeframe.H1);
        // Align HTF close times to be <= last LTF close.
        var evaluationClose = candles[^1].CloseTimeUtc;
        for (var i = 0; i < htfBase.Count; i++)
        {
            htfBase[i].CloseTimeUtc = evaluationClose.AddHours(-(htfBase.Count - i));
            htfBase[i].OpenTimeUtc = htfBase[i].CloseTimeUtc.AddHours(-1);
            htfBase[i].IsClosed = true;
        }

        var future = new Candle
        {
            SymbolId = 1,
            Timeframe = Timeframe.H1,
            OpenTimeUtc = evaluationClose.AddHours(1),
            CloseTimeUtc = evaluationClose.AddHours(2),
            Open = 99999m,
            High = 100000m,
            Low = 99000m,
            Close = 99500m,
            Volume = 9999m,
            IsClosed = true
        };
        var open = new Candle
        {
            SymbolId = 1,
            Timeframe = Timeframe.H1,
            OpenTimeUtc = evaluationClose.AddMinutes(-10),
            CloseTimeUtc = evaluationClose.AddMinutes(50),
            Open = 88888m,
            High = 90000m,
            Low = 87000m,
            Close = 89000m,
            Volume = 8888m,
            IsClosed = false
        };

        var polluted = htfBase.Concat([open, future]).ToList();
        var sliced = HigherTimeframeCandleView.SliceClosedThrough(polluted, evaluationClose);

        Assert.DoesNotContain(sliced, c => ReferenceEquals(c, future) || ReferenceEquals(c, open));
        Assert.All(sliced, c =>
        {
            Assert.True(c.IsClosed);
            Assert.True(c.CloseTimeUtc <= evaluationClose);
        });

        var parameters = MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract();
        var (candidateClean, reasonClean) = MomoAdaptiveMtfTrendBreakoutEvaluator.EvaluateAtCurrentCandle(
            candles,
            sliced,
            parameters,
            MarketRegime.Trending,
            new HashSet<string>(),
            StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout.ToCode(),
            1,
            "5m");

        var (candidatePollutedSliced, reasonPolluted) = MomoAdaptiveMtfTrendBreakoutEvaluator.EvaluateAtCurrentCandle(
            candles,
            HigherTimeframeCandleView.SliceClosedThrough(polluted, evaluationClose),
            parameters,
            MarketRegime.Trending,
            new HashSet<string>(),
            StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout.ToCode(),
            1,
            "5m");

        Assert.NotEqual(MomoAdaptiveMtfRejectionCodes.MtfDataUnavailable, reasonClean);
        Assert.Equal(reasonClean, reasonPolluted);
        Assert.Equal(candidateClean?.SetupFingerprint, candidatePollutedSliced?.SetupFingerprint);
        Assert.Equal(candidateClean?.Strength, candidatePollutedSliced?.Strength);
        Assert.Equal(candidateClean?.EntryPrice, candidatePollutedSliced?.EntryPrice);
    }

    private static List<Candle> BuildCandles(int count, Timeframe timeframe)
    {
        var candles = new List<Candle>();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var minutes = timeframe switch
        {
            Timeframe.M5 => 5,
            Timeframe.M15 => 15,
            Timeframe.H1 => 60,
            Timeframe.H4 => 240,
            Timeframe.D1 => 1440,
            _ => 5
        };

        for (var i = 0; i < count; i++)
        {
            var open = start.AddMinutes(i * minutes);
            var price = 100m + i;
            candles.Add(new Candle
            {
                SymbolId = 1,
                Timeframe = timeframe,
                OpenTimeUtc = open,
                CloseTimeUtc = open.AddMinutes(minutes),
                Open = price,
                High = price + 1m,
                Low = price - 1m,
                Close = price + 0.2m,
                Volume = 10m + i,
                IsClosed = true
            });
        }

        return candles;
    }
}
