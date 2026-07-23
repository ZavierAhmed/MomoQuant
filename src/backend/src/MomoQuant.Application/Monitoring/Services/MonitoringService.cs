using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Monitoring.Dtos;

namespace MomoQuant.Application.Monitoring.Services;

public interface IMonitoringService
{
    Task<ServiceResult<SystemStatusDto>> GetSystemStatusAsync(CancellationToken cancellationToken = default);
}

public sealed class MonitoringService : IMonitoringService
{
    private static readonly TimeSpan RecentWindow = TimeSpan.FromHours(24);

    private readonly ISystemHealthService _systemHealthService;
    private readonly IMonitoringDataRepository _monitoringDataRepository;
    private readonly ISystemHealthLogRepository _systemHealthLogRepository;

    public MonitoringService(
        ISystemHealthService systemHealthService,
        IMonitoringDataRepository monitoringDataRepository,
        ISystemHealthLogRepository systemHealthLogRepository)
    {
        _systemHealthService = systemHealthService;
        _monitoringDataRepository = monitoringDataRepository;
        _systemHealthLogRepository = systemHealthLogRepository;
    }

    public async Task<ServiceResult<SystemStatusDto>> GetSystemStatusAsync(CancellationToken cancellationToken = default)
    {
        var health = await _systemHealthService.GetOverallHealthAsync(cancellationToken);
        if (!health.Succeeded || health.Data is null)
        {
            return ServiceResult<SystemStatusDto>.Fail(health.ErrorMessage ?? "Unable to retrieve system status.");
        }

        var fromUtc = DateTime.UtcNow.Subtract(RecentWindow);
        var criticalFilter = new MonitoringQueryFilter
        {
            FromUtc = fromUtc,
            ToUtc = DateTime.UtcNow,
            Severity = Domain.Enums.LogSeverity.Critical,
            Limit = 200,
            Page = 1,
            PageSize = 200
        };

        var criticalLogs = await _systemHealthLogRepository.GetRecentAsync(criticalFilter, cancellationToken);

        var componentStatus = health.Data.Components.ToDictionary(component => component.Name, component => component.Status);

        return ServiceResult<SystemStatusDto>.Ok(new SystemStatusDto
        {
            ApiStatus = componentStatus.GetValueOrDefault("API", "Unknown"),
            DatabaseStatus = componentStatus.GetValueOrDefault("Database", "Unknown"),
            RedisStatus = componentStatus.GetValueOrDefault("Redis", "Unknown"),
            AiServiceStatus = componentStatus.GetValueOrDefault("AI Service", "Unknown"),
            ActivePaperSessions = await _monitoringDataRepository.CountActivePaperSessionsAsync(cancellationToken),
            RunningBacktests = await _monitoringDataRepository.CountRunningBacktestsAsync(cancellationToken),
            RunningReplaySessions = await _monitoringDataRepository.CountRunningReplaySessionsAsync(cancellationToken),
            RecentCriticalErrors = criticalLogs.Count,
            RecentAiFailures = await _monitoringDataRepository.CountRecentAiFailuresAsync(fromUtc, cancellationToken),
            RecentRiskRejections = await _monitoringDataRepository.CountRecentRiskRejectionsAsync(fromUtc, cancellationToken),
            LastCandleImportUtc = await _monitoringDataRepository.GetLastCandleImportUtcAsync(cancellationToken),
            LastIndicatorRecalculationUtc = await _monitoringDataRepository.GetLastIndicatorRecalculationUtcAsync(cancellationToken),
            GeneratedAtUtc = DateTime.UtcNow
        });
    }
}
