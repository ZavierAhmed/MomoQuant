using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Monitoring.Dtos;
using MomoQuant.Domain.Audit;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Monitoring.Services;

public interface IRecentErrorService
{
    Task<ServiceResult<IReadOnlyList<RecentErrorDto>>> GetRecentErrorsAsync(
        MonitoringQuery query,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<RecentEventDto>>> GetRecentEventsAsync(
        MonitoringQuery query,
        CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<SafetyEventDto>>> GetSafetyEventsAsync(
        MonitoringQuery query,
        CancellationToken cancellationToken = default);
}

public sealed class RecentErrorService : IRecentErrorService
{
    private readonly ISystemHealthLogRepository _systemHealthLogRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IMonitoringQueryValidator _validator;

    public RecentErrorService(
        ISystemHealthLogRepository systemHealthLogRepository,
        IAuditLogRepository auditLogRepository,
        IMonitoringQueryValidator validator)
    {
        _systemHealthLogRepository = systemHealthLogRepository;
        _auditLogRepository = auditLogRepository;
        _validator = validator;
    }

    public async Task<ServiceResult<IReadOnlyList<RecentErrorDto>>> GetRecentErrorsAsync(
        MonitoringQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateList(query);
        if (!validation.Succeeded || validation.Data is null)
        {
            return ServiceResult<IReadOnlyList<RecentErrorDto>>.Fail(
                validation.ErrorMessage ?? "Invalid query.",
                validation.ErrorField);
        }

        var filter = validation.Data;
        var logs = await _systemHealthLogRepository.GetRecentAsync(filter, cancellationToken);
        var errors = logs
            .Where(log => log.Severity is LogSeverity.Error or LogSeverity.Critical)
            .Select(log => new RecentErrorDto
            {
                Id = log.Id,
                Source = "SystemHealthLog",
                Subsystem = log.ServiceName,
                Severity = MonitoringHealthMapper.ToSeverityString(log.Severity),
                Message = log.Message,
                OccurredAtUtc = log.CheckedAtUtc
            })
            .Take(filter.Limit)
            .ToList();

        return ServiceResult<IReadOnlyList<RecentErrorDto>>.Ok(errors);
    }

    public async Task<ServiceResult<IReadOnlyList<RecentEventDto>>> GetRecentEventsAsync(
        MonitoringQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateList(query);
        if (!validation.Succeeded || validation.Data is null)
        {
            return ServiceResult<IReadOnlyList<RecentEventDto>>.Fail(
                validation.ErrorMessage ?? "Invalid query.",
                validation.ErrorField);
        }

        var healthLogs = await _systemHealthLogRepository.GetRecentAsync(validation.Data, cancellationToken);
        var events = healthLogs.Select(log => new RecentEventDto
        {
            Id = log.Id,
            EventType = log.Status.ToString(),
            Subsystem = log.ServiceName,
            Severity = MonitoringHealthMapper.ToSeverityString(log.Severity),
            Message = log.Message,
            OccurredAtUtc = log.CheckedAtUtc
        }).Take(validation.Data.Limit).ToList();

        return ServiceResult<IReadOnlyList<RecentEventDto>>.Ok(events);
    }

    public async Task<ServiceResult<IReadOnlyList<SafetyEventDto>>> GetSafetyEventsAsync(
        MonitoringQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.ValidateList(query);
        if (!validation.Succeeded || validation.Data is null)
        {
            return ServiceResult<IReadOnlyList<SafetyEventDto>>.Fail(
                validation.ErrorMessage ?? "Invalid query.",
                validation.ErrorField);
        }

        var auditFilter = new Audit.Dtos.AuditLogQueryFilter
        {
            FromUtc = validation.Data.FromUtc,
            ToUtc = validation.Data.ToUtc,
            UserId = validation.Data.UserId,
            Page = 1,
            PageSize = validation.Data.Limit
        };

        var (auditLogs, _) = await _auditLogRepository.GetPagedAsync(auditFilter, cancellationToken);
        var criticalHealth = await _systemHealthLogRepository.GetRecentAsync(new MonitoringQueryFilter
        {
            FromUtc = validation.Data.FromUtc,
            ToUtc = validation.Data.ToUtc,
            Severity = LogSeverity.Critical,
            Limit = validation.Data.Limit,
            Page = 1,
            PageSize = validation.Data.Limit
        }, cancellationToken);

        var safetyEvents = auditLogs
            .Where(log => AuditActions.SafetyActions.Contains(log.Action))
            .Select(MapSafetyFromAudit)
            .Concat(criticalHealth.Select(MapSafetyFromHealth))
            .OrderByDescending(item => item.OccurredAtUtc)
            .Take(validation.Data.Limit)
            .ToList();

        return ServiceResult<IReadOnlyList<SafetyEventDto>>.Ok(safetyEvents);
    }

    private static SafetyEventDto MapSafetyFromAudit(Domain.Audit.AuditLog log) => new()
    {
        Id = log.Id,
        EventType = log.Action,
        Severity = MonitoringHealthMapper.ToSeverityString(log.Severity),
        Message = $"{log.Action} on {log.EntityType}",
        UserId = log.UserId,
        UserEmail = log.UserEmail,
        OccurredAtUtc = log.CreatedAtUtc
    };

    private static SafetyEventDto MapSafetyFromHealth(Domain.Monitoring.SystemHealthLog log) => new()
    {
        Id = log.Id,
        EventType = AuditActions.SystemHealthCheckFailed,
        Severity = MonitoringHealthMapper.ToSeverityString(log.Severity),
        Message = log.Message,
        OccurredAtUtc = log.CheckedAtUtc
    };
}
