using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies;

public interface IHigherTimeframeDatasetEnricher
{
    Task<BacktestDataset> EnrichForStrategiesAsync(
        BacktestDataset dataset,
        IReadOnlyList<PreparedStrategy> strategies,
        CancellationToken cancellationToken = default);

    Task<BacktestDataset> EnrichAsync(
        BacktestDataset dataset,
        IReadOnlyCollection<Timeframe> higherTimeframes,
        CancellationToken cancellationToken = default);
}

public sealed class HigherTimeframeDatasetEnricher : IHigherTimeframeDatasetEnricher
{
    private const int DefaultWarmUpCount = 600;

    private readonly ICandleRepository _candleRepository;

    public HigherTimeframeDatasetEnricher(ICandleRepository candleRepository)
    {
        _candleRepository = candleRepository;
    }

    public async Task<BacktestDataset> EnrichForStrategiesAsync(
        BacktestDataset dataset,
        IReadOnlyList<PreparedStrategy> strategies,
        CancellationToken cancellationToken = default)
    {
        if (strategies.Count == 0)
        {
            return dataset;
        }

        var required = StrategyHigherTimeframeSupport.CollectRequiredHigherTimeframes(strategies, dataset.Timeframe);
        // Fill every missing mapped HTF while preserving any series already present.
        return await EnrichAsync(dataset, required, cancellationToken);
    }

    public async Task<BacktestDataset> EnrichAsync(
        BacktestDataset dataset,
        IReadOnlyCollection<Timeframe> higherTimeframes,
        CancellationToken cancellationToken = default)
    {
        if (higherTimeframes.Count == 0 || dataset.Candles.Count == 0)
        {
            return dataset;
        }

        var merged = new Dictionary<Timeframe, IReadOnlyList<Candle>>(dataset.HigherTimeframeSeriesByTimeframe);
        var fromUtc = dataset.Candles[0].OpenTimeUtc;
        var toUtc = dataset.Candles[^1].CloseTimeUtc;

        foreach (var higherTimeframe in higherTimeframes)
        {
            if (merged.ContainsKey(higherTimeframe))
            {
                continue;
            }

            var candles = await _candleRepository.GetCandlesChronologicalAsync(
                dataset.SymbolId,
                higherTimeframe,
                fromUtc,
                toUtc,
                warmUpCount: DefaultWarmUpCount,
                cancellationToken);

            merged[higherTimeframe] = candles ?? Array.Empty<Candle>();
        }

        if (merged.Count == dataset.HigherTimeframeSeriesByTimeframe.Count)
        {
            return dataset;
        }

        return new BacktestDataset
        {
            SymbolId = dataset.SymbolId,
            SymbolName = dataset.SymbolName,
            Timeframe = dataset.Timeframe,
            Candles = dataset.Candles,
            IndicatorSnapshots = dataset.IndicatorSnapshots,
            EvaluationIndices = dataset.EvaluationIndices,
            HigherTimeframeSeriesByTimeframe = merged
        };
    }
}
