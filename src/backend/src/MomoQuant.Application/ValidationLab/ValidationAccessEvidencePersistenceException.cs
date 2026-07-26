namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Safe, user-visible error codes for validation access-evidence persistence failures.
/// </summary>
public static class ValidationAccessPersistenceErrorCodes
{
    public const string PersistenceFailed = ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed;
    public const string InputBatchConflict = "VALIDATION_ACCESS_INPUT_BATCH_CONFLICT";
    public const string PersistedPayloadConflict = "VALIDATION_ACCESS_PERSISTED_PAYLOAD_CONFLICT";
    public const string CommitOutcomeUnknown = "VALIDATION_ACCESS_COMMIT_OUTCOME_UNKNOWN";
    public const string ConfirmationUnavailable = "VALIDATION_ACCESS_CONFIRMATION_UNAVAILABLE";
    public const string RetryExhausted = "VALIDATION_ACCESS_PERSISTENCE_RETRY_EXHAUSTED";
}

/// <summary>
/// Thrown when validation candle-access audit evidence cannot be fully payload-confirmed as durable.
/// Fail-closed: callers must not treat training as leakage-Passed or continue ranking/selection/freeze.
/// Base type of all typed access-evidence persistence failures so existing orchestration catch paths
/// continue to fail closed for every subtype.
/// </summary>
public class ValidationAccessEvidencePersistenceException : Exception
{
    public const string Code = ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed;

    public ValidationAccessBatchPersistResult PersistResult { get; }

    public string ErrorCode { get; }

    public ValidationAccessEvidencePersistenceException(ValidationAccessBatchPersistResult persistResult)
        : base(BuildMessage(persistResult))
    {
        PersistResult = persistResult;
        ErrorCode = Code;
    }

    public ValidationAccessEvidencePersistenceException(
        ValidationAccessBatchPersistResult persistResult,
        Exception innerException)
        : base(BuildMessage(persistResult), innerException)
    {
        PersistResult = persistResult;
        ErrorCode = Code;
    }

    protected ValidationAccessEvidencePersistenceException(
        string errorCode,
        string safeMessage,
        ValidationAccessBatchPersistResult persistResult,
        Exception? innerException)
        : base(safeMessage, innerException)
    {
        PersistResult = persistResult;
        ErrorCode = errorCode;
    }

    private static string BuildMessage(ValidationAccessBatchPersistResult result)
    {
        var missing = result.MissingEventIds.Count;
        var requested = result.RequestedEventIds.Count;
        return
            $"Validation access audit evidence was not fully payload-confirmed after persist. " +
            $"Confirmed {result.ConfirmedCount}/{requested}; missing {missing}; " +
            $"payload conflicts {result.PayloadConflictCount}. " +
            $"CommitStatus={result.CommitStatus}; VerificationStatus={result.VerificationStatus}; " +
            $"RecoveryStatus={result.RecoveryStatus}.";
    }
}

/// <summary>
/// The same AccessEventId appeared more than once inside one input batch with conflicting
/// canonical payloads. Thrown before any database query or transaction is started.
/// </summary>
public sealed class ValidationAccessInputBatchConflictException : ValidationAccessEvidencePersistenceException
{
    public Guid AccessEventId { get; }
    public IReadOnlyList<string> ConflictingPayloadHashes { get; }
    public IReadOnlyList<string> ConflictingFields { get; }
    public string SafeMessage { get; }

    public ValidationAccessInputBatchConflictException(
        Guid accessEventId,
        IReadOnlyList<string> conflictingPayloadHashes,
        IReadOnlyList<string> conflictingFields,
        ValidationAccessBatchPersistResult persistResult)
        : base(
            ValidationAccessPersistenceErrorCodes.InputBatchConflict,
            BuildSafeMessage(accessEventId, conflictingFields),
            persistResult,
            innerException: null)
    {
        AccessEventId = accessEventId;
        ConflictingPayloadHashes = conflictingPayloadHashes;
        ConflictingFields = conflictingFields;
        SafeMessage = BuildSafeMessage(accessEventId, conflictingFields);
    }

    private static string BuildSafeMessage(Guid accessEventId, IReadOnlyList<string> conflictingFields) =>
        $"Conflicting duplicate access events for AccessEventId {accessEventId} inside one batch. " +
        $"Conflicting fields: {string.Join(", ", conflictingFields)}. " +
        "The batch was rejected before any database write.";
}

/// <summary>
/// An AccessEventId already persisted in the database carries a canonical payload that does not
/// match the requested event. The stored row is never overwritten; the request fails closed.
/// </summary>
public sealed class ValidationAccessPersistedPayloadConflictException : ValidationAccessEvidencePersistenceException
{
    public Guid AccessEventId { get; }
    public string? RequestedPayloadHash { get; }
    public string? PersistedPayloadHash { get; }
    public IReadOnlyList<string> ConflictingFieldNames { get; }
    public string SafeMessage { get; }

    public ValidationAccessPersistedPayloadConflictException(
        Guid accessEventId,
        string? requestedPayloadHash,
        string? persistedPayloadHash,
        IReadOnlyList<string> conflictingFieldNames,
        ValidationAccessBatchPersistResult persistResult)
        : base(
            ValidationAccessPersistenceErrorCodes.PersistedPayloadConflict,
            BuildSafeMessage(accessEventId, conflictingFieldNames),
            persistResult,
            innerException: null)
    {
        AccessEventId = accessEventId;
        RequestedPayloadHash = requestedPayloadHash;
        PersistedPayloadHash = persistedPayloadHash;
        ConflictingFieldNames = conflictingFieldNames;
        SafeMessage = BuildSafeMessage(accessEventId, conflictingFieldNames);
    }

    private static string BuildSafeMessage(Guid accessEventId, IReadOnlyList<string> conflictingFieldNames) =>
        $"Persisted access event {accessEventId} has a conflicting immutable payload. " +
        $"Conflicting fields: {string.Join(", ", conflictingFieldNames)}. " +
        "The stored row was not modified and the requested event was not confirmed.";
}

/// <summary>
/// Commit succeeded or its outcome is unknown, but the durable state could not be confirmed
/// within the bounded confirmation attempts. Cursor must remain unchanged; retry is allowed.
/// </summary>
public sealed class ValidationAccessConfirmationUnavailableException : ValidationAccessEvidencePersistenceException
{
    public ValidationAccessConfirmationUnavailableException(
        ValidationAccessBatchPersistResult persistResult,
        Exception? innerException)
        : base(
            ValidationAccessPersistenceErrorCodes.ConfirmationUnavailable,
            "Validation access evidence confirmation is currently unavailable. " +
            "Durable state could not be verified; the flush remains retryable and no cursor advanced.",
            persistResult,
            innerException)
    {
    }
}

/// <summary>
/// Bounded persistence/confirmation retries were exhausted without full payload confirmation.
/// </summary>
public sealed class ValidationAccessPersistenceRetryExhaustedException : ValidationAccessEvidencePersistenceException
{
    public int Attempts { get; }

    public ValidationAccessPersistenceRetryExhaustedException(
        int attempts,
        ValidationAccessBatchPersistResult persistResult,
        Exception? innerException)
        : base(
            ValidationAccessPersistenceErrorCodes.RetryExhausted,
            $"Validation access evidence persistence retries exhausted after {attempts} attempt(s) " +
            "without full payload confirmation. No cursor advanced.",
            persistResult,
            innerException)
    {
        Attempts = attempts;
    }
}

/// <summary>
/// Signal that a transaction commit was attempted and may have succeeded on the server even though
/// the client observed a failure. Never classify this as a rollback; verify durable state first.
/// </summary>
public sealed class ValidationAccessCommitOutcomeUnknownException : Exception
{
    public const string Code = ValidationAccessPersistenceErrorCodes.CommitOutcomeUnknown;

    public string ErrorCode => Code;

    public bool CommitMayHaveSucceeded => true;

    public ValidationAccessCommitOutcomeUnknownException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
