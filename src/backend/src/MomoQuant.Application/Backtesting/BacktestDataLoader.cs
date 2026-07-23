using MomoQuant.Application.Abstractions;
using MomoQuant.Application.MarketData;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Backtesting;

public interface IBacktestDataLoader
{
    Task<BacktestDataset?> LoadSymbolTimeframeAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        int warmUpCount,
        CancellationToken cancellationToken = default);
}

public sealed class BacktestDataset
{
    public required long SymbolId { get; init; }
    public required string SymbolName { get; init; }
    public required Timeframe Timeframe { get; init; }
    public required IReadOnlyList<Candle> Candles { get; init; }
    public required IReadOnlyDictionary<long, IndicatorSnapshot> IndicatorSnapshots { get; init; }
    public required IReadOnlyList<int> EvaluationIndices { get; init; }
}

public sealed class BacktestDataLoader : IBacktestDataLoader
{
    private const int DefaultWarmUpCount = 600;

    private readonly ICandleRepository _candleRepository;
    private readonly IIndicatorSnapshotRepository _indicatorSnapshotRepository;
    private readonly ISymbolRepository _symbolRepository;

    public BacktestDataLoader(
        ICandleRepository candleRepository,
        IIndicatorSnapshotRepository indicatorSnapshotRepository,
        ISymbolRepository symbolRepository)
    {
        _candleRepository = candleRepository;
        _indicatorSnapshotRepository = indicatorSnapshotRepository;
        _symbolRepository = symbolRepository;
    }

    public async Task<BacktestDataset?> LoadSymbolTimeframeAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        int warmUpCount,
        CancellationToken cancellationToken = default)
    {
        var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
        if (symbol is null || symbol.ExchangeId != exchangeId)
        {
            return null;
        }

        var warmUp = Math.Max(warmUpCount, DefaultWarmUpCount);
        var candles = await _candleRepository.GetCandlesChronologicalAsync(
            symbolId,
            timeframe,
            fromUtc,
            toUtc,
            warmUpCount: warmUp,
            cancellationToken);

        if (candles.Count == 0)
        {
            return null;
        }

        var evaluationIndices = candles
            .Select((candle, index) => (candle, index))
            .Where(item => item.candle.OpenTimeUtc >= fromUtc && item.candle.OpenTimeUtc <= toUtc)
            .Select(item => item.index)
            .ToList();

        if (evaluationIndices.Count == 0)
        {
            return null;
        }

        var candleIds = candles.Select(candle => candle.Id).ToList();
        var snapshots = await _indicatorSnapshotRepository.GetByCandleIdsAsync(
            symbolId,
            timeframe,
            candleIds,
            cancellationToken);

        return new BacktestDataset
        {
            SymbolId = symbolId,
            SymbolName = symbol.SymbolName,
            Timeframe = timeframe,
            Candles = candles,
            IndicatorSnapshots = snapshots,
            EvaluationIndices = evaluationIndices
        };
    }
}
