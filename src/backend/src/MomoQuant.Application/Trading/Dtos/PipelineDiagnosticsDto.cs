using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;

namespace MomoQuant.Application.Trading.Dtos;

public sealed class PipelineDiagnosticsDto
{
    public int CandleCount { get; init; }
    public int IndicatorSnapshotCount { get; init; }
    public int StrategyEvaluations { get; init; }
    public int NoTradeSignals { get; init; }
    public int EntrySignals { get; init; }
    public int CandidateSignals { get; init; }
    public int WarningSignals { get; init; }
    public int InvalidSignals { get; init; }
    public int ConfidenceEvaluations { get; init; }
    public int ConfidenceApproved { get; init; }
    public int ConfidenceRejected { get; init; }
    public int RiskEvaluations { get; init; }
    public int RiskApproved { get; init; }
    public int RiskRejected { get; init; }
    public int OrdersCreated { get; init; }
    public int OrdersFilled { get; init; }
    public int OrdersMissed { get; init; }
    public int TradesOpened { get; init; }
    public int TradesClosed { get; init; }
    public bool AiEnabled { get; init; }
    public int AiDecisionsCreated { get; init; }
    public decimal EffectiveMinConfidenceScore { get; init; }
    public decimal? AverageNormalizedConfidenceScore { get; init; }
    public decimal? LowestConfidenceScore { get; init; }
    public decimal? HighestConfidenceScore { get; init; }
    public required IReadOnlyList<PipelineRuleCountDto> TopRiskRejectionRules { get; init; }
    public required IReadOnlyList<PipelineReasonCountDto> TopNoTradeReasons { get; init; }
    public required IReadOnlyList<PipelineReasonCountDto> TopStrategySignalReasons { get; init; }
    public required IReadOnlyList<CandidateTradeRecord> CandidateTrades { get; init; }
    public required IReadOnlyList<ShadowTradeRecord> ShadowTrades { get; init; }
    public required RejectionQualityDto RejectionQuality { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public BbLiquiditySweepPipelineDiagnosticsDto? BbLiquiditySweep { get; init; }
}

public sealed class BbLiquiditySweepPipelineDiagnosticsDto
{
    public required BbLiquiditySweepFunnelCounts FunnelCounts { get; init; }
    public required IReadOnlyDictionary<string, int> NoTradeReasonBreakdown { get; init; }
    public required string PipelineSummary { get; init; }
    public string? WhyZeroTradesAnalysis { get; init; }
    public string? TopNoTradeReason { get; init; }
    public int TopNoTradeReasonCount { get; init; }
    public IReadOnlyList<BbLiquiditySweepSampleEvaluation> SampleRejectedEvaluations { get; init; } = [];
}

public sealed class PipelineRuleCountDto
{
    public required string RuleKey { get; init; }
    public int Count { get; init; }
}

public sealed class PipelineReasonCountDto
{
    public string? StrategyCode { get; init; }
    public required string Reason { get; init; }
    public int Count { get; init; }
}

public sealed class RejectionQualityDto
{
    public int RejectedCandidateCount { get; init; }
    public int RejectedByConfidenceCount { get; init; }
    public int RejectedByRiskCount { get; init; }
    public int RejectedByBothCount { get; init; }
    public int ShadowTradesSimulated { get; init; }
    public int RejectedWouldHaveWon { get; init; }
    public int RejectedWouldHaveLost { get; init; }
    public int RejectedBreakEven { get; init; }
    public int RejectedNotEnoughData { get; init; }
    public decimal ShadowNetPnl { get; init; }
    public int ConfidenceFalseRejectCount { get; init; }
    public int RiskFalseRejectCount { get; init; }
    public int ConfidenceCorrectRejectCount { get; init; }
    public int RiskCorrectRejectCount { get; init; }
}
