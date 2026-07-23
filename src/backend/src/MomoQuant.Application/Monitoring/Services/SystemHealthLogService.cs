using Microsoft.Extensions.Logging;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Monitoring.Abstractions;
using MomoQuant.Application.Monitoring.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Monitoring.Services;

public interface ISystemHealthLogService
{
    Task PersistAsync(HealthCheckResult result, CancellationToken cancellationToken = default);
    Task<ServiceResult<Shared.Contracts.PagedResult<SystemHealthLogDto>>> GetPagedAsync(
        MonitoringQuery query,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<SystemHealthLogDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default);
}

public sealed class SystemHealthLogService : ISystemHealthLogService
{
    private readonly ISystemHealthLogRepository _repository;
    private readonly IMonitoringQueryValidator _validator;
    private readonly ILogger<SystemHealthLogService> _logger;

    public SystemHealthLogService(
        ISystemHealthLogRepository repository,
        IMonitoringQueryValidator validator,
        ILogger<SystemHealthLogService> logger)
    {
        _repository = repository;
        _validator = validator;
        _logger = logger;
    }

    public async Task PersistAsync(HealthCheckResult result, CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            await _repository.AddAsync(new Domain.Monitoring.SystemHealthLog
            {
                ServiceName = result.Subsystem.ToString(),
                Status = result.Status,
                Severity = result.Severity,
                Message = result.Message,
                DetailsJson = result.DetailsJson,
                LatencyMs = result.LatencyMs,
                CheckedAtUtc = now,
                CreatedAtUtc = now
            }, cancellationToken);

            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist system health log for {Subsystem}.", result.Subsystem);
        }
    }

    public async Task<ServiceResult<Shared.Contracts.PagedResult<SystemHealthLogDto>>> GetPagedAsync(
        MonitoringQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(query, requirePagination: true);
        if (!validation.Succeeded || validation.Data is null)
        {
            return ServiceResult<Shared.Contracts.PagedResult<SystemHealthLogDto>>.Fail(
                validation.ErrorMessage ?? "Invalid query.",
                validation.ErrorField);
        }

        var (items, totalCount) = await _repository.GetPagedAsync(validation.Data, cancellationToken);
        return ServiceResult<Shared.Contracts.PagedResult<SystemHealthLogDto>>.Ok(new Shared.Contracts.PagedResult<SystemHealthLogDto>
        {
            Items = items.Select(Map).ToList(),
            Page = validation.Data.Page,
            PageSize = validation.Data.PageSize,
            TotalCount = totalCount
        });
    }

    public async Task<ServiceResult<SystemHealthLogDto>> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var log = await _repository.GetByIdAsync(id, cancellationToken);
        return log is null
            ? ServiceResult<SystemHealthLogDto>.Fail("System health log was not found.")
            : ServiceResult<SystemHealthLogDto>.Ok(Map(log));
    }

    private static SystemHealthLogDto Map(Domain.Monitoring.SystemHealthLog log) => new()
    {
        Id = log.Id,
        Subsystem = log.ServiceName,
        Status = MonitoringHealthMapper.ToStatusString(log.Status),
        Severity = MonitoringHealthMapper.ToSeverityString(log.Severity),
        Message = log.Message,
        DetailsJson = SensitiveDataSanitizer.SanitizeJson(log.DetailsJson),
        LatencyMs = log.LatencyMs,
        CheckedAtUtc = log.CheckedAtUtc,
        CreatedAt = log.CreatedAtUtc
    };
}
