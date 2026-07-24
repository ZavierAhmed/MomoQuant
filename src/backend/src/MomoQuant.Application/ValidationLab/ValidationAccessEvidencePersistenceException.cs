namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Thrown when validation candle-access audit evidence cannot be fully confirmed as durable.
/// Fail-closed: callers must not treat training as leakage-Passed or continue ranking/selection/freeze.
/// </summary>
public sealed class ValidationAccessEvidencePersistenceException : Exception
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

    private static string BuildMessage(ValidationAccessBatchPersistResult result)
    {
        var missing = result.MissingEventIds.Count;
        var requested = result.RequestedEventIds.Count;
        return
            $"Validation access audit evidence was not fully confirmed after persist. " +
            $"Confirmed {result.ConfirmedCount}/{requested}; missing {missing}. " +
            $"CommitStatus={result.CommitStatus}; VerificationStatus={result.VerificationStatus}.";
    }
}
