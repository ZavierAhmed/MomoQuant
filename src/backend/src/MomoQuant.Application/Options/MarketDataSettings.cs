namespace MomoQuant.Application.Options;

public sealed class MarketDataSettings
{
    public const string SectionName = "MarketData";

    public string HistoricalProvider { get; set; } = "Fake";

    public string LiveProvider { get; set; } = "Binance";

    public BinanceMarketDataSettings Binance { get; set; } = new();

    public LiveBootstrapSettings LiveBootstrap { get; set; } = new();
}

public sealed class BinanceMarketDataSettings
{
    public string BaseUrl { get; set; } = "https://fapi.binance.com";

    /// <summary>Base host for futures public market streams, e.g. wss://fstream.binance.com</summary>
    public string WebSocketBaseUrl { get; set; } = "wss://fstream.binance.com";

    /// <summary>Optional override for raw single-stream endpoint, e.g. wss://fstream.binance.com/market/ws</summary>
    public string? WebSocketRawBaseUrl { get; set; }

    /// <summary>Optional override for combined multi-stream endpoint, e.g. wss://fstream.binance.com/market/stream</summary>
    public string? WebSocketCombinedBaseUrl { get; set; }

    /// <summary>Seconds to wait after connect before warning that no kline messages arrived.</summary>
    public int NoMessageWarningSeconds { get; set; } = 10;

    public int ReconnectDelaySeconds { get; set; } = 5;

    public int MaxReconnectAttempts { get; set; } = 20;

    public int RequestDelayMs { get; set; } = 250;

    public int Limit { get; set; } = 1500;

    public int MaxDaysPerImport { get; set; } = 30;

    public List<string> AllowedSymbols { get; set; } =
    [
        "BTCUSDT",
        "ETHUSDT",
        "SOLUSDT",
        "BNBUSDT",
        "XRPUSDT"
    ];

    public List<string> AllowedIntervals { get; set; } =
    [
        "1m",
        "3m",
        "5m",
        "15m",
        "30m",
        "1h",
        "4h",
        "1d",
        "1w"
    ];

    public string ResolveRawBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(WebSocketRawBaseUrl))
        {
            return WebSocketRawBaseUrl.TrimEnd('/');
        }

        return $"{WebSocketBaseUrl.TrimEnd('/')}/market/ws";
    }

    public string ResolveCombinedBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(WebSocketCombinedBaseUrl))
        {
            return WebSocketCombinedBaseUrl.TrimEnd('/');
        }

        return $"{WebSocketBaseUrl.TrimEnd('/')}/market/stream";
    }
}
