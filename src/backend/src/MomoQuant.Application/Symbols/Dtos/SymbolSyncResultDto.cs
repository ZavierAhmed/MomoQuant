namespace MomoQuant.Application.Symbols.Dtos;

public sealed class SymbolSyncResultDto
{
    public required long ExchangeId { get; init; }
    public required int CreatedCount { get; init; }
    public required int UpdatedCount { get; init; }
    public required int TotalCount { get; init; }
}
