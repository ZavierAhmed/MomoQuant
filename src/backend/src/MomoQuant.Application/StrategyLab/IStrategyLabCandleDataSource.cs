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

    public static StrategyLabDataset FromBacktest(BacktestDataset dataset) =>
        new()
        {
            SymbolId = dataset.SymbolId,
            SymbolName = dataset.SymbolName,
            Timeframe = dataset.Timeframe,
            Candles = dataset.Candles,
            IndicatorSnapshots = dataset.IndicatorSnapshots,
            EvaluationIndices = dataset.EvaluationIndices
        };

    public BacktestDataset ToBacktest() =>
        new()
        {
            SymbolId = SymbolId,
            SymbolName = SymbolName,
            Timeframe = Timeframe,
            Candles = Candles,
            IndicatorSnapshots = IndicatorSnapshots,
            EvaluationIndices = EvaluationIndices
        };
}
