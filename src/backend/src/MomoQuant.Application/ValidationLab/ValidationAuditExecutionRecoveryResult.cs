using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.ValidationLab;

/// <summary>Result of restart recovery for a durable audit execution (Milestone 23.0E2C1B).</summary>
public sealed class ValidationAuditExecutionRecoveryResult
{
    public Guid AuditExecutionId { get; init; }
    public ValidationAuditExecutionStatus PreviousStatus { get; init; }
    public ValidationAuditRecoveryDecision RecoveryDecision { get; init; }
    public int ConfirmedBatchCount { get; init; }
    public int UnresolvedBatchCount { get; init; }
    public long RecoveredLastConfirmedSequence { get; init; }
    public int RecoveredConfirmedEventCount { get; init; }
    public long? FirstMissingSequence { get; init; }
    public long? FinalExpectedSequence { get; init; }
    public bool CanContinueSameExecution { get; init; }
    public bool MustRerunTrial { get; init; }
    public bool RequiresStrategyLabExecution { get; init; }
    public bool IsComplete { get; init; }
    public string? FailureCode { get; init; }
}
