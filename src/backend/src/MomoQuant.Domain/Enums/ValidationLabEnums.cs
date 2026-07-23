namespace MomoQuant.Domain.Enums;

public enum ValidationExperimentType
{
    ValidateExistingFrozenConfiguration = 1,
    TrainingSearchHoldoutValidation = 2
}

public enum ValidationExperimentStatus
{
    Draft = 0,
    DataPreparing = 1,
    DataReady = 2,
    TrainingRunning = 3,
    TrainingCompleted = 4,
    ConfigurationFrozen = 5,
    ValidationRunning = 6,
    Completed = 7,
    Failed = 8,
    Cancelled = 9,
    TrainingInterrupted = 10,
    ResumePreparing = 11,
    TrainingResumed = 12,
    TrainingPaused = 13
}

public enum ValidationRevealStatus
{
    Hidden = 0,
    Frozen = 1,
    Revealed = 2
}

public enum ValidationPrimaryQualificationLayer
{
    RawStrategy = 1,
    ConfidenceQualified = 2,
    RiskOnly = 3,
    FullPipeline = 4
}

public enum ValidationSegmentType
{
    Training = 1,
    Validation = 2
}

public enum ValidationLayerType
{
    RawStrategy = 1,
    ConfidenceQualified = 2,
    RiskOnly = 3,
    FullPipeline = 4
}

public enum ValidationSegmentClassification
{
    Training = 1,
    Validation = 2,
    BoundaryCensored = 3,
    WarmupSuppressed = 4,
    Invalid = 5,
    ExcludedBySegmentSessionReset = 6,
    AddedBySegmentSessionReset = 7
}

public enum CandidateReconciliationStatus
{
    ExactMatch = 1,
    ExactMatchWithBoundaryCensoring = 2,
    ExplainedSessionBoundaryDifference = 3,
    UnexplainedDifference = 4,
    Invalid = 5
}

public enum SegmentDetectorContinuityMode
{
    FreshSessionWithWarmup = 1,
    ContinuousDetectorState = 2,
    SourceRunPartitionOnly = 3
}

public enum ParameterStabilityApplicability
{
    Applicable = 1,
    NotApplicable = 2,
    InsufficientTrials = 3,
    Evaluated = 4
}

public enum ValidationLeakageAuditStatus
{
    Passed = 1,
    Failed = 2,
    NotAvailable = 3
}

public enum ExpectancyMetricType
{
    GrossExpectancyR = 1,
    NetExpectancyR = 2
}

public enum ProfitFactorMetricType
{
    GrossProfitFactor = 1,
    NetProfitFactor = 2
}

public enum ExpiredTradeMetricPolicy
{
    IncludeAsClosedAtExpiry = 1,
    ExcludeFromClosedMetrics = 2,
    IncludeOnlyWhenExitPriceKnown = 3
}

public enum QualificationRuleStatus
{
    Passed = 1,
    Failed = 2,
    Warning = 3,
    NotApplicable = 4,
    NotEvaluated = 5
}

public enum QualificationRuleApplicability
{
    Applicable = 1,
    NotApplicableForExperimentType = 2,
    InsufficientSample = 3,
    MissingMetric = 4
}

public enum StrategyRobustnessDecision
{
    Passed = 1,
    ConditionallyPassed = 2,
    FailedInsufficientTrainingSample = 3,
    FailedInsufficientValidationSample = 4,
    FailedNegativeTrainingExpectancy = 5,
    FailedNegativeValidationExpectancy = 6,
    FailedPerformanceCollapse = 7,
    FailedExcessiveValidationDrawdown = 8,
    FailedOpportunityCollapse = 9,
    FailedParameterInstability = 10,
    FailedCostSensitivity = 11,
    FailedDataQuality = 12,
    FailedConfigurationMismatch = 13,
    Invalid = 14,
    FailedDataIntegrity = 15,
    FailedNoTrainingTrialPassedGuardrails = 16
}

public enum ValidationOverlayStatus
{
    Improved = 1,
    Mixed = 2,
    Degraded = 3,
    InsufficientSample = 4,
    NotEvaluated = 5
}

public enum ValidationTrialStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    GuardrailRejected = 4,
    Interrupted = 5,
    LeakageFailed = 6
}

public enum ValidationTrialRecoverySource
{
    None = 0,
    ExistingStrategyLabRun = 1
}

public enum ValidationCandidateMetricClassification
{
    TrainingIncluded = 1,
    ValidationIncluded = 2,
    BoundaryCensored = 3,
    CrossSegmentOverlapExcludedFromValidation = 4,
    CrossSegmentOverlapExcludedFromTraining = 5,
    WarmupSuppressed = 6,
    AuditOnly = 7,
    Invalid = 8
}

public enum ValidationExportVerificationStatus
{
    Passed = 1,
    Failed = 2,
    NotRun = 3
}

public enum ValidationLaboratoryReadiness
{
    Ready = 1,
    ReadyWithWarnings = 2,
    Blocked = 3
}

public enum ValidationExperimentSupersessionStatus
{
    None = 0,
    Superseded = 1
}

public enum ValidationSelectionIntegrityStatus
{
    NotEvaluated = 0,
    Valid = 1,
    Passed = 1,
    InvalidSelectedTrial = 2,
    SelectionPolicyViolation = 3,
    NoEligibleTrial = 4,
    FailedNoEligibleTrials = 5,
    FailedSelectedTrialMissing = 6,
    FailedSelectedTrialNotTerminal = 7,
    FailedSelectedTrialIneligible = 8,
    FailedSelectedTrialNotRanked = 9,
    FailedParameterFingerprintMismatch = 10,
    FailedFrozenSnapshotMissing = 11,
    FailedFrozenSnapshotEmpty = 12,
    FailedFrozenFingerprintInvalid = 13,
    FailedMultipleSelectedTrials = 14,
    InfrastructureOnlyFallback = 15
}

public enum FrozenSnapshotValidationStatus
{
    NotEvaluated = 0,
    Valid = 1,
    Missing = 2,
    Empty = 3,
    StructurallyIncomplete = 4,
    InvalidJson = 5,
    ParameterDefinitionMismatch = 6,
    FingerprintMismatch = 7
}

public enum ValidationRiskBasisType
{
    NormalizedOneUnit = 1,
    RawCandidateQuantity = 2,
    PositionSized = 3,
    ShadowPortfolioPosition = 4,
    NotAvailable = 5
}

public enum ValidationMetricApplicability
{
    Evaluated = 1,
    NotEvaluated = 2,
    InsufficientSample = 3,
    InvalidRiskBasis = 4,
    MissingCostModel = 5,
    LegacyOnly = 6
}

public enum ValidationRiskBasisValidationStatus
{
    Valid = 1,
    MissingEntry = 2,
    MissingStop = 3,
    MissingQuantity = 4,
    NonPositiveRisk = 5,
    CurrencyMismatch = 6,
    PersistedRiskMismatch = 7,
    GrossRReconciliationFailed = 8,
    NetRReconciliationFailed = 9,
    LayerMismatch = 10,
    NotAvailable = 11,
    /// <summary>Aggregate-only: no statuses / no evaluable sample (ValidationMetrics/v1.3.2).</summary>
    InsufficientSample = 12,
    /// <summary>Aggregate-only: one or more invalid/missing/incompatible basis statuses.</summary>
    InvalidRiskBasis = 13
}