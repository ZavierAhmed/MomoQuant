namespace MomoQuant.Domain.MarketData;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class Candle : Entity
{
    public long ExchangeId { get; set; }
    public long SymbolId { get; set; }
    public Timeframe Timeframe { get; set; }
    public DateTime OpenTimeUtc { get; set; }
    public DateTime CloseTimeUtc { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
    public decimal QuoteVolume { get; set; }
    public int TradeCount { get; set; }
    public bool IsClosed { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
