using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.StrategyLab;

public sealed record CandleContentFingerprintResult(
    string FullSha256,
    string ShortDisplayHash,
    string AlgorithmVersion,
    string? LegacyMetadataFingerprint,
    int CandleCount,
    DateTime? FirstOpenTimeUtc,
    DateTime? LastOpenTimeUtc);

/// <summary>
/// Shared Strategy/Validation Laboratory candle-content fingerprint (OHLCV).
/// Does not rewrite historical metadata-only fingerprints.
/// </summary>
public static class CandleContentFingerprintService
{
    public const string AlgorithmVersion = "CandleContentFingerprint/v1";

    public static CandleContentFingerprintResult Compute(
        IReadOnlyList<Candle> candles,
        string? legacyMetadataFingerprint = null)
    {
        if (candles.Count == 0)
        {
            var empty = SHA256.HashData(Encoding.UTF8.GetBytes(string.Empty));
            var emptyHex = Convert.ToHexString(empty);
            return new CandleContentFingerprintResult(
                emptyHex,
                emptyHex[..16],
                AlgorithmVersion,
                legacyMetadataFingerprint,
                0,
                null,
                null);
        }

        var ordered = candles
            .Select(c => new
            {
                OpenTimeUtc = DateTime.SpecifyKind(c.OpenTimeUtc, DateTimeKind.Utc),
                c.Open,
                c.High,
                c.Low,
                c.Close,
                c.Volume
            })
            .OrderBy(c => c.OpenTimeUtc)
            .ToList();

        DateTime? prev = null;
        foreach (var c in ordered)
        {
            if (prev is not null && c.OpenTimeUtc == prev.Value)
            {
                throw new InvalidOperationException(
                    $"Duplicate candle OpenTimeUtc {c.OpenTimeUtc:O} rejected for content fingerprint.");
            }

            prev = c.OpenTimeUtc;
        }

        var sb = new StringBuilder(ordered.Count * 80);
        foreach (var c in ordered)
        {
            sb.Append(c.OpenTimeUtc.ToString("O", CultureInfo.InvariantCulture)).Append('|')
                .Append(FormatDecimal(c.Open)).Append('|')
                .Append(FormatDecimal(c.High)).Append('|')
                .Append(FormatDecimal(c.Low)).Append('|')
                .Append(FormatDecimal(c.Close)).Append('|')
                .Append(FormatDecimal(c.Volume))
                .Append('\n');
        }

        var full = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        return new CandleContentFingerprintResult(
            full,
            full[..16],
            AlgorithmVersion,
            legacyMetadataFingerprint,
            ordered.Count,
            ordered[0].OpenTimeUtc,
            ordered[^1].OpenTimeUtc);
    }

    private static string FormatDecimal(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);
}
