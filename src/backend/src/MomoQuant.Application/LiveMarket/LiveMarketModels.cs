using MomoQuant.Application.MarketData;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.LiveMarket;

public sealed class LiveMarketSubscriptionKey : IEquatable<LiveMarketSubscriptionKey>
{
    public required long ExchangeId { get; init; }
    public required long SymbolId { get; init; }
    public required string Symbol { get; init; }
    public required Timeframe Timeframe { get; init; }

    public string StreamName => $"{Symbol.ToLowerInvariant()}@kline_{TimeframeParser.ToApiString(Timeframe)}";

    public bool Equals(LiveMarketSubscriptionKey? other) =>
        other is not null
        && ExchangeId == other.ExchangeId
        && SymbolId == other.SymbolId
        && Timeframe == other.Timeframe;

    public override bool Equals(object? obj) => Equals(obj as LiveMarketSubscriptionKey);

    public override int GetHashCode() => HashCode.Combine(ExchangeId, SymbolId, Timeframe);
}

public sealed class LiveCandleUpdate
{
    public required long ExchangeId { get; init; }
    public required long SymbolId { get; init; }
    public required string Symbol { get; init; }
    public required Timeframe Timeframe { get; init; }
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
    public required DateTime EventTimeUtc { get; init; }
    public required string Source { get; init; }
}

public sealed class LiveMarketSnapshot
{
    public required long ExchangeId { get; init; }
    public required long SymbolId { get; init; }
    public required string Symbol { get; init; }
    public required Timeframe Timeframe { get; init; }
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
    public LiveCandleUpdate? CurrentCandle { get; init; }
    public LiveCandleUpdate? LastClosedCandle { get; init; }
    public string Source { get; init; } = "BinanceWebSocket";
}

public enum LiveSubscriptionStatus
{
    Connecting,
    Connected,
    Disconnected,
    Reconnecting,
    Failed
}

public sealed class LiveSubscriptionState
{
    public required LiveMarketSubscriptionKey Key { get; init; }
    public LiveSubscriptionStatus Status { get; set; } = LiveSubscriptionStatus.Connecting;
    public DateTime? SubscribedAtUtc { get; set; }
    public DateTime? LastUpdateUtc { get; set; }
    public DateTime? LastClosedCandleUtc { get; set; }
    public string? LastError { get; set; }
    public string? Warning { get; set; }
    public HashSet<long> LinkedSessionIds { get; } = [];
}

public sealed class LiveMarketConnectionStatus
{
    public required string Provider { get; init; }
    public bool Connected { get; init; }
    public string? LastError { get; init; }
    public int ReconnectAttempts { get; init; }
    public required IReadOnlyList<LiveSubscriptionState> Subscriptions { get; init; }
}
