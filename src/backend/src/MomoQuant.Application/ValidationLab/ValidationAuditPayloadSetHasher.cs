using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Canonical ordered payload-set hashing for durable audit batch manifests and final execution hashes
/// (Milestone 23.0E2C1 WP18).
/// </summary>
public interface IValidationAuditPayloadSetHasher
{
    const int MaxManifestEvents = 64;

    string ComputeSetHash(IEnumerable<ValidationAuditPayloadSetEntry> entries);

    (string ExpectedEventIdsJson, string ExpectedPayloadHashesJson) BuildManifestJsons(
        IEnumerable<ValidationAuditPayloadSetEntry> entries);

    /// <summary>
    /// Ensures entries are contiguous starting at <paramref name="expectedFirstSequence"/>,
    /// with no duplicate sequences or AccessEventIds. Fail closed on violation.
    /// </summary>
    void ValidateContiguousSequences(
        IEnumerable<ValidationAuditPayloadSetEntry> entries,
        long expectedFirstSequence);

    /// <summary>
    /// Fail-closed when any two inclusive sequence ranges overlap.
    /// </summary>
    void ValidateNoOverlappingRanges(IEnumerable<(long FirstSequence, long LastSequence)> ranges);
}

/// <summary>One ordered contract entry for set hashing / batch manifests.</summary>
public sealed record ValidationAuditPayloadSetEntry(
    long ScopeSequenceNumber,
    Guid AccessEventId,
    string AccessPayloadHash,
    string AccessPayloadContractVersion);

public sealed class ValidationAuditPayloadSetHasher : IValidationAuditPayloadSetHasher
{
    public const int MaxManifestEvents = IValidationAuditPayloadSetHasher.MaxManifestEvents;

    public string ComputeSetHash(IEnumerable<ValidationAuditPayloadSetEntry> entries)
    {
        var ordered = OrderAndValidateSize(entries);
        var canonical = SerializeCanonicalEntries(ordered);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }

    public (string ExpectedEventIdsJson, string ExpectedPayloadHashesJson) BuildManifestJsons(
        IEnumerable<ValidationAuditPayloadSetEntry> entries)
    {
        var ordered = OrderAndValidateSize(entries);
        ValidateNoDuplicateIdsOrSequences(ordered);

        using var idsStream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(idsStream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartArray();
            foreach (var entry in ordered)
            {
                writer.WriteStringValue(FormatAccessEventId(entry.AccessEventId));
            }

            writer.WriteEndArray();
        }

        using var hashesStream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(hashesStream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartArray();
            foreach (var entry in ordered)
            {
                writer.WriteStringValue(NormalizeHash(entry.AccessPayloadHash));
            }

            writer.WriteEndArray();
        }

        return (
            Encoding.UTF8.GetString(idsStream.ToArray()),
            Encoding.UTF8.GetString(hashesStream.ToArray()));
    }

    public void ValidateContiguousSequences(
        IEnumerable<ValidationAuditPayloadSetEntry> entries,
        long expectedFirstSequence)
    {
        var ordered = OrderAndValidateSize(entries);
        ValidateNoDuplicateIdsOrSequences(ordered);

        if (ordered.Count == 0)
        {
            return;
        }

        var expected = expectedFirstSequence;
        foreach (var entry in ordered)
        {
            if (entry.ScopeSequenceNumber != expected)
            {
                throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_SEQUENCE_GAP",
                    $"Expected contiguous sequence {expected.ToString(CultureInfo.InvariantCulture)} " +
                    $"but found {entry.ScopeSequenceNumber.ToString(CultureInfo.InvariantCulture)}.");
            }

            expected++;
        }
    }

    public void ValidateNoOverlappingRanges(IEnumerable<(long FirstSequence, long LastSequence)> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        var list = ranges.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var a = list[i];
            if (a.FirstSequence > a.LastSequence)
            {
                throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_SEQUENCE_GAP",
                    $"Invalid range [{a.FirstSequence},{a.LastSequence}].");
            }

            for (var j = i + 1; j < list.Count; j++)
            {
                var b = list[j];
                if (a.FirstSequence <= b.LastSequence && b.FirstSequence <= a.LastSequence)
                {
                    throw new ValidationAuditExecutionException(
                        "VALIDATION_AUDIT_BATCH_OVERLAP",
                        $"Sequence ranges [{a.FirstSequence},{a.LastSequence}] and [{b.FirstSequence},{b.LastSequence}] overlap.");
                }
            }
        }
    }

    private static List<ValidationAuditPayloadSetEntry> OrderAndValidateSize(
        IEnumerable<ValidationAuditPayloadSetEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var list = entries
            .Select(NormalizeEntry)
            .OrderBy(e => e.ScopeSequenceNumber)
            .ThenBy(e => e.AccessEventId)
            .ToList();

        if (list.Count > MaxManifestEvents)
        {
            throw new ValidationAuditExecutionException(
                "VALIDATION_AUDIT_MANIFEST_SIZE_EXCEEDED",
                $"Audit batch manifest size {list.Count} exceeds maximum of {MaxManifestEvents}.");
        }

        return list;
    }

    private static ValidationAuditPayloadSetEntry NormalizeEntry(ValidationAuditPayloadSetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.AccessEventId == Guid.Empty)
        {
            throw new ValidationAuditExecutionException(
                "VALIDATION_AUDIT_MANIFEST_INVALID",
                "AccessEventId must be a non-empty Guid.");
        }

        if (string.IsNullOrWhiteSpace(entry.AccessPayloadHash))
        {
            throw new ValidationAuditExecutionException(
                "VALIDATION_AUDIT_MANIFEST_INVALID",
                "AccessPayloadHash is required.");
        }

        if (string.IsNullOrWhiteSpace(entry.AccessPayloadContractVersion))
        {
            throw new ValidationAuditExecutionException(
                "VALIDATION_AUDIT_MANIFEST_INVALID",
                "AccessPayloadContractVersion is required.");
        }

        return entry with
        {
            AccessPayloadHash = NormalizeHash(entry.AccessPayloadHash),
            AccessPayloadContractVersion = entry.AccessPayloadContractVersion.Trim()
        };
    }

    private static void ValidateNoDuplicateIdsOrSequences(IReadOnlyList<ValidationAuditPayloadSetEntry> ordered)
    {
        var seenSequences = new HashSet<long>();
        var seenIds = new HashSet<Guid>();
        foreach (var entry in ordered)
        {
            if (!seenSequences.Add(entry.ScopeSequenceNumber))
            {
                throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_DUPLICATE_SEQUENCE",
                    $"Duplicate ScopeSequenceNumber {entry.ScopeSequenceNumber.ToString(CultureInfo.InvariantCulture)}.");
            }

            if (!seenIds.Add(entry.AccessEventId))
            {
                throw new ValidationAuditExecutionException(
                    "VALIDATION_AUDIT_DUPLICATE_ACCESS_EVENT_ID",
                    $"Duplicate AccessEventId {FormatAccessEventId(entry.AccessEventId)}.");
            }
        }
    }

    private static string SerializeCanonicalEntries(IReadOnlyList<ValidationAuditPayloadSetEntry> ordered)
    {
        ValidateNoDuplicateIdsOrSequences(ordered);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartArray();
            foreach (var entry in ordered)
            {
                writer.WriteStartObject();
                writer.WriteNumber("scopeSequenceNumber", entry.ScopeSequenceNumber);
                writer.WriteString("accessEventId", FormatAccessEventId(entry.AccessEventId));
                writer.WriteString("accessPayloadHash", NormalizeHash(entry.AccessPayloadHash));
                writer.WriteString("accessPayloadContractVersion", entry.AccessPayloadContractVersion);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string FormatAccessEventId(Guid id) =>
        id.ToString("D").ToUpperInvariant();

    private static string NormalizeHash(string hash) =>
        hash.Trim().ToUpperInvariant();
}
