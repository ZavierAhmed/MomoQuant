namespace MomoQuant.Application.Validation.Dtos;

public sealed class CandleCoverageDto
{
    public required string Symbol { get; init; }
    public required string Exchange { get; init; }
    public required string Timeframe { get; init; }
    public DateTime RequiredFromUtc { get; init; }
    public DateTime RequiredToUtc { get; init; }
    public DateTime? AvailableFromUtc { get; init; }
    public DateTime? AvailableToUtc { get; init; }
    public int CandleCount { get; init; }
    public int MissingCandleCountEstimate { get; init; }
    public string CoverageStatus { get; init; } = "Missing";
    public bool ImportedDuringRun { get; init; }
    public string? ImportError { get; init; }
}

public sealed class StrategyFunnelDiagnosticsDto
{
    public int Evaluations { get; init; }
    public int SuperTrendBullishCount { get; init; }
    public int SuperTrendBearishCount { get; init; }
    public int VolatilityGatePassedCount { get; init; }
    public int VolatilityGateFailedCount { get; init; }
    public int MomentumPassedCount { get; init; }
    public int MomentumFailedCount { get; init; }
    public int RetestDetectedCount { get; init; }
    public int RetestMissingCount { get; init; }
    public int ConfirmationDetectedCount { get; init; }
    public int ConfirmationMissingCount { get; init; }
    public int CandidateSignals { get; init; }
    public int RiskRejectedCount { get; init; }
    public int TradesCreated { get; init; }
    public string? TopRejectionReason { get; init; }
    public IReadOnlyDictionary<string, int> RejectionReasonBreakdown { get; init; } = new Dictionary<string, int>();
    public string? PipelineSummary { get; init; }
}

public sealed class ZeroTradeAnalysisDto
{
    public required string MostLikelyBlocker { get; init; }
    public required string SuggestedNextAction { get; init; }
    public string? RelatedParameter { get; init; }
    public required string Explanation { get; init; }
    public string? ReasonCode { get; init; }
}

public sealed class StrategyResearchBacktestResult
{
    public required StrategyPerformanceMetricsDto Metrics { get; init; }
    public string BacktestEngineUsed { get; init; } = "BacktestEngine";
    public int CandleCount { get; init; }
    public int TotalCandlesLoaded { get; init; }
    public int WarmupCandlesLoaded { get; init; }
    public int WarmupCandlesRequired { get; init; }
    public int SkippedForWarmupCount { get; init; }
    public int IndicatorSnapshotCount { get; init; }
    public int StrategyEvaluations { get; init; }
    public int EntrySignals { get; init; }
    public int RiskRejectedCount { get; init; }
    public StrategyFunnelDiagnosticsDto? Funnel { get; init; }
    public ZeroTradeAnalysisDto? ZeroTradeAnalysis { get; init; }
    public string? DiagnosticsSummary { get; init; }
    public bool EngineEvaluationBug { get; init; }
}

public sealed class StrategyResearchExecutionOptions
{
    public string ExecutionMode { get; init; } = "MarketFill";
    public decimal MakerFeeRate { get; init; } = 0.0002m;
    public decimal TakerFeeRate { get; init; } = 0.0005m;
    public int OrderExpiryCandles { get; init; } = 3;
    public bool UseAiScoring { get; init; }
    public bool StrictAiRequired { get; init; }
    public decimal MinConfidenceScore { get; init; } = 80m;
    public decimal SlippagePercent { get; init; }
    public bool AutoImportCandles { get; init; } = true;
    public string? VgResearchProfile { get; init; }
}
