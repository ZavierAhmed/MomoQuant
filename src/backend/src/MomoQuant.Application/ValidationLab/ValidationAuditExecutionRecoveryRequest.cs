using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Optional restart context for durable audit recovery (Milestone 23.0E2C1C).
/// </summary>
public sealed class ValidationAuditExecutionRecoveryRequest
{
    public string? CurrentLeaseOwner { get; init; }
    public bool IsResume { get; init; }
    public ValidationTrialStatus? TrialStatus { get; init; }
}
