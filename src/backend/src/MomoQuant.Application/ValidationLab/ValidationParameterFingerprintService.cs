using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.ValidationLab;

public interface IValidationParameterFingerprintService
{
    string FingerprintAlgorithmVersion { get; }
    string ComputeFingerprint(IReadOnlyDictionary<string, string> parameters);
    string ComputeFingerprintFromSnapshotJson(string snapshotJson);
    bool IsEmptyContentFingerprint(string? fingerprint);
    FrozenSnapshotValidationStatus ValidateParameterSnapshot(
        string? snapshotJson,
        bool requireNonEmptyParameters = true);
}

/// <summary>
/// ValidationParameterFingerprint/v1 — deterministic canonical parameter hashing.
/// </summary>
public sealed class ValidationParameterFingerprintService : IValidationParameterFingerprintService
{
    public const string Version = "ValidationParameterFingerprint/v1";
    public const string EmptyContentFingerprint = "E3B0C44298FC1C14";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public string FingerprintAlgorithmVersion => Version;

    public string ComputeFingerprint(IReadOnlyDictionary<string, string> parameters)
    {
        return ComputeCanonical(parameters).ShortDisplayHash;
    }

    public ParameterFingerprintResult ComputeCanonical(IReadOnlyDictionary<string, string> parameters)
    {
        var canonical = Canonicalize(parameters);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        var full = Convert.ToHexString(hash);
        return new ParameterFingerprintResult(
            FullSha256: full,
            ShortDisplayHash: full[..16],
            AlgorithmVersion: Version,
            CanonicalSnapshot: canonical,
            ValidationStatus: string.IsNullOrWhiteSpace(canonical)
                ? FrozenSnapshotValidationStatus.Empty
                : FrozenSnapshotValidationStatus.Valid);
    }

    public string ComputeFingerprintFromSnapshotJson(string snapshotJson)
    {
        var dict = DeserializeParameters(snapshotJson);
        return ComputeFingerprint(dict);
    }

    public bool IsEmptyContentFingerprint(string? fingerprint) =>
        string.Equals(fingerprint, EmptyContentFingerprint, StringComparison.OrdinalIgnoreCase);

    public FrozenSnapshotValidationStatus ValidateParameterSnapshot(
        string? snapshotJson,
        bool requireNonEmptyParameters = true)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            return FrozenSnapshotValidationStatus.Missing;
        }

        if (string.IsNullOrWhiteSpace(snapshotJson.Trim()))
        {
            return FrozenSnapshotValidationStatus.Empty;
        }

        Dictionary<string, string> dict;
        try
        {
            dict = DeserializeParameters(snapshotJson);
        }
        catch
        {
            return FrozenSnapshotValidationStatus.InvalidJson;
        }

        if (requireNonEmptyParameters && dict.Count == 0)
        {
            return FrozenSnapshotValidationStatus.Empty;
        }

        if (requireNonEmptyParameters && dict.Values.All(string.IsNullOrWhiteSpace))
        {
            return FrozenSnapshotValidationStatus.Empty;
        }

        var fp = ComputeFingerprint(dict);
        if (IsEmptyContentFingerprint(fp))
        {
            return FrozenSnapshotValidationStatus.Empty;
        }

        return FrozenSnapshotValidationStatus.Valid;
    }

    internal static string Canonicalize(IReadOnlyDictionary<string, string> parameters)
    {
        var ordered = parameters
            .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => $"{NormalizeKey(p.Key)}={NormalizeValue(p.Value)}");
        return string.Join("|", ordered);
    }

    private static string NormalizeKey(string key) => key.Trim().ToLowerInvariant();

    private static string NormalizeValue(string value)
    {
        var trimmed = value.Trim();
        if (bool.TryParse(trimmed, out var b))
        {
            return b ? "true" : "false";
        }

        if (decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var dec)
            || decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out dec))
        {
            return dec.ToString("0.############################", CultureInfo.InvariantCulture);
        }

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lng))
        {
            return lng.ToString(CultureInfo.InvariantCulture);
        }

        if (string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase)
            || trimmed.Length == 0)
        {
            return "null";
        }

        return trimmed;
    }

    private static Dictionary<string, string> DeserializeParameters(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() is "{}" or "null")
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
        return dict is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed record ParameterFingerprintResult(
    string FullSha256,
    string ShortDisplayHash,
    string AlgorithmVersion,
    string CanonicalSnapshot,
    FrozenSnapshotValidationStatus ValidationStatus);
