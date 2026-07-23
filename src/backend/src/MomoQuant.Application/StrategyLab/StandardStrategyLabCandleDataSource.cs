using MomoQuant.Application.Backtesting;
using MomoQuant.Application.MarketData;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.StrategyLab;

/// <summary>
/// General-research candle source. Uses <see cref="IBacktestDataLoader"/> (coverage remains in the runner).
/// </summary>
public sealed class StandardStrategyLabCandleDataSource : IStrategyLabCandleDataSource
{
    private readonly IBacktestDataLoader _dataLoader;

    public StandardStrategyLabCandleDataSource(IBacktestDataLoader dataLoader) =>
        _dataLoader = dataLoader;

    public async Task<StrategyLabDataset> LoadAsync(
        StrategyLabRun run,
        int warmupCandles,
        CancellationToken cancellationToken = default)
    {
        if (!TimeframeParser.TryParse(run.Timeframe, out var parsedTimeframe))
        {
            throw new InvalidOperationException(TimeframeNormalizer.UnsupportedTimeframeMessage(run.Timeframe));
        }

        var dataset = await _dataLoader.LoadSymbolTimeframeAsync(
            run.ExchangeId,
            run.SymbolId,
            parsedTimeframe,
            run.FromUtc,
            run.ToUtc,
            warmupCandles,
            cancellationToken);

        if (dataset is null || dataset.Candles.Count == 0)
        {
            throw new InvalidOperationException("No candle data available after import verification.");
        }

        return StrategyLabDataset.FromBacktest(dataset);
    }
}
