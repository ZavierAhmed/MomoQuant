namespace MomoQuant.Domain.Backtesting;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class BacktestStrategyResult : Entity
{
    public long BacktestRunId { get; set; }
    public StrategyCode StrategyCode { get; set; }
    public int TotalSignals { get; set; }
    public int ApprovedSignals { get; set; }
    public int RejectedSignals { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal NetPnl { get; set; }
    public decimal WinRatePercent { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
    public decimal AverageConfidenceScore { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
