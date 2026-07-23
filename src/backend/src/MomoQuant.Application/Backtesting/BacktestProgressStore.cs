using System.Collections.Concurrent;

namespace MomoQuant.Application.Backtesting;

public sealed class BacktestProgressSnapshot
{
    public DateTime CurrentCandleTimeUtc { get; init; }
    public int CandleIndex { get; init; }
    public int TotalCandles { get; init; }
    public int ElapsedSeconds { get; init; }
    public int SignalsGenerated { get; init; }
    public int TradesGenerated { get; init; }
}

public interface IBacktestProgressStore
{
    void Report(long runItemId, BacktestProgressSnapshot snapshot);

    BacktestProgressSnapshot? Get(long runItemId);

    void Clear(long runItemId);
}

public sealed class BacktestProgressStore : IBacktestProgressStore
{
    private readonly ConcurrentDictionary<long, BacktestProgressSnapshot> _snapshots = new();

    public void Report(long runItemId, BacktestProgressSnapshot snapshot)
    {
        _snapshots[runItemId] = snapshot;
    }

    public BacktestProgressSnapshot? Get(long runItemId)
    {
        return _snapshots.TryGetValue(runItemId, out var snapshot) ? snapshot : null;
    }

    public void Clear(long runItemId)
    {
        _snapshots.TryRemove(runItemId, out _);
    }
}
