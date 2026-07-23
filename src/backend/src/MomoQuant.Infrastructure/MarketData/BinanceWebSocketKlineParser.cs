using System.Globalization;
using System.Text.Json;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.MarketData;

namespace MomoQuant.Infrastructure.MarketData;

public static class BinanceWebSocketKlineParser
{
    public static string BuildStreamName(string symbol, string timeframeApi) =>
        $"{symbol.Trim().ToLowerInvariant()}@kline_{timeframeApi.Trim().ToLowerInvariant()}";

    public static bool TryParseKlineMessage(string json, out LiveCandleUpdate? update)
    {
        update = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // Combined stream payload: { "stream": "...", "data": { "e": "kline", ... } }
        if (root.TryGetProperty("data", out var dataElement)
            && dataElement.ValueKind == JsonValueKind.Object)
        {
            root = dataElement;
        }

        // Ignore subscribe/unsubscribe acknowledgements: { "result": null, "id": 1 }
        if (root.TryGetProperty("result", out _) && root.TryGetProperty("id", out _))
        {
            return false;
        }

        if (!root.TryGetProperty("e", out var eventType) || eventType.GetString() != "kline")
        {
            return false;
        }

        if (!root.TryGetProperty("k", out var kline) || kline.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var symbol = ResolveSymbol(root, kline);
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        var interval = kline.TryGetProperty("i", out var intervalElement)
            ? intervalElement.GetString() ?? string.Empty
            : string.Empty;

        if (!TimeframeParser.TryParse(interval, out var timeframe))
        {
            return false;
        }

        var eventTimeUtc = root.TryGetProperty("E", out var eventTimeElement)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ReadInt64(eventTimeElement)).UtcDateTime
            : DateTime.UtcNow;

        update = new LiveCandleUpdate
        {
            ExchangeId = 0,
            SymbolId = 0,
            Symbol = symbol.ToUpperInvariant(),
            Timeframe = timeframe,
            OpenTimeUtc = DateTimeOffset.FromUnixTimeMilliseconds(ReadInt64(kline.GetProperty("t"))).UtcDateTime,
            CloseTimeUtc = DateTimeOffset.FromUnixTimeMilliseconds(ReadInt64(kline.GetProperty("T"))).UtcDateTime,
            Open = ParseDecimal(kline.GetProperty("o")),
            High = ParseDecimal(kline.GetProperty("h")),
            Low = ParseDecimal(kline.GetProperty("l")),
            Close = ParseDecimal(kline.GetProperty("c")),
            Volume = ParseDecimal(kline.GetProperty("v")),
            QuoteVolume = ParseDecimal(kline.GetProperty("q")),
            TradeCount = ReadInt32(kline.GetProperty("n")),
            IsClosed = kline.GetProperty("x").GetBoolean(),
            EventTimeUtc = eventTimeUtc,
            Source = "BinanceWebSocket"
        };

        return true;
    }

    private static string ResolveSymbol(JsonElement root, JsonElement kline)
    {
        if (root.TryGetProperty("s", out var symbolElement))
        {
            var symbol = symbolElement.GetString();
            if (!string.IsNullOrWhiteSpace(symbol))
            {
                return symbol;
            }
        }

        if (kline.TryGetProperty("s", out var kSymbol))
        {
            return kSymbol.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static long ReadInt64(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Number => element.GetInt64(),
            JsonValueKind.String => long.Parse(element.GetString()!, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("Expected numeric timestamp field.")
        };

    private static int ReadInt32(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Number => element.GetInt32(),
            JsonValueKind.String => int.Parse(element.GetString()!, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("Expected numeric trade count field.")
        };

    private static decimal ParseDecimal(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => decimal.Parse(element.GetString()!, CultureInfo.InvariantCulture),
            JsonValueKind.Number => element.GetDecimal(),
            _ => throw new InvalidOperationException("Binance kline numeric field must be a string or number.")
        };
}
