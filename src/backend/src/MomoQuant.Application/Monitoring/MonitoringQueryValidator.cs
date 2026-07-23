using MomoQuant.Application.Common;
using MomoQuant.Application.Monitoring.Dtos;
using MomoQuant.Domain.Audit;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Monitoring;

public interface IMonitoringQueryValidator
{
    ServiceResult<MonitoringQueryFilter> Validate(MonitoringQuery query, bool requirePagination = false);
    ServiceResult<MonitoringQueryFilter> ValidateList(MonitoringQuery query);
}

public sealed class MonitoringQueryValidator : IMonitoringQueryValidator
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int MaxLimit = 200;

    private static readonly TimeSpan DefaultRange = TimeSpan.FromDays(7);

    public ServiceResult<MonitoringQueryFilter> Validate(MonitoringQuery query, bool requirePagination = false) =>
        ValidateInternal(query, requirePagination, applyDefaultRange: true);

    public ServiceResult<MonitoringQueryFilter> ValidateList(MonitoringQuery query) =>
        ValidateInternal(query, requirePagination: false, applyDefaultRange: true);

    private static ServiceResult<MonitoringQueryFilter> ValidateInternal(
        MonitoringQuery query,
        bool requirePagination,
        bool applyDefaultRange)
    {
        var toUtc = query.ToUtc ?? DateTime.UtcNow;
        var fromUtc = query.FromUtc ?? (applyDefaultRange ? toUtc.Subtract(DefaultRange) : DateTime.MinValue);

        if (query.FromUtc.HasValue && query.ToUtc.HasValue && fromUtc >= toUtc)
        {
            return ServiceResult<MonitoringQueryFilter>.Fail("FromUtc must be before ToUtc.", "fromUtc");
        }

        LogSeverity? severity = null;
        if (!string.IsNullOrWhiteSpace(query.Severity))
        {
            if (!Enum.TryParse<LogSeverity>(query.Severity, ignoreCase: true, out var parsedSeverity))
            {
                return ServiceResult<MonitoringQueryFilter>.Fail("Severity is invalid.", "severity");
            }

            severity = parsedSeverity;
        }

        MonitoringSubsystem? subsystem = null;
        if (!string.IsNullOrWhiteSpace(query.Subsystem))
        {
            if (!Enum.TryParse<MonitoringSubsystem>(query.Subsystem, ignoreCase: true, out var parsedSubsystem))
            {
                return ServiceResult<MonitoringQueryFilter>.Fail("Subsystem is invalid.", "subsystem");
            }

            subsystem = parsedSubsystem;
        }

        if (!string.IsNullOrWhiteSpace(query.EventType) && !AuditActions.All.Contains(query.EventType))
        {
            return ServiceResult<MonitoringQueryFilter>.Fail("EventType is invalid.", "eventType");
        }

        TradingMode? mode = null;
        if (!string.IsNullOrWhiteSpace(query.Mode))
        {
            if (!Enum.TryParse<TradingMode>(query.Mode, ignoreCase: true, out var parsedMode))
            {
                return ServiceResult<MonitoringQueryFilter>.Fail("Mode is invalid.", "mode");
            }

            mode = parsedMode;
        }

        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? DefaultPageSize : query.PageSize;
        if (pageSize > MaxPageSize)
        {
            return ServiceResult<MonitoringQueryFilter>.Fail($"PageSize cannot exceed {MaxPageSize}.", "pageSize");
        }

        var limit = query.Limit ?? DefaultPageSize;
        if (limit <= 0)
        {
            limit = DefaultPageSize;
        }

        if (limit > MaxLimit)
        {
            return ServiceResult<MonitoringQueryFilter>.Fail($"Limit cannot exceed {MaxLimit}.", "limit");
        }

        if (requirePagination && pageSize > MaxPageSize)
        {
            return ServiceResult<MonitoringQueryFilter>.Fail($"PageSize cannot exceed {MaxPageSize}.", "pageSize");
        }

        return ServiceResult<MonitoringQueryFilter>.Ok(new MonitoringQueryFilter
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Severity = severity,
            Subsystem = subsystem,
            EventType = query.EventType,
            UserId = query.UserId,
            Mode = mode,
            Limit = limit,
            Page = page,
            PageSize = pageSize
        });
    }
}
