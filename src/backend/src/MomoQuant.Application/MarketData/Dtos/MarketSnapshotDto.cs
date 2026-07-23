namespace MomoQuant.Application.MarketData.Dtos;

public sealed class MarketSnapshotDto
{
    public required long SymbolId { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public CandleDto? LatestCandle { get; init; }
    public decimal? LatestPrice { get; init; }
    public DateTime? LatestUpdateTimeUtc { get; init; }
    public required int CandleCountAvailable { get; init; }
    public bool IndicatorsAvailable { get; init; }
    public decimal? Spread { get; init; }
}
