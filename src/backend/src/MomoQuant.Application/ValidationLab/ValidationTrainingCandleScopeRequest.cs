using MomoQuant.Application.Strategies;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public enum ValidationWarmupStatus
{
    NotRequired = 0,
    Complete = 1,
    Insufficient = 2
}

public enum ValidationCandleAccessPurpose
{
    Unspecified = 0,
    WarmupBefore = 1,
    EvaluationRange = 2,
    ByOpenTime = 3,
    StrategyLabDataset = 4,
    RepositoryRange = 5,
    RepositoryRecent = 6,
    RepositoryCount = 7,
    RepositoryLookup = 8,
    Indexer = 9
}

/// <summary>Caller identity + access purpose for audited candle reads.</summary>
public sealed class ValidationCandleAccessContext
{
    public required string CallerComponent { get; init; }
    public ValidationCandleAccessPurpose AccessPurpose { get; init; } = ValidationCandleAccessPurpose.Unspecified;

    public static ValidationCandleAccessContext Create(
        string callerComponent,
        ValidationCandleAccessPurpose purpose) =>
        new()
        {
            CallerComponent = string.IsNullOrWhiteSpace(callerComponent) ? "Unknown" : callerComponent.Trim(),
            AccessPurpose = purpose
        };

    public string ToCallerAuditLabel()
    {
        if (AccessPurpose == ValidationCandleAccessPurpose.Unspecified)
        {
            return CallerComponent.Length <= 128 ? CallerComponent : CallerComponent[..128];
        }

        var label = $"{CallerComponent}:{AccessPurpose}";
        return label.Length <= 128 ? label : label[..128];
    }
}

/// <summary>v2 factory request — all fields required and validated before scope creation.</summary>
public sealed class ValidationTrainingCandleScopeRequest
{
    public required long ValidationExperimentId { get; init; }
    public required long SymbolId { get; init; }
    public required string SymbolName { get; init; }
    public required string Timeframe { get; init; }
    public required DateTime TrainingEvaluationStartUtc { get; init; }
    public required DateTime TrainingEvaluationEndExclusiveUtc { get; init; }
    public required DateTime ValidationBoundaryUtc { get; init; }
    public required int RequiredWarmupCandleCount { get; init; }
    public required string RequirementsVersion { get; init; }
    public long? StrategyId { get; init; }
    public string? StrategyCode { get; init; }
    public string? StrategyVersion { get; init; }

    public static ValidationTrainingCandleScopeRequest FromExperiment(
        ValidationExperiment experiment,
        StrategyExecutionRequirements requirements,
        DateTime trainingEvaluationEndExclusiveUtc)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentNullException.ThrowIfNull(requirements);

        if (experiment.TrainingStartUtc is null || experiment.ValidationStartUtc is null)
        {
            throw new InvalidOperationException(
                "Training candle scope requires TrainingStartUtc and ValidationStartUtc.");
        }

        return new ValidationTrainingCandleScopeRequest
        {
            ValidationExperimentId = experiment.Id,
            SymbolId = experiment.SymbolId,
            SymbolName = experiment.Symbol,
            Timeframe = experiment.Timeframe,
            TrainingEvaluationStartUtc = DateTime.SpecifyKind(experiment.TrainingStartUtc.Value, DateTimeKind.Utc),
            TrainingEvaluationEndExclusiveUtc = DateTime.SpecifyKind(trainingEvaluationEndExclusiveUtc, DateTimeKind.Utc),
            ValidationBoundaryUtc = DateTime.SpecifyKind(experiment.ValidationStartUtc.Value, DateTimeKind.Utc),
            RequiredWarmupCandleCount = requirements.RequiredWarmupCandleCount,
            RequirementsVersion = requirements.RequirementsVersion,
            StrategyId = requirements.StrategyId,
            StrategyCode = requirements.StrategyCode,
            StrategyVersion = requirements.StrategyVersion ?? experiment.StrategyVersion
        };
    }

    /// <summary>Obsolete-path helper when only experiment.RequiredWarmupCandles is available.</summary>
    public static ValidationTrainingCandleScopeRequest FromExperimentLegacy(
        ValidationExperiment experiment,
        DateTime trainingEvaluationEndExclusiveUtc,
        int? requiredWarmupOverride = null)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        if (experiment.TrainingStartUtc is null || experiment.ValidationStartUtc is null)
        {
            throw new InvalidOperationException(
                "Training candle scope requires TrainingStartUtc and ValidationStartUtc.");
        }

        var warmup = requiredWarmupOverride ?? Math.Max(0, experiment.RequiredWarmupCandles);
        return new ValidationTrainingCandleScopeRequest
        {
            ValidationExperimentId = experiment.Id,
            SymbolId = experiment.SymbolId,
            SymbolName = experiment.Symbol,
            Timeframe = experiment.Timeframe,
            TrainingEvaluationStartUtc = DateTime.SpecifyKind(experiment.TrainingStartUtc.Value, DateTimeKind.Utc),
            TrainingEvaluationEndExclusiveUtc = DateTime.SpecifyKind(trainingEvaluationEndExclusiveUtc, DateTimeKind.Utc),
            ValidationBoundaryUtc = DateTime.SpecifyKind(experiment.ValidationStartUtc.Value, DateTimeKind.Utc),
            RequiredWarmupCandleCount = warmup,
            RequirementsVersion = StrategyExecutionRequirements.Version,
            StrategyCode = experiment.StrategyCode,
            StrategyVersion = experiment.StrategyVersion
        };
    }

    public void Validate()
    {
        if (ValidationExperimentId <= 0)
            throw new ArgumentException("ValidationExperimentId must be positive.", nameof(ValidationExperimentId));
        if (SymbolId <= 0)
            throw new ArgumentException("SymbolId must be positive.", nameof(SymbolId));
        if (string.IsNullOrWhiteSpace(SymbolName))
            throw new ArgumentException("SymbolName is required.", nameof(SymbolName));
        if (string.IsNullOrWhiteSpace(Timeframe))
            throw new ArgumentException("Timeframe is required.", nameof(Timeframe));
        if (TrainingEvaluationStartUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("TrainingEvaluationStartUtc must be UTC.", nameof(TrainingEvaluationStartUtc));
        if (TrainingEvaluationEndExclusiveUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("TrainingEvaluationEndExclusiveUtc must be UTC.", nameof(TrainingEvaluationEndExclusiveUtc));
        if (ValidationBoundaryUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("ValidationBoundaryUtc must be UTC.", nameof(ValidationBoundaryUtc));
        if (TrainingEvaluationEndExclusiveUtc <= TrainingEvaluationStartUtc)
            throw new ArgumentException(
                "TrainingEvaluationEndExclusiveUtc must be after TrainingEvaluationStartUtc.",
                nameof(TrainingEvaluationEndExclusiveUtc));
        if (TrainingEvaluationStartUtc >= ValidationBoundaryUtc)
            throw new ArgumentException(
                "TrainingEvaluationStartUtc must be before ValidationBoundaryUtc.",
                nameof(TrainingEvaluationStartUtc));
        if (RequiredWarmupCandleCount < 0)
            throw new ArgumentException("RequiredWarmupCandleCount cannot be negative.", nameof(RequiredWarmupCandleCount));
        if (string.IsNullOrWhiteSpace(RequirementsVersion))
            throw new ArgumentException("RequirementsVersion is required.", nameof(RequirementsVersion));
    }
}

/// <summary>Immutable partition metadata exposed by the training candle scope.</summary>
public sealed class ValidationCandlePartitionMetadata
{
    public required long ValidationExperimentId { get; init; }
    public required int RequiredWarmupCandleCount { get; init; }
    public required int AvailableWarmupCandleCount { get; init; }
    public required int EvaluationCandleCount { get; init; }
    public required int TotalCandleCount { get; init; }
    public required ValidationWarmupStatus WarmupStatus { get; init; }
    public required DateTime TrainingEvaluationStartUtc { get; init; }
    public required DateTime TrainingEvaluationEndExclusiveUtc { get; init; }
    public required DateTime ValidationBoundaryUtc { get; init; }
    public required long SymbolId { get; init; }
    public required string SymbolName { get; init; }
    public required string Timeframe { get; init; }
    public required string RequirementsVersion { get; init; }
    public int EvaluationStartIndex { get; init; }
    public string? WarmupContentFingerprint { get; init; }
    public string? EvaluationContentFingerprint { get; init; }
    public string? CombinedContentFingerprint { get; init; }
}
