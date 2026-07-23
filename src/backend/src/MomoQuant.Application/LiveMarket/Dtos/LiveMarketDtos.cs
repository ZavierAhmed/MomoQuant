namespace MomoQuant.Application.LiveMarket.Dtos;

public sealed class LiveMarketSubscribeRequest
{
    public long ExchangeId { get; init; }
    public long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public long? PaperSessionId { get; init; }
}

public sealed class LiveMarketStatusDto
{
    public required string Provider { get; init; }
    public bool Connected { get; init; }
    public required IReadOnlyList<LiveMarketSubscriptionDto> Subscriptions { get; init; }
    public string? LastError { get; init; }
    public int ReconnectAttempts { get; init; }
}

public sealed class LiveMarketSubscriptionDto
{
    public long ExchangeId { get; init; }
    public long SymbolId { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public required string StreamName { get; init; }
    public required string Status { get; init; }
    public DateTime? SubscribedAtUtc { get; init; }
    public DateTime? LastUpdateUtc { get; init; }
    public DateTime? LastClosedCandleUtc { get; init; }
    public string? Warning { get; init; }
    public string? LastError { get; init; }
}

public sealed class LiveMarketSnapshotDto
{
    public long ExchangeId { get; init; }
    public long SymbolId { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public decimal? LatestPrice { get; init; }
    public decimal? Open { get; init; }
    public decimal? High { get; init; }
    public decimal? Low { get; init; }
    public decimal? Close { get; init; }
    public decimal? Volume { get; init; }
    public decimal? QuoteVolume { get; init; }
    public int? TradeCount { get; init; }
    public DateTime? OpenTimeUtc { get; init; }
    public DateTime? CloseTimeUtc { get; init; }
    public bool? IsClosed { get; init; }
    public DateTime? LastUpdateUtc { get; init; }
    public DateTime? LastLiveUpdateUtc { get; init; }
    public DateTime? LastClosedCandleUtc { get; init; }
    public LiveCandleDto? CurrentCandle { get; init; }
    public LiveCandleDto? LastClosedCandle { get; init; }
    public string Source { get; init; } = "BinanceWebSocket";
}

public sealed class LiveCandleDto
{
    public DateTime OpenTimeUtc { get; init; }
    public DateTime CloseTimeUtc { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
    public decimal QuoteVolume { get; init; }
    public int TradeCount { get; init; }
    public bool IsClosed { get; init; }
}

public sealed class LiveMarketDiagnosticsDto
{
    public required string Provider { get; init; }
    public bool Connected { get; init; }
    public int ReconnectAttempts { get; init; }
    public string? LastError { get; init; }
    public required IReadOnlyList<LiveMarketSubscriptionDiagnosticsDto> Subscriptions { get; init; }
}

public sealed class LiveMarketSubscriptionDiagnosticsDto
{
    public long ExchangeId { get; init; }
    public long SymbolId { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public required string StreamName { get; init; }
    public required string ConnectionState { get; init; }
    public DateTime? SubscribedAtUtc { get; init; }
    public DateTime? LastRawMessageAtUtc { get; init; }
    public DateTime? LastParsedMessageAtUtc { get; init; }
    public DateTime? LastSnapshotUpdateUtc { get; init; }
    public DateTime? LastClosedCandleUtc { get; init; }
    public long MessagesReceived { get; init; }
    public long MessagesParsed { get; init; }
    public long ParseErrors { get; init; }
    public string? LastError { get; init; }
    public string? Warning { get; init; }
    public IReadOnlyList<long> LinkedSessionIds { get; init; } = [];
}
