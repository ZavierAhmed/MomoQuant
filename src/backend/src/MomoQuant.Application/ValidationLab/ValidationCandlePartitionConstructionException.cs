namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Failure reason codes for validation candle partition construction.
/// Thrown when input candles or metadata fail strict validation during scope construction.
/// </summary>
public static class ValidationCandlePartitionConstructionFailureReasons
{
    public const string WarmupCandleOutsidePartition = "WARMUP_CANDLE_OUTSIDE_PARTITION";
    public const string EvaluationCandleOutsidePartition = "EVALUATION_CANDLE_OUTSIDE_PARTITION";
    public const string ValidationBoundaryCandlePresent = "VALIDATION_BOUNDARY_CANDLE_PRESENT";
    public const string DuplicateOpenTime = "DUPLICATE_OPEN_TIME";
    public const string NonMonotonicOpenTime = "NON_MONOTONIC_OPEN_TIME";
    public const string SymbolMismatch = "SYMBOL_MISMATCH";
    public const string TimeframeMismatch = "TIMEFRAME_MISMATCH";
    public const string OpenCandleNotAllowed = "OPEN_CANDLE_NOT_ALLOWED";
    public const string WarmupCountMetadataMismatch = "WARMUP_COUNT_METADATA_MISMATCH";
    public const string EvaluationCountMetadataMismatch = "EVALUATION_COUNT_METADATA_MISMATCH";
    public const string TotalCountMetadataMismatch = "TOTAL_COUNT_METADATA_MISMATCH";
    public const string PartitionIndexMismatch = "PARTITION_INDEX_MISMATCH";
    public const string WarmupFingerprintMismatch = "WARMUP_FINGERPRINT_MISMATCH";
    public const string EvaluationFingerprintMismatch = "EVALUATION_FINGERPRINT_MISMATCH";
    public const string CombinedFingerprintMismatch = "COMBINED_FINGERPRINT_MISMATCH";
    public const string UnsupportedPartitionContractVersion = "UNSUPPORTED_PARTITION_CONTRACT_VERSION";

    // Legacy aliases kept for any transitional callers/tests.
    public const string DUPLICATE_TIMESTAMP = DuplicateOpenTime;
    public const string WARMUP_CANDLE_AT_OR_AFTER_EVALUATION_START = WarmupCandleOutsidePartition;
    public const string EVALUATION_CANDLE_BEFORE_START = EvaluationCandleOutsidePartition;
    public const string CANDLE_AT_OR_AFTER_VALIDATION_BOUNDARY = ValidationBoundaryCandlePresent;
    public const string SYMBOL_MISMATCH = SymbolMismatch;
    public const string NON_MONOTONIC_WARMUP = NonMonotonicOpenTime;
    public const string NON_MONOTONIC_EVALUATION = NonMonotonicOpenTime;
    public const string WARMUP_COUNT_MISMATCH = WarmupCountMetadataMismatch;
    public const string EVALUATION_COUNT_MISMATCH = EvaluationCountMetadataMismatch;
    public const string FINGERPRINT_MISMATCH = WarmupFingerprintMismatch;
}

/// <summary>
/// Exception thrown when validation candle partition construction fails due to invalid input data.
/// Fail-closed: no scope is returned and no access event is recorded.
/// </summary>
public sealed class ValidationCandlePartitionConstructionException : Exception
{
    public const string ErrorCodeValue = "VALIDATION_CANDLE_PARTITION_CONSTRUCTION_INVALID";
    public const string SupportedPartitionContractVersion = "ValidationCandlePartition/v2";

    public string ErrorCode { get; }
    public string FailureReasonCode { get; }
    public string PartitionContractVersion { get; }
    public long ValidationExperimentId { get; }
    public Guid ScopeExecutionId { get; }
    public string? ExpectedValue { get; }
    public string? ActualValue { get; }
    public string SafeMessage { get; }

    public ValidationCandlePartitionConstructionException(
        long validationExperimentId,
        Guid scopeExecutionId,
        string failureReasonCode,
        string message,
        string? expectedValue = null,
        string? actualValue = null,
        string? partitionContractVersion = null)
        : base($"[{ErrorCodeValue}] {failureReasonCode}: {message}")
    {
        ErrorCode = ErrorCodeValue;
        FailureReasonCode = failureReasonCode;
        ValidationExperimentId = validationExperimentId;
        ScopeExecutionId = scopeExecutionId;
        ExpectedValue = expectedValue;
        ActualValue = actualValue;
        PartitionContractVersion = partitionContractVersion ?? SupportedPartitionContractVersion;
        SafeMessage = message;
    }

    public ValidationCandlePartitionConstructionException(
        string failureReasonCode,
        string message,
        string? expectedValue = null,
        string? actualValue = null,
        string? partitionContractVersion = null)
        : this(
            validationExperimentId: 0,
            scopeExecutionId: Guid.Empty,
            failureReasonCode,
            message,
            expectedValue,
            actualValue,
            partitionContractVersion)
    {
    }
}
