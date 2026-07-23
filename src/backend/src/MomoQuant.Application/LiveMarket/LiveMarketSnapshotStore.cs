using MomoQuant.Application.LiveMarket.Dtos;
using MomoQuant.Application.MarketData;

namespace MomoQuant.Application.LiveMarket;

public interface ILiveMarketSnapshotStore
{
    void Update(LiveMarketSnapshot snapshot);

    LiveMarketSnapshot? Get(long symbolId, string timeframe);

    IReadOnlyList<LiveMarketSnapshot> GetAll();
}

public sealed class LiveMarketSnapshotStore : ILiveMarketSnapshotStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, LiveMarketSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

    public void Update(LiveMarketSnapshot snapshot)
    {
        var key = BuildKey(snapshot.SymbolId, TimeframeParser.ToApiString(snapshot.Timeframe));
        lock (_sync)
        {
            _snapshots[key] = snapshot;
        }
    }

    public LiveMarketSnapshot? Get(long symbolId, string timeframe)
    {
        var key = BuildKey(symbolId, timeframe);
        lock (_sync)
        {
            return _snapshots.TryGetValue(key, out var snapshot) ? snapshot : null;
        }
    }

    public IReadOnlyList<LiveMarketSnapshot> GetAll()
    {
        lock (_sync)
        {
            return _snapshots.Values.ToList();
        }
    }

    private static string BuildKey(long symbolId, string timeframe) => $"{symbolId}:{timeframe}";
}

public static class LiveMarketMapper
{
    public static LiveMarketStatusDto MapStatus(LiveMarketConnectionStatus status) => new()
    {
        Provider = status.Provider,
        Connected = status.Connected,
        LastError = status.LastError,
        ReconnectAttempts = status.ReconnectAttempts,
        Subscriptions = status.Subscriptions.Select(MapSubscription).ToList()
    };

    public static LiveMarketSubscriptionDto MapSubscription(LiveSubscriptionState state) => new()
    {
        ExchangeId = state.Key.ExchangeId,
        SymbolId = state.Key.SymbolId,
        Symbol = state.Key.Symbol,
        Timeframe = TimeframeParser.ToApiString(state.Key.Timeframe),
        StreamName = state.Key.StreamName,
        Status = state.Status.ToString(),
        SubscribedAtUtc = state.SubscribedAtUtc,
        LastUpdateUtc = state.LastUpdateUtc,
        LastClosedCandleUtc = state.LastClosedCandleUtc,
        Warning = state.Warning,
        LastError = state.LastError
    };

    public static LiveMarketSnapshotDto MapSnapshot(LiveMarketSnapshot snapshot) => new()
    {
        ExchangeId = snapshot.ExchangeId,
        SymbolId = snapshot.SymbolId,
        Symbol = snapshot.Symbol,
        Timeframe = TimeframeParser.ToApiString(snapshot.Timeframe),
        LatestPrice = snapshot.LatestPrice,
        Open = snapshot.Open,
        High = snapshot.High,
        Low = snapshot.Low,
        Close = snapshot.Close,
        Volume = snapshot.Volume,
        QuoteVolume = snapshot.QuoteVolume,
        TradeCount = snapshot.TradeCount,
        OpenTimeUtc = snapshot.OpenTimeUtc,
        CloseTimeUtc = snapshot.CloseTimeUtc,
        IsClosed = snapshot.IsClosed,
        LastUpdateUtc = snapshot.LastUpdateUtc,
        LastLiveUpdateUtc = snapshot.LastLiveUpdateUtc,
        LastClosedCandleUtc = snapshot.LastClosedCandleUtc,
        CurrentCandle = snapshot.CurrentCandle is null ? null : MapCandle(snapshot.CurrentCandle),
        LastClosedCandle = snapshot.LastClosedCandle is null ? null : MapCandle(snapshot.LastClosedCandle),
        Source = snapshot.Source
    };

    public static LiveCandleDto MapCandle(LiveCandleUpdate update) => new()
    {
        OpenTimeUtc = update.OpenTimeUtc,
        CloseTimeUtc = update.CloseTimeUtc,
        Open = update.Open,
        High = update.High,
        Low = update.Low,
        Close = update.Close,
        Volume = update.Volume,
        QuoteVolume = update.QuoteVolume,
        TradeCount = update.TradeCount,
        IsClosed = update.IsClosed
    };
}
