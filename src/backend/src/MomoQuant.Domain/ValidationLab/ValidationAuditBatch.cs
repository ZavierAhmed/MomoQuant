using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Domain.ValidationLab;

/// <summary>
/// Immutable durable batch manifest for one flush of access-audit events (Milestone 23.0E2C1).
/// Logical FK is <see cref="AuditExecutionId"/> (not the numeric execution row Id).
/// </summary>
public class ValidationAuditBatch : Entity
{
    public const string ContractVersionV1 = "ValidationAuditBatch/v1";

    /// <summary>Immutable public identity for this batch manifest.</summary>
    public Guid AuditBatchId { get; set; }

    /// <summary>Logical FK to <see cref="ValidationAuditExecution.AuditExecutionId"/>.</summary>
    public Guid AuditExecutionId { get; set; }

    public int BatchNumber { get; set; }
    public long FirstSequence { get; set; }
    public long LastSequence { get; set; }
    public int ExpectedEventCount { get; set; }

    /// <summary>Canonical ordered AccessEventId JSON (longtext at persistence).</summary>
    public string ExpectedEventIdsJson { get; set; } = "[]";

    /// <summary>Canonical ordered AccessPayloadHash JSON (longtext at persistence).</summary>
    public string ExpectedPayloadHashesJson { get; set; } = "[]";

    /// <summary>Uppercase SHA-256 of the ordered manifest payload set; max length 64.</summary>
    public string ExpectedPayloadSetHash { get; set; } = string.Empty;

    public ValidationAuditBatchStatus Status { get; set; } = ValidationAuditBatchStatus.Created;

    public int PersistenceAttemptCount { get; set; }
    public int ConfirmationAttemptCount { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }

    /// <summary>Safe failure code; max length 128 at persistence.</summary>
    public string? FailureCode { get; set; }

    public string AuditBatchContractVersion { get; set; } = ContractVersionV1;

    /// <summary>Optimistic concurrency token; starts at 1.</summary>
    public ulong RowVersion { get; set; } = 1;

    /// <summary>
    /// Returns true when this batch's inclusive sequence range overlaps
    /// <paramref name="other"/>'s range (same execution assumed by caller).
    /// </summary>
    public bool Overlaps(ValidationAuditBatch other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return FirstSequence <= other.LastSequence && other.FirstSequence <= LastSequence;
    }

    /// <summary>
    /// Fail-closed validation that the declared range is contiguous and matches
    /// <see cref="ExpectedEventCount"/>.
    /// </summary>
    public void ValidateRangeIntegrity()
    {
        if (FirstSequence < 1 || LastSequence < FirstSequence)
        {
            throw new InvalidOperationException(
                $"Batch {AuditBatchId} has invalid sequence range [{FirstSequence},{LastSequence}].");
        }

        var span = LastSequence - FirstSequence + 1;
        if (span != ExpectedEventCount)
        {
            throw new InvalidOperationException(
                $"Batch {AuditBatchId} ExpectedEventCount {ExpectedEventCount} does not match range span {span}.");
        }
    }
}
