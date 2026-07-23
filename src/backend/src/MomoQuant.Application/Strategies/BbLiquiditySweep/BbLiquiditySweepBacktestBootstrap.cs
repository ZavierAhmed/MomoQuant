using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Strategies.BbLiquiditySweep;

public interface IBbLiquiditySweepBacktestBootstrap
{
    Task PrecomputeAsync(
        long tradingSessionId,
        BacktestDataset dataset,
        BbLiquiditySweepCisdStrategyBase strategy,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default);
}

public sealed class BbLiquiditySweepBacktestBootstrap : IBbLiquiditySweepBacktestBootstrap
{
    private readonly ICandleRepository _candleRepository;

    public BbLiquiditySweepBacktestBootstrap(ICandleRepository candleRepository) =>
        _candleRepository = candleRepository;

    public async Task PrecomputeAsync(
        long tradingSessionId,
        BacktestDataset dataset,
        BbLiquiditySweepCisdStrategyBase strategy,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        const int warmUp = 500;
        var first = dataset.Candles[0];
        var last = dataset.Candles[^1];

        var oneMinute = await _candleRepository.GetCandlesChronologicalAsync(
            dataset.SymbolId,
            Timeframe.M1,
            first.OpenTimeUtc,
            last.CloseTimeUtc,
            warmUp,
            cancellationToken);

        var fiveMinute = await _candleRepository.GetCandlesChronologicalAsync(
            dataset.SymbolId,
            Timeframe.M5,
            first.OpenTimeUtc,
            last.CloseTimeUtc,
            warmUp,
            cancellationToken);

        strategy.PrecomputeData(
            dataset.SymbolId,
            dataset.Candles,
            oneMinute.Count > 0 ? oneMinute : null,
            fiveMinute.Count > 0 ? fiveMinute : null,
            parameters,
            tradingSessionId);
    }
}
