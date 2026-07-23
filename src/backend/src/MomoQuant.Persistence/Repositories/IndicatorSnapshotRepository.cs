using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;

namespace MomoQuant.Persistence.Repositories;

public sealed class IndicatorSnapshotRepository : IIndicatorSnapshotRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public IndicatorSnapshotRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<IndicatorSnapshot?> GetByKeyAsync(
        long symbolId,
        Timeframe timeframe,
        long candleId,
        CancellationToken cancellationToken = default) =>
        _dbContext.IndicatorSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(
                snapshot =>
                    snapshot.SymbolId == symbolId &&
                    snapshot.Timeframe == timeframe &&
                    snapshot.CandleId == candleId,
                cancellationToken);

    public async Task<Dictionary<long, IndicatorSnapshot>> GetByCandleIdsAsync(
        long symbolId,
        Timeframe timeframe,
        IReadOnlyCollection<long> candleIds,
        CancellationToken cancellationToken = default)
    {
        if (candleIds.Count == 0)
        {
            return new Dictionary<long, IndicatorSnapshot>();
        }

        var snapshots = await _dbContext.IndicatorSnapshots
            .Where(snapshot =>
                snapshot.SymbolId == symbolId &&
                snapshot.Timeframe == timeframe &&
                candleIds.Contains(snapshot.CandleId))
            .ToListAsync(cancellationToken);

        return snapshots.ToDictionary(snapshot => snapshot.CandleId);
    }

    public Task AddRangeAsync(IReadOnlyCollection<IndicatorSnapshot> snapshots, CancellationToken cancellationToken = default)
    {
        _dbContext.IndicatorSnapshots.AddRange(snapshots);
        return Task.CompletedTask;
    }

    public Task UpdateRangeAsync(IReadOnlyCollection<IndicatorSnapshot> snapshots, CancellationToken cancellationToken = default)
    {
        _dbContext.IndicatorSnapshots.UpdateRange(snapshots);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyList<IndicatorSnapshot>> GetRecentForSymbolAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime beforeOrAtOpenTimeUtc,
        int count,
        CancellationToken cancellationToken = default)
    {
        var candleIds = await _dbContext.Candles.AsNoTracking()
            .Where(candle =>
                candle.SymbolId == symbolId &&
                candle.Timeframe == timeframe &&
                candle.OpenTimeUtc <= beforeOrAtOpenTimeUtc)
            .OrderByDescending(candle => candle.OpenTimeUtc)
            .Take(count)
            .Select(candle => candle.Id)
            .ToListAsync(cancellationToken);

        if (candleIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.IndicatorSnapshots.AsNoTracking()
            .Where(snapshot =>
                snapshot.SymbolId == symbolId &&
                snapshot.Timeframe == timeframe &&
                candleIds.Contains(snapshot.CandleId))
            .OrderBy(snapshot => snapshot.CandleId)
            .ToListAsync(cancellationToken);
    }
}
