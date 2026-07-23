namespace MomoQuant.Domain.MarketData;

using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

public class MarketDataImport : AuditableEntity
{
    public long ExchangeId { get; set; }
    public long SymbolId { get; set; }
    public Timeframe Timeframe { get; set; }
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public MarketDataImportStatus Status { get; set; }
    public int TotalReceived { get; set; }
    public int InsertedCount { get; set; }
    public int SkippedDuplicateCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public long? CreatedByUserId { get; set; }
}
