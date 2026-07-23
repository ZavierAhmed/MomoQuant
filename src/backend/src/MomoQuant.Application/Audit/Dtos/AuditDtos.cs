namespace MomoQuant.Application.Audit.Dtos;

public sealed class AuditLogQuery
{
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public string? Severity { get; init; }
    public string? EventType { get; init; }
    public long? UserId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public sealed class AuditLogDto
{
    public long Id { get; init; }
    public long? UserId { get; init; }
    public string? UserEmail { get; init; }
    public required string Action { get; init; }
    public string? EntityType { get; init; }
    public long? EntityId { get; init; }
    public required string Severity { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? OldValuesJson { get; init; }
    public string? NewValuesJson { get; init; }
    public string? MetadataJson { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class AuditSummaryDto
{
    public int TotalLogs { get; init; }
    public int CriticalCount { get; init; }
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public int InfoCount { get; init; }
    public required IReadOnlyList<AuditActionCountDto> TopActions { get; init; }
    public DateTime GeneratedAtUtc { get; init; }
}

public sealed class AuditActionCountDto
{
    public required string Action { get; init; }
    public int Count { get; init; }
}

public sealed record AuditLogQueryFilter
{
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public Domain.Enums.LogSeverity? Severity { get; init; }
    public string? Action { get; init; }
    public long? UserId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
