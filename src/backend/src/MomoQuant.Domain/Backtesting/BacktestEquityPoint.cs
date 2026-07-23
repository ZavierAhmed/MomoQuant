namespace MomoQuant.Domain.Backtesting;

using MomoQuant.Domain.Common;

public class BacktestEquityPoint : Entity
{
    public long BacktestRunId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public decimal Balance { get; set; }
    public decimal Equity { get; set; }
    public decimal Drawdown { get; set; }
    public decimal DrawdownPercent { get; set; }
    public int OpenPositionCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
