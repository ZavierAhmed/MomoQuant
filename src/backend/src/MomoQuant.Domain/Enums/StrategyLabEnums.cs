namespace MomoQuant.Domain.Enums;

public enum StrategyLabExecutionMode
{
    RawStrategy = 1,
    StrategyPlusConfidenceObservation = 2,
    StrategyPlusRiskObservation = 3,
    FullPipelineComparison = 4
}

public enum StrategyLabRunStatus
{
    Created = 1,
    PreparingData = 2,
    Running = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6,
    CheckingCoverage = 7,
    ImportingCandles = 8,
    VerifyingCoverage = 9,
    PreparingStrategy = 10,
    Evaluating = 11,
    SimulatingOutcomes = 12
}

public enum StrategyResearchCandidateStatus
{
    Detected = 1,
    StrategyQualified = 2,
    SimulationInvalid = 3,
    Simulated = 4,
    Closed = 5
}

public enum RawOutcomeStatus
{
    Pending = 1,
    Open = 2,
    Winner = 3,
    Loser = 4,
    Breakeven = 5,
    Expired = 6,
    Invalid = 7
}

public enum ResearchConfidenceDecision
{
    NotEvaluated = 1,
    Approved = 2,
    Rejected = 3
}

public enum ResearchRiskDecision
{
    NotEvaluated = 1,
    Approved = 2,
    Rejected = 3
}

public enum ResearchRiskPolicyEligibilityDecision
{
    NotEvaluated = 1,
    Eligible = 2,
    Ineligible = 3
}

public enum PortfolioRiskAssessmentStatus
{
    NotEvaluated = 1,
    Evaluated = 2,
    Unavailable = 3
}

public enum ResearchFinalPipelineDecision
{
    RawOnly = 1,
    Approved = 2,
    RejectedByConfidence = 3,
    RejectedByRisk = 4,
    RejectedByBoth = 5,
    RejectedByPolicy = 6
}

public enum StrategyEvidenceQuality
{
    VeryLow = 1,
    Low = 2,
    Medium = 3,
    High = 4
}

public enum ResearchExitOutcome
{
    NotSet = 0,
    TargetHit = 1,
    StopHit = 2,
    Expired = 3,
    Cancelled = 4,
    Invalid = 5,
    Open = 6
}

public enum ResearchNetResult
{
    Unknown = 0,
    Profitable = 1,
    Losing = 2,
    Breakeven = 3
}

public enum ResearchRiskScoreDecision
{
    NotEvaluated = 1,
    Passed = 2,
    Failed = 3
}

public enum ResearchHardRuleComplianceDecision
{
    NotEvaluated = 1,
    Compliant = 2,
    NonCompliant = 3
}

public enum ExposureSemanticsVersion
{
    LegacyAmbiguous = 1,
    NotionalExposureV1 = 2,
    MarginUsageV1 = 3,
    ExplicitFuturesExposureV2 = 4
}

public enum DrawdownCalculationMode
{
    RealizedOnly = 1,
    MarkToMarket = 2
}

public enum StrategyLabPortfolioPath
{
    RiskOnly = 1,
    FullPipeline = 2
}

public enum ShadowEntryDecision
{
    NotEvaluated = 0,
    Opened = 1,
    RejectedByCandidateRisk = 2,
    RejectedByPortfolioRisk = 3,
    RejectedByConfidence = 4,
    RejectedByPolicy = 5,
    RejectedByMultipleSources = 6,
    Invalid = 7
}

public enum GenericRiskFieldSource
{
    Legacy = 0,
    IsolatedCandidate = 1,
    RiskOnly = 2,
    FullPipeline = 3
}
