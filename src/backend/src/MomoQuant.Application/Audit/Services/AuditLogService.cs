using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Audit.Dtos;
using MomoQuant.Application.Common;
using MomoQuant.Application.Monitoring;
using MomoQuant.Domain.Enums;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Audit.Services;

public interface IAuditLogService
{
    Task<ServiceResult<PagedResult<AuditLogDto>>> GetLogsAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
    Task<ServiceResult<AuditLogDto>> GetLogByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<ServiceResult<AuditSummaryDto>> GetSummaryAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
}

public sealed class AuditLogService : IAuditLogService
{
    private readonly IAuditLogRepository _repository;
    private readonly IAuditLogQueryValidator _validator;

    public AuditLogService(IAuditLogRepository repository, IAuditLogQueryValidator validator)
    {
        _repository = repository;
        _validator = validator;
    }

    public async Task<ServiceResult<PagedResult<AuditLogDto>>> GetLogsAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(query);
        if (!validation.Succeeded || validation.Data is null)
        {
            return ServiceResult<PagedResult<AuditLogDto>>.Fail(
                validation.ErrorMessage ?? "Invalid query.",
                validation.ErrorField);
        }

        var (items, totalCount) = await _repository.GetPagedAsync(validation.Data, cancellationToken);
        return ServiceResult<PagedResult<AuditLogDto>>.Ok(new PagedResult<AuditLogDto>
        {
            Items = items.Select(Map).ToList(),
            Page = validation.Data.Page,
            PageSize = validation.Data.PageSize,
            TotalCount = totalCount
        });
    }

    public async Task<ServiceResult<AuditLogDto>> GetLogByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var log = await _repository.GetByIdAsync(id, cancellationToken);
        return log is null
            ? ServiceResult<AuditLogDto>.Fail("Audit log was not found.")
            : ServiceResult<AuditLogDto>.Ok(Map(log));
    }

    public async Task<ServiceResult<AuditSummaryDto>> GetSummaryAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default)
    {
        var validation = _validator.Validate(query);
        if (!validation.Succeeded || validation.Data is null)
        {
            return ServiceResult<AuditSummaryDto>.Fail(
                validation.ErrorMessage ?? "Invalid query.",
                validation.ErrorField);
        }

        var filter = validation.Data;
        var total = await _repository.CountAsync(filter, cancellationToken);
        var topActions = await _repository.GetActionCountsAsync(filter, top: 10, cancellationToken);

        async Task<int> CountSeverity(LogSeverity severity)
        {
            var severityFilter = filter with { Severity = severity };
            return await _repository.CountAsync(severityFilter, cancellationToken);
        }

        return ServiceResult<AuditSummaryDto>.Ok(new AuditSummaryDto
        {
            TotalLogs = total,
            CriticalCount = await CountSeverity(LogSeverity.Critical),
            ErrorCount = await CountSeverity(LogSeverity.Error),
            WarningCount = await CountSeverity(LogSeverity.Warning),
            InfoCount = await CountSeverity(LogSeverity.Info),
            TopActions = topActions
                .Select(item => new AuditActionCountDto { Action = item.Action, Count = item.Count })
                .ToList(),
            GeneratedAtUtc = DateTime.UtcNow
        });
    }

    private static AuditLogDto Map(Domain.Audit.AuditLog log) => new()
    {
        Id = log.Id,
        UserId = log.UserId,
        UserEmail = log.UserEmail,
        Action = log.Action,
        EntityType = log.EntityType,
        EntityId = log.EntityId,
        Severity = MonitoringHealthMapper.ToSeverityString(log.Severity),
        IpAddress = log.IpAddress,
        UserAgent = log.UserAgent,
        OldValuesJson = SensitiveDataSanitizer.SanitizeJson(log.OldValueJson),
        NewValuesJson = SensitiveDataSanitizer.SanitizeJson(log.NewValueJson),
        MetadataJson = SensitiveDataSanitizer.SanitizeJson(log.MetadataJson),
        CreatedAt = log.CreatedAtUtc
    };
}
