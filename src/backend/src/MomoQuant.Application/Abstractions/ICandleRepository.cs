using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Abstractions;

public interface ICandleRepository
{
    Task<IReadOnlyList<Candle>> GetCandlesAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime? fromUtc,
        DateTime? toUtc,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Candle>> GetCandlesChronologicalAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime? fromUtc,
        DateTime? toUtc,
        int warmUpCount = 0,
        CancellationToken cancellationToken = default);

    Task<Candle?> GetLatestCandleAsync(
        long symbolId,
        Timeframe timeframe,
        CancellationToken cancellationToken = default);

    Task<int> CountCandlesAsync(
        long symbolId,
        Timeframe timeframe,
        CancellationToken cancellationToken = default);

    Task<HashSet<DateTime>> GetExistingOpenTimesAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        IReadOnlyCollection<DateTime> openTimesUtc,
        CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyCollection<Candle> candles, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<Candle?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Candle>> GetRecentCandlesAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime beforeOrAtOpenTimeUtc,
        int count,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DateTime>> GetOpenTimesInRangeAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    Task<int> CountDuplicateKeysInRangeAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);
}
