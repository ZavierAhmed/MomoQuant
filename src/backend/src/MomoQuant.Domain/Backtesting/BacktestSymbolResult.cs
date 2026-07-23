namespace MomoQuant.Domain.Backtesting;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class BacktestSymbolResult : Entity
{
    public long BacktestRunId { get; set; }
    public long SymbolId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public Timeframe Timeframe { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal NetPnl { get; set; }
    public decimal WinRatePercent { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
    public decimal TotalFees { get; set; }
    public int MissedOrders { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
