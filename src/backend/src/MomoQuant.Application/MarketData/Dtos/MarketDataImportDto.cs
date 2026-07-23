using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.MarketData.Dtos;

public sealed class MarketDataImportDto
{
    public required long ImportId { get; init; }
    public required long ExchangeId { get; init; }
    public required long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public required DateTime FromUtc { get; init; }
    public required DateTime ToUtc { get; init; }
    public required MarketDataImportStatus Status { get; init; }
    public required int TotalReceived { get; init; }
    public required int InsertedCount { get; init; }
    public required int SkippedDuplicateCount { get; init; }
    public string? ErrorMessage { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
}
