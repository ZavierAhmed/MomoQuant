using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.StrategyLab.Dtos;

public sealed class StrategyLabCandidateQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }
    public string? Search { get; set; }
    public TradeDirection? Direction { get; set; }
    public RawOutcomeStatus? RawOutcome { get; set; }
    public ResearchConfidenceDecision? ConfidenceDecision { get; set; }
    public decimal? ConfidenceMin { get; set; }
    public decimal? ConfidenceMax { get; set; }
    public ResearchRiskDecision? RiskDecision { get; set; }
    public decimal? RiskMin { get; set; }
    public decimal? RiskMax { get; set; }
    public bool? ProfitableOnly { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public string? QuickFilter { get; set; }
    public ShadowEntryDecision? RiskOnlyEntryDecision { get; set; }
    public ShadowEntryDecision? FullPipelineEntryDecision { get; set; }
    public string? PathDecisionDifference { get; set; }
    public string? RiskOnlyFailedRule { get; set; }
    public string? FullPipelineFailedRule { get; set; }
    public decimal? RiskOnlyDrawdownMin { get; set; }
    public decimal? FullPipelineDrawdownMin { get; set; }
}

public sealed class PagedResultDto<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalItems { get; init; }
    public int TotalPages { get; init; }
}

public sealed class GateDecisionSummaryDto
{
    public int EvaluatedCount { get; init; }
    public int ApprovedCount { get; init; }
    public int RejectedCount { get; init; }
    public decimal ApprovalRate { get; init; }
    public decimal RejectionRate { get; init; }
    public decimal? CurrentThreshold { get; init; }
    public decimal? AverageScore { get; init; }
    public decimal? MedianScore { get; init; }
    public decimal? AverageWinnerScore { get; init; }
    public decimal? MedianWinnerScore { get; init; }
    public decimal? AverageLoserScore { get; init; }
    public decimal? MedianLoserScore { get; init; }
}

public sealed class RejectedOutcomeGroupDto
{
    public int Count { get; init; }
    public decimal PercentageOfOutcomeGroup { get; init; }
    public decimal? AverageScore { get; init; }
    public decimal? MedianScore { get; init; }
    public decimal? AverageMarginBelowThreshold { get; init; }
    public decimal HypotheticalNetPnl { get; init; }
    public decimal? HypotheticalAverageR { get; init; }
}

public sealed class ScoreBucketDto
{
    public required string Label { get; init; }
    public int MinInclusive { get; init; }
    public int MaxInclusive { get; init; }
    public int CandidateCount { get; init; }
    public int WinnerCount { get; init; }
    public int LoserCount { get; init; }
    public decimal WinRate { get; init; }
    public decimal NetPnl { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal? AverageR { get; init; }
    public decimal? AverageMfe { get; init; }
    public decimal? AverageMae { get; init; }
}

public sealed class ThresholdSimulationRowDto
{
    public decimal Threshold { get; init; }
    public bool IsCurrentThreshold { get; init; }
    public int AcceptedCount { get; init; }
    public int RejectedCount { get; init; }
    public decimal AcceptedWinRate { get; init; }
    public decimal AcceptedNetPnl { get; init; }
    public decimal AcceptedProfitFactor { get; init; }
    public decimal AcceptedMaxDrawdownPercent { get; init; }
    public decimal? AcceptedAverageR { get; init; }
    public decimal PercentOfRawPnlPreserved { get; init; }
}

public sealed class RiskRejectionReasonRowDto
{
    public required string Reason { get; init; }
    public int RejectedCount { get; init; }
    public int WinnerCount { get; init; }
    public int LoserCount { get; init; }
    public decimal WinRate { get; init; }
    public decimal HypotheticalNetPnl { get; init; }
    public decimal? AverageR { get; init; }
}

public sealed class WinnerLoserComparisonDto
{
    public int WinnerCount { get; init; }
    public int LoserCount { get; init; }
    public decimal? AverageWinnerConfidence { get; init; }
    public decimal? AverageLoserConfidence { get; init; }
    public decimal? MedianWinnerConfidence { get; init; }
    public decimal? MedianLoserConfidence { get; init; }
    public decimal? AverageWinnerRiskScore { get; init; }
    public decimal? AverageLoserRiskScore { get; init; }
    public decimal? MedianWinnerRiskScore { get; init; }
    public decimal? MedianLoserRiskScore { get; init; }
    public decimal? AverageWinnerMfe { get; init; }
    public decimal? AverageLoserMfe { get; init; }
    public decimal? AverageWinnerMae { get; init; }
    public decimal? AverageLoserMae { get; init; }
    public decimal? AverageWinnerStopDistancePercent { get; init; }
    public decimal? AverageLoserStopDistancePercent { get; init; }
    public decimal? AverageWinnerRewardRisk { get; init; }
    public decimal? AverageLoserRewardRisk { get; init; }
}

public sealed class EnhancedGatedSubsetDto
{
    public int CandidateCount { get; init; }
    public int ClosedTradeCount { get; init; }
    public int Winners { get; init; }
    public int Losers { get; init; }
    public decimal WinRate { get; init; }
    public decimal NetPnl { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdownPercent { get; init; }
    public decimal? AverageConfidence { get; init; }
    public decimal? AverageRiskScore { get; init; }
    public decimal? AverageR { get; init; }
}

public sealed class StrategyLabGateAnalysisDto
{
    public StrategyLabExecutionMode ExecutionMode { get; init; }
    public string Disclaimer { get; init; } =
        "Threshold simulation uses already observed raw candidate outcomes. It does not change live or saved confidence settings.";
    public GateDecisionSummaryDto? ConfidenceSummary { get; init; }
    public RejectedOutcomeGroupDto? ConfidenceRejectedWinners { get; init; }
    public RejectedOutcomeGroupDto? ConfidenceRejectedLosers { get; init; }
    public IReadOnlyList<ScoreBucketDto> ConfidenceBuckets { get; init; } = [];
    public IReadOnlyList<ThresholdSimulationRowDto> ConfidenceThresholdSimulation { get; init; } = [];
    public GateDecisionSummaryDto? RiskSummary { get; init; }
    public RejectedOutcomeGroupDto? RiskRejectedWinners { get; init; }
    public RejectedOutcomeGroupDto? RiskRejectedLosers { get; init; }
    public IReadOnlyList<RiskRejectionReasonRowDto> RiskReasonAnalysis { get; init; } = [];
    public WinnerLoserComparisonDto OverallWinnerLoserComparison { get; init; } = new();
    public decimal? ConfidenceSeparation { get; set; }
    public ScoreDistributionDiagnosticsDto? ConfidenceScoreDiagnostics { get; init; }
    public ScoreDistributionDiagnosticsDto? RiskScoreDiagnostics { get; init; }
    public EnhancedGatedSubsetDto Raw { get; init; } = new();
    public EnhancedGatedSubsetDto ConfidenceApproved { get; init; } = new();
    public EnhancedGatedSubsetDto ConfidenceRejected { get; init; } = new();
    public EnhancedGatedSubsetDto RiskApproved { get; init; } = new();
    public EnhancedGatedSubsetDto RiskRejected { get; init; } = new();
    public EnhancedGatedSubsetDto FullPipeline { get; init; } = new();
    public IReadOnlyList<string> Interpretations { get; set; } = [];
}
