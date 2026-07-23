namespace MomoQuant.Domain.Backtesting;

using MomoQuant.Domain.Common;

public class BacktestResult : Entity
{
    public long BacktestRunId { get; set; }
    public decimal InitialBalance { get; set; }
    public decimal FinalBalance { get; set; }
    public decimal NetPnl { get; set; }
    public decimal NetPnlPercent { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal GrossLoss { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public int BreakEvenTrades { get; set; }
    public decimal WinRate { get; set; }
    public decimal WinRatePercent { get; set; }
    public decimal GrossPnl { get; set; }
    public decimal TotalFees { get; set; }
    public decimal TotalSlippage { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal Expectancy { get; set; }
    public decimal AverageWin { get; set; }
    public decimal AverageLoss { get; set; }
    public decimal LargestWin { get; set; }
    public decimal LargestLoss { get; set; }
    public decimal AverageRewardRisk { get; set; }
    public int TotalSignals { get; set; }
    public int ApprovedSignals { get; set; }
    public int RejectedSignals { get; set; }
    public int MissedOrders { get; set; }
    public int FilledOrders { get; set; }
    public int CancelledOrders { get; set; }
    public decimal? SharpeRatio { get; set; }
    public decimal? SortinoRatio { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
