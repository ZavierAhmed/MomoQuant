namespace MomoQuant.Domain.Sessions;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class TradingSession : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public TradingMode Mode { get; set; }
    public TradingSessionStatus Status { get; set; }
    public long ExchangeId { get; set; }
    public long StartedByUserId { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? StoppedAtUtc { get; set; }
    public decimal InitialBalance { get; set; }
    public decimal? FinalBalance { get; set; }
    public string? Notes { get; set; }
}
