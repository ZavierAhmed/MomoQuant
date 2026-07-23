namespace MomoQuant.Application.Common;

/// <summary>
/// Standardized research-operation status contract (Milestone 23.0 WP-P).
/// Progress is persisted by owning services; clients should poll at low frequency.
/// </summary>
public sealed class ResearchOperationStatus
{
    public string OperationId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string OperationType { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public decimal PercentComplete { get; init; }
    public int RequestedWorkCount { get; init; }
    public int CompletedWorkCount { get; init; }
    public int FailedWorkCount { get; init; }
    public string? ActiveWorkItem { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? LastProgressAtUtc { get; init; }
    public DateTime? LastHeartbeatAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public string? ErrorCode { get; init; }
    public string? UserSafeErrorMessage { get; init; }
    public string? DiagnosticReference { get; init; }
    public string? LeaseOwner { get; init; }
}

public static class ResearchOperationStatusMapper
{
    public static ResearchOperationStatus FromValidationTraining(
        long experimentId,
        string status,
        string stage,
        ValidationLab.ValidationTrainingProgressDto progress,
        string? leaseOwner = null,
        string? correlationId = null,
        string? errorCode = null,
        string? userSafeError = null) =>
        new()
        {
            OperationId = $"vl-train-{experimentId}",
            CorrelationId = correlationId ?? $"vl-{experimentId}",
            OperationType = "ValidationLaboratory.Training",
            EntityId = experimentId.ToString(),
            Stage = stage,
            Status = status,
            PercentComplete = progress.ProgressPercent,
            RequestedWorkCount = progress.RequestedTrialCount,
            CompletedWorkCount = progress.CompletedTrialCount,
            FailedWorkCount = progress.FailedTrialCount,
            ActiveWorkItem = progress.ActiveTrialNumber is int n ? $"Trial {n}" : null,
            LastProgressAtUtc = progress.LastProgressAtUtc,
            LeaseOwner = leaseOwner,
            ErrorCode = errorCode,
            UserSafeErrorMessage = userSafeError,
            DiagnosticReference = $"ValidationExperiment:{experimentId}"
        };
}
