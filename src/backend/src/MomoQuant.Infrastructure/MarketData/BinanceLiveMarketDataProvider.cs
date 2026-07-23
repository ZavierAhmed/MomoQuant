using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.Options;

namespace MomoQuant.Infrastructure.MarketData;

public interface IBinanceLiveWebSocketClient
{
    bool IsConnected { get; }

    int ReconnectAttempts { get; }

    event Action<string>? MessageReceived;

    event Action<string?>? ConnectionStateChanged;

    Task ConnectAsync(IReadOnlyCollection<string> streamNames, CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Connects to Binance USD-M futures public combined streams.
/// Example: wss://fstream.binance.com/market/stream?streams=bnbusdt@kline_3m
/// </summary>
public sealed class BinanceLiveWebSocketClient : IBinanceLiveWebSocketClient, IAsyncDisposable
{
    private readonly MarketDataSettings _settings;
    private readonly ILogger<BinanceLiveWebSocketClient> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private string? _activeEndpoint;
    private int _disposed;

    public BinanceLiveWebSocketClient(IOptions<MarketDataSettings> settings, ILogger<BinanceLiveWebSocketClient> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public int ReconnectAttempts { get; private set; }

    public event Action<string>? MessageReceived;

    public event Action<string?>? ConnectionStateChanged;

    public async Task ConnectAsync(IReadOnlyCollection<string> streamNames, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        var streams = streamNames
            .Where(stream => !string.IsNullOrWhiteSpace(stream))
            .Select(stream => stream.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(stream => stream, StringComparer.Ordinal)
            .ToList();

        if (streams.Count == 0)
        {
            await DisconnectAsync(cancellationToken);
            return;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            var endpoint = BuildEndpoint(streams);
            if (IsConnected && string.Equals(_activeEndpoint, endpoint, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await DisconnectInternalAsync(cancellationToken);

            _socket = new ClientWebSocket();
            var uri = new Uri(endpoint);
            await _socket.ConnectAsync(uri, cancellationToken);

            _activeEndpoint = endpoint;
            ReconnectAttempts = 0;
            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), CancellationToken.None);

            foreach (var stream in streams)
            {
                _logger.LogInformation("Subscribing to Binance public stream {StreamName}", stream);
            }

            _logger.LogInformation("Binance live WebSocket connected to {Endpoint}", SanitizeEndpoint(endpoint));
            ConnectionStateChanged?.Invoke(null);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            return;
        }

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            await DisconnectInternalAsync(cancellationToken);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public void IncrementReconnectAttempts() => ReconnectAttempts++;

    private async Task DisconnectInternalAsync(CancellationToken cancellationToken)
    {
        if (_receiveCts is not null)
        {
            await _receiveCts.CancelAsync();
            _receiveCts.Dispose();
            _receiveCts = null;
        }

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask;
            }
            catch (OperationCanceledException)
            {
            }

            _receiveTask = null;
        }

        if (_socket is not null)
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error while closing Binance live WebSocket.");
                }
            }

            _socket.Dispose();
            _socket = null;
        }

        _activeEndpoint = null;
        ConnectionStateChanged?.Invoke("Disconnected");
    }

    private string BuildEndpoint(IReadOnlyList<string> streams)
    {
        if (streams.Count == 1)
        {
            // Raw single-stream: wss://fstream.binance.com/market/ws/bnbusdt@kline_3m
            return $"{_settings.Binance.ResolveRawBaseUrl()}/{streams[0]}";
        }

        // Combined multi-stream: wss://fstream.binance.com/market/stream?streams=bnbusdt@kline_3m/btcusdt@kline_3m
        var joined = string.Join('/', streams);
        return $"{_settings.Binance.ResolveCombinedBaseUrl()}?streams={joined}";
    }

    private static string SanitizeEndpoint(string endpoint)
    {
        // Endpoint contains only public stream names — no secrets.
        return endpoint;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[32 * 1024];
        var builder = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested
                   && _socket is not null
                   && _socket.State == WebSocketState.Open)
            {
                builder.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        ConnectionStateChanged?.Invoke("Closed by server");
                        return;
                    }

                    builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                var message = builder.ToString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    MessageReceived?.Invoke(message);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Binance live WebSocket receive loop failed.");
            ConnectionStateChanged?.Invoke(ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _connectionLock.WaitAsync(CancellationToken.None);
        try
        {
            await DisconnectInternalAsync(CancellationToken.None);
        }
        finally
        {
            _connectionLock.Release();
            _connectionLock.Dispose();
        }
    }
}

public sealed class StreamDiagnosticsCounters
{
    public long MessagesReceived;
    public long MessagesParsed;
    public long ParseErrors;
    public DateTime? LastRawMessageAtUtc;
    public DateTime? LastParsedMessageAtUtc;
    public DateTime? LastSnapshotUpdateUtc;
    public DateTime? LastClosedCandleUtc;
    public string? LastError;
    public string? Warning;
    public DateTime? SubscribedAtUtc;
}

public sealed class BinanceLiveMarketDataProvider
{
    private readonly IBinanceLiveWebSocketClient _client;
    private readonly MarketDataSettings _settings;
    private readonly ILogger<BinanceLiveMarketDataProvider> _logger;
    private readonly ConcurrentDictionary<string, StreamDiagnosticsCounters> _diagnostics = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _activeStreams = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private CancellationTokenSource? _noMessageWatchCts;

    public BinanceLiveMarketDataProvider(
        IBinanceLiveWebSocketClient client,
        IOptions<MarketDataSettings> settings,
        ILogger<BinanceLiveMarketDataProvider> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
        _client.MessageReceived += HandleMessage;
        _client.ConnectionStateChanged += reason => ConnectionStateChanged?.Invoke(reason);
    }

    public bool IsConnected => _client.IsConnected;

    public int ReconnectAttempts => _client.ReconnectAttempts;

    public event Action<LiveCandleUpdate>? CandleReceived;

    public event Action<string?>? ConnectionStateChanged;

    public event Action<string, StreamDiagnosticsCounters>? DiagnosticsUpdated;

    public IReadOnlyDictionary<string, StreamDiagnosticsCounters> GetDiagnostics() =>
        _diagnostics.ToDictionary(pair => pair.Key, pair => CloneCounters(pair.Value), StringComparer.OrdinalIgnoreCase);

    public async Task ConnectAsync(IReadOnlyCollection<string> streamNames, CancellationToken cancellationToken)
    {
        var streams = streamNames
            .Where(stream => !string.IsNullOrWhiteSpace(stream))
            .Select(stream => stream.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _activeStreams.Clear();
        foreach (var stream in streams)
        {
            _activeStreams[stream] = 0;
            var counters = _diagnostics.GetOrAdd(stream, _ => new StreamDiagnosticsCounters());
            counters.SubscribedAtUtc ??= DateTime.UtcNow;
            counters.Warning = null;
            counters.LastError = null;
        }

        await _client.ConnectAsync(streams, cancellationToken);
        StartNoMessageWatch(streams);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        StopNoMessageWatch();
        return _client.DisconnectAsync(cancellationToken);
    }

    public async Task SubscribeAsync(IEnumerable<string> streamNames, CancellationToken cancellationToken)
    {
        foreach (var stream in streamNames.Where(stream => !string.IsNullOrWhiteSpace(stream)))
        {
            var normalized = stream.Trim().ToLowerInvariant();
            _activeStreams[normalized] = 0;
            var counters = _diagnostics.GetOrAdd(normalized, _ => new StreamDiagnosticsCounters());
            counters.SubscribedAtUtc ??= DateTime.UtcNow;
            counters.Warning = null;
            _logger.LogInformation("Subscribing to Binance public stream {StreamName}", normalized);
        }

        await _client.ConnectAsync(_activeStreams.Keys.ToList(), cancellationToken);
        StartNoMessageWatch(_activeStreams.Keys.ToList());
    }

    public async Task UnsubscribeAsync(IEnumerable<string> streamNames, CancellationToken cancellationToken)
    {
        foreach (var stream in streamNames.Where(stream => !string.IsNullOrWhiteSpace(stream)))
        {
            _activeStreams.TryRemove(stream.Trim().ToLowerInvariant(), out _);
        }

        await _client.ConnectAsync(_activeStreams.Keys.ToList(), cancellationToken);
        if (_activeStreams.IsEmpty)
        {
            StopNoMessageWatch();
        }
        else
        {
            StartNoMessageWatch(_activeStreams.Keys.ToList());
        }
    }

    private void HandleMessage(string message)
    {
        var now = DateTime.UtcNow;
        string? streamHint = null;

        try
        {
            // Best-effort stream attribution from combined payload.
            if (message.Contains("\"stream\"", StringComparison.Ordinal))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(message);
                if (doc.RootElement.TryGetProperty("stream", out var streamElement))
                {
                    streamHint = streamElement.GetString()?.ToLowerInvariant();
                }
            }
        }
        catch
        {
            // Ignore attribution failures; counters still update below.
        }

        var targets = streamHint is not null
            ? new[] { streamHint }
            : _activeStreams.Keys.ToArray();

        foreach (var stream in targets)
        {
            var counters = _diagnostics.GetOrAdd(stream, _ => new StreamDiagnosticsCounters());
            Interlocked.Increment(ref counters.MessagesReceived);
            counters.LastRawMessageAtUtc = now;
            counters.Warning = null;
            DiagnosticsUpdated?.Invoke(stream, CloneCounters(counters));
        }

        try
        {
            if (!BinanceWebSocketKlineParser.TryParseKlineMessage(message, out var update) || update is null)
            {
                return;
            }

            var streamName = BinanceWebSocketKlineParser.BuildStreamName(
                update.Symbol,
                TimeframeParserToApi(update.Timeframe));

            var counters = _diagnostics.GetOrAdd(streamName, _ => new StreamDiagnosticsCounters());
            Interlocked.Increment(ref counters.MessagesParsed);
            counters.LastParsedMessageAtUtc = now;
            counters.LastError = null;
            counters.Warning = null;
            DiagnosticsUpdated?.Invoke(streamName, CloneCounters(counters));

            CandleReceived?.Invoke(update);
        }
        catch (Exception ex)
        {
            foreach (var stream in targets)
            {
                var counters = _diagnostics.GetOrAdd(stream, _ => new StreamDiagnosticsCounters());
                Interlocked.Increment(ref counters.ParseErrors);
                counters.LastError = ex.Message;
                DiagnosticsUpdated?.Invoke(stream, CloneCounters(counters));
            }

            _logger.LogWarning(ex, "Failed to parse Binance live kline message.");
        }
    }

    public void MarkSnapshotUpdated(string streamName, DateTime atUtc, bool isClosed)
    {
        var counters = _diagnostics.GetOrAdd(streamName, _ => new StreamDiagnosticsCounters());
        counters.LastSnapshotUpdateUtc = atUtc;
        if (isClosed)
        {
            counters.LastClosedCandleUtc = atUtc;
        }

        DiagnosticsUpdated?.Invoke(streamName, CloneCounters(counters));
    }

    public void MarkParseError(string streamName, string error)
    {
        var counters = _diagnostics.GetOrAdd(streamName, _ => new StreamDiagnosticsCounters());
        Interlocked.Increment(ref counters.ParseErrors);
        counters.LastError = error;
        DiagnosticsUpdated?.Invoke(streamName, CloneCounters(counters));
    }

    private void StartNoMessageWatch(IReadOnlyCollection<string> streams)
    {
        StopNoMessageWatch();
        var delaySeconds = Math.Max(_settings.Binance.NoMessageWarningSeconds, 1);
        _noMessageWatchCts = new CancellationTokenSource();
        var token = _noMessageWatchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                foreach (var stream in streams)
                {
                    if (!_diagnostics.TryGetValue(stream, out var counters))
                    {
                        continue;
                    }

                    if (counters.MessagesReceived == 0 && _client.IsConnected)
                    {
                        counters.Warning = "Connected but no kline messages received yet.";
                        DiagnosticsUpdated?.Invoke(stream, CloneCounters(counters));
                        _logger.LogWarning(
                            "Connected to Binance public stream {StreamName} but no kline messages received within {Seconds}s.",
                            stream,
                            delaySeconds);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    private void StopNoMessageWatch()
    {
        if (_noMessageWatchCts is null)
        {
            return;
        }

        _noMessageWatchCts.Cancel();
        _noMessageWatchCts.Dispose();
        _noMessageWatchCts = null;
    }

    private static StreamDiagnosticsCounters CloneCounters(StreamDiagnosticsCounters source) => new()
    {
        MessagesReceived = Interlocked.Read(ref source.MessagesReceived),
        MessagesParsed = Interlocked.Read(ref source.MessagesParsed),
        ParseErrors = Interlocked.Read(ref source.ParseErrors),
        LastRawMessageAtUtc = source.LastRawMessageAtUtc,
        LastParsedMessageAtUtc = source.LastParsedMessageAtUtc,
        LastSnapshotUpdateUtc = source.LastSnapshotUpdateUtc,
        LastClosedCandleUtc = source.LastClosedCandleUtc,
        LastError = source.LastError,
        Warning = source.Warning,
        SubscribedAtUtc = source.SubscribedAtUtc
    };

    private static string TimeframeParserToApi(Domain.Enums.Timeframe timeframe) =>
        Application.MarketData.TimeframeParser.ToApiString(timeframe);
}
