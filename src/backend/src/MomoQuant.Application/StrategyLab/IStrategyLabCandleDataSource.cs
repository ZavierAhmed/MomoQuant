using MomoQuant.Application.Backtesting;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.StrategyLab;

public interface IStrategyLabCandleDataSource
{
    Task<StrategyLabDataset> LoadAsync(
        StrategyLabRun run,
        int warmupCandles,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Candle dataset for Strategy Laboratory evaluation. Mirrors <see cref="BacktestDataset"/>.
/// </summary>
public sealed class StrategyLabDataset
{
    public required long SymbolId { get; init; }
    public required string SymbolName { get; init; }
    public required Timeframe Timeframe { get; init; }
    public required IReadOnlyList<Candle> Candles { get; init; }
    public required IReadOnlyDictionary<long, IndicatorSnapshot> IndicatorSnapshots { get; init; }
    public required IReadOnlyList<int> EvaluationIndices { get; init; }

    /// <summary>Warm-up bars prepended before the evaluation window (0 when none).</summary>
    public int WarmupCandleCount { get; init; }

    public string? WarmupContentFingerprint { get; init; }
    public string? EvaluationContentFingerprint { get; init; }
    public string? CombinedContentFingerprint { get; init; }

    /// <summary>
    /// Closed higher-timeframe series keyed by timeframe. Preserved through FromBacktest/ToBacktest.
    /// </summary>
    public IReadOnlyDictionary<Timeframe, IReadOnlyList<Candle>> HigherTimeframeSeriesByTimeframe { get; init; }
        = new Dictionary<Timeframe, IReadOnlyList<Candle>>();

    public static StrategyLabDataset FromBacktest(BacktestDataset dataset) =>
        new()
        {
            SymbolId = dataset.SymbolId,
            SymbolName = dataset.SymbolName,
            Timeframe = dataset.Timeframe,
            Candles = dataset.Candles,
            IndicatorSnapshots = dataset.IndicatorSnapshots,
            EvaluationIndices = dataset.EvaluationIndices,
            WarmupCandleCount = Math.Max(0, dataset.Candles.Count - dataset.EvaluationIndices.Count),
            HigherTimeframeSeriesByTimeframe = CopyHtfSeries(dataset.HigherTimeframeSeriesByTimeframe)
        };

    public BacktestDataset ToBacktest() =>
        new()
        {
            SymbolId = SymbolId,
            SymbolName = SymbolName,
            Timeframe = Timeframe,
            Candles = Candles,
            IndicatorSnapshots = IndicatorSnapshots,
            EvaluationIndices = EvaluationIndices,
            HigherTimeframeSeriesByTimeframe = CopyHtfSeries(HigherTimeframeSeriesByTimeframe)
        };

    private static IReadOnlyDictionary<Timeframe, IReadOnlyList<Candle>> CopyHtfSeries(
        IReadOnlyDictionary<Timeframe, IReadOnlyList<Candle>>? source)
    {
        if (source is null || source.Count == 0)
        {
            return new Dictionary<Timeframe, IReadOnlyList<Candle>>();
        }

        var copy = new Dictionary<Timeframe, IReadOnlyList<Candle>>();
        foreach (var (tf, candles) in source.OrderBy(kv => (int)kv.Key))
        {
            copy[tf] = candles;
        }

        return copy;
    }
}
