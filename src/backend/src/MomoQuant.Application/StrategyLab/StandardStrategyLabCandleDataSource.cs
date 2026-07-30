using MomoQuant.Application.Backtesting;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Strategies;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.StrategyLab;

/// <summary>
/// General-research candle source. Uses <see cref="IBacktestDataLoader"/> (coverage remains in the runner),
/// then enriches mapped higher-timeframe series for Adaptive via <see cref="IHigherTimeframeDatasetEnricher"/>.
/// Validation Laboratory must not use this type — use <see cref="ValidationTrainingStrategyLabCandleDataSource"/>.
/// </summary>
public sealed class StandardStrategyLabCandleDataSource : IStrategyLabCandleDataSource
{
    private readonly IBacktestDataLoader _dataLoader;
    private readonly IHigherTimeframeDatasetEnricher? _higherTimeframeDatasetEnricher;
    private readonly IStrategyRegistry? _strategyRegistry;

    public StandardStrategyLabCandleDataSource(IBacktestDataLoader dataLoader)
        : this(dataLoader, higherTimeframeDatasetEnricher: null, strategyRegistry: null)
    {
    }

    public StandardStrategyLabCandleDataSource(
        IBacktestDataLoader dataLoader,
        IHigherTimeframeDatasetEnricher? higherTimeframeDatasetEnricher,
        IStrategyRegistry? strategyRegistry)
    {
        _dataLoader = dataLoader;
        _higherTimeframeDatasetEnricher = higherTimeframeDatasetEnricher;
        _strategyRegistry = strategyRegistry;
    }

    public async Task<StrategyLabDataset> LoadAsync(
        StrategyLabRun run,
        int warmupCandles,
        CancellationToken cancellationToken = default)
    {
        if (!TimeframeParser.TryParse(run.Timeframe, out var parsedTimeframe))
        {
            throw new InvalidOperationException(TimeframeNormalizer.UnsupportedTimeframeMessage(run.Timeframe));
        }

        var contractVersion = run.CandleLoadContractVersion ?? StrategyLabCandleLoadContractVersions.LegacyV1;

        var dataset = await _dataLoader.LoadSymbolTimeframeAsync(
            run.ExchangeId,
            run.SymbolId,
            parsedTimeframe,
            run.FromUtc,
            run.ToUtc,
            warmupCandles,
            contractVersion,
            cancellationToken);

        if (dataset is null || dataset.Candles.Count == 0)
        {
            throw new InvalidOperationException("No candle data available after import verification.");
        }

        if (_higherTimeframeDatasetEnricher is not null && _strategyRegistry is not null)
        {
            StrategyCode strategyCode;
            try
            {
                strategyCode = StrategyCodeExtensions.FromCode(run.StrategyCode);
            }
            catch (ArgumentOutOfRangeException)
            {
                return StrategyLabDataset.FromBacktest(dataset);
            }

            var plugin = _strategyRegistry.GetByCode(strategyCode);
            if (plugin is not null && StrategyHigherTimeframeSupport.UsesMomoAdaptiveMapping(plugin))
            {
                var prepared = new PreparedStrategy
                {
                    Strategy = new Strategy
                    {
                        Id = 0,
                        Code = plugin.Code,
                        Name = plugin.Name,
                        Description = plugin.Description,
                        Version = "1.0.0",
                        IsEnabled = true,
                        CreatedAtUtc = DateTime.UtcNow
                    },
                    Plugin = plugin
                };

                dataset = await _higherTimeframeDatasetEnricher.EnrichForStrategiesAsync(
                    dataset,
                    [prepared],
                    cancellationToken);
            }
        }

        return StrategyLabDataset.FromBacktest(dataset);
    }
}
