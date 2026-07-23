namespace MomoQuant.Domain.Ai;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class AiDecision : Entity
{
    public long? TradingSessionId { get; set; }
    public long SymbolId { get; set; }
    public Timeframe Timeframe { get; set; }
    public long? CandleId { get; set; }
    public long? SignalId { get; set; }
    public MarketRegime MarketRegime { get; set; }
    public decimal? RegimeConfidence { get; set; }
    public decimal ConfidenceScore { get; set; }
    public string? ConfidenceClassification { get; set; }
    public StrategyCode? PreferredStrategyCode { get; set; }
    public bool IsAnomalous { get; set; }
    public string? AnomalySeverity { get; set; }
    public decimal? RiskAdjustment { get; set; }
    public bool TradeAllowed { get; set; }
    public string? Summary { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string? ReasonsJson { get; set; }
    public string? WarningsJson { get; set; }
    public string? RawRequestJson { get; set; }
    public string? RawResponseJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
