using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Infrastructure.MarketData;

public sealed class FakeHistoricalCandleProvider : IHistoricalCandleProvider
{
    public Task<IReadOnlyList<HistoricalCandleDefinition>> GetCandlesAsync(
        string exchangeCode,
        string symbolName,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        _ = exchangeCode;

        var candles = new List<HistoricalCandleDefinition>();
        var intervalMinutes = (int)timeframe;
        var cursor = AlignToTimeframe(fromUtc, intervalMinutes);
        var seed = HashCode.Combine(symbolName.ToUpperInvariant(), timeframe);

        while (cursor < toUtc)
        {
            var closeTime = cursor.AddMinutes(intervalMinutes);
            if (closeTime > toUtc)
            {
                break;
            }

            var basePrice = GetBasePrice(symbolName);
            var open = GeneratePrice(basePrice, seed, cursor, 0);
            var close = GeneratePrice(basePrice, seed, cursor, 1);
            var high = Math.Max(open, close) + GenerateWiggle(seed, cursor, 2);
            var low = Math.Min(open, close) - GenerateWiggle(seed, cursor, 3);
            var volume = 100m + (Math.Abs(GenerateInt(seed, cursor, 4)) % 500);

            candles.Add(new HistoricalCandleDefinition
            {
                OpenTimeUtc = cursor,
                CloseTimeUtc = closeTime,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume,
                QuoteVolume = volume * close,
                TradeCount = 50 + (Math.Abs(GenerateInt(seed, cursor, 5)) % 200),
                IsClosed = true
            });

            cursor = closeTime;
        }

        return Task.FromResult<IReadOnlyList<HistoricalCandleDefinition>>(candles);
    }

    private static DateTime AlignToTimeframe(DateTime value, int intervalMinutes)
    {
        var ticks = value.Ticks - (value.Ticks % TimeSpan.FromMinutes(intervalMinutes).Ticks);
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    private static decimal GetBasePrice(string symbolName) =>
        symbolName.ToUpperInvariant() switch
        {
            "BTCUSDT" => 42000m,
            "ETHUSDT" => 2200m,
            "SOLUSDT" => 95m,
            "BNBUSDT" => 310m,
            "XRPUSDT" => 0.55m,
            _ => 100m
        };

    private static decimal GeneratePrice(decimal basePrice, int seed, DateTime openTimeUtc, int salt)
    {
        var drift = (GenerateInt(seed, openTimeUtc, salt) % 200 - 100) / 10000m;
        return decimal.Round(basePrice * (1m + drift), 8);
    }

    private static decimal GenerateWiggle(int seed, DateTime openTimeUtc, int salt)
    {
        var value = GenerateInt(seed, openTimeUtc, salt) % 50;
        return value / 10000m;
    }

    private static int GenerateInt(int seed, DateTime openTimeUtc, int salt) =>
        HashCode.Combine(seed, openTimeUtc.Ticks, salt);
}
