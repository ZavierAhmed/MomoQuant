using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;

namespace MomoQuant.Application.Abstractions;

public interface IIndicatorSnapshotRepository
{
    Task<IndicatorSnapshot?> GetByKeyAsync(
        long symbolId,
        Timeframe timeframe,
        long candleId,
        CancellationToken cancellationToken = default);

    Task<Dictionary<long, IndicatorSnapshot>> GetByCandleIdsAsync(
        long symbolId,
        Timeframe timeframe,
        IReadOnlyCollection<long> candleIds,
        CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyCollection<IndicatorSnapshot> snapshots, CancellationToken cancellationToken = default);

    Task UpdateRangeAsync(IReadOnlyCollection<IndicatorSnapshot> snapshots, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IndicatorSnapshot>> GetRecentForSymbolAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime beforeOrAtOpenTimeUtc,
        int count,
        CancellationToken cancellationToken = default);
}
