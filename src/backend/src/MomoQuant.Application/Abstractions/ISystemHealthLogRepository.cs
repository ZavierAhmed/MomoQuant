using MomoQuant.Application.Monitoring.Dtos;
using MomoQuant.Domain.Monitoring;

namespace MomoQuant.Application.Abstractions;

public interface ISystemHealthLogRepository
{
    Task AddAsync(SystemHealthLog log, CancellationToken cancellationToken = default);
    Task<SystemHealthLog?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<SystemHealthLog> Items, int TotalCount)> GetPagedAsync(
        MonitoringQueryFilter filter,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SystemHealthLog>> GetRecentAsync(
        MonitoringQueryFilter filter,
        CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IMonitoringDataRepository
{
    Task<int> CountActivePaperSessionsAsync(CancellationToken cancellationToken = default);
    Task<int> CountRunningBacktestsAsync(CancellationToken cancellationToken = default);
    Task<int> CountRunningReplaySessionsAsync(CancellationToken cancellationToken = default);
    Task<int> CountRecentRiskRejectionsAsync(DateTime fromUtc, CancellationToken cancellationToken = default);
    Task<int> CountRecentAiFailuresAsync(DateTime fromUtc, CancellationToken cancellationToken = default);
    Task<DateTime?> GetLastCandleImportUtcAsync(CancellationToken cancellationToken = default);
    Task<DateTime?> GetLastIndicatorRecalculationUtcAsync(CancellationToken cancellationToken = default);
    Task<DateTime?> GetLatestCandleTimeUtcAsync(CancellationToken cancellationToken = default);
    Task<DateTime?> GetLatestIndicatorSnapshotTimeUtcAsync(CancellationToken cancellationToken = default);
    Task<int> CountEnabledStrategiesAsync(CancellationToken cancellationToken = default);
    Task<int> CountRiskProfilesAsync(CancellationToken cancellationToken = default);
    Task<int> CountOpenPaperPositionsAsync(CancellationToken cancellationToken = default);
    Task<bool> GetEmergencyStopEnabledAsync(CancellationToken cancellationToken = default);
    Task<bool> HasMarketDataAsync(CancellationToken cancellationToken = default);
    Task<bool> HasIndicatorsAsync(CancellationToken cancellationToken = default);
}
