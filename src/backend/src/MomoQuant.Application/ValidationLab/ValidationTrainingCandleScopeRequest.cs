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
    /// LTF-only warmup fingerprint bootstrap — no HTF load and no bound audit identity (Milestone 23.1B1C).
    /// </summary>
    public bool LtfOnlyWarmupBootstrap { get; init; }

    /// <summary>Authoritative experiment for canonical identity binding before candle access.</summary>
    public ValidationExperiment? CanonicalExperiment { get; init; }

    /// <summary>Resolved requirements for canonical identity binding before candle access.</summary>
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
    /// Quarantined legacy helper — must not be used without a bound durable audit execution.
    /// Prefer <see cref="FromExperiment"/> after resolving <see cref="StrategyExecutionRequirements"/>.
    /// </summary>
    [Obsolete("Quarantined — use FromExperiment with bound audit execution. Legacy path must not be used without bound audit.")]
    public static ValidationTrainingCandleScopeRequest FromExperimentLegacy(
        ValidationExperiment experiment,
        DateTime trainingEvaluationEndExclusiveUtc,
        int? requiredWarmupOverride = null,
        bool ltfOnlyWarmupBootstrap = false)
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
            StrategyVersion = experiment.StrategyVersion,
            ExchangeId = experiment.ExchangeId,
            LtfOnlyWarmupBootstrap = ltfOnlyWarmupBootstrap
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
            if (BoundAuditExecutionId is not null
                || BoundScopeExecutionId is not null
                || !string.IsNullOrWhiteSpace(BoundExecutionToken)
                || BoundAttemptNumber is > 0)
            {
                throw new ArgumentException(
                    "LTF-only warmup bootstrap must not bind durable audit execution identity.",
                    nameof(LtfOnlyWarmupBootstrap));
            }
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
