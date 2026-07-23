namespace MomoQuant.Application.Ai.Dtos;

public sealed class AiHealthDto
{
    public required string Status { get; init; }
    public required string Service { get; init; }
    public required string Version { get; init; }
}

public sealed class DetectRegimeRequestDto
{
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public decimal? Ema20 { get; init; }
    public decimal? Ema50 { get; init; }
    public decimal? Ema200 { get; init; }
    public decimal? Close { get; init; }
    public decimal? AtrPercent { get; init; }
    public decimal? Rsi14 { get; init; }
    public decimal? Volume { get; init; }
    public decimal? VolumeSma20 { get; init; }
    public bool? SwingHighRising { get; init; }
    public bool? SwingLowRising { get; init; }
    public decimal? RecentRangePercent { get; init; }
}

public sealed class DetectRegimeResponseDto
{
    public required string Regime { get; init; }
    public int Confidence { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = [];
    public bool UsedFallback { get; init; }
}

public sealed class ScoreConfidenceRequestDto
{
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public required string StrategyCode { get; init; }
    public required string SignalDirection { get; init; }
    public required string MarketRegime { get; init; }
    public decimal StrategyStrength { get; init; }
    public decimal? EmaAlignmentScore { get; init; }
    public bool? VolumeConfirmation { get; init; }
    public decimal? Rsi14 { get; init; }
    public decimal? AtrPercent { get; init; }
    public decimal? RewardRiskRatio { get; init; }
    public decimal? SpreadPercent { get; init; }
    public decimal? RecentWinRate { get; init; }
}

public sealed class ScoreConfidenceResponseDto
{
    public string? AdvisoryRulesVersion { get; init; }
    public string? EvaluationStatus { get; init; }
    public bool IsStrategySupported { get; init; }
    public IReadOnlyList<string> SupportedInputs { get; init; } = [];
    public IReadOnlyList<string> MissingInputs { get; init; } = [];
    public int? AdvisoryScore { get; init; }
    public string? AdvisoryClassification { get; init; }
    public int ConfidenceScore { get; init; }
    public required string Classification { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool UsedFallback { get; init; }

    /// <summary>Advisory-only eligibility. Does not authorize trade execution.</summary>
    public bool AdvisoryEligible { get; init; }

    /// <summary>Temporary compat alias for <see cref="AdvisoryEligible"/>.</summary>
    public bool TradeAllowed
    {
        get => AdvisoryEligible;
        init => AdvisoryEligible = value;
    }
}

public sealed class DetectAnomalyRequestDto
{
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public decimal? AtrPercent { get; init; }
    public decimal? Volume { get; init; }
    public decimal? VolumeSma20 { get; init; }
    public decimal? SpreadPercent { get; init; }
    public decimal? CandleRangePercent { get; init; }
    public decimal? PriceGapPercent { get; init; }
}

public sealed class DetectAnomalyResponseDto
{
    public bool IsAnomalous { get; init; }
    public required string Severity { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = [];
    public bool UsedFallback { get; init; }
}

public sealed class ExplainTradeRequestDto
{
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public required string StrategyCode { get; init; }
    public required string SignalDirection { get; init; }
    public required string MarketRegime { get; init; }
    public decimal ConfidenceScore { get; init; }
    public required string RiskDecision { get; init; }
    public required string RiskReason { get; init; }
    public required string StrategyReason { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class ExplainTradeResponseDto
{
    public required string Summary { get; init; }
    public IReadOnlyList<string> Details { get; init; } = [];
    public required string Caution { get; init; }
    public bool UsedFallback { get; init; }
}

public sealed class AiDecisionDto
{
    public long Id { get; init; }
    public long? TradingSessionId { get; init; }
    public long? StrategySignalId { get; init; }
    public long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public string? StrategyCode { get; init; }
    public required string MarketRegime { get; init; }
    public decimal? RegimeConfidence { get; init; }
    public decimal ConfidenceScore { get; init; }
    public string? ConfidenceClassification { get; init; }
    public bool IsAnomalous { get; init; }
    public string? AnomalySeverity { get; init; }
    public bool TradeAllowed { get; init; }
    public string? Summary { get; init; }
    public string? Explanation { get; init; }
    public string? ReasonsJson { get; init; }
    public string? WarningsJson { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public bool UsedFallback { get; init; }
}

public sealed class EvaluateSignalRequestDto
{
    public long StrategySignalId { get; init; }
    public long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public long RiskProfileId { get; init; }
}

public sealed class EvaluateSignalResponseDto
{
    public long AiDecisionId { get; init; }
    public required DetectRegimeResponseDto Regime { get; init; }
    public required ScoreConfidenceResponseDto Confidence { get; init; }
    public DetectAnomalyResponseDto? Anomaly { get; init; }

    /// <summary>Advisory-only eligibility. Does not authorize trade execution.</summary>
    public bool AdvisoryEligible { get; init; }

    /// <summary>Temporary compat alias for <see cref="AdvisoryEligible"/>.</summary>
    public bool TradeAllowed
    {
        get => AdvisoryEligible;
        init => AdvisoryEligible = value;
    }
}
