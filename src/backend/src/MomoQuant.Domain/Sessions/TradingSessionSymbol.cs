namespace MomoQuant.Domain.Sessions;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class TradingSessionSymbol : Entity
{
    public long TradingSessionId { get; set; }
    public long SymbolId { get; set; }
    public Timeframe Timeframe { get; set; }
    public Timeframe HigherTimeframe { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
}
