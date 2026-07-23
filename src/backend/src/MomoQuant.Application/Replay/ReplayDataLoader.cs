using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.MarketData;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Replay;

public interface IReplayDataLoader
{
    Task<BacktestDataset?> LoadAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);
}

public sealed class ReplayDataLoader : IReplayDataLoader
{
    private readonly IBacktestDataLoader _backtestDataLoader;

    public ReplayDataLoader(IBacktestDataLoader backtestDataLoader)
    {
        _backtestDataLoader = backtestDataLoader;
    }

    public Task<BacktestDataset?> LoadAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default) =>
        _backtestDataLoader.LoadSymbolTimeframeAsync(
            exchangeId,
            symbolId,
            timeframe,
            fromUtc,
            toUtc,
            warmUpCount: 600,
            cancellationToken);
}
