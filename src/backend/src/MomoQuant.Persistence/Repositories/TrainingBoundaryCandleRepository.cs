using MomoQuant.Application.Abstractions;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Persistence.Repositories;

/// <summary>
/// Decorates candle reads: when a Validation Laboratory training scope is ambient,
/// range/index access is enforced and recorded; prohibited access throws
/// <see cref="ValidationDataLeakageException"/>.
/// </summary>
public sealed class TrainingBoundaryCandleRepository : ICandleRepository, IUnscopedCandleReader
{
    private readonly CandleRepository _inner;

    public TrainingBoundaryCandleRepository(CandleRepository inner) => _inner = inner;

    public Task<IReadOnlyList<Candle>> GetCandlesChronologicalUnscopedAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime? fromUtc,
        DateTime? toUtc,
        int warmUpCount = 0,
        CancellationToken cancellationToken = default) =>
        _inner.GetCandlesChronologicalAsync(symbolId, timeframe, fromUtc, toUtc, warmUpCount, cancellationToken);

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime? fromUtc,
        DateTime? toUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            var range = scope.GetRange(fromUtc, toUtc, nameof(GetCandlesAsync));
            return limit > 0 ? range.Take(limit).ToList() : range;
        }

        return await _inner.GetCandlesAsync(symbolId, timeframe, fromUtc, toUtc, limit, cancellationToken);
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesChronologicalAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime? fromUtc,
        DateTime? toUtc,
        int warmUpCount = 0,
        CancellationToken cancellationToken = default)
    {
        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            // Warm-up must still remain inside the training boundary.
            var range = scope.GetRange(fromUtc, toUtc, nameof(GetCandlesChronologicalAsync));
            if (warmUpCount <= 0 || range.Count == 0)
            {
                return range;
            }

            var first = range[0].OpenTimeUtc;
            var warm = scope.Candles
                .Where(c => c.OpenTimeUtc < first)
                .TakeLast(warmUpCount)
                .Concat(range)
                .ToList();
            return warm;
        }

        return await _inner.GetCandlesChronologicalAsync(
            symbolId, timeframe, fromUtc, toUtc, warmUpCount, cancellationToken);
    }

    public Task<Candle?> GetLatestCandleAsync(
        long symbolId,
        Timeframe timeframe,
        CancellationToken cancellationToken = default)
    {
        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            var last = scope.Candles.LastOrDefault();
            if (last is not null)
            {
                _ = scope.GetByOpenTimeUtc(last.OpenTimeUtc, nameof(GetLatestCandleAsync));
            }

            return Task.FromResult(last);
        }

        return _inner.GetLatestCandleAsync(symbolId, timeframe, cancellationToken);
    }

    public Task<int> CountCandlesAsync(
        long symbolId,
        Timeframe timeframe,
        CancellationToken cancellationToken = default)
    {
        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            return Task.FromResult(scope.Count);
        }

        return _inner.CountCandlesAsync(symbolId, timeframe, cancellationToken);
    }

    public Task<HashSet<DateTime>> GetExistingOpenTimesAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        IReadOnlyCollection<DateTime> openTimesUtc,
        CancellationToken cancellationToken = default)
    {
        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            foreach (var t in openTimesUtc)
            {
                if (DateTime.SpecifyKind(t, DateTimeKind.Utc) >= scope.ValidationBoundaryUtc)
                {
                    _ = scope.GetByOpenTimeUtc(t, nameof(GetExistingOpenTimesAsync));
                }
            }

            var allowed = openTimesUtc
                .Select(t => DateTime.SpecifyKind(t, DateTimeKind.Utc))
                .Where(t => t < scope.ValidationBoundaryUtc)
                .ToHashSet();
            return Task.FromResult(allowed);
        }

        return _inner.GetExistingOpenTimesAsync(exchangeId, symbolId, timeframe, openTimesUtc, cancellationToken);
    }

    public Task AddRangeAsync(IReadOnlyCollection<Candle> candles, CancellationToken cancellationToken = default) =>
        _inner.AddRangeAsync(candles, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _inner.SaveChangesAsync(cancellationToken);

    public async Task<Candle?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var candle = await _inner.GetByIdAsync(id, cancellationToken);
        if (candle is null)
        {
            return null;
        }

        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            return scope.GetByOpenTimeUtc(candle.OpenTimeUtc, nameof(GetByIdAsync));
        }

        return candle;
    }

    public async Task<IReadOnlyList<Candle>> GetRecentCandlesAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime beforeOrAtOpenTimeUtc,
        int count,
        CancellationToken cancellationToken = default)
    {
        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            var ts = DateTime.SpecifyKind(beforeOrAtOpenTimeUtc, DateTimeKind.Utc);
            if (ts >= scope.ValidationBoundaryUtc)
            {
                _ = scope.GetByOpenTimeUtc(ts, nameof(GetRecentCandlesAsync));
            }

            return scope.Candles
                .Where(c => c.OpenTimeUtc <= ts)
                .TakeLast(count)
                .ToList();
        }

        return await _inner.GetRecentCandlesAsync(symbolId, timeframe, beforeOrAtOpenTimeUtc, count, cancellationToken);
    }

    public async Task<IReadOnlyList<DateTime>> GetOpenTimesInRangeAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        if (ValidationTrainingCandleScopeAmbient.Current is { } scope)
        {
            var range = scope.GetRange(fromUtc, toUtc, nameof(GetOpenTimesInRangeAsync));
            return range.Select(c => c.OpenTimeUtc).ToList();
        }

        return await _inner.GetOpenTimesInRangeAsync(exchangeId, symbolId, timeframe, fromUtc, toUtc, cancellationToken);
    }

    public Task<int> CountDuplicateKeysInRangeAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        if (ValidationTrainingCandleScopeAmbient.Current is not null)
        {
            // Training scope is immutable and duplicate-free by construction.
            _ = ValidationTrainingCandleScopeAmbient.Current.GetRange(fromUtc, toUtc, nameof(CountDuplicateKeysInRangeAsync));
            return Task.FromResult(0);
        }

        return _inner.CountDuplicateKeysInRangeAsync(exchangeId, symbolId, timeframe, fromUtc, toUtc, cancellationToken);
    }
}
