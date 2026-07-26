using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Supported canonical access-payload contract versions.
/// </summary>
public static class ValidationAccessPayloadContractVersions
{
    public const string V1 = "ValidationAccessPayload/v1";
    public const string Current = V1;

    public static bool IsSupported(string? version) =>
        string.Equals(version, V1, StringComparison.Ordinal);
}

/// <summary>
/// Immutable canonical representation of one persisted validation candle-access event.
/// Contains only immutable payload fields — never persistence-attempt metadata
/// (database Id, FlushAttemptCount, PersistedAtUtc, CreatedAtUtc, EF state).
/// AccessEventId identifies the payload but is not part of the hashed payload itself.
/// </summary>
public sealed record ValidationAccessEventCanonicalPayload
{
    public required Guid ScopeExecutionId { get; init; }
    public required long ScopeSequenceNumber { get; init; }
    public required long ValidationExperimentId { get; init; }
    public required long? TrialId { get; init; }
    public required int? TrialNumber { get; init; }
    public required string CallerComponent { get; init; }
    public required string? AccessPurpose { get; init; }
    public required DateTime? RequestedStartUtc { get; init; }
    public required DateTime? RequestedEndUtc { get; init; }
    public required int? RequestedCandleCount { get; init; }
    public required DateTime? ReturnedStartUtc { get; init; }
    public required DateTime? ReturnedEndUtc { get; init; }
    public required int ReturnedCandleCount { get; init; }
    public required DateTime? MinimumReturnedTimestampUtc { get; init; }
    public required DateTime? MaximumReturnedTimestampUtc { get; init; }
    public required string? CandleContentFingerprint { get; init; }
    public required DateTime AccessedAtUtc { get; init; }
    public required bool WasDenied { get; init; }
    public required string? DenialCode { get; init; }
    public required string? DenialReason { get; init; }
    public required string? CorrelationId { get; init; }
    public required string? DatasetPartition { get; init; }
    public required string RecorderVersion { get; init; }
}

/// <summary>
/// Builds the canonical immutable payload, its deterministic serialization, and its SHA-256 hash
/// from the final mapped persistent representation (post-truncation, UTC-normalized,
/// microsecond precision to match MySQL datetime(6)).
/// </summary>
public interface IValidationAccessPayloadCanonicalizer
{
    string ContractVersion { get; }

    ValidationAccessEventCanonicalPayload Create(ValidationCandleAccessAudit audit);

    string SerializeCanonical(ValidationAccessEventCanonicalPayload payload);

    string ComputeSha256(ValidationAccessEventCanonicalPayload payload);

    string ComputeSha256(ValidationCandleAccessAudit audit);

    bool PayloadEquals(ValidationCandleAccessAudit requested, ValidationCandleAccessAudit persisted);

    /// <summary>Returns the immutable payload field names whose canonical values differ.</summary>
    IReadOnlyList<string> GetConflictingFieldNames(
        ValidationCandleAccessAudit requested,
        ValidationCandleAccessAudit persisted);
}

public sealed class ValidationAccessPayloadCanonicalizer : IValidationAccessPayloadCanonicalizer
{
    public string ContractVersion => ValidationAccessPayloadContractVersions.Current;

    public ValidationAccessEventCanonicalPayload Create(ValidationCandleAccessAudit audit)
    {
        ArgumentNullException.ThrowIfNull(audit);
        return new ValidationAccessEventCanonicalPayload
        {
            ScopeExecutionId = audit.ScopeExecutionId,
            ScopeSequenceNumber = audit.ScopeSequenceNumber,
            ValidationExperimentId = audit.ValidationExperimentId,
            TrialId = audit.TrialId,
            TrialNumber = audit.TrialNumber,
            CallerComponent = audit.CallerComponent,
            AccessPurpose = audit.AccessPurpose,
            RequestedStartUtc = NormalizeUtc(audit.RequestedStartUtc),
            RequestedEndUtc = NormalizeUtc(audit.RequestedEndUtc),
            RequestedCandleCount = audit.RequestedCandleCount,
            ReturnedStartUtc = NormalizeUtc(audit.ReturnedStartUtc),
            ReturnedEndUtc = NormalizeUtc(audit.ReturnedEndUtc),
            ReturnedCandleCount = audit.ReturnedCandleCount,
            MinimumReturnedTimestampUtc = NormalizeUtc(audit.MinimumReturnedTimestampUtc),
            MaximumReturnedTimestampUtc = NormalizeUtc(audit.MaximumReturnedTimestampUtc),
            CandleContentFingerprint = audit.CandleContentFingerprint,
            AccessedAtUtc = NormalizeUtc(audit.AccessedAtUtc),
            WasDenied = audit.WasDenied,
            DenialCode = audit.DenialCode,
            DenialReason = audit.DenialReason,
            CorrelationId = audit.CorrelationId,
            DatasetPartition = audit.DatasetPartition,
            RecorderVersion = audit.RecorderVersion
        };
    }

    public string SerializeCanonical(ValidationAccessEventCanonicalPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("contractVersion", ContractVersion);
            writer.WriteString("scopeExecutionId", payload.ScopeExecutionId.ToString("D"));
            writer.WriteNumber("scopeSequenceNumber", payload.ScopeSequenceNumber);
            writer.WriteNumber("validationExperimentId", payload.ValidationExperimentId);
            WriteNullableNumber(writer, "trialId", payload.TrialId);
            WriteNullableNumber(writer, "trialNumber", payload.TrialNumber);
            writer.WriteString("callerComponent", payload.CallerComponent);
            WriteNullableString(writer, "accessPurpose", payload.AccessPurpose);
            WriteNullableTimestamp(writer, "requestedStartUtc", payload.RequestedStartUtc);
            WriteNullableTimestamp(writer, "requestedEndUtc", payload.RequestedEndUtc);
            WriteNullableNumber(writer, "requestedCandleCount", payload.RequestedCandleCount);
            WriteNullableTimestamp(writer, "returnedStartUtc", payload.ReturnedStartUtc);
            WriteNullableTimestamp(writer, "returnedEndUtc", payload.ReturnedEndUtc);
            writer.WriteNumber("returnedCandleCount", payload.ReturnedCandleCount);
            WriteNullableTimestamp(writer, "minimumReturnedTimestampUtc", payload.MinimumReturnedTimestampUtc);
            WriteNullableTimestamp(writer, "maximumReturnedTimestampUtc", payload.MaximumReturnedTimestampUtc);
            WriteNullableString(writer, "candleContentFingerprint", payload.CandleContentFingerprint);
            writer.WriteString("accessedAtUtc", FormatTimestamp(payload.AccessedAtUtc));
            writer.WriteBoolean("wasDenied", payload.WasDenied);
            WriteNullableString(writer, "denialCode", payload.DenialCode);
            WriteNullableString(writer, "denialReason", payload.DenialReason);
            WriteNullableString(writer, "correlationId", payload.CorrelationId);
            WriteNullableString(writer, "datasetPartition", payload.DatasetPartition);
            writer.WriteString("recorderVersion", payload.RecorderVersion);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public string ComputeSha256(ValidationAccessEventCanonicalPayload payload)
    {
        var canonical = SerializeCanonical(payload);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }

    public string ComputeSha256(ValidationCandleAccessAudit audit) =>
        ComputeSha256(Create(audit));

    public bool PayloadEquals(ValidationCandleAccessAudit requested, ValidationCandleAccessAudit persisted) =>
        Create(requested) == Create(persisted);

    public IReadOnlyList<string> GetConflictingFieldNames(
        ValidationCandleAccessAudit requested,
        ValidationCandleAccessAudit persisted)
    {
        var a = Create(requested);
        var b = Create(persisted);
        var conflicts = new List<string>();

        void Compare<T>(string name, T x, T y)
        {
            if (!EqualityComparer<T>.Default.Equals(x, y))
            {
                conflicts.Add(name);
            }
        }

        Compare(nameof(a.ScopeExecutionId), a.ScopeExecutionId, b.ScopeExecutionId);
        Compare(nameof(a.ScopeSequenceNumber), a.ScopeSequenceNumber, b.ScopeSequenceNumber);
        Compare(nameof(a.ValidationExperimentId), a.ValidationExperimentId, b.ValidationExperimentId);
        Compare(nameof(a.TrialId), a.TrialId, b.TrialId);
        Compare(nameof(a.TrialNumber), a.TrialNumber, b.TrialNumber);
        Compare(nameof(a.CallerComponent), a.CallerComponent, b.CallerComponent);
        Compare(nameof(a.AccessPurpose), a.AccessPurpose, b.AccessPurpose);
        Compare(nameof(a.RequestedStartUtc), a.RequestedStartUtc, b.RequestedStartUtc);
        Compare(nameof(a.RequestedEndUtc), a.RequestedEndUtc, b.RequestedEndUtc);
        Compare(nameof(a.RequestedCandleCount), a.RequestedCandleCount, b.RequestedCandleCount);
        Compare(nameof(a.ReturnedStartUtc), a.ReturnedStartUtc, b.ReturnedStartUtc);
        Compare(nameof(a.ReturnedEndUtc), a.ReturnedEndUtc, b.ReturnedEndUtc);
        Compare(nameof(a.ReturnedCandleCount), a.ReturnedCandleCount, b.ReturnedCandleCount);
        Compare(nameof(a.MinimumReturnedTimestampUtc), a.MinimumReturnedTimestampUtc, b.MinimumReturnedTimestampUtc);
        Compare(nameof(a.MaximumReturnedTimestampUtc), a.MaximumReturnedTimestampUtc, b.MaximumReturnedTimestampUtc);
        Compare(nameof(a.CandleContentFingerprint), a.CandleContentFingerprint, b.CandleContentFingerprint);
        Compare(nameof(a.AccessedAtUtc), a.AccessedAtUtc, b.AccessedAtUtc);
        Compare(nameof(a.WasDenied), a.WasDenied, b.WasDenied);
        Compare(nameof(a.DenialCode), a.DenialCode, b.DenialCode);
        Compare(nameof(a.DenialReason), a.DenialReason, b.DenialReason);
        Compare(nameof(a.CorrelationId), a.CorrelationId, b.CorrelationId);
        Compare(nameof(a.DatasetPartition), a.DatasetPartition, b.DatasetPartition);
        Compare(nameof(a.RecorderVersion), a.RecorderVersion, b.RecorderVersion);

        return conflicts;
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, long? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteNumber(name, value.Value);
        }
    }

    private static void WriteNullableTimestamp(Utf8JsonWriter writer, string name, DateTime? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, FormatTimestamp(value.Value));
        }
    }

    private static string FormatTimestamp(DateTime utc) =>
        utc.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>
    /// UTC-kind normalization plus truncation to microsecond precision (MySQL datetime(6)),
    /// so pre-persist and post-read canonical values are identical.
    /// </summary>
    private static DateTime NormalizeUtc(DateTime value)
    {
        var utc = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return new DateTime(utc.Ticks - (utc.Ticks % 10), DateTimeKind.Utc);
    }

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value is null ? null : NormalizeUtc(value.Value);
}
