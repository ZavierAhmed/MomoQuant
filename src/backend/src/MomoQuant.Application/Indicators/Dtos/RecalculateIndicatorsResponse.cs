namespace MomoQuant.Application.Indicators.Dtos;

public sealed class RecalculateIndicatorsResponse
{
    public required long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public required DateTime FromUtc { get; init; }
    public required DateTime ToUtc { get; init; }
    public required int CandlesProcessed { get; init; }
    public required int SnapshotsInserted { get; init; }
    public required int SnapshotsUpdated { get; init; }
    public required string Status { get; init; }
}
