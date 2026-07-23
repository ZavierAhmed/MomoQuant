namespace MomoQuant.Domain.Monitoring;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class SystemHealthLog : Entity
{
    public string ServiceName { get; set; } = string.Empty;
    public SystemHealthStatus Status { get; set; }
    public LogSeverity Severity { get; set; } = LogSeverity.Info;
    public string Message { get; set; } = string.Empty;
    public string? DetailsJson { get; set; }
    public int? LatencyMs { get; set; }
    public DateTime CheckedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
