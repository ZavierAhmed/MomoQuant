using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.ValidationLab;

public static class ValidationCandleFingerprint
{
    public static string Build(IReadOnlyList<Candle> candles)
    {
        if (candles.Count == 0)
        {
            return "EMPTY";
        }

        var sb = new StringBuilder(candles.Count * 64);
        foreach (var c in candles.OrderBy(x => x.OpenTimeUtc))
        {
            sb.Append(c.OpenTimeUtc.ToString("O")).Append('|')
                .Append(c.Open.ToString("G29")).Append('|')
                .Append(c.High.ToString("G29")).Append('|')
                .Append(c.Low.ToString("G29")).Append('|')
                .Append(c.Close.ToString("G29")).Append('|')
                .Append(c.Volume.ToString("G29")).Append(';');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash)[..16];
    }

    public static string BuildSnapshotJson(
        string exchange,
        string symbol,
        string timeframe,
        DateTime requestedStartUtc,
        DateTime requestedEndUtc,
        IReadOnlyList<Candle> candles,
        string dataSource,
        int missingCandleCount,
        int duplicateCandleCount,
        string? gapDiagnosticsJson,
        IReadOnlyList<long>? importBatchIds)
    {
        var ordered = candles.OrderBy(c => c.OpenTimeUtc).ToList();
        var fingerprint = Build(ordered);
        var payload = new
        {
            exchange,
            symbol,
            timeframe,
            requestedStartUtc = requestedStartUtc.ToString("O"),
            requestedEndUtc = requestedEndUtc.ToString("O"),
            candleCount = ordered.Count,
            firstCandleOpenTimeUtc = ordered.Count > 0 ? ordered[0].OpenTimeUtc.ToString("O") : null,
            lastCandleOpenTimeUtc = ordered.Count > 0 ? ordered[^1].OpenTimeUtc.ToString("O") : null,
            candleDataFingerprint = fingerprint,
            dataSource,
            importBatchIds = importBatchIds ?? [],
            missingCandleCount,
            duplicateCandleCount,
            gapDiagnosticsJson = gapDiagnosticsJson ?? "{}",
            createdAtUtc = DateTime.UtcNow.ToString("O")
        };

        return JsonSerializer.Serialize(payload);
    }
}
