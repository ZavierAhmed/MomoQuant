using MomoQuant.Application.MarketData;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
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
    Indexer = 9,
    WarmupLoad = 10,
    EvaluationLoad = 11,
    DatasetMaterialization = 12,
    EvaluationPartial = 13,
    WarmupDiagnostic = 14,
    DirectWarmup = 15,
    DirectEvaluation = 16,
    /// <summary>Higher-timeframe series access during dataset materialization.</summary>
    HigherTimeframeAccess = 17,

    /// <summary>Factory bootstrap HTF load during scope construction (Milestone 23.1B1B).</summary>
    FactoryBootstrapHtfLoad = 18
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

    /// <summary>Authoritative exchange for canonical validation training (from experiment).</summary>
    public long ExchangeId { get; init; }

    /// <summary>Bound durable scope identity (Milestone 23.0E2C1). When set, the scope must use this exact Guid.</summary>
    public Guid? BoundScopeExecutionId { get; init; }

    /// <summary>Bound durable audit-execution identity.</summary>
    public Guid? BoundAuditExecutionId { get; init; }

    /// <summary>Opaque execution token from the durable audit execution.</summary>
    public string? BoundExecutionToken { get; init; }

    /// <summary>Attempt number from the durable audit execution.</summary>
    public int? BoundAttemptNumber { get; init; }

    /// <summary>
    /// Obsolete — use <see cref="ValidationLtfWarmupBootstrapRequest"/> and
    /// <see cref="IValidationTrainingCandleScopeFactory.CreateLtfWarmupBootstrapAsync"/>.
    /// </summary>
    [Obsolete("Use ValidationLtfWarmupBootstrapRequest and CreateLtfWarmupBootstrapAsync.")]
    public bool LtfOnlyWarmupBootstrap { get; init; }

    /// <summary>Obsolete — use <see cref="ValidationCanonicalTrainingCandleScopeRequest.Experiment"/>.</summary>
    [Obsolete("Use ValidationCanonicalTrainingCandleScopeRequest.Experiment.")]
    public ValidationExperiment? CanonicalExperiment { get; init; }

    /// <summary>Obsolete — use <see cref="ValidationCanonicalTrainingCandleScopeRequest.Requirements"/>.</summary>
    [Obsolete("Use ValidationCanonicalTrainingCandleScopeRequest.Requirements.")]
    public StrategyExecutionRequirements? CanonicalRequirements { get; init; }

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
            StrategyVersion = requirements.StrategyVersion ?? experiment.StrategyVersion,
            ExchangeId = experiment.ExchangeId
        };
    }

    /// <summary>
    /// Quarantined legacy helper — use <see cref="ValidationLtfWarmupBootstrapRequest.FromExperimentLegacy"/>.
    /// </summary>
    [Obsolete("Use ValidationLtfWarmupBootstrapRequest.FromExperimentLegacy.")]
    public static ValidationTrainingCandleScopeRequest FromExperimentLegacy(
        ValidationExperiment experiment,
        DateTime trainingEvaluationEndExclusiveUtc,
        int? requiredWarmupOverride = null,
        bool ltfOnlyWarmupBootstrap = false)
    {
        _ = ltfOnlyWarmupBootstrap;
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
            StrategyVersion = experiment.StrategyVersion,
            ExchangeId = experiment.ExchangeId
        };
    }

    /// <summary>
    /// Canonical workflow identity validation — requires bound strategy and audit execution fields.
    /// </summary>
    public void ValidateCanonical()
    {
        Validate();
        if (StrategyId is not > 0)
            throw new ArgumentException("StrategyId is required for canonical validation training.", nameof(StrategyId));
        if (string.IsNullOrWhiteSpace(StrategyCode))
            throw new ArgumentException("StrategyCode is required for canonical validation training.", nameof(StrategyCode));
        if (string.IsNullOrWhiteSpace(StrategyVersion))
            throw new ArgumentException("StrategyVersion is required for canonical validation training.", nameof(StrategyVersion));
        if (ExchangeId <= 0)
            throw new ArgumentException("ExchangeId must be positive for canonical validation training.", nameof(ExchangeId));
        if (BoundScopeExecutionId is null || BoundScopeExecutionId == Guid.Empty)
            throw new ArgumentException("BoundScopeExecutionId is required for canonical validation training.", nameof(BoundScopeExecutionId));
        if (BoundAuditExecutionId is null || BoundAuditExecutionId == Guid.Empty)
            throw new ArgumentException("BoundAuditExecutionId is required for canonical validation training.", nameof(BoundAuditExecutionId));
        if (string.IsNullOrWhiteSpace(BoundExecutionToken))
            throw new ArgumentException("BoundExecutionToken is required for canonical validation training.", nameof(BoundExecutionToken));
        if (BoundAttemptNumber is not > 0)
            throw new ArgumentException("BoundAttemptNumber must be positive for canonical validation training.", nameof(BoundAttemptNumber));

        StrategyCode strategyEnum;
        try
        {
            strategyEnum = StrategyCodeExtensions.FromCode(StrategyCode);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new ArgumentException(
                $"Unknown or unsupported strategy code '{StrategyCode}' for canonical validation training.",
                nameof(StrategyCode),
                ex);
        }

        if (!CanonicalStrategyPortfolio.IsCanonicalActive(strategyEnum))
        {
            throw new ArgumentException(
                $"Strategy code '{StrategyCode}' is not in the canonical active portfolio.",
                nameof(StrategyCode));
        }

        if (!CanonicalStrategyVersionPolicy.IsSupportedProductionVersion(strategyEnum, StrategyVersion))
        {
            throw new ArgumentException(
                $"Strategy version '{StrategyVersion}' is not a supported production version for '{StrategyCode}'.",
                nameof(StrategyVersion));
        }
    }

    /// <summary>
    /// Binds and compares experiment, requirements, and scope request before any candle repository access.
    /// </summary>
    public void ValidateCanonicalBindings(
        ValidationExperiment experiment,
        StrategyExecutionRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentNullException.ThrowIfNull(requirements);
        ValidateCanonical();

        if (experiment.Id != ValidationExperimentId)
        {
            throw new ArgumentException(
                $"ValidationExperimentId {ValidationExperimentId} does not match experiment {experiment.Id}.",
                nameof(ValidationExperimentId));
        }

        if (experiment.SymbolId != SymbolId)
        {
            throw new ArgumentException(
                $"SymbolId {SymbolId} does not match experiment symbol {experiment.SymbolId}.",
                nameof(SymbolId));
        }

        if (!string.Equals(experiment.Symbol, SymbolName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"SymbolName '{SymbolName}' does not match experiment symbol '{experiment.Symbol}'.",
                nameof(SymbolName));
        }

        if (!string.Equals(experiment.Timeframe, Timeframe, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Timeframe '{Timeframe}' does not match experiment timeframe '{experiment.Timeframe}'.",
                nameof(Timeframe));
        }

        if (experiment.ExchangeId != ExchangeId)
        {
            throw new ArgumentException(
                $"ExchangeId {ExchangeId} does not match experiment exchange {experiment.ExchangeId}.",
                nameof(ExchangeId));
        }

        if (requirements.StrategyId != StrategyId)
        {
            throw new ArgumentException(
                $"StrategyId {StrategyId} does not match resolved requirements strategy {requirements.StrategyId}.",
                nameof(StrategyId));
        }

        if (!string.Equals(requirements.StrategyCode, StrategyCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"StrategyCode '{StrategyCode}' does not match resolved requirements '{requirements.StrategyCode}'.",
                nameof(StrategyCode));
        }

        if (!string.Equals(requirements.StrategyVersion, StrategyVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"StrategyVersion '{StrategyVersion}' does not match resolved requirements '{requirements.StrategyVersion}'.",
                nameof(StrategyVersion));
        }

        if (!string.Equals(requirements.RequirementsVersion, RequirementsVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"RequirementsVersion '{RequirementsVersion}' does not match resolved requirements '{requirements.RequirementsVersion}'.",
                nameof(RequirementsVersion));
        }

        if (!string.Equals(experiment.StrategyCode, StrategyCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"StrategyCode '{StrategyCode}' does not match experiment strategy '{experiment.StrategyCode}'.",
                nameof(StrategyCode));
        }

        if (!string.Equals(experiment.StrategyVersion, StrategyVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"StrategyVersion '{StrategyVersion}' does not match experiment version '{experiment.StrategyVersion}'.",
                nameof(StrategyVersion));
        }

        var strategyEnum = StrategyCodeExtensions.FromCode(StrategyCode!);
        if (strategyEnum == global::MomoQuant.Domain.Enums.StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout
            && !TimeframeParser.TryParse(Timeframe, out var execTf))
        {
            throw new InvalidOperationException(
                $"Canonical Adaptive validation requires a parseable execution timeframe '{Timeframe}'.");
        }
    }

    public void Validate()
    {
        if (LtfOnlyWarmupBootstrap)
        {
            throw new ArgumentException(
                "LtfOnlyWarmupBootstrap is obsolete. Use ValidationLtfWarmupBootstrapRequest and CreateLtfWarmupBootstrapAsync.",
                nameof(LtfOnlyWarmupBootstrap));
        }

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

/// <summary>LTF-only warmup bootstrap request — no HTF, no bound audit identity (Milestone 23.1B1C1).</summary>
public sealed class ValidationLtfWarmupBootstrapRequest
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
    public long? ExchangeId { get; init; }
    public long? StrategyId { get; init; }
    public string? StrategyCode { get; init; }
    public string? StrategyVersion { get; init; }

    public static ValidationLtfWarmupBootstrapRequest FromExperiment(
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

        return new ValidationLtfWarmupBootstrapRequest
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
            StrategyVersion = requirements.StrategyVersion ?? experiment.StrategyVersion,
            ExchangeId = experiment.ExchangeId > 0 ? experiment.ExchangeId : null
        };
    }

    public static ValidationLtfWarmupBootstrapRequest FromExperimentLegacy(
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
        return new ValidationLtfWarmupBootstrapRequest
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
            StrategyVersion = experiment.StrategyVersion,
            ExchangeId = experiment.ExchangeId > 0 ? experiment.ExchangeId : null
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

/// <summary>Canonical validation training scope request with authoritative bindings (Milestone 23.1B1C1).</summary>
public sealed class ValidationCanonicalTrainingCandleScopeRequest
{
    public required ValidationExperiment Experiment { get; init; }
    public required StrategyExecutionRequirements Requirements { get; init; }
    public required ValidationAuditExecution AuditExecution { get; init; }
    public required ValidationParameterTrial Trial { get; init; }
    public required DateTime TrainingEvaluationEndExclusiveUtc { get; init; }

    /// <summary>
    /// Validates ALL consistency before any candle access: experiment, requirements, audit, strategy, and HTF contract.
    /// </summary>
    public void ValidateAuthoritativeBindings()
    {
        ArgumentNullException.ThrowIfNull(Experiment);
        ArgumentNullException.ThrowIfNull(Requirements);
        ArgumentNullException.ThrowIfNull(AuditExecution);
        ArgumentNullException.ThrowIfNull(Trial);

        if (TrainingEvaluationEndExclusiveUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                "TrainingEvaluationEndExclusiveUtc must be UTC.",
                nameof(TrainingEvaluationEndExclusiveUtc));
        }

        if (Experiment.TrainingStartUtc is null || Experiment.ValidationStartUtc is null)
        {
            throw new InvalidOperationException(
                "Canonical validation training requires TrainingStartUtc and ValidationStartUtc on the experiment.");
        }

        var trainingStart = DateTime.SpecifyKind(Experiment.TrainingStartUtc.Value, DateTimeKind.Utc);
        var boundary = DateTime.SpecifyKind(Experiment.ValidationStartUtc.Value, DateTimeKind.Utc);

        if (TrainingEvaluationEndExclusiveUtc <= trainingStart)
        {
            throw new ArgumentException(
                "TrainingEvaluationEndExclusiveUtc must be after experiment TrainingStartUtc.",
                nameof(TrainingEvaluationEndExclusiveUtc));
        }

        if (trainingStart >= boundary)
        {
            throw new ArgumentException(
                "Experiment TrainingStartUtc must be before ValidationStartUtc.",
                nameof(Experiment));
        }

        if (AuditExecution.ValidationExperimentId != Experiment.Id)
        {
            throw new ArgumentException(
                $"Audit execution experiment {AuditExecution.ValidationExperimentId} does not match experiment {Experiment.Id}.",
                nameof(AuditExecution));
        }

        if (Experiment.SymbolId <= 0)
        {
            throw new ArgumentException("Experiment SymbolId must be positive.", nameof(Experiment));
        }

        if (string.IsNullOrWhiteSpace(Experiment.Symbol))
        {
            throw new ArgumentException("Experiment Symbol is required.", nameof(Experiment));
        }

        if (string.IsNullOrWhiteSpace(Experiment.Timeframe))
        {
            throw new ArgumentException("Experiment Timeframe is required.", nameof(Experiment));
        }

        if (Experiment.ExchangeId <= 0)
        {
            throw new ArgumentException("Experiment ExchangeId must be positive.", nameof(Experiment));
        }

        if (Requirements.StrategyId <= 0)
        {
            throw new ArgumentException("Requirements StrategyId must be positive.", nameof(Requirements));
        }

        if (string.IsNullOrWhiteSpace(Requirements.StrategyCode))
        {
            throw new ArgumentException("Requirements StrategyCode is required.", nameof(Requirements));
        }

        if (string.IsNullOrWhiteSpace(Requirements.StrategyVersion))
        {
            throw new ArgumentException("Requirements StrategyVersion is required.", nameof(Requirements));
        }

        if (string.IsNullOrWhiteSpace(Requirements.RequirementsVersion))
        {
            throw new ArgumentException("Requirements RequirementsVersion is required.", nameof(Requirements));
        }

        if (!string.Equals(Experiment.StrategyCode, Requirements.StrategyCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Requirements StrategyCode '{Requirements.StrategyCode}' does not match experiment '{Experiment.StrategyCode}'.",
                nameof(Requirements));
        }

        if (!string.Equals(Experiment.StrategyVersion, Requirements.StrategyVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Requirements StrategyVersion '{Requirements.StrategyVersion}' does not match experiment '{Experiment.StrategyVersion}'.",
                nameof(Requirements));
        }

        if (AuditExecution.AuditExecutionId == Guid.Empty)
        {
            throw new ArgumentException("AuditExecution AuditExecutionId is required.", nameof(AuditExecution));
        }

        if (AuditExecution.ScopeExecutionId == Guid.Empty)
        {
            throw new ArgumentException("AuditExecution ScopeExecutionId is required.", nameof(AuditExecution));
        }

        if (string.IsNullOrWhiteSpace(AuditExecution.ExecutionToken))
        {
            throw new ArgumentException("AuditExecution ExecutionToken is required.", nameof(AuditExecution));
        }

        if (AuditExecution.AttemptNumber <= 0)
        {
            throw new ArgumentException("AuditExecution AttemptNumber must be positive.", nameof(AuditExecution));
        }

        if (Trial.Id <= 0)
        {
            throw new ArgumentException("Trial Id must be positive.", nameof(Trial));
        }

        if (Trial.ValidationExperimentId != Experiment.Id)
        {
            throw new ArgumentException(
                $"Trial experiment {Trial.ValidationExperimentId} does not match experiment {Experiment.Id}.",
                nameof(Trial));
        }

        if (Trial.TrialNumber <= 0)
        {
            throw new ArgumentException("Trial TrialNumber must be positive.", nameof(Trial));
        }

        if (AuditExecution.ValidationTrialId != Trial.Id)
        {
            throw new ArgumentException(
                $"Audit execution trial {AuditExecution.ValidationTrialId} does not match trial {Trial.Id}.",
                nameof(AuditExecution));
        }

        if (AuditExecution.TrialNumber != Trial.TrialNumber)
        {
            throw new ArgumentException(
                $"Audit execution trial number {AuditExecution.TrialNumber} does not match trial {Trial.TrialNumber}.",
                nameof(AuditExecution));
        }

        if (AuditExecution.ExecutionType != ValidationAuditExecutionType.Trial)
        {
            throw new ArgumentException(
                $"Audit execution type {AuditExecution.ExecutionType} must be Trial.",
                nameof(AuditExecution));
        }

        if (Trial.AuthoritativeAuditExecutionId != AuditExecution.AuditExecutionId)
        {
            throw new ArgumentException(
                $"Trial authoritative audit execution {Trial.AuthoritativeAuditExecutionId} does not match audit {AuditExecution.AuditExecutionId}.",
                nameof(Trial));
        }

        if (Trial.AuditAttemptNumber > 0 && Trial.AuditAttemptNumber != AuditExecution.AttemptNumber)
        {
            throw new ArgumentException(
                $"Trial audit attempt {Trial.AuditAttemptNumber} does not match audit attempt {AuditExecution.AttemptNumber}.",
                nameof(Trial));
        }

        if (AuditExecution.Status == ValidationAuditExecutionStatus.Superseded)
        {
            throw new ArgumentException(
                "Audit execution is Superseded and cannot bind canonical training scope.",
                nameof(AuditExecution));
        }

        if (AuditExecution.Status == ValidationAuditExecutionStatus.Failed)
        {
            throw new ArgumentException(
                "Audit execution is Failed and cannot bind canonical training scope.",
                nameof(AuditExecution));
        }

        StrategyCode strategyEnum;
        try
        {
            strategyEnum = StrategyCodeExtensions.FromCode(Requirements.StrategyCode);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new ArgumentException(
                $"Unknown or unsupported strategy code '{Requirements.StrategyCode}'.",
                nameof(Requirements),
                ex);
        }

        if (!CanonicalStrategyPortfolio.IsCanonicalActive(strategyEnum))
        {
            throw new ArgumentException(
                $"Strategy code '{Requirements.StrategyCode}' is not in the canonical active portfolio.",
                nameof(Requirements));
        }

        if (!CanonicalStrategyVersionPolicy.IsSupportedProductionVersion(strategyEnum, Requirements.StrategyVersion))
        {
            throw new ArgumentException(
                $"Strategy version '{Requirements.StrategyVersion}' is not a supported production version for '{Requirements.StrategyCode}'.",
                nameof(Requirements));
        }

        if (strategyEnum == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout)
        {
            if (!Requirements.RequiresHigherTimeframePartition)
            {
                throw new ArgumentException(
                    "Adaptive validation requires RequiresHigherTimeframePartition on requirements.",
                    nameof(Requirements));
            }

            if (string.IsNullOrWhiteSpace(Requirements.RequiredHigherTimeframeApi))
            {
                throw new InvalidOperationException(
                    $"Adaptive validation requires a mapped HTF for execution timeframe '{Experiment.Timeframe}'.");
            }

            if (!TimeframeParser.TryParse(Experiment.Timeframe, out var execTf))
            {
                throw new InvalidOperationException(
                    $"Canonical Adaptive validation requires a parseable execution timeframe '{Experiment.Timeframe}'.");
            }

            if (!TimeframeParser.TryParse(Requirements.RequiredHigherTimeframeApi, out var requiredHtf))
            {
                throw new InvalidOperationException(
                    $"Requirements RequiredHigherTimeframeApi '{Requirements.RequiredHigherTimeframeApi}' is not parseable.");
            }

            var resolvedHtf = MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(execTf);
            if (resolvedHtf != requiredHtf)
            {
                throw new InvalidOperationException(
                    $"Requirements HTF '{Requirements.RequiredHigherTimeframeApi}' does not match Adaptive mapping " +
                    $"'{TimeframeParser.ToApiString(resolvedHtf)}' for execution timeframe '{Experiment.Timeframe}'.");
            }

            if (!string.Equals(
                    Requirements.HigherTimeframeMappingContractVersion,
                    StrategyHigherTimeframeSupport.AdaptiveHtfMappingContractVersion,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Adaptive validation requires HigherTimeframeMappingContractVersion " +
                    $"'{StrategyHigherTimeframeSupport.AdaptiveHtfMappingContractVersion}'.");
            }
        }
        else if (strategyEnum is StrategyCode.PriceStructureBreakoutRetest
                 or StrategyCode.MomoVolatilityRangeReversion)
        {
            if (Requirements.RequiresHigherTimeframePartition)
            {
                throw new ArgumentException(
                    $"Strategy '{Requirements.StrategyCode}' must not require an HTF partition.",
                    nameof(Requirements));
            }
        }
    }
}

/// <summary>Warmup access request for strict partition enforcement.</summary>
public sealed class ValidationWarmupAccessRequest
{
    public required DateTime BeforeOpenTimeUtc { get; init; }
    public required int Count { get; init; }
    public required ValidationCandleAccessPurpose Purpose { get; init; }
    public required string CallerComponent { get; init; }
}

/// <summary>Evaluation access request for strict partition enforcement.</summary>
public sealed class ValidationEvaluationAccessRequest
{
    public required DateTime FromUtc { get; init; }
    public required DateTime ToExclusiveUtc { get; init; }
    public required bool AllowPartial { get; init; }
    public required ValidationCandleAccessPurpose Purpose { get; init; }
    public required string CallerComponent { get; init; }
}

/// <summary>Dataset materialization request matching run exactly.</summary>
public sealed class ValidationDatasetMaterializationRequest
{
    public required long SymbolId { get; init; }
    public required string SymbolName { get; init; }
    public required string Timeframe { get; init; }
    public required DateTime EvaluationFromUtc { get; init; }
    public required DateTime EvaluationToExclusiveUtc { get; init; }
    public required int WarmupCandleCount { get; init; }
    public required string CallerComponent { get; init; }

    /// <summary>
    /// Optional; when present must match the bound scope strategy identity.
    /// Never trusted as the source of Adaptive HTF requirements (Milestone 23.1B1A).
    /// </summary>
    public string? StrategyCode { get; init; }

    /// <summary>
    /// Compatibility remnant. Non-empty values are rejected as untrusted
    /// (<see cref="ValidationCandlePartitionDenialCodes.UntrustedCallerHtf"/>).
    /// HTF must be bound on the scope/partition at construction.
    /// </summary>
    public IReadOnlyDictionary<Timeframe, IReadOnlyList<Candle>>? HigherTimeframeSeriesByTimeframe { get; init; }
}

/// <summary>Immutable partition metadata exposed by the training candle scope.</summary>
public sealed class ValidationCandlePartitionMetadata
{
    public required long ValidationExperimentId { get; init; }
    public required int RequiredWarmupCandleCount { get; init; }
    public required int AvailableWarmupCandleCount { get; init; }
    public required int EvaluationCandleCount { get; init; }
    public required int TotalCandleCount { get; set; }
    public required ValidationWarmupStatus WarmupStatus { get; init; }
    public required DateTime TrainingEvaluationStartUtc { get; init; }
    public required DateTime TrainingEvaluationEndExclusiveUtc { get; init; }
    public required DateTime ValidationBoundaryUtc { get; init; }
    public required long SymbolId { get; init; }
    public required string SymbolName { get; init; }
    public required string Timeframe { get; init; }
    public required string RequirementsVersion { get; init; }
    public int EvaluationStartIndex { get; set; }
    public string? WarmupContentFingerprint { get; set; }
    public string? EvaluationContentFingerprint { get; init; }
    public string? CombinedContentFingerprint { get; init; }

    /// <summary>Bound strategy identity from scope construction (Milestone 23.1B1A).</summary>
    public string? StrategyCode { get; init; }

    /// <summary>Bound strategy version from scope construction.</summary>
    public string? StrategyVersion { get; init; }

    /// <summary>Exchange captured from LTF candles / request when available.</summary>
    public long? ExchangeId { get; init; }

    /// <summary>Mapped Adaptive HTF when the bound strategy requires one.</summary>
    public string? MappedHigherTimeframe { get; init; }

    /// <summary>Content fingerprint of the immutable HTF partition (when bound).</summary>
    public string? HigherTimeframeContentFingerprint { get; init; }
    
    // v2 fields
    public DateTime? WarmupStartUtc { get; init; }
    public DateTime? WarmupEndExclusiveUtc { get; init; }
    public int? WarmupStartIndex { get; init; }
    public int? WarmupEndExclusiveIndex { get; init; }
    public int? EvaluationEndExclusiveIndex { get; init; }
    public int? WarmupCandleCount { get; init; }
    public string PartitionContractVersion { get; init; } = "ValidationCandlePartition/v2";
}
