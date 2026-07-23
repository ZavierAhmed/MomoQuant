using MomoQuant.Domain.Common;

namespace MomoQuant.Domain.TradingSystems;

public class SkLivePaperEvent : Entity
{
    public long SessionId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? DetailsJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
