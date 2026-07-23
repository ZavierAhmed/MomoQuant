using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.UnitTests.TradingSystems;

internal static class SkTestData
{
    private static readonly DateTime Base = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Builds closed candles from a price series (High=Low=Close=Open=price).</summary>
    public static List<Candle> FromPrices(
        IReadOnlyList<decimal> prices,
        Timeframe timeframe = Timeframe.M15,
        long symbolId = 1,
        long exchangeId = 1)
    {
        var minutes = (int)timeframe;
        var candles = new List<Candle>(prices.Count);
        for (var i = 0; i < prices.Count; i++)
        {
            var price = prices[i];
            candles.Add(new Candle
            {
                Id = i + 1,
                ExchangeId = exchangeId,
                SymbolId = symbolId,
                Timeframe = timeframe,
                OpenTimeUtc = Base.AddMinutes(i * minutes),
                CloseTimeUtc = Base.AddMinutes((i + 1) * minutes),
                Open = price,
                High = price,
                Low = price,
                Close = price,
                Volume = 10m,
                QuoteVolume = 10m * price,
                TradeCount = 5,
                IsClosed = true,
                CreatedAtUtc = Base
            });
        }

        return candles;
    }

    /// <summary>A zigzag price series with clear alternating swing highs and lows.</summary>
    public static List<decimal> ZigZagPrices()
    {
        var prices = new List<decimal> { 100m };

        void Ramp(decimal from, decimal to, int steps)
        {
            for (var i = 1; i <= steps; i++)
            {
                prices.Add(from + ((to - from) * i / steps));
            }
        }

        Ramp(100m, 120m, 5);
        Ramp(120m, 100m, 5);
        Ramp(100m, 130m, 6);
        Ramp(130m, 105m, 6);
        Ramp(105m, 125m, 5);
        Ramp(125m, 100m, 5);
        Ramp(100m, 118m, 5);
        Ramp(118m, 98m, 5);
        Ramp(98m, 128m, 6);
        Ramp(128m, 104m, 6);
        Ramp(104m, 122m, 5);
        Ramp(122m, 100m, 5);
        Ramp(100m, 116m, 5);

        return prices;
    }
}
