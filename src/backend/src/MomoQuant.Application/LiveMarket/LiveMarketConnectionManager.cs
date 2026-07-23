using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.LiveMarket.Dtos;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.TradingSystems;
using MomoQuant.Application.PaperTrading;
using MomoQuant.Application.Options;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.LiveMarket;

public sealed class LiveMarketConnectionManager : BackgroundService, ILiveMarketConnectionManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MarketDataSettings _settings;
    private readonly ILiveMarketSnapshotStore _snapshotStore;
    private readonly ILiveMarketEventPublisher _eventPublisher;
    private readonly ILiveKlineStreamProvider _provider;
    private readonly ILogger<LiveMarketConnectionManager> _logger;
    private readonly object _sync = new();
    private readonly Dictionary<string, LiveSubscriptionState> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LiveMarketSubscriptionKey> _keys = new(StringComparer.OrdinalIgnoreCase);

    private string? _lastError;

    public LiveMarketConnectionManager(
        IServiceScopeFactory scopeFactory,
        IOptions<MarketDataSettings> settings,
        ILiveMarketSnapshotStore snapshotStore,
        ILiveMarketEventPublisher eventPublisher,
        ILiveKlineStreamProvider provider,
        ILogger<LiveMarketConnectionManager> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _snapshotStore = snapshotStore;
        _eventPublisher = eventPublisher;
        _provider = provider;
        _logger = logger;
        _provider.CandleReceived += OnCandleReceived;
        _provider.ConnectionStateChanged += OnConnectionStateChanged;
    }

    public bool IsAvailable =>
        string.Equals(_settings.LiveProvider, "Binance", StringComparison.OrdinalIgnoreCase);

    public bool IsConnected => _provider.IsConnected;

    public event Action<LiveCandleUpdate>? CandleUpdated;

    public event Action<LiveCandleUpdate>? CandleClosed;

    public event Action<LiveMarketConnectionStatus>? ConnectionStatusChanged;

    public LiveMarketConnectionStatus GetStatus()
    {
        lock (_sync)
        {
            return BuildStatus();
        }
    }

    public LiveMarketDiagnosticsDto GetDiagnostics()
    {
        var providerDiagnostics = _provider.GetDiagnostics();
        List<LiveMarketSubscriptionDiagnosticsDto> items;

        lock (_sync)
        {
            items = _subscriptions.Values.Select(state =>
            {
                providerDiagnostics.TryGetValue(state.Key.StreamName, out var counters);
                return new LiveMarketSubscriptionDiagnosticsDto
                {
                    ExchangeId = state.Key.ExchangeId,
                    SymbolId = state.Key.SymbolId,
                    Symbol = state.Key.Symbol,
                    Timeframe = TimeframeParser.ToApiString(state.Key.Timeframe),
                    StreamName = state.Key.StreamName,
                    ConnectionState = ResolveConnectionState(state, counters),
                    SubscribedAtUtc = state.SubscribedAtUtc ?? counters?.SubscribedAtUtc,
                    LastRawMessageAtUtc = counters?.LastRawMessageAtUtc,
                    LastParsedMessageAtUtc = counters?.LastParsedMessageAtUtc,
                    LastSnapshotUpdateUtc = counters?.LastSnapshotUpdateUtc,
                    LastClosedCandleUtc = counters?.LastClosedCandleUtc ?? state.LastClosedCandleUtc,
                    MessagesReceived = counters?.MessagesReceived ?? 0,
                    MessagesParsed = counters?.MessagesParsed ?? 0,
                    ParseErrors = counters?.ParseErrors ?? 0,
                    LastError = counters?.LastError ?? state.LastError,
                    Warning = ResolveWarning(state, counters),
                    LinkedSessionIds = state.LinkedSessionIds.ToList()
                };
            }).ToList();
        }

        return new LiveMarketDiagnosticsDto
        {
            Provider = _settings.LiveProvider,
            Connected = _provider.IsConnected,
            ReconnectAttempts = _provider.ReconnectAttempts,
            LastError = _lastError,
            Subscriptions = items
        };
    }

    public bool IsSubscribed(long symbolId, Timeframe timeframe)
    {
        lock (_sync)
        {
            return _subscriptions.Values.Any(state =>
                state.Key.SymbolId == symbolId && state.Key.Timeframe == timeframe);
        }
    }

    public void LinkSession(long sessionId, long symbolId, Timeframe timeframe)
    {
        lock (_sync)
        {
            foreach (var state in _subscriptions.Values)
            {
                if (state.Key.SymbolId == symbolId && state.Key.Timeframe == timeframe)
                {
                    state.LinkedSessionIds.Add(sessionId);
                }
            }
        }
    }

    public void UnlinkSession(long sessionId)
    {
        lock (_sync)
        {
            foreach (var state in _subscriptions.Values)
            {
                state.LinkedSessionIds.Remove(sessionId);
            }
        }
    }

    public async Task<ServiceResult<LiveMarketStatusDto>> SubscribeAsync(
        LiveMarketSubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return ServiceResult<LiveMarketStatusDto>.Fail(
                "Live market provider is unavailable. LivePaper cannot start.",
                "provider");
        }

        if (!TimeframeParser.TryParse(request.Timeframe, out var timeframe))
        {
            return ServiceResult<LiveMarketStatusDto>.Fail("Timeframe is invalid.", "timeframe");
        }

        using var scope = _scopeFactory.CreateScope();
        var symbolRepository = scope.ServiceProvider.GetRequiredService<ISymbolRepository>();
        var exchangeRepository = scope.ServiceProvider.GetRequiredService<IExchangeRepository>();

        var symbol = await symbolRepository.GetByIdAsync(request.SymbolId, cancellationToken);
        if (symbol is null || symbol.ExchangeId != request.ExchangeId)
        {
            return ServiceResult<LiveMarketStatusDto>.Fail("Symbol was not found.", "symbolId");
        }

        var exchange = await exchangeRepository.GetByIdAsync(request.ExchangeId, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<LiveMarketStatusDto>.Fail("Exchange was not found.", "exchangeId");
        }

        if (!IsSymbolAllowed(symbol.SymbolName))
        {
            return ServiceResult<LiveMarketStatusDto>.Fail(
                $"Symbol '{symbol.SymbolName}' is not supported by the live market provider.",
                "symbolId");
        }

        if (!IsIntervalAllowed(request.Timeframe))
        {
            return ServiceResult<LiveMarketStatusDto>.Fail(
                $"Timeframe '{request.Timeframe}' is not supported by the live market provider.",
                "timeframe");
        }

        var key = new LiveMarketSubscriptionKey
        {
            ExchangeId = request.ExchangeId,
            SymbolId = symbol.Id,
            Symbol = symbol.SymbolName.ToUpperInvariant(),
            Timeframe = timeframe
        };

        var subscriptionKey = key.StreamName;
        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(subscriptionKey, out var state))
            {
                state = new LiveSubscriptionState
                {
                    Key = key,
                    Status = LiveSubscriptionStatus.Connecting,
                    SubscribedAtUtc = DateTime.UtcNow
                };
                _subscriptions[subscriptionKey] = state;
                _keys[subscriptionKey] = key;
            }

            if (request.PaperSessionId is long sessionId)
            {
                state.LinkedSessionIds.Add(sessionId);
            }
        }

        _logger.LogInformation("Subscribing to Binance public stream {StreamName}", subscriptionKey);

        try
        {
            await _provider.SubscribeAsync([subscriptionKey], cancellationToken);

            lock (_sync)
            {
                if (_subscriptions.TryGetValue(subscriptionKey, out var state))
                {
                    state.Status = _provider.IsConnected
                        ? LiveSubscriptionStatus.Connected
                        : LiveSubscriptionStatus.Connecting;
                    state.LastError = null;
                }
            }

            _lastError = null;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            lock (_sync)
            {
                if (_subscriptions.TryGetValue(subscriptionKey, out var state))
                {
                    state.Status = LiveSubscriptionStatus.Failed;
                    state.LastError = ex.Message;
                }
            }

            return ServiceResult<LiveMarketStatusDto>.Fail(
                $"Failed to subscribe to live stream {subscriptionKey}: {ex.Message}",
                "provider");
        }

        await PublishStatusAsync(cancellationToken);
        return ServiceResult<LiveMarketStatusDto>.Ok(LiveMarketMapper.MapStatus(GetStatus()));
    }

    public async Task<ServiceResult<LiveMarketStatusDto>> UnsubscribeAsync(
        LiveMarketSubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TimeframeParser.TryParse(request.Timeframe, out var timeframe))
        {
            return ServiceResult<LiveMarketStatusDto>.Fail("Timeframe is invalid.", "timeframe");
        }

        using var scope = _scopeFactory.CreateScope();
        var symbolRepository = scope.ServiceProvider.GetRequiredService<ISymbolRepository>();
        var symbol = await symbolRepository.GetByIdAsync(request.SymbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<LiveMarketStatusDto>.Fail("Symbol was not found.", "symbolId");
        }

        var streamName = $"{symbol.SymbolName.ToLowerInvariant()}@kline_{TimeframeParser.ToApiString(timeframe)}";
        await _provider.UnsubscribeAsync([streamName], cancellationToken);

        lock (_sync)
        {
            _subscriptions.Remove(streamName);
            _keys.Remove(streamName);
        }

        await PublishStatusAsync(cancellationToken);
        return ServiceResult<LiveMarketStatusDto>.Ok(LiveMarketMapper.MapStatus(GetStatus()));
    }

    public async Task<ServiceResult<LiveMarketStatusDto>> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return ServiceResult<LiveMarketStatusDto>.Fail("Live market data provider is unavailable.", "provider");
        }

        List<string> streams;
        lock (_sync)
        {
            streams = _subscriptions.Keys.ToList();
            foreach (var state in _subscriptions.Values)
            {
                state.Status = LiveSubscriptionStatus.Reconnecting;
            }
        }

        await _provider.DisconnectAsync(cancellationToken);

        if (streams.Count > 0)
        {
            await _provider.ConnectAsync(streams, cancellationToken);
            lock (_sync)
            {
                foreach (var state in _subscriptions.Values)
                {
                    state.Status = _provider.IsConnected
                        ? LiveSubscriptionStatus.Connected
                        : LiveSubscriptionStatus.Disconnected;
                }
            }
        }

        await PublishStatusAsync(cancellationToken);
        return ServiceResult<LiveMarketStatusDto>.Ok(LiveMarketMapper.MapStatus(GetStatus()));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Live market connection manager started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (IsAvailable)
                {
                    List<string> streams;
                    lock (_sync)
                    {
                        streams = _subscriptions.Keys.ToList();
                        RefreshWarningsFromDiagnostics();
                    }

                    if (streams.Count > 0 && !_provider.IsConnected)
                    {
                        await TryReconnectAsync(stoppingToken);
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Live market connection monitor failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(_settings.Binance.ReconnectDelaySeconds, 1)), stoppingToken);
        }
    }

    private async Task TryReconnectAsync(CancellationToken cancellationToken)
    {
        if (_provider.ReconnectAttempts >= Math.Max(_settings.Binance.MaxReconnectAttempts, 1))
        {
            _lastError = "Maximum reconnect attempts reached.";
            return;
        }

        lock (_sync)
        {
            foreach (var state in _subscriptions.Values)
            {
                state.Status = LiveSubscriptionStatus.Reconnecting;
            }
        }

        await PublishStatusAsync(cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(_settings.Binance.ReconnectDelaySeconds, 1)), cancellationToken);

        try
        {
            await ReconnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _logger.LogWarning(ex, "Live market reconnect failed.");
        }
    }

    private void OnConnectionStateChanged(string? reason)
    {
        if (!string.IsNullOrWhiteSpace(reason))
        {
            _lastError = reason;
        }

        lock (_sync)
        {
            foreach (var state in _subscriptions.Values)
            {
                state.Status = _provider.IsConnected
                    ? LiveSubscriptionStatus.Connected
                    : LiveSubscriptionStatus.Disconnected;
                if (!string.IsNullOrWhiteSpace(reason) && !_provider.IsConnected)
                {
                    state.LastError = reason;
                }
            }
        }

        _ = PublishStatusAsync(CancellationToken.None);
    }

    private void OnCandleReceived(LiveCandleUpdate update)
    {
        LiveMarketSubscriptionKey? key = null;
        lock (_sync)
        {
            var streamName = $"{update.Symbol.ToLowerInvariant()}@kline_{TimeframeParser.ToApiString(update.Timeframe)}";
            if (_keys.TryGetValue(streamName, out var mapped))
            {
                key = mapped;
            }
        }

        if (key is null)
        {
            _logger.LogDebug(
                "Received live kline for {Symbol} {Timeframe} but no active subscription was found.",
                update.Symbol,
                update.Timeframe);
            return;
        }

        var enriched = new LiveCandleUpdate
        {
            ExchangeId = key.ExchangeId,
            SymbolId = key.SymbolId,
            Symbol = key.Symbol,
            Timeframe = key.Timeframe,
            OpenTimeUtc = update.OpenTimeUtc,
            CloseTimeUtc = update.CloseTimeUtc,
            Open = update.Open,
            High = update.High,
            Low = update.Low,
            Close = update.Close,
            Volume = update.Volume,
            QuoteVolume = update.QuoteVolume,
            TradeCount = update.TradeCount,
            IsClosed = update.IsClosed,
            EventTimeUtc = update.EventTimeUtc,
            Source = update.Source
        };

        var snapshot = BuildSnapshot(key, enriched);
        _snapshotStore.Update(snapshot);
        _provider.MarkSnapshotUpdated(key.StreamName, enriched.EventTimeUtc, enriched.IsClosed);

        lock (_sync)
        {
            if (_subscriptions.TryGetValue(key.StreamName, out var state))
            {
                state.LastUpdateUtc = enriched.EventTimeUtc;
                state.Status = LiveSubscriptionStatus.Connected;
                state.Warning = null;
                state.LastError = null;
                if (enriched.IsClosed)
                {
                    state.LastClosedCandleUtc = enriched.CloseTimeUtc;
                }
            }
        }

        CandleUpdated?.Invoke(enriched);
        _ = _eventPublisher.PublishCandleUpdatedAsync(enriched, CancellationToken.None);
        _ = _eventPublisher.PublishSnapshotUpdatedAsync(snapshot, CancellationToken.None);

        if (enriched.IsClosed)
        {
            CandleClosed?.Invoke(enriched);
            _ = _eventPublisher.PublishCandleClosedAsync(enriched, CancellationToken.None);
            _ = HandleClosedCandleAsync(enriched);
        }
    }

    private async Task HandleClosedCandleAsync(LiveCandleUpdate update)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var persistence = scope.ServiceProvider.GetRequiredService<ILiveCandlePersistenceService>();
            var indicatorUpdate = scope.ServiceProvider.GetRequiredService<ILiveIndicatorUpdateService>();
            var livePaperHandler = scope.ServiceProvider.GetRequiredService<ILivePaperCandleHandler>();

            var candle = await persistence.PersistClosedCandleAsync(update, CancellationToken.None);
            if (candle is not null)
            {
                await indicatorUpdate.UpdateForClosedCandleAsync(
                    update.SymbolId,
                    update.Timeframe,
                    candle.Id,
                    CancellationToken.None);
            }

            await livePaperHandler.HandleClosedCandleAsync(update, candle, CancellationToken.None);

            var skLivePaperHandler = scope.ServiceProvider.GetService<ISkLivePaperCandleHandler>();
            if (skLivePaperHandler is not null)
            {
                await skLivePaperHandler.HandleClosedCandleAsync(update, candle, CancellationToken.None);
            }

            await _eventPublisher.PublishPaperSessionUpdatedAsync(update.SymbolId, update.Timeframe, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process closed live candle for {Symbol} {Timeframe}.", update.Symbol, update.Timeframe);
        }
    }

    private LiveMarketSnapshot BuildSnapshot(LiveMarketSubscriptionKey key, LiveCandleUpdate update)
    {
        var existing = _snapshotStore.Get(key.SymbolId, TimeframeParser.ToApiString(key.Timeframe));
        return new LiveMarketSnapshot
        {
            ExchangeId = key.ExchangeId,
            SymbolId = key.SymbolId,
            Symbol = key.Symbol,
            Timeframe = key.Timeframe,
            LatestPrice = update.Close,
            Open = update.Open,
            High = update.High,
            Low = update.Low,
            Close = update.Close,
            Volume = update.Volume,
            QuoteVolume = update.QuoteVolume,
            TradeCount = update.TradeCount,
            OpenTimeUtc = update.OpenTimeUtc,
            CloseTimeUtc = update.CloseTimeUtc,
            IsClosed = update.IsClosed,
            LastUpdateUtc = update.EventTimeUtc,
            LastLiveUpdateUtc = update.EventTimeUtc,
            LastClosedCandleUtc = update.IsClosed ? update.CloseTimeUtc : existing?.LastClosedCandleUtc,
            CurrentCandle = update,
            LastClosedCandle = update.IsClosed ? update : existing?.LastClosedCandle,
            Source = "BinanceWebSocket"
        };
    }

    private void RefreshWarningsFromDiagnostics()
    {
        var diagnostics = _provider.GetDiagnostics();
        foreach (var state in _subscriptions.Values)
        {
            if (!diagnostics.TryGetValue(state.Key.StreamName, out var counters))
            {
                continue;
            }

            state.Warning = ResolveWarning(state, counters);
            state.LastError = counters.LastError ?? state.LastError;
            if (counters.LastRawMessageAtUtc is not null)
            {
                state.LastUpdateUtc = counters.LastRawMessageAtUtc;
            }
        }
    }

    private string ResolveConnectionState(LiveSubscriptionState state, LiveStreamDiagnostics? counters)
    {
        if (!_provider.IsConnected)
        {
            return LiveSubscriptionStatus.Disconnected.ToString();
        }

        if (counters is { MessagesReceived: 0 } && state.Status == LiveSubscriptionStatus.Connected)
        {
            return LiveSubscriptionStatus.Connected.ToString();
        }

        return state.Status.ToString();
    }

    private string? ResolveWarning(LiveSubscriptionState state, LiveStreamDiagnostics? counters)
    {
        if (counters is null)
        {
            return state.Warning;
        }

        if (counters.ParseErrors > 0 && counters.MessagesReceived > 0 && counters.MessagesParsed == 0)
        {
            return "Live messages are arriving but parsing is failing.";
        }

        if (_provider.IsConnected && counters.MessagesReceived == 0)
        {
            return counters.Warning
                ?? "Connected but no kline messages received yet.";
        }

        if (counters.MessagesParsed > 0 && counters.LastClosedCandleUtc is null)
        {
            return $"Live price is updating. Waiting for the current {TimeframeParser.ToApiString(state.Key.Timeframe)} candle to close before strategy evaluation.";
        }

        return counters.Warning ?? state.Warning;
    }

    private LiveMarketConnectionStatus BuildStatus()
    {
        RefreshWarningsFromDiagnostics();
        return new LiveMarketConnectionStatus
        {
            Provider = _settings.LiveProvider,
            Connected = _provider.IsConnected,
            LastError = _lastError,
            ReconnectAttempts = _provider.ReconnectAttempts,
            Subscriptions = _subscriptions.Values.ToList()
        };
    }

    private async Task PublishStatusAsync(CancellationToken cancellationToken)
    {
        var status = GetStatus();
        ConnectionStatusChanged?.Invoke(status);
        await _eventPublisher.PublishConnectionStatusAsync(status, cancellationToken);
    }

    private bool IsSymbolAllowed(string symbolName) =>
        _settings.Binance.AllowedSymbols.Any(symbol =>
            string.Equals(symbol, symbolName, StringComparison.OrdinalIgnoreCase));

    private bool IsIntervalAllowed(string timeframe) =>
        _settings.Binance.AllowedIntervals.Any(interval =>
            string.Equals(interval, timeframe, StringComparison.OrdinalIgnoreCase));
}
