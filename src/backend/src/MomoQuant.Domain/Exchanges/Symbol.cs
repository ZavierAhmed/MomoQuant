namespace MomoQuant.Domain.Exchanges;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class Symbol : AuditableEntity
{
    public long ExchangeId { get; set; }
    public string SymbolName { get; set; } = string.Empty;
    public string BaseAsset { get; set; } = string.Empty;
    public string QuoteAsset { get; set; } = string.Empty;
    public ContractType ContractType { get; set; }
    public int PricePrecision { get; set; }
    public int QuantityPrecision { get; set; }
    public decimal MinQty { get; set; }
    public decimal MinNotional { get; set; }
    public decimal TickSize { get; set; }
    public decimal StepSize { get; set; }
    public decimal MakerFeeRate { get; set; }
    public decimal TakerFeeRate { get; set; }
    public bool IsActive { get; set; } = true;
}
