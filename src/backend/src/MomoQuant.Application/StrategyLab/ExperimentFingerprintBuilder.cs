using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.StrategyLab;

public static class ExperimentFingerprintBuilder
{
    public static string Build(
        string strategyCode,
        string strategyVersion,
        long exchangeId,
        long symbolId,
        string symbol,
        string timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        StrategyLabExecutionMode executionMode,
        IReadOnlyDictionary<string, string> parameters,
        string? featureFlagsJson,
        decimal initialBalance,
        string feeSettingsJson,
        string slippageSettingsJson)
    {
        var payload = new
        {
            strategyCode,
            strategyVersion,
            exchangeId,
            symbolId,
            symbol,
            timeframe,
            fromUtc = fromUtc.ToString("O"),
            toUtc = toUtc.ToString("O"),
            executionMode = executionMode.ToString(),
            parameters = parameters.OrderBy(p => p.Key).ToDictionary(p => p.Key, p => p.Value),
            featureFlagsJson = featureFlagsJson ?? "{}",
            initialBalance,
            feeSettingsJson,
            slippageSettingsJson
        };

        var json = JsonSerializer.Serialize(payload);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        var shortHash = Convert.ToHexString(hash)[..6];
        var prefix = strategyCode switch
        {
            "PRICE_STRUCTURE_BREAKOUT_RETEST" => "PSBR",
            "PRICE_STRUCTURE_LIQUIDITY_SWEEP_RECLAIM" => "PSLS",
            _ => "SLAB"
        };

        return $"{prefix}-{symbol}-{timeframe}-{shortHash}";
    }

    public static string BuildCandleDatasetFingerprint(
        long exchangeId,
        long symbolId,
        string timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        int candleCount,
        DateTime? firstCandleUtc,
        DateTime? lastCandleUtc)
    {
        // Legacy metadata-only fingerprint (historical compatibility).
        var raw = $"{exchangeId}|{symbolId}|{timeframe}|{fromUtc:O}|{toUtc:O}|{candleCount}|{firstCandleUtc:O}|{lastCandleUtc:O}";
        return SetupFingerprintHasher.Hash(raw);
    }

    /// <summary>
    /// New content fingerprint: full SHA-256 of canonical OHLCV. Returns short display hash for
    /// fields that historically stored 16-char hashes; callers should persist FullSha256 separately when available.
    /// </summary>
    public static CandleContentFingerprintResult BuildCandleContentFingerprint(
        IReadOnlyList<Domain.MarketData.Candle> candles,
        string? legacyMetadataFingerprint = null) =>
        CandleContentFingerprintService.Compute(candles, legacyMetadataFingerprint);
}

internal static class SetupFingerprintHasher
{
    public static string Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes)[..16];
    }
}
