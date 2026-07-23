using MomoQuant.Domain.Common;

namespace MomoQuant.Domain.Research;

/// <summary>
/// MySQL-backed research operation status (Milestone 23.0B restart durability).
/// Survives application-host restarts; progress updates are monotonic.
/// </summary>
public class ResearchOperationStatusEntity : Entity
{
    public string OperationId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal PercentComplete { get; set; }
    public int RequestedWorkCount { get; set; }
    public int CompletedWorkCount { get; set; }
    public int FailedWorkCount { get; set; }
    public string? ActiveWorkItem { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? LastProgressAtUtc { get; set; }
    public DateTime? LastHeartbeatAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorCode { get; set; }
    public string? UserSafeErrorMessage { get; set; }
    public string? DiagnosticReference { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
