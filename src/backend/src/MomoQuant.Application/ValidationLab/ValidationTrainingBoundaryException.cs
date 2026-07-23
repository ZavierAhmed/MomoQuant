namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Base type for training candle boundary / leakage failures handled by
/// <see cref="IValidationTrainingFailureHandler"/>.
/// </summary>
public abstract class ValidationTrainingBoundaryException : Exception
{
    public long? ValidationExperimentId { get; }
    public DateTime? ValidationBoundaryUtc { get; }
    public string CallerComponent { get; }
    public DateTime? RequestedStartUtc { get; }
    public DateTime? RequestedEndUtc { get; }
    public virtual string ErrorCode { get; }

    protected ValidationTrainingBoundaryException(
        string errorCode,
        long? validationExperimentId,
        DateTime? validationBoundaryUtc,
        string callerComponent,
        DateTime? requestedStartUtc,
        DateTime? requestedEndUtc,
        string message)
        : base(message)
    {
        ErrorCode = errorCode;
        ValidationExperimentId = validationExperimentId;
        ValidationBoundaryUtc = validationBoundaryUtc is null
            ? null
            : DateTime.SpecifyKind(validationBoundaryUtc.Value, DateTimeKind.Utc);
        CallerComponent = callerComponent;
        RequestedStartUtc = requestedStartUtc;
        RequestedEndUtc = requestedEndUtc;
    }

    /// <summary>Compatibility constructor used by <see cref="ValidationDataLeakageException"/>.</summary>
    protected ValidationTrainingBoundaryException(
        long validationExperimentId,
        DateTime validationBoundaryUtc,
        string callerComponent,
        DateTime? requestedStartUtc,
        DateTime? requestedEndUtc,
        string message)
        : this(
            ValidationTrainingFailureCodes.ValidationDataLeakage,
            validationExperimentId,
            validationBoundaryUtc,
            callerComponent,
            requestedStartUtc,
            requestedEndUtc,
            message)
    {
    }
}

public sealed class ValidationTrainingDataSourceMissingException : ValidationTrainingBoundaryException
{
    public const string Code = "VALIDATION_TRAINING_DATA_SOURCE_MISSING";
    public override string ErrorCode => Code;

    public ValidationTrainingDataSourceMissingException(
        long? validationExperimentId,
        string callerComponent,
        string? message = null)
        : base(
            Code,
            validationExperimentId,
            null,
            callerComponent,
            null,
            null,
            message ?? "ValidationTraining requires an explicit IStrategyLabCandleDataSource bound to the training scope.")
    {
    }
}

public sealed class ValidationTrainingUnscopedAccessException : ValidationTrainingBoundaryException
{
    public const string Code = "VALIDATION_TRAINING_UNSCOPED_ACCESS";
    public override string ErrorCode => Code;

    public ValidationTrainingUnscopedAccessException(
        long? validationExperimentId,
        DateTime? trainingBoundaryUtc,
        string callerComponent,
        string? message = null)
        : base(
            Code,
            validationExperimentId,
            trainingBoundaryUtc,
            callerComponent,
            null,
            null,
            message ?? "Unscoped candle access is forbidden while ValidationTraining is active.")
    {
    }
}

public sealed class ValidationTrainingBoundaryViolationException : ValidationTrainingBoundaryException
{
    public const string Code = "VALIDATION_TRAINING_BOUNDARY_VIOLATION";
    public override string ErrorCode => Code;

    public ValidationTrainingBoundaryViolationException(
        long? validationExperimentId,
        DateTime trainingBoundaryUtc,
        string callerComponent,
        DateTime? requestedStartUtc,
        DateTime? requestedEndUtc,
        string? message = null)
        : base(
            Code,
            validationExperimentId,
            trainingBoundaryUtc,
            callerComponent,
            requestedStartUtc,
            requestedEndUtc,
            message ?? $"Candle request crosses ValidationStartUtc {trainingBoundaryUtc:O}.")
    {
    }
}

public sealed class ValidationTrainingCoverageImportForbiddenException : ValidationTrainingBoundaryException
{
    public const string Code = "VALIDATION_TRAINING_COVERAGE_IMPORT_FORBIDDEN";
    public override string ErrorCode => Code;

    public ValidationTrainingCoverageImportForbiddenException(
        long? validationExperimentId,
        DateTime? trainingBoundaryUtc,
        string callerComponent,
        string? message = null)
        : base(
            Code,
            validationExperimentId,
            trainingBoundaryUtc,
            callerComponent,
            null,
            null,
            message ?? "Coverage auto-import is forbidden during ValidationTraining.")
    {
    }
}
