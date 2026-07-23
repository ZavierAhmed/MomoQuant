namespace MomoQuant.Application.MarketData.Dtos;

public sealed class CandleDto
{
    public required long Id { get; init; }
    public required long ExchangeId { get; init; }
    public required long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public required DateTime OpenTimeUtc { get; init; }
    public required DateTime CloseTimeUtc { get; init; }
    public required decimal Open { get; init; }
    public required decimal High { get; init; }
    public required decimal Low { get; init; }
    public required decimal Close { get; init; }
    public required decimal Volume { get; init; }
    public required decimal QuoteVolume { get; init; }
    public required int TradeCount { get; init; }
    public required bool IsClosed { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}
