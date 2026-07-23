namespace MomoQuant.Application.LiveMarket;

public interface ILiveKlineStreamProvider
{
    bool IsConnected { get; }

    int ReconnectAttempts { get; }

    event Action<LiveCandleUpdate>? CandleReceived;

    event Action<string?>? ConnectionStateChanged;

    Task ConnectAsync(IReadOnlyCollection<string> streamNames, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task SubscribeAsync(IEnumerable<string> streamNames, CancellationToken cancellationToken = default);

    Task UnsubscribeAsync(IEnumerable<string> streamNames, CancellationToken cancellationToken = default);

    IReadOnlyDictionary<string, LiveStreamDiagnostics> GetDiagnostics();

    void MarkSnapshotUpdated(string streamName, DateTime atUtc, bool isClosed);
}

public sealed class LiveStreamDiagnostics
{
    public long MessagesReceived { get; init; }
    public long MessagesParsed { get; init; }
    public long ParseErrors { get; init; }
    public DateTime? LastRawMessageAtUtc { get; init; }
    public DateTime? LastParsedMessageAtUtc { get; init; }
    public DateTime? LastSnapshotUpdateUtc { get; init; }
    public DateTime? LastClosedCandleUtc { get; init; }
    public string? LastError { get; init; }
    public string? Warning { get; init; }
    public DateTime? SubscribedAtUtc { get; init; }
}
