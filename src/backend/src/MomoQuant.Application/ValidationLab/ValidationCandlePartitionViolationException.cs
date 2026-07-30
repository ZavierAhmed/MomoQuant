namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Denial codes for candle partition enforcement violations.
/// </summary>
public static class ValidationCandlePartitionDenialCodes
{
    public const string WarmupRequestAfterEvaluationStart = "WARMUP_REQUEST_AFTER_EVALUATION_START";
    public const string WarmupRequestBeforeAvailableWarmup = "WARMUP_REQUEST_BEFORE_AVAILABLE_WARMUP";
    public const string EvaluationRequestBeforeEvaluationStart = "EVALUATION_REQUEST_BEFORE_EVALUATION_START";
    public const string EvaluationRequestAfterEvaluationEnd = "EVALUATION_REQUEST_AFTER_EVALUATION_END";
    public const string RequestAtOrAfterValidationBoundary = "REQUEST_AT_OR_AFTER_VALIDATION_BOUNDARY";
    public const string WarmupCountMismatch = "WARMUP_COUNT_MISMATCH";
    public const string RunStartMismatch = "RUN_START_MISMATCH";
    public const string RunEndMismatch = "RUN_END_MISMATCH";
    public const string RunRangeMismatch = "RUN_RANGE_MISMATCH";
    public const string SymbolMismatch = "SYMBOL_MISMATCH";
    public const string TimeframeMismatch = "TIMEFRAME_MISMATCH";
    public const string PartitionRangeInvalid = "PARTITION_RANGE_INVALID";
    public const string CrossPartitionCompatibilityReadForbidden = "CROSS_PARTITION_COMPATIBILITY_READ_FORBIDDEN";

    /// <summary>Caller-supplied HTF on materialization request is untrusted (Milestone 23.1B1A).</summary>
    public const string UntrustedCallerHtf = "UntrustedCallerHtf";

    /// <summary>Materialization StrategyCode differs from bound scope strategy identity.</summary>
    public const string SpoofedStrategyIdentity = "SpoofedStrategyIdentity";

    /// <summary>Adaptive requires mapped HTF but the scope-owned partition has none.</summary>
    public const string MissingPartitionHtf = "MissingPartitionHtf";

    public const string HtfCloseBeyondBoundary = "HtfCloseBeyondBoundary";
    public const string HtfOpenCandle = "HtfOpenCandle";
    public const string HtfWrongSymbol = "HtfWrongSymbol";
    public const string HtfWrongExchange = "HtfWrongExchange";
    public const string HtfWrongTimeframe = "HtfWrongTimeframe";
    public const string HtfUnordered = "HtfUnordered";
    public const string HtfDuplicate = "HtfDuplicate";
    public const string HtfInvalidTimestamp = "HtfInvalidTimestamp";
    public const string HtfInvalidCandleRange = "HtfInvalidCandleRange";
    public const string HtfOverlapping = "HtfOverlapping";
}

/// <summary>
/// Typed exception for candle partition enforcement violations in validation training.
/// Thrown when a candle access request violates the strict partition contract (v2).
/// </summary>
public sealed class ValidationCandlePartitionViolationException : ValidationTrainingBoundaryException
{
    public const string Code = "VALIDATION_CANDLE_PARTITION_VIOLATION";

    public Guid ScopeExecutionId { get; }
    public new DateTime? RequestedStartUtc { get; }
    public DateTime? RequestedEndExclusiveUtc { get; }
    public int? RequestedCandleCount { get; }
    public DateTime FixedEvaluationStartUtc { get; }
    public DateTime FixedEvaluationEndExclusiveUtc { get; }
    public string DenialCode { get; }
    public string SafeMessage { get; }

    public override string ErrorCode => Code;

    public ValidationCandlePartitionViolationException(
        long validationExperimentId,
        Guid scopeExecutionId,
        DateTime? validationBoundaryUtc,
        DateTime? requestedStartUtc,
        DateTime? requestedEndExclusiveUtc,
        int? requestedCandleCount,
        DateTime fixedEvaluationStartUtc,
        DateTime fixedEvaluationEndExclusiveUtc,
        string denialCode,
        string safeMessage,
        string? callerComponent = null)
        : base(
            Code,
            validationExperimentId,
            validationBoundaryUtc,
            callerComponent ?? "ValidationTrainingCandleScope",
            requestedStartUtc,
            requestedEndExclusiveUtc,
            safeMessage)
    {
        ScopeExecutionId = scopeExecutionId;
        RequestedStartUtc = requestedStartUtc;
        RequestedEndExclusiveUtc = requestedEndExclusiveUtc;
        RequestedCandleCount = requestedCandleCount;
        FixedEvaluationStartUtc = DateTime.SpecifyKind(fixedEvaluationStartUtc, DateTimeKind.Utc);
        FixedEvaluationEndExclusiveUtc = DateTime.SpecifyKind(fixedEvaluationEndExclusiveUtc, DateTimeKind.Utc);
        DenialCode = denialCode;
        SafeMessage = safeMessage;
    }
}
