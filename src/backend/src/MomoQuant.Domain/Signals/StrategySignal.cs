namespace MomoQuant.Domain.Signals;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class StrategySignal : Entity
{
    public long TradingSessionId { get; set; }
    public long StrategyId { get; set; }
    public long SymbolId { get; set; }
    public Timeframe Timeframe { get; set; }
    public long? CandleId { get; set; }
    public SignalType SignalType { get; set; }
    public TradeDirection Direction { get; set; }
    public decimal Strength { get; set; }
    public decimal ConfidenceContribution { get; set; }
    public decimal? EntryPrice { get; set; }
    public decimal? SuggestedStopLoss { get; set; }
    public decimal? SuggestedTakeProfit { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? RawDataJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
