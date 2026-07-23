using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.UnitTests.ValidationLab;

public sealed class ValidationCandleFingerprintTests
{
    [Fact]
    public void Fingerprint_is_deterministic()
    {
        var candles = new List<Candle>
        {
            new()
            {
                OpenTimeUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Open = 100m, High = 101m, Low = 99m, Close = 100.5m, Volume = 10m,
                Timeframe = Timeframe.M15
            },
            new()
            {
                OpenTimeUtc = new DateTime(2024, 1, 1, 0, 15, 0, DateTimeKind.Utc),
                Open = 100.5m, High = 102m, Low = 100m, Close = 101m, Volume = 12m,
                Timeframe = Timeframe.M15
            }
        };

        var a = ValidationCandleFingerprint.Build(candles);
        var b = ValidationCandleFingerprint.Build(candles.AsEnumerable().Reverse().ToList());
        Assert.Equal(a, b);
        Assert.Equal(16, a.Length);
    }
}
