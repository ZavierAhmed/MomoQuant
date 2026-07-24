namespace MomoQuant.Application.ValidationLab;

public enum ValidationAccessBatchCommitStatus
{
    NotAttempted = 0,
    Committed = 1,
    Unknown = 2,
    Failed = 3
}

public enum ValidationAccessBatchVerificationStatus
{
    NotVerified = 0,
    FullyConfirmed = 1,
    PartiallyMissing = 2,
    Failed = 3
}

/// <summary>
/// Outcome of an idempotent validation candle-access audit batch persist.
/// Success requires <see cref="ConfirmedPersistedEventIds"/> to equal <see cref="RequestedEventIds"/> as sets.
/// </summary>
public sealed class ValidationAccessBatchPersistResult
{
    public IReadOnlyList<Guid> RequestedEventIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> NewlyInsertedEventIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> AlreadyExistingEventIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> ConfirmedPersistedEventIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> MissingEventIds { get; init; } = Array.Empty<Guid>();
    public ValidationAccessBatchCommitStatus CommitStatus { get; init; }
    public ValidationAccessBatchVerificationStatus VerificationStatus { get; init; }

    public int NewlyInsertedCount => NewlyInsertedEventIds.Count;
    public int AlreadyExistingCount => AlreadyExistingEventIds.Count;
    public int ConfirmedCount => ConfirmedPersistedEventIds.Count;
    public int MissingCount => MissingEventIds.Count;

    public bool IsFullyConfirmed
    {
        get
        {
            if (RequestedEventIds.Count != ConfirmedPersistedEventIds.Count)
            {
                return false;
            }

            var requested = RequestedEventIds.ToHashSet();
            return requested.SetEquals(ConfirmedPersistedEventIds);
        }
    }

    public static ValidationAccessBatchPersistResult EmptyNoWork() => new()
    {
        CommitStatus = ValidationAccessBatchCommitStatus.NotAttempted,
        VerificationStatus = ValidationAccessBatchVerificationStatus.FullyConfirmed
    };

    public static ValidationAccessBatchPersistResult Create(
        IReadOnlyList<Guid> requested,
        IReadOnlyList<Guid> newlyInserted,
        IReadOnlyList<Guid> alreadyExisting,
        IReadOnlyList<Guid> confirmed,
        ValidationAccessBatchCommitStatus commitStatus)
    {
        var requestedSet = requested.ToHashSet();
        var confirmedSet = confirmed.ToHashSet();
        var missing = requestedSet.Except(confirmedSet).OrderBy(x => x).ToList();
        var verification = missing.Count == 0
            ? ValidationAccessBatchVerificationStatus.FullyConfirmed
            : missing.Count == requestedSet.Count
                ? ValidationAccessBatchVerificationStatus.Failed
                : ValidationAccessBatchVerificationStatus.PartiallyMissing;

        return new ValidationAccessBatchPersistResult
        {
            RequestedEventIds = requested.Distinct().ToList(),
            NewlyInsertedEventIds = newlyInserted.Distinct().ToList(),
            AlreadyExistingEventIds = alreadyExisting.Distinct().ToList(),
            ConfirmedPersistedEventIds = confirmed.Distinct().ToList(),
            MissingEventIds = missing,
            CommitStatus = commitStatus,
            VerificationStatus = verification
        };
    }
}
