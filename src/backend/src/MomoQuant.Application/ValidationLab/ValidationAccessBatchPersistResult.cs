namespace MomoQuant.Application.ValidationLab;

public enum ValidationAccessBatchCommitStatus
{
    NotAttempted = 0,
    CommitSucceeded = 1,
    CommitOutcomeUnknown = 2,
    KnownRolledBack = 3,
    FailedPermanent = 4
}

public enum ValidationAccessBatchVerificationStatus
{
    NotAttempted = 0,
    FullyPayloadConfirmed = 1,
    PartiallyPayloadConfirmed = 2,
    ConfirmationUnavailable = 3,
    PayloadConflict = 4,
    InputConflict = 5,
    FailedPermanent = 6
}

public enum ValidationAccessBatchRecoveryStatus
{
    None = 0,
    ConfirmedAfterNormalCommit = 1,
    ConfirmedAfterAmbiguousCommit = 2,
    MissingEventsRetriedAndConfirmed = 3,
    RetryExhausted = 4
}

/// <summary>
/// Versioned (v2) outcome of an idempotent validation candle-access audit batch persist.
/// Full confirmation requires every distinct requested AccessEventId to be represented by a row
/// whose canonical immutable payload matches the requested event — ID existence alone is never enough.
/// </summary>
public sealed class ValidationAccessBatchPersistResult
{
    public const string ContractVersion = "ValidationAccessBatchPersistResult/v2";

    public string ResultContractVersion { get; init; } = ContractVersion;

    /// <summary>Distinct AccessEventIds the caller asked to persist.</summary>
    public IReadOnlyList<Guid> RequestedEventIds { get; init; } = Array.Empty<Guid>();

    /// <summary>IDs that appeared more than once in the input batch with byte-identical canonical payloads.</summary>
    public IReadOnlyList<Guid> IdenticalInputDuplicateEventIds { get; init; } = Array.Empty<Guid>();

    /// <summary>IDs actually submitted to the write transaction (missing rows only).</summary>
    public IReadOnlyList<Guid> AttemptedEventIds { get; init; } = Array.Empty<Guid>();

    /// <summary>Pre-existing rows whose stored hash matched the requested canonical payload hash.</summary>
    public IReadOnlyList<Guid> ExistingPayloadVerifiedEventIds { get; init; } = Array.Empty<Guid>();

    /// <summary>Pre-existing hashless (historical) rows verified by full canonical payload comparison.</summary>
    public IReadOnlyList<Guid> LegacyPayloadVerifiedEventIds { get; init; } = Array.Empty<Guid>();

    /// <summary>Diagnostic only: IDs believed newly inserted by this caller. Race-sensitive; never used for correctness.</summary>
    public IReadOnlyList<Guid> NewlyInsertedEventIds { get; init; } = Array.Empty<Guid>();

    /// <summary>Diagnostic only: IDs believed to have existed before this call. Race-sensitive; never used for correctness.</summary>
    public IReadOnlyList<Guid> AlreadyExistingEventIds { get; init; } = Array.Empty<Guid>();

    /// <summary>IDs confirmed durable with a matching canonical payload (the correctness set).</summary>
    public IReadOnlyList<Guid> ConfirmedMatchingEventIds { get; init; } = Array.Empty<Guid>();

    /// <summary>Confirmed payload hash per confirmed event, for caller-side re-verification.</summary>
    public IReadOnlyDictionary<Guid, string> ConfirmedPayloadHashes { get; init; } =
        new Dictionary<Guid, string>();

    /// <summary>Requested IDs not found durable during final confirmation.</summary>
    public IReadOnlyList<Guid> MissingEventIds { get; init; } = Array.Empty<Guid>();

    /// <summary>Requested IDs whose persisted payload conflicts with the requested payload.</summary>
    public IReadOnlyList<Guid> PayloadConflictEventIds { get; init; } = Array.Empty<Guid>();

    /// <summary>IDs duplicated inside the input batch with conflicting payloads.</summary>
    public IReadOnlyList<Guid> InputConflictEventIds { get; init; } = Array.Empty<Guid>();

    public ValidationAccessBatchCommitStatus CommitStatus { get; init; }
    public ValidationAccessBatchVerificationStatus VerificationStatus { get; init; }
    public ValidationAccessBatchRecoveryStatus RecoveryStatus { get; init; }

    public int PersistenceAttemptCount { get; init; }
    public int ConfirmationAttemptCount { get; init; }
    public bool UsedFreshConfirmationContext { get; init; }
    public string? LastSafeErrorCode { get; init; }
    public DateTime CompletedAtUtc { get; init; }

    public int NewlyInsertedCount => NewlyInsertedEventIds.Count;
    public int AlreadyExistingCount => AlreadyExistingEventIds.Count;
    public int ConfirmedCount => ConfirmedMatchingEventIds.Count;
    public int MissingCount => MissingEventIds.Count;
    public int PayloadConflictCount => PayloadConflictEventIds.Count;

    /// <summary>
    /// True only when every distinct requested event is payload-confirmed, nothing is missing,
    /// no payload or input conflict exists, and verification reached FullyPayloadConfirmed.
    /// </summary>
    public bool IsFullyConfirmed
    {
        get
        {
            if (VerificationStatus != ValidationAccessBatchVerificationStatus.FullyPayloadConfirmed)
            {
                return false;
            }

            if (MissingEventIds.Count != 0
                || PayloadConflictEventIds.Count != 0
                || InputConflictEventIds.Count != 0)
            {
                return false;
            }

            var requested = RequestedEventIds.ToHashSet();
            return requested.Count == ConfirmedMatchingEventIds.Count
                && requested.SetEquals(ConfirmedMatchingEventIds);
        }
    }

    public static ValidationAccessBatchPersistResult EmptyNoWork() => new()
    {
        CommitStatus = ValidationAccessBatchCommitStatus.NotAttempted,
        VerificationStatus = ValidationAccessBatchVerificationStatus.FullyPayloadConfirmed,
        RecoveryStatus = ValidationAccessBatchRecoveryStatus.None,
        CompletedAtUtc = DateTime.UtcNow
    };
}
