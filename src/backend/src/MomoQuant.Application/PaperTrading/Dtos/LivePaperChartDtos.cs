namespace MomoQuant.Application.PaperTrading.Dtos;

public sealed class LivePaperChartDto
{
    public long SessionId { get; init; }
    public required string Mode { get; init; }
    public required string Symbol { get; init; }
    public required string Exchange { get; init; }
    public required string Timeframe { get; init; }
    public bool Connected { get; init; }
    public decimal? LatestPrice { get; init; }
    public DateTime? LastLiveUpdateUtc { get; init; }
    public DateTime? LastClosedCandleUtc { get; init; }
    public DateTime? LastProcessedCandleUtc { get; init; }
    public required IReadOnlyList<LivePaperChartCandleDto> Candles { get; init; }
    public required IReadOnlyList<LivePaperChartIndicatorDto> Indicators { get; init; }
    public LivePaperChartCandleDto? CurrentCandle { get; init; }
    public required IReadOnlyList<LivePaperChartMarkerDto> OrderMarkers { get; init; }
    public required IReadOnlyList<LivePaperChartMarkerDto> TradeMarkers { get; init; }
    public required IReadOnlyList<LivePaperChartMarkerDto> RiskMarkers { get; init; }
    public required IReadOnlyList<LivePaperChartMarkerDto> AiMarkers { get; init; }
    public required IReadOnlyList<LivePaperChartMarkerDto> MissedOrderMarkers { get; init; }
    public required IReadOnlyList<LivePaperChartRangeLevelDto> RangeLevels { get; init; }
}

public sealed class LivePaperChartCandleDto
{
    public long? CandleId { get; init; }
    public DateTime Time { get; init; }
    public DateTime OpenTimeUtc { get; init; }
    public DateTime CloseTimeUtc { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
    public bool IsClosed { get; init; }
    public bool IsForming { get; init; }
}

public sealed class LivePaperChartIndicatorDto
{
    public DateTime Time { get; init; }
    public decimal? Ema20 { get; init; }
    public decimal? Ema50 { get; init; }
    public decimal? Ema200 { get; init; }
    public decimal? Vwap { get; init; }
}

public sealed class LivePaperChartMarkerDto
{
    public DateTime Time { get; init; }
    public required string Type { get; init; }
    public required string Side { get; init; }
    public decimal? Price { get; init; }
    public string? Label { get; init; }
    public string? Color { get; init; }
}

public sealed class LivePaperChartRangeLevelDto
{
    public required string Label { get; init; }
    public decimal Price { get; init; }
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public required string Color { get; init; }
}
