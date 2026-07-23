using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Exchanges.Dtos;

namespace MomoQuant.Infrastructure.Exchanges;

/// <summary>
/// Discovers Binance USD-M Futures symbols using public market-data endpoints only:
///   GET /fapi/v1/exchangeInfo
///   GET /fapi/v1/ticker/24hr
/// No API keys, no private/account/order endpoints.
/// </summary>
public sealed class BinanceFuturesSymbolDiscoveryService : IBinanceFuturesSymbolDiscoveryService
{
    private const string ExchangeInfoPath = "fapi/v1/exchangeInfo";
    private const string Ticker24hPath = "fapi/v1/ticker/24hr";

    private readonly HttpClient _httpClient;
    private readonly ILogger<BinanceFuturesSymbolDiscoveryService> _logger;

    public BinanceFuturesSymbolDiscoveryService(
        HttpClient httpClient,
        ILogger<BinanceFuturesSymbolDiscoveryService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BinanceFuturesDiscoveredSymbolDto>> DiscoverTopSymbolsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 500);

        var definitions = await LoadExchangeInfoAsync(cancellationToken);
        var tickers = await LoadTickersAsync(cancellationToken);

        var merged = definitions
            .Select(definition =>
            {
                var ticker = tickers.GetValueOrDefault(definition.Symbol);
                return definition with
                {
                    LastPrice = ticker?.LastPrice ?? 0m,
                    PriceChangePercent24h = ticker?.PriceChangePercent ?? 0m,
                    QuoteVolume24h = ticker?.QuoteVolume ?? 0m,
                    Trades24h = ticker?.Trades ?? 0
                };
            })
            .OrderByDescending(symbol => symbol.QuoteVolume24h)
            .Take(safeLimit)
            .ToList();

        var rank = 1;
        return merged
            .Select(symbol => ToDto(symbol, rank++))
            .ToList();
    }

    public async Task<IReadOnlyList<BinanceFuturesDiscoveredSymbolDto>> GetSymbolMetadataAsync(
        IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken = default)
    {
        if (symbols.Count == 0)
        {
            return [];
        }

        var requested = symbols
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Where(symbol => symbol.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var definitions = await LoadExchangeInfoAsync(cancellationToken);

        return definitions
            .Where(definition => requested.Contains(definition.Symbol))
            .Select(definition => ToDto(definition, 0))
            .ToList();
    }

    private async Task<IReadOnlyList<DiscoveredSymbol>> LoadExchangeInfoAsync(CancellationToken cancellationToken)
    {
        var json = await GetStringAsync(ExchangeInfoPath, cancellationToken);
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("symbols", out var symbolsElement)
            || symbolsElement.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("Binance exchangeInfo response did not contain a symbols array.");
            return [];
        }

        var results = new List<DiscoveredSymbol>();

        foreach (var element in symbolsElement.EnumerateArray())
        {
            var contractType = GetString(element, "contractType");
            var status = GetString(element, "status");
            var quoteAsset = GetString(element, "quoteAsset");

            var isTradablePerpetualUsdt =
                string.Equals(contractType, "PERPETUAL", StringComparison.OrdinalIgnoreCase)
                && string.Equals(status, "TRADING", StringComparison.OrdinalIgnoreCase)
                && string.Equals(quoteAsset, "USDT", StringComparison.OrdinalIgnoreCase);

            if (!isTradablePerpetualUsdt)
            {
                continue;
            }

            var (tickSize, stepSize, minQty, minNotional) = ParseFilters(element);

            results.Add(new DiscoveredSymbol
            {
                Symbol = GetString(element, "symbol"),
                BaseAsset = GetString(element, "baseAsset"),
                QuoteAsset = quoteAsset,
                ContractType = contractType,
                Status = status,
                MarginAsset = GetString(element, "marginAsset", "USDT"),
                PricePrecision = GetInt(element, "pricePrecision"),
                QuantityPrecision = GetInt(element, "quantityPrecision"),
                TickSize = tickSize,
                StepSize = stepSize,
                MinQty = minQty,
                MinNotional = minNotional
            });
        }

        return results;
    }

    private async Task<Dictionary<string, TickerStats>> LoadTickersAsync(CancellationToken cancellationToken)
    {
        var tickers = new Dictionary<string, TickerStats>(StringComparer.Ordinal);

        try
        {
            var json = await GetStringAsync(Ticker24hPath, cancellationToken);
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return tickers;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                var symbol = GetString(element, "symbol");
                if (symbol.Length == 0)
                {
                    continue;
                }

                tickers[symbol] = new TickerStats
                {
                    LastPrice = GetDecimal(element, "lastPrice"),
                    PriceChangePercent = GetDecimal(element, "priceChangePercent"),
                    QuoteVolume = GetDecimal(element, "quoteVolume"),
                    Trades = GetLong(element, "count")
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Binance 24hr ticker stats. Continuing with metadata only.");
        }

        return tickers;
    }

    private async Task<string> GetStringAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var summary = body.Length <= 200 ? body : body[..200];
            throw new InvalidOperationException($"Binance request '{path}' failed: HTTP {(int)response.StatusCode} {summary}");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static (decimal TickSize, decimal StepSize, decimal MinQty, decimal MinNotional) ParseFilters(JsonElement element)
    {
        decimal tickSize = 0m;
        decimal stepSize = 0m;
        decimal minQty = 0m;
        decimal minNotional = 0m;

        if (!element.TryGetProperty("filters", out var filters) || filters.ValueKind != JsonValueKind.Array)
        {
            return (tickSize, stepSize, minQty, minNotional);
        }

        foreach (var filter in filters.EnumerateArray())
        {
            var filterType = GetString(filter, "filterType");
            switch (filterType)
            {
                case "PRICE_FILTER":
                    tickSize = GetDecimal(filter, "tickSize");
                    break;
                case "LOT_SIZE":
                    stepSize = GetDecimal(filter, "stepSize");
                    minQty = GetDecimal(filter, "minQty");
                    break;
                case "MIN_NOTIONAL":
                    minNotional = GetDecimal(filter, "notional");
                    break;
            }
        }

        return (tickSize, stepSize, minQty, minNotional);
    }

    private static BinanceFuturesDiscoveredSymbolDto ToDto(DiscoveredSymbol symbol, int rank) => new()
    {
        Rank = rank,
        Symbol = symbol.Symbol,
        BaseAsset = symbol.BaseAsset,
        QuoteAsset = symbol.QuoteAsset,
        ContractType = symbol.ContractType,
        Status = symbol.Status,
        MarginAsset = symbol.MarginAsset,
        PricePrecision = symbol.PricePrecision,
        QuantityPrecision = symbol.QuantityPrecision,
        TickSize = symbol.TickSize,
        StepSize = symbol.StepSize,
        MinQty = symbol.MinQty,
        MinNotional = symbol.MinNotional,
        LastPrice = symbol.LastPrice,
        PriceChangePercent24h = symbol.PriceChangePercent24h,
        QuoteVolume24h = symbol.QuoteVolume24h,
        Trades24h = symbol.Trades24h,
        AlreadyAdded = false
    };

    private static string GetString(JsonElement element, string property, string fallback = "")
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? fallback;
        }

        return fallback;
    }

    private static int GetInt(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return 0;
    }

    private static long GetLong(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String
                && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return 0;
    }

    private static decimal GetDecimal(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String
                && decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return 0m;
    }

    private sealed record DiscoveredSymbol
    {
        public required string Symbol { get; init; }
        public required string BaseAsset { get; init; }
        public required string QuoteAsset { get; init; }
        public string ContractType { get; init; } = "PERPETUAL";
        public string Status { get; init; } = "TRADING";
        public string MarginAsset { get; init; } = "USDT";
        public int PricePrecision { get; init; }
        public int QuantityPrecision { get; init; }
        public decimal TickSize { get; init; }
        public decimal StepSize { get; init; }
        public decimal MinQty { get; init; }
        public decimal MinNotional { get; init; }
        public decimal LastPrice { get; init; }
        public decimal PriceChangePercent24h { get; init; }
        public decimal QuoteVolume24h { get; init; }
        public long Trades24h { get; init; }
    }

    private sealed record TickerStats
    {
        public decimal LastPrice { get; init; }
        public decimal PriceChangePercent { get; init; }
        public decimal QuoteVolume { get; init; }
        public long Trades { get; init; }
    }
}
