namespace MomoQuant.Domain.Execution;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class OrderFill : Entity
{
    public long OrderId { get; set; }
    public string? ExternalFillId { get; set; }
    public decimal FillPrice { get; set; }
    public decimal FillQuantity { get; set; }
    public decimal Fee { get; set; }
    public string FeeAsset { get; set; } = string.Empty;
    public LiquidityType LiquidityType { get; set; }
    public DateTime FilledAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
