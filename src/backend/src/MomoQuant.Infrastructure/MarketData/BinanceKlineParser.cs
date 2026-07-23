using System.Globalization;
using System.Net;
using System.Text.Json;
using MomoQuant.Application.Abstractions;

namespace MomoQuant.Infrastructure.MarketData;

public static class BinanceKlineParser
{
    public static IReadOnlyList<HistoricalCandleDefinition> ParseKlines(string json, DateTime? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Binance kline response must be a JSON array.");
        }

        var now = utcNow ?? DateTime.UtcNow;
        var candles = new List<HistoricalCandleDefinition>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            candles.Add(ParseKline(element, now));
        }

        return candles;
    }

    public static HistoricalCandleDefinition ParseKline(JsonElement element, DateTime? utcNow = null)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() < 11)
        {
            throw new InvalidOperationException("Binance kline entry must be an array with at least 11 values.");
        }

        var openTimeUtc = FromUnixMilliseconds(element[0]);
        var closeTimeUtc = FromUnixMilliseconds(element[6]);
        var now = utcNow ?? DateTime.UtcNow;

        return new HistoricalCandleDefinition
        {
            OpenTimeUtc = openTimeUtc,
            CloseTimeUtc = closeTimeUtc,
            Open = ParseDecimal(element[1]),
            High = ParseDecimal(element[2]),
            Low = ParseDecimal(element[3]),
            Close = ParseDecimal(element[4]),
            Volume = ParseDecimal(element[5]),
            QuoteVolume = ParseDecimal(element[7]),
            TradeCount = element[8].GetInt32(),
            IsClosed = closeTimeUtc < now
        };
    }

    public static DateTime FromUnixMilliseconds(JsonElement element)
    {
        var milliseconds = element.GetInt64();
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
    }

    public static decimal ParseDecimal(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => decimal.Parse(element.GetString()!, CultureInfo.InvariantCulture),
            JsonValueKind.Number => element.GetDecimal(),
            _ => throw new InvalidOperationException("Binance kline numeric field must be a string or number.")
        };
    }
}
