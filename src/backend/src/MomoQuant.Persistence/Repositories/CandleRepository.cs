using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Persistence.Repositories;

public sealed class CandleRepository : ICandleRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public CandleRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime? fromUtc,
        DateTime? toUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Candles.AsNoTracking()
            .Where(candle => candle.SymbolId == symbolId && candle.Timeframe == timeframe);

        if (fromUtc.HasValue)
        {
            query = query.Where(candle => candle.OpenTimeUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(candle => candle.OpenTimeUtc < toUtc.Value);
        }

        return await query
            .OrderBy(candle => candle.OpenTimeUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesChronologicalAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime? fromUtc,
        DateTime? toUtc,
        int warmUpCount = 0,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Candle> warmUpCandles = [];
        if (warmUpCount > 0 && fromUtc.HasValue)
        {
            warmUpCandles = await _dbContext.Candles.AsNoTracking()
                .Where(candle =>
                    candle.SymbolId == symbolId &&
                    candle.Timeframe == timeframe &&
                    candle.OpenTimeUtc < fromUtc.Value)
                .OrderByDescending(candle => candle.OpenTimeUtc)
                .Take(warmUpCount)
                .ToListAsync(cancellationToken);
        }

        var query = _dbContext.Candles.AsNoTracking()
            .Where(candle => candle.SymbolId == symbolId && candle.Timeframe == timeframe);

        if (fromUtc.HasValue)
        {
            query = query.Where(candle => candle.OpenTimeUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(candle => candle.OpenTimeUtc < toUtc.Value);
        }

        var rangeCandles = await query
            .OrderBy(candle => candle.OpenTimeUtc)
            .ToListAsync(cancellationToken);

        if (warmUpCandles.Count == 0)
        {
            return rangeCandles;
        }

        return warmUpCandles
            .OrderBy(candle => candle.OpenTimeUtc)
            .Concat(rangeCandles)
            .ToList();
    }

    public Task<Candle?> GetLatestCandleAsync(
        long symbolId,
        Timeframe timeframe,
        CancellationToken cancellationToken = default) =>
        _dbContext.Candles.AsNoTracking()
            .Where(candle => candle.SymbolId == symbolId && candle.Timeframe == timeframe)
            .OrderByDescending(candle => candle.OpenTimeUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> CountCandlesAsync(
        long symbolId,
        Timeframe timeframe,
        CancellationToken cancellationToken = default) =>
        _dbContext.Candles.AsNoTracking()
            .CountAsync(candle => candle.SymbolId == symbolId && candle.Timeframe == timeframe, cancellationToken);

    public async Task<HashSet<DateTime>> GetExistingOpenTimesAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        IReadOnlyCollection<DateTime> openTimesUtc,
        CancellationToken cancellationToken = default)
    {
        if (openTimesUtc.Count == 0)
        {
            return [];
        }

        var existing = await _dbContext.Candles.AsNoTracking()
            .Where(candle =>
                candle.ExchangeId == exchangeId &&
                candle.SymbolId == symbolId &&
                candle.Timeframe == timeframe &&
                openTimesUtc.Contains(candle.OpenTimeUtc))
            .Select(candle => candle.OpenTimeUtc)
            .ToListAsync(cancellationToken);

        return existing.ToHashSet();
    }

    public Task AddRangeAsync(IReadOnlyCollection<Candle> candles, CancellationToken cancellationToken = default)
    {
        _dbContext.Candles.AddRange(candles);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    public Task<Candle?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.Candles.AsNoTracking().FirstOrDefaultAsync(candle => candle.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Candle>> GetRecentCandlesAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime beforeOrAtOpenTimeUtc,
        int count,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Candles.AsNoTracking()
            .Where(candle =>
                candle.SymbolId == symbolId &&
                candle.Timeframe == timeframe &&
                candle.OpenTimeUtc <= beforeOrAtOpenTimeUtc)
            .OrderByDescending(candle => candle.OpenTimeUtc)
            .Take(count)
            .OrderBy(candle => candle.OpenTimeUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DateTime>> GetOpenTimesInRangeAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Candles.AsNoTracking()
            .Where(candle =>
                candle.ExchangeId == exchangeId &&
                candle.SymbolId == symbolId &&
                candle.Timeframe == timeframe &&
                candle.OpenTimeUtc >= fromUtc &&
                candle.OpenTimeUtc < toUtc)
            .OrderBy(candle => candle.OpenTimeUtc)
            .Select(candle => candle.OpenTimeUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountDuplicateKeysInRangeAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var duplicateGroups = await _dbContext.Candles.AsNoTracking()
            .Where(candle =>
                candle.ExchangeId == exchangeId &&
                candle.SymbolId == symbolId &&
                candle.Timeframe == timeframe &&
                candle.OpenTimeUtc >= fromUtc &&
                candle.OpenTimeUtc < toUtc)
            .GroupBy(candle => candle.OpenTimeUtc)
            .Where(group => group.Count() > 1)
            .Select(group => group.Count())
            .ToListAsync(cancellationToken);

        return duplicateGroups.Sum(count => count - 1);
    }
}
