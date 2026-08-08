using System.Text.Json;
using System.Text.RegularExpressions;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Audit;

public static class AuditEvidenceCodes
{
    public const string Invalid = "AUDIT_EVIDENCE_INVALID";
    public const string Unavailable = "AUDIT_EVIDENCE_UNAVAILABLE";
}

public static class RequiredAuditActions
{
    public const string ParameterSetDeploymentQualified = "PARAMETER_SET_DEPLOYMENT_QUALIFIED";
    public const string PaperDeploymentQualificationVerified = "PAPER_DEPLOYMENT_QUALIFICATION_VERIFIED";
    public const string PaperSessionCreated = "PAPER_SESSION_CREATED";
    public const string PaperSessionStarted = "PAPER_SESSION_STARTED";
    public const string PaperSessionResumed = "PAPER_SESSION_RESUMED";
    public const string PaperSessionFailed = "PAPER_SESSION_FAILED";
}

public sealed class AuditEvidenceException : Exception
{
    public AuditEvidenceException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }

    public static AuditEvidenceException Invalid(string message) =>
        new(AuditEvidenceCodes.Invalid, message);

    public static AuditEvidenceException Unavailable(Exception innerException) =>
        new(AuditEvidenceCodes.Unavailable, "Required audit evidence could not be persisted.", innerException);
}

public interface IRequiredAuditMetadata
{
}

public sealed record ParameterSetPublicationAuditMetadata(
    long ParameterSetId,
    string StrategyCode,
    long ExperimentId,
    long TrialId,
    string ParameterFingerprint,
    string EvidenceVersion,
    DateTime QualifiedAtUtc) : IRequiredAuditMetadata;

public sealed record PaperQualificationAuditMetadata(
    long PaperSessionId,
    long TradingSessionId,
    string Phase,
    long ParameterSetId,
    long StrategyId,
    long SymbolId,
    string Timeframe,
    long ExperimentId,
    long TrialId,
    string ParameterFingerprint,
    string EvidenceVersion,
    DateTime VerifiedAtUtc) : IRequiredAuditMetadata;

public sealed record PaperSessionTransitionAuditMetadata(
    long PaperSessionId,
    long TradingSessionId,
    string Phase,
    long ParameterSetId,
    long StrategyId,
    long SymbolId,
    string Timeframe,
    long ExperimentId,
    long TrialId,
    string ParameterFingerprint,
    string EvidenceVersion,
    DateTime VerifiedAtUtc) : IRequiredAuditMetadata;

public sealed record PaperSessionFailureAuditMetadata(
    long PaperSessionId,
    long TradingSessionId,
    string Phase,
    string FailureCode) : IRequiredAuditMetadata;

public sealed record RequiredAuditRequest(
    string Action,
    string EntityType,
    long EntityId,
    long? UserId,
    long? TradingSessionId,
    LogSeverity Severity,
    IRequiredAuditMetadata Metadata,
    DateTime? TimestampUtc = null);

public sealed record AuditTelemetryRequest(
    string Action,
    string EntityType,
    long? EntityId,
    long? UserId,
    string? OldValueJson,
    string? NewValueJson,
    string? IpAddress,
    string? UserAgent,
    DateTime? TimestampUtc = null);

public interface IRequiredAuditWriter
{
    void AttachRequired(RequiredAuditRequest request, CancellationToken cancellationToken = default);
}

public interface IAuditTelemetryWriter
{
    Task WriteTelemetryAsync(AuditTelemetryRequest request, CancellationToken cancellationToken = default);
}

public sealed record PreparedAuditPayload(
    string Action,
    string EntityType,
    long? EntityId,
    long? UserId,
    long? TradingSessionId,
    LogSeverity Severity,
    string? OldValueJson,
    string? NewValueJson,
    string? IpAddress,
    string? UserAgent,
    DateTime CreatedAtUtc);

public static partial class AuditWritePayloadProtection
{
    private const int MaximumJsonLength = 8192;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string[] ForbiddenTerms =
    [
        "password", "secret", "token", "apikey", "api_key", "authorization", "cookie",
        "passphrase", "connectionstring", "connection_string", "exception", "stacktrace",
        "stack_trace", "rawrequest", "raw_request", "parametersjson", "parameters_json"
    ];

    public static PreparedAuditPayload PrepareRequired(RequiredAuditRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequiredIdentity(request.Action, request.EntityType, request.EntityId, request.UserId, request.TradingSessionId);
        ValidateRequiredShape(request.Action, request.Metadata, request.EntityId, request.TradingSessionId);

        var json = JsonSerializer.Serialize(request.Metadata, request.Metadata.GetType(), JsonOptions);
        if (json.Length > MaximumJsonLength || ContainsForbiddenTerm(json))
        {
            throw AuditEvidenceException.Invalid("Required audit metadata is unsafe or oversized.");
        }

        return new PreparedAuditPayload(
            request.Action,
            request.EntityType,
            request.EntityId,
            request.UserId,
            request.TradingSessionId,
            request.Severity,
            null,
            json,
            null,
            null,
            NormalizeUtc(request.TimestampUtc));
    }

    public static PreparedAuditPayload PrepareTelemetry(AuditTelemetryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateTelemetryIdentity(request.Action, request.EntityType, request.EntityId, request.UserId);

        return new PreparedAuditPayload(
            request.Action,
            request.EntityType,
            request.EntityId,
            request.UserId,
            null,
            LogSeverity.Info,
            SanitizeTelemetryJson(request.OldValueJson),
            SanitizeTelemetryJson(request.NewValueJson),
            ValidateBoundedText(request.IpAddress, 64),
            ValidateBoundedText(request.UserAgent, 512),
            NormalizeUtc(request.TimestampUtc));
    }

    private static void ValidateRequiredShape(
        string action,
        IRequiredAuditMetadata metadata,
        long entityId,
        long? tradingSessionId)
    {
        if (metadata is null)
        {
            throw AuditEvidenceException.Invalid("Required audit metadata is missing.");
        }

        var expected = action switch
        {
            RequiredAuditActions.ParameterSetDeploymentQualified => typeof(ParameterSetPublicationAuditMetadata),
            RequiredAuditActions.PaperDeploymentQualificationVerified => typeof(PaperQualificationAuditMetadata),
            RequiredAuditActions.PaperSessionCreated or RequiredAuditActions.PaperSessionStarted
                or RequiredAuditActions.PaperSessionResumed => typeof(PaperSessionTransitionAuditMetadata),
            RequiredAuditActions.PaperSessionFailed => typeof(PaperSessionFailureAuditMetadata),
            _ => throw AuditEvidenceException.Invalid("The action is not allowed for required audit evidence.")
        };

        if (metadata.GetType() != expected)
        {
            throw AuditEvidenceException.Invalid("The required audit metadata does not match its action.");
        }

        switch (metadata)
        {
            case ParameterSetPublicationAuditMetadata value:
                RequirePositive(value.ParameterSetId, value.ExperimentId, value.TrialId);
                RequireEqual(entityId, value.ParameterSetId);
                ValidateSafeValue(value.StrategyCode, 128);
                ValidateSafeValue(value.ParameterFingerprint, 128);
                ValidateSafeValue(value.EvidenceVersion, 128);
                ValidateUtc(value.QualifiedAtUtc);
                break;
            case PaperQualificationAuditMetadata value:
                ValidatePaperEvidence(value.PaperSessionId, value.TradingSessionId, value.Phase,
                    value.ParameterSetId, value.StrategyId, value.SymbolId, value.Timeframe,
                    value.ExperimentId, value.TrialId, value.ParameterFingerprint,
                    value.EvidenceVersion, value.VerifiedAtUtc, entityId, tradingSessionId);
                break;
            case PaperSessionTransitionAuditMetadata value:
                ValidatePaperEvidence(value.PaperSessionId, value.TradingSessionId, value.Phase,
                    value.ParameterSetId, value.StrategyId, value.SymbolId, value.Timeframe,
                    value.ExperimentId, value.TrialId, value.ParameterFingerprint,
                    value.EvidenceVersion, value.VerifiedAtUtc, entityId, tradingSessionId);
                break;
            case PaperSessionFailureAuditMetadata value:
                RequirePositive(value.PaperSessionId, value.TradingSessionId);
                RequireEqual(entityId, value.PaperSessionId);
                RequireEqual(tradingSessionId, value.TradingSessionId);
                ValidatePhase(value.Phase);
                ValidateSafeCode(value.FailureCode);
                break;
        }
    }

    private static void ValidatePaperEvidence(
        long paperSessionId,
        long metadataTradingSessionId,
        string phase,
        long parameterSetId,
        long strategyId,
        long symbolId,
        string timeframe,
        long experimentId,
        long trialId,
        string fingerprint,
        string evidenceVersion,
        DateTime verifiedAtUtc,
        long entityId,
        long? requestTradingSessionId)
    {
        RequirePositive(paperSessionId, metadataTradingSessionId, parameterSetId, strategyId, symbolId, experimentId, trialId);
        RequireEqual(entityId, paperSessionId);
        RequireEqual(requestTradingSessionId, metadataTradingSessionId);
        ValidatePhase(phase);
        ValidateSafeValue(timeframe, 32);
        ValidateSafeValue(fingerprint, 128);
        ValidateSafeValue(evidenceVersion, 128);
        ValidateUtc(verifiedAtUtc);
    }

    private static void ValidateRequiredIdentity(
        string action,
        string entityType,
        long? entityId,
        long? userId,
        long? tradingSessionId)
    {
        if (!IsRequiredAction(action)
            || !SafeEntity().IsMatch(entityType) || entityType.Length > 128
            || entityId is <= 0 || userId is <= 0 || tradingSessionId is <= 0)
        {
            throw AuditEvidenceException.Invalid("Audit identity is invalid.");
        }
    }

    private static void ValidateTelemetryIdentity(
        string action,
        string entityType,
        long? entityId,
        long? userId)
    {
        if (!TelemetryAction().IsMatch(action) || action.Length > 128
            || !SafeEntity().IsMatch(entityType) || entityType.Length > 128
            || entityId is <= 0 || userId is <= 0)
        {
            throw AuditEvidenceException.Invalid("Audit identity is invalid.");
        }
    }

    private static bool IsRequiredAction(string action) => action is
        RequiredAuditActions.ParameterSetDeploymentQualified
        or RequiredAuditActions.PaperDeploymentQualificationVerified
        or RequiredAuditActions.PaperSessionCreated
        or RequiredAuditActions.PaperSessionStarted
        or RequiredAuditActions.PaperSessionResumed
        or RequiredAuditActions.PaperSessionFailed;

    private static string? SanitizeTelemetryJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        if (json.Length > MaximumJsonLength)
        {
            throw AuditEvidenceException.Invalid("Telemetry audit metadata is oversized.");
        }

        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteSanitized(writer, document.RootElement);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSanitized(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (ContainsForbiddenTerm(property.Name))
                    {
                        writer.WriteStringValue("[REDACTED]");
                    }
                    else
                    {
                        WriteSanitized(writer, property.Value);
                    }
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteSanitized(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                var value = element.GetString();
                writer.WriteStringValue(value is not null && ContainsForbiddenTerm(value) ? "[REDACTED]" : value);
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static string? ValidateBoundedText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Length > maximumLength || ContainsForbiddenTerm(value))
        {
            throw AuditEvidenceException.Invalid("Telemetry audit text is unsafe or oversized.");
        }

        return value;
    }

    private static bool ContainsForbiddenTerm(string value)
    {
        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return ForbiddenTerms.Any(term =>
            normalized.Contains(term.Replace("_", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidatePhase(string value)
    {
        if (value is not ("Create" or "Start" or "Resume" or "Activation"))
        {
            throw AuditEvidenceException.Invalid("Required audit phase is invalid.");
        }
    }

    private static void ValidateSafeCode(string value)
    {
        if (value.Length is < 1 or > 128 || !SafeIdentifier().IsMatch(value) || ContainsForbiddenTerm(value))
        {
            throw AuditEvidenceException.Invalid("Required audit outcome code is invalid.");
        }
    }

    private static void ValidateSafeValue(string value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || ContainsForbiddenTerm(value))
        {
            throw AuditEvidenceException.Invalid("Required audit metadata contains an unsafe value.");
        }
    }

    private static void ValidateUtc(DateTime value)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw AuditEvidenceException.Invalid("Required audit timestamps must be UTC.");
        }
    }

    private static DateTime NormalizeUtc(DateTime? value)
    {
        var timestamp = value ?? DateTime.UtcNow;
        ValidateUtc(timestamp);
        return timestamp;
    }

    private static void RequirePositive(params long[] values)
    {
        if (values.Any(value => value <= 0))
        {
            throw AuditEvidenceException.Invalid("Required audit identifiers must be positive.");
        }
    }

    private static void RequireEqual(long? actual, long expected)
    {
        if (actual != expected)
        {
            throw AuditEvidenceException.Invalid("Required audit identity does not match its metadata.");
        }
    }

    [GeneratedRegex("^[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdentifier();

    [GeneratedRegex("^[A-Z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex TelemetryAction();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeEntity();
}
