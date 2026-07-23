using MomoQuant.Application.LiveMarket;

namespace MomoQuant.Infrastructure.MarketData;

public sealed class BinanceLiveKlineStreamProvider : ILiveKlineStreamProvider
{
    private readonly BinanceLiveMarketDataProvider _provider;

    public BinanceLiveKlineStreamProvider(BinanceLiveMarketDataProvider provider)
    {
        _provider = provider;
        _provider.CandleReceived += update => CandleReceived?.Invoke(update);
        _provider.ConnectionStateChanged += reason => ConnectionStateChanged?.Invoke(reason);
    }

    public bool IsConnected => _provider.IsConnected;

    public int ReconnectAttempts => _provider.ReconnectAttempts;

    public event Action<LiveCandleUpdate>? CandleReceived;

    public event Action<string?>? ConnectionStateChanged;

    public Task ConnectAsync(IReadOnlyCollection<string> streamNames, CancellationToken cancellationToken = default) =>
        _provider.ConnectAsync(streamNames, cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        _provider.DisconnectAsync(cancellationToken);

    public Task SubscribeAsync(IEnumerable<string> streamNames, CancellationToken cancellationToken = default) =>
        _provider.SubscribeAsync(streamNames, cancellationToken);

    public Task UnsubscribeAsync(IEnumerable<string> streamNames, CancellationToken cancellationToken = default) =>
        _provider.UnsubscribeAsync(streamNames, cancellationToken);

    public IReadOnlyDictionary<string, LiveStreamDiagnostics> GetDiagnostics()
    {
        return _provider.GetDiagnostics().ToDictionary(
            pair => pair.Key,
            pair => new LiveStreamDiagnostics
            {
                MessagesReceived = pair.Value.MessagesReceived,
                MessagesParsed = pair.Value.MessagesParsed,
                ParseErrors = pair.Value.ParseErrors,
                LastRawMessageAtUtc = pair.Value.LastRawMessageAtUtc,
                LastParsedMessageAtUtc = pair.Value.LastParsedMessageAtUtc,
                LastSnapshotUpdateUtc = pair.Value.LastSnapshotUpdateUtc,
                LastClosedCandleUtc = pair.Value.LastClosedCandleUtc,
                LastError = pair.Value.LastError,
                Warning = pair.Value.Warning,
                SubscribedAtUtc = pair.Value.SubscribedAtUtc
            },
            StringComparer.OrdinalIgnoreCase);
    }

    public void MarkSnapshotUpdated(string streamName, DateTime atUtc, bool isClosed) =>
        _provider.MarkSnapshotUpdated(streamName, atUtc, isClosed);
}
