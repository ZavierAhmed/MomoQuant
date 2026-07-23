namespace MomoQuant.Domain.PaperTrading;

using MomoQuant.Domain.Common;

public class PaperAccount : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal InitialBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public decimal CurrentEquity { get; set; }
    public string Currency { get; set; } = "USDT";
    public decimal TotalRealizedPnl { get; set; }
    public decimal TotalUnrealizedPnl { get; set; }
    public decimal TotalFees { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
    public bool IsActive { get; set; } = true;
}
