using MomoQuant.Application.Audit.Dtos;
using MomoQuant.Application.Common;
using MomoQuant.Application.Monitoring;
using MomoQuant.Domain.Audit;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Audit;

public interface IAuditLogQueryValidator
{
    ServiceResult<AuditLogQueryFilter> Validate(AuditLogQuery query);
}

public sealed class AuditLogQueryValidator : IAuditLogQueryValidator
{
    private static readonly TimeSpan DefaultRange = TimeSpan.FromDays(30);

    public ServiceResult<AuditLogQueryFilter> Validate(AuditLogQuery query)
    {
        var toUtc = query.ToUtc ?? DateTime.UtcNow;
        var fromUtc = query.FromUtc ?? toUtc.Subtract(DefaultRange);

        if (query.FromUtc.HasValue && query.ToUtc.HasValue && fromUtc >= toUtc)
        {
            return ServiceResult<AuditLogQueryFilter>.Fail("FromUtc must be before ToUtc.", "fromUtc");
        }

        LogSeverity? severity = null;
        if (!string.IsNullOrWhiteSpace(query.Severity))
        {
            if (!Enum.TryParse<LogSeverity>(query.Severity, ignoreCase: true, out var parsedSeverity))
            {
                return ServiceResult<AuditLogQueryFilter>.Fail("Severity is invalid.", "severity");
            }

            severity = parsedSeverity;
        }

        if (!string.IsNullOrWhiteSpace(query.EventType) && !AuditActions.All.Contains(query.EventType))
        {
            return ServiceResult<AuditLogQueryFilter>.Fail("EventType is invalid.", "eventType");
        }

        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? MonitoringQueryValidator.DefaultPageSize : query.PageSize;
        if (pageSize > MonitoringQueryValidator.MaxPageSize)
        {
            return ServiceResult<AuditLogQueryFilter>.Fail(
                $"PageSize cannot exceed {MonitoringQueryValidator.MaxPageSize}.",
                "pageSize");
        }

        return ServiceResult<AuditLogQueryFilter>.Ok(new AuditLogQueryFilter
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Severity = severity,
            Action = query.EventType,
            UserId = query.UserId,
            Page = page,
            PageSize = pageSize
        });
    }
}
