using MomoQuant.Domain.Common;

namespace MomoQuant.Domain.Benchmarks;

public class StrategyBenchmarkResult : Entity
{
    public long BenchmarkRunId { get; set; }
    public long StrategyId { get; set; }
    public string StrategyCode { get; set; } = string.Empty;
    public string StrategyName { get; set; } = string.Empty;
    public long? SymbolId { get; set; }
    public string? Symbol { get; set; }
    public string? Timeframe { get; set; }
    public long? BacktestRunId { get; set; }
    public decimal InitialBalance { get; set; }
    public decimal FinalBalance { get; set; }
    public decimal NetPnl { get; set; }
    public decimal NetPnlPercent { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal GrossLoss { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public int BreakEvenTrades { get; set; }
    public decimal WinRatePercent { get; set; }
    public decimal AverageWin { get; set; }
    public decimal AverageLoss { get; set; }
    public decimal LargestWin { get; set; }
    public decimal LargestLoss { get; set; }
    public decimal AverageRewardRisk { get; set; }
    public decimal TotalFees { get; set; }
    public int TotalSignals { get; set; }
    public int EntrySignals { get; set; }
    public int NoTradeSignals { get; set; }
    public int ApprovedSignals { get; set; }
    public int RejectedSignals { get; set; }
    public int MissedOrders { get; set; }
    public int FilledOrders { get; set; }
    public decimal AverageConfidenceScore { get; set; }
    public string Grade { get; set; } = "N/A";
    public decimal Score { get; set; }
    public string StrengthsJson { get; set; } = "[]";
    public string WeaknessesJson { get; set; } = "[]";
    public string WarningsJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; }
}
