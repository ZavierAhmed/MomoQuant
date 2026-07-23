namespace MomoQuant.Application.Strategies.VolatilityGatedSuperTrend.Dtos;

public sealed class VolatilityGatedSuperTrendFunnelCounts
{
    public int Evaluations { get; set; }
    public int SuperTrendBullishCount { get; set; }
    public int SuperTrendBearishCount { get; set; }
    public int VolatilityGatePassed { get; set; }
    public int VolatilityGateFailed { get; set; }
    public int MomentumPassed { get; set; }
    public int MomentumFailed { get; set; }
    public int RetestCount { get; set; }
    public int RetestMissing { get; set; }
    public int ConfirmationCount { get; set; }
    public int ConfirmationMissing { get; set; }
    public int CandidateSignals { get; set; }
    public int TradesCreated { get; set; }
    public Dictionary<string, int> RejectionReasonBreakdown { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class VolatilityGatedSuperTrendDiagnosticsDto
{
    public DateTime CandleTimeUtc { get; set; }
    public decimal Close { get; set; }
    public decimal? SuperTrendLine { get; set; }
    public string TrendDirection { get; set; } = "Neutral";
    public bool TrendFlip { get; set; }
    public decimal? FastAtr { get; set; }
    public decimal? SlowAtr { get; set; }
    public decimal? VolatilityRatio { get; set; }
    public bool VolatilityGatePassed { get; set; }
    public decimal? MacdLine { get; set; }
    public decimal? MacdSignal { get; set; }
    public decimal? MacdHistogram { get; set; }
    public bool MomentumPassed { get; set; }
    public decimal? DistanceFromSuperTrend { get; set; }
    public bool RetestDetected { get; set; }
    public bool ConfirmationDetected { get; set; }
    public string? CandidateDirection { get; set; }
    public string? RejectionReason { get; set; }
    public decimal? RiskReward { get; set; }
    public string FinalDecision { get; set; } = "NoTrade";
}

public sealed class VolatilityGatedSuperTrendEvaluationResult
{
    public bool IsEntry { get; init; }
    public string? Direction { get; init; }
    public decimal? EntryPrice { get; init; }
    public decimal? StopLoss { get; init; }
    public decimal? TakeProfit { get; init; }
    public decimal? Strength { get; init; }
    public string? Reason { get; init; }
    public string? RejectionCode { get; init; }
    public VolatilityGatedSuperTrendDiagnosticsDto? Diagnostics { get; init; }
}
