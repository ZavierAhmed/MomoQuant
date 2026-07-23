namespace MomoQuant.Domain.Audit;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class AuditLog : Entity
{
    public long? UserId { get; set; }
    public string? UserEmail { get; set; }
    public long? TradingSessionId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public long? EntityId { get; set; }
    public LogSeverity Severity { get; set; } = LogSeverity.Info;
    public string? OldValueJson { get; set; }
    public string? NewValueJson { get; set; }
    public string? MetadataJson { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
