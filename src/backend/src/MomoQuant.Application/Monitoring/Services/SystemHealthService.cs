using Microsoft.Extensions.Logging;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Monitoring.Abstractions;
using MomoQuant.Application.Monitoring.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Monitoring.Services;

public interface ISystemHealthService
{
    Task<ServiceResult<HealthResponseDto>> GetOverallHealthAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<ComponentHealthDto>> GetDatabaseHealthAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<ComponentHealthDto>> GetRedisHealthAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<ComponentHealthDto>> GetAiHealthAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<ComponentHealthDto>>> GetSubsystemsHealthAsync(CancellationToken cancellationToken = default);
}

public sealed class SystemHealthService : ISystemHealthService
{
    private readonly IEnumerable<IHealthCheckProvider> _providers;
    private readonly ISubsystemHealthCheckProvider _subsystemProvider;
    private readonly ISystemHealthLogService _systemHealthLogService;
    private readonly ILogger<SystemHealthService> _logger;

    public SystemHealthService(
        IEnumerable<IHealthCheckProvider> providers,
        ISubsystemHealthCheckProvider subsystemProvider,
        ISystemHealthLogService systemHealthLogService,
        ILogger<SystemHealthService> logger)
    {
        _providers = providers;
        _subsystemProvider = subsystemProvider;
        _systemHealthLogService = systemHealthLogService;
        _logger = logger;
    }

    public async Task<ServiceResult<HealthResponseDto>> GetOverallHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var components = new List<ComponentHealthDto>();
            var statuses = new List<SystemHealthStatus>();

            foreach (var provider in _providers)
            {
                var result = await CheckAndPersistAsync(provider, cancellationToken);
                components.Add(Map(result));
                statuses.Add(result.Status);
            }

            var databaseStatus = components.FirstOrDefault(component => component.Name == "Database")?.Status;
            var overall = databaseStatus == "Unhealthy"
                ? SystemHealthStatus.Unhealthy
                : MonitoringHealthMapper.AggregateCoreHealth(
                    ParseStatus(databaseStatus),
                    statuses.Where((_, index) => _providers.ElementAt(index).ComponentName != "Database"));

            return ServiceResult<HealthResponseDto>.Ok(new HealthResponseDto
            {
                Status = MonitoringHealthMapper.ToStatusString(overall),
                CheckedAtUtc = DateTime.UtcNow,
                Components = components
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Overall health check failed.");
            return ServiceResult<HealthResponseDto>.Fail("Health check failed.");
        }
    }

    public async Task<ServiceResult<ComponentHealthDto>> GetDatabaseHealthAsync(CancellationToken cancellationToken = default) =>
        await GetSingleProviderHealthAsync("Database", cancellationToken);

    public async Task<ServiceResult<ComponentHealthDto>> GetRedisHealthAsync(CancellationToken cancellationToken = default) =>
        await GetSingleProviderHealthAsync("Redis", cancellationToken);

    public async Task<ServiceResult<ComponentHealthDto>> GetAiHealthAsync(CancellationToken cancellationToken = default) =>
        await GetSingleProviderHealthAsync("AI Service", cancellationToken);

    public async Task<ServiceResult<IReadOnlyList<ComponentHealthDto>>> GetSubsystemsHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var results = await _subsystemProvider.CheckAllAsync(cancellationToken);
            foreach (var result in results)
            {
                await _systemHealthLogService.PersistAsync(result, cancellationToken);
            }

            return ServiceResult<IReadOnlyList<ComponentHealthDto>>.Ok(results.Select(Map).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subsystem health check failed.");
            return ServiceResult<IReadOnlyList<ComponentHealthDto>>.Fail("Subsystem health check failed.");
        }
    }

    private async Task<ServiceResult<ComponentHealthDto>> GetSingleProviderHealthAsync(
        string componentName,
        CancellationToken cancellationToken)
    {
        var provider = _providers.FirstOrDefault(item => item.ComponentName == componentName);
        if (provider is null)
        {
            return ServiceResult<ComponentHealthDto>.Fail($"{componentName} health provider is not configured.");
        }

        var result = await CheckAndPersistAsync(provider, cancellationToken);
        return ServiceResult<ComponentHealthDto>.Ok(Map(result));
    }

    private async Task<HealthCheckResult> CheckAndPersistAsync(
        IHealthCheckProvider provider,
        CancellationToken cancellationToken)
    {
        HealthCheckResult result;
        try
        {
            result = await provider.CheckAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check provider {Provider} failed.", provider.ComponentName);
            result = new HealthCheckResult
            {
                Name = provider.ComponentName,
                Subsystem = MonitoringSubsystem.Unknown,
                Status = SystemHealthStatus.Unhealthy,
                Severity = LogSeverity.Error,
                Message = "Health check failed unexpectedly.",
                LatencyMs = null
            };
        }

        await _systemHealthLogService.PersistAsync(result, cancellationToken);
        return result;
    }

    private static ComponentHealthDto Map(HealthCheckResult result) => new()
    {
        Name = result.Name,
        Status = MonitoringHealthMapper.ToStatusString(result.Status),
        LatencyMs = result.LatencyMs,
        Message = result.Message
    };

    private static SystemHealthStatus ParseStatus(string? status) =>
        Enum.TryParse<SystemHealthStatus>(status, ignoreCase: true, out var parsed)
            ? parsed
            : SystemHealthStatus.Unknown;
}
