using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Domain.ValidationLab;

/// <summary>
/// Durable identity for one validation audit-execution attempt (Milestone 23.0E2C1).
/// External identity is <see cref="AuditExecutionId"/>; numeric <see cref="Entity.Id"/> is storage-only.
/// </summary>
public class ValidationAuditExecution : Entity
{
    public const string ContractVersionV1 = "ValidationAuditExecution/v1";

    /// <summary>Immutable public identity for this audit execution.</summary>
    public Guid AuditExecutionId { get; set; }

    public long ValidationExperimentId { get; set; }
    public long ValidationTrialId { get; set; }
    public int TrialNumber { get; set; }

    /// <summary>Scope identity bound before candle access; immutable after creation.</summary>
    public Guid ScopeExecutionId { get; set; }

    public int AttemptNumber { get; set; }

    /// <summary>Opaque execution token; max length 128 at persistence.</summary>
    public string ExecutionToken { get; set; } = string.Empty;

    /// <summary>Optional lease owner; max length 128 at persistence.</summary>
    public string? LeaseOwner { get; set; }

    public ValidationAuditExecutionType ExecutionType { get; set; } = ValidationAuditExecutionType.Trial;
    public ValidationAuditExecutionStatus Status { get; set; } = ValidationAuditExecutionStatus.Created;

    public DateTime StartedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? SupersededAtUtc { get; set; }

    public Guid? SupersededByAuditExecutionId { get; set; }

    /// <summary>Safe failure / supersession reason code; max length 128 at persistence.</summary>
    public string? FailureCode { get; set; }

    public ValidationAuditRecoveryStatus RecoveryStatus { get; set; } = ValidationAuditRecoveryStatus.None;

    public long? FinalExpectedSequence { get; set; }
    public long LastConfirmedSequence { get; set; }
    public int? ExpectedEventCount { get; set; }
    public int ConfirmedEventCount { get; set; }

    /// <summary>Uppercase SHA-256 of the complete ordered payload set; max length 64.</summary>
    public string? FinalPayloadSetHash { get; set; }

    public string AuditContractVersion { get; set; } = ContractVersionV1;

    /// <summary>
    /// When true, <see cref="FinalExpectedSequence"/> of zero is permitted at completion.
    /// Default false — zero-access success must never be inferred after interruption.
    /// </summary>
    public bool AllowsZeroAccess { get; set; }

    /// <summary>Optimistic concurrency token; starts at 1.</summary>
    public ulong RowVersion { get; set; } = 1;

    /// <summary>
    /// Advances <see cref="LastConfirmedSequence"/> monotonically. Decreases are rejected.
    /// </summary>
    public void AdvanceLastConfirmedSequence(long newLast, DateTime utcNow)
    {
        if (newLast < LastConfirmedSequence)
        {
            throw new InvalidOperationException(
                $"LastConfirmedSequence cannot decrease from {LastConfirmedSequence} to {newLast}.");
        }

        if (!CanAdvanceSequence(newLast))
        {
            throw new InvalidOperationException(
                $"Cannot advance LastConfirmedSequence to {newLast} when Status is {Status}.");
        }

        LastConfirmedSequence = newLast;
        UpdatedAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }

    /// <summary>
    /// Declares or confirms <see cref="FinalExpectedSequence"/>. Cannot change after Completed.
    /// </summary>
    public void SetFinalExpectedSequence(long value, DateTime utcNow)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "FinalExpectedSequence must be >= 0.");
        }

        if (Status == ValidationAuditExecutionStatus.Completed)
        {
            if (FinalExpectedSequence is long existing && existing == value)
            {
                return;
            }

            throw new InvalidOperationException(
                $"FinalExpectedSequence cannot change after Completion (locked to {FinalExpectedSequence}).");
        }

        if (Status == ValidationAuditExecutionStatus.Superseded)
        {
            throw new InvalidOperationException(
                $"FinalExpectedSequence cannot be set on a Superseded execution.");
        }

        if (FinalExpectedSequence is long locked && locked != value)
        {
            throw new InvalidOperationException(
                $"FinalExpectedSequence is already set to {locked} and cannot change to {value}.");
        }

        FinalExpectedSequence = value;
        UpdatedAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }

    /// <summary>
    /// Returns true when <paramref name="newLast"/> is monotonic (does not decrease)
    /// and the execution is not in a terminal non-advancing state.
    /// </summary>
    public bool CanAdvanceSequence(long newLast)
    {
        if (Status is ValidationAuditExecutionStatus.Completed
            or ValidationAuditExecutionStatus.Superseded
            or ValidationAuditExecutionStatus.Failed)
        {
            return false;
        }

        return newLast >= LastConfirmedSequence;
    }

    /// <summary>
    /// Marks this execution superseded for rerun. Completed executions cannot be superseded.
    /// Already-superseded executions remain terminal and are not mutated again.
    /// </summary>
    public void MarkSuperseded(
        Guid supersededByAuditExecutionId,
        DateTime utcNow,
        string? failureCode = null)
    {
        if (Status == ValidationAuditExecutionStatus.Completed)
        {
            throw new InvalidOperationException(
                $"Audit execution {AuditExecutionId} is Completed and cannot be superseded.");
        }

        if (Status == ValidationAuditExecutionStatus.Superseded)
        {
            throw new InvalidOperationException(
                $"Audit execution {AuditExecutionId} is already Superseded.");
        }

        if (supersededByAuditExecutionId == Guid.Empty)
        {
            throw new ArgumentException(
                "SupersededByAuditExecutionId must be a non-empty Guid.",
                nameof(supersededByAuditExecutionId));
        }

        var now = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        Status = ValidationAuditExecutionStatus.Superseded;
        SupersededAtUtc = now;
        SupersededByAuditExecutionId = supersededByAuditExecutionId;
        RecoveryStatus = ValidationAuditRecoveryStatus.SupersededForRerun;
        if (!string.IsNullOrWhiteSpace(failureCode))
        {
            FailureCode = failureCode;
        }

        UpdatedAtUtc = now;
    }

    /// <summary>
    /// Validates WP1 completion preconditions. Does not mutate state.
    /// <paramref name="hasUnresolvedBatch"/> must be supplied by the caller from durable batch state.
    /// </summary>
    public ValidationAuditCompletenessCode ValidateCompletionPreconditions(bool hasUnresolvedBatch = false)
    {
        if (Status == ValidationAuditExecutionStatus.Superseded)
        {
            return ValidationAuditCompletenessCode.Superseded;
        }

        if (Status == ValidationAuditExecutionStatus.RecoveryRequired)
        {
            return ValidationAuditCompletenessCode.RecoveryRequired;
        }

        if (Status is ValidationAuditExecutionStatus.Created
            or ValidationAuditExecutionStatus.InProgress
            or ValidationAuditExecutionStatus.FlushManifested
            or ValidationAuditExecutionStatus.EventsConfirmed)
        {
            // Still progressing unless structural fields already prove incompleteness below.
        }

        if (FinalExpectedSequence is null)
        {
            return ValidationAuditCompletenessCode.FinalSequenceMissing;
        }

        if (FinalExpectedSequence.Value == 0 && !AllowsZeroAccess)
        {
            // Zero final sequence requires an explicit no-access contract.
            return ValidationAuditCompletenessCode.FinalSequenceMissing;
        }

        if (ExpectedEventCount is null)
        {
            return ValidationAuditCompletenessCode.EventMissing;
        }

        if (string.IsNullOrWhiteSpace(FinalPayloadSetHash))
        {
            return ValidationAuditCompletenessCode.PayloadMismatch;
        }

        if (hasUnresolvedBatch)
        {
            return ValidationAuditCompletenessCode.ManifestMissing;
        }

        if (LastConfirmedSequence != FinalExpectedSequence.Value)
        {
            return LastConfirmedSequence < FinalExpectedSequence.Value
                ? ValidationAuditCompletenessCode.SequenceGap
                : ValidationAuditCompletenessCode.DuplicateSequence;
        }

        if (ConfirmedEventCount != ExpectedEventCount.Value)
        {
            return ConfirmedEventCount < ExpectedEventCount.Value
                ? ValidationAuditCompletenessCode.EventMissing
                : ValidationAuditCompletenessCode.DuplicateSequence;
        }

        if (Status is ValidationAuditExecutionStatus.Failed)
        {
            return ValidationAuditCompletenessCode.RecoveryRequired;
        }

        return ValidationAuditCompletenessCode.Complete;
    }
}
