using System.Globalization;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>
/// Milestone 23.0E2B WP1 — canonical immutable access payload, deterministic serialization,
/// SHA-256 stability, and exclusion of mutable persistence metadata.
/// </summary>
public sealed class ValidationAccessPayloadCanonicalizerTests
{
    private static readonly ValidationAccessPayloadCanonicalizer Canonicalizer = new();

    [Fact]
    public void CanonicalSerialization_HasStablePropertyOrder_AndInvariantFormat()
    {
        var audit = NewAudit();
        var json = Canonicalizer.SerializeCanonical(Canonicalizer.Create(audit));

        // Fixed property order, first to last.
        var expectedOrder = new[]
        {
            "contractVersion", "scopeExecutionId", "scopeSequenceNumber", "validationExperimentId",
            "trialId", "trialNumber", "callerComponent", "accessPurpose",
            "requestedStartUtc", "requestedEndUtc", "requestedCandleCount",
            "returnedStartUtc", "returnedEndUtc", "returnedCandleCount",
            "minimumReturnedTimestampUtc", "maximumReturnedTimestampUtc",
            "candleContentFingerprint", "accessedAtUtc", "wasDenied",
            "denialCode", "denialReason", "correlationId", "datasetPartition", "recorderVersion"
        };
        var positions = expectedOrder.Select(name => json.IndexOf($"\"{name}\"", StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p).ToList(), positions);

        // No whitespace-sensitive formatting.
        Assert.DoesNotContain("\n", json);
        Assert.StartsWith("{\"contractVersion\":\"ValidationAccessPayload/v1\"", json);
    }

    [Fact]
    public void CanonicalTimestamps_AreUtcNormalized_MicrosecondPrecision_RoundTripInvariant()
    {
        var unspecified = new DateTime(2024, 6, 1, 12, 30, 45, DateTimeKind.Unspecified)
            .AddTicks(1234567); // sub-microsecond ticks present
        var audit = NewAudit();
        audit.AccessedAtUtc = unspecified;

        var payload = Canonicalizer.Create(audit);
        Assert.Equal(DateTimeKind.Utc, payload.AccessedAtUtc.Kind);
        Assert.Equal(0, payload.AccessedAtUtc.Ticks % 10); // truncated to MySQL datetime(6) precision

        var json = Canonicalizer.SerializeCanonical(payload);
        var formatted = payload.AccessedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        Assert.Contains(formatted, json);
    }

    [Fact]
    public void MicrosecondTruncation_MakesPrePersistAndPostReadHashesEqual()
    {
        var audit = NewAudit();
        audit.AccessedAtUtc = audit.AccessedAtUtc.AddTicks(7); // sub-microsecond noise lost by MySQL

        var truncatedTwin = NewAudit();
        truncatedTwin.AccessEventId = audit.AccessEventId;

        Assert.Equal(Canonicalizer.ComputeSha256(truncatedTwin), Canonicalizer.ComputeSha256(audit));
    }

    [Fact]
    public void MutablePersistenceFields_AreExcludedFromHash()
    {
        var a = NewAudit();
        var b = NewAudit();
        b.AccessEventId = a.AccessEventId;
        b.Id = 999_999;
        b.FlushAttemptCount = 42;
        b.PersistedAtUtc = DateTime.UtcNow.AddDays(1);
        b.CreatedAtUtc = DateTime.UtcNow.AddDays(2);
        b.AccessPayloadHash = "IGNORED";
        b.AccessPayloadContractVersion = "IGNORED";

        Assert.Equal(Canonicalizer.ComputeSha256(a), Canonicalizer.ComputeSha256(b));
    }

    [Fact]
    public void IdenticalPayloads_ProduceIdenticalHash_AndRetryReproducesIt()
    {
        var a = NewAudit();
        var hash1 = Canonicalizer.ComputeSha256(a);
        var hash2 = Canonicalizer.ComputeSha256(a); // simulated retry: same immutable payload
        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length);
        Assert.Matches("^[0-9A-F]{64}$", hash1);
    }

    [Theory]
    [InlineData("ScopeSequenceNumber")]
    [InlineData("ValidationExperimentId")]
    [InlineData("CallerComponent")]
    [InlineData("WasDenied")]
    [InlineData("CandleContentFingerprint")]
    [InlineData("ScopeExecutionId")]
    public void SingleImmutableFieldChange_ChangesHash_AndIsReportedAsConflictingField(string field)
    {
        var a = NewAudit();
        var b = NewAudit();
        b.AccessEventId = a.AccessEventId;
        switch (field)
        {
            case "ScopeSequenceNumber": b.ScopeSequenceNumber = a.ScopeSequenceNumber + 1; break;
            case "ValidationExperimentId": b.ValidationExperimentId = a.ValidationExperimentId + 1; break;
            case "CallerComponent": b.CallerComponent = a.CallerComponent + "X"; break;
            case "WasDenied": b.WasDenied = !a.WasDenied; break;
            case "CandleContentFingerprint": b.CandleContentFingerprint = "FFFF"; break;
            case "ScopeExecutionId": b.ScopeExecutionId = Guid.NewGuid(); break;
        }

        Assert.NotEqual(Canonicalizer.ComputeSha256(a), Canonicalizer.ComputeSha256(b));
        Assert.False(Canonicalizer.PayloadEquals(a, b));
        Assert.Contains(field, Canonicalizer.GetConflictingFieldNames(a, b));
    }

    [Fact]
    public void HashIsComputedFromTruncatedPersistentRepresentation()
    {
        // Recorder Map truncates DenialReason to the 512-char database column.
        var longReason = new string('R', 600);
        var record = new ValidationCandleAccessRecord
        {
            AccessEventId = Guid.NewGuid(),
            ScopeExecutionId = Guid.NewGuid(),
            ScopeSequenceNumber = 1,
            ValidationExperimentId = 42,
            CallerComponent = "Test",
            WasDenied = true,
            DenialCode = "TEST",
            DenialReason = longReason,
            AccessedAtUtc = DateTime.UtcNow,
            ReturnedCandleCount = 0
        };

        var mapped = ValidationCandleAccessRecorder.Map(record, 1, DateTime.UtcNow);
        Assert.Equal(512, mapped.DenialReason!.Length);

        var hashOfMapped = Canonicalizer.ComputeSha256(mapped);

        var truncatedTwin = CloneAudit(mapped);
        truncatedTwin.DenialReason = longReason[..512];
        Assert.Equal(Canonicalizer.ComputeSha256(truncatedTwin), hashOfMapped);

        var untruncatedTwin = CloneAudit(mapped);
        untruncatedTwin.DenialReason = longReason;
        Assert.NotEqual(Canonicalizer.ComputeSha256(untruncatedTwin), hashOfMapped);
    }

    [Fact]
    public void NullableFields_HaveStableRepresentation()
    {
        var a = NewAudit();
        a.DenialCode = null;
        a.TrialId = null;
        var b = NewAudit();
        b.AccessEventId = a.AccessEventId;
        b.DenialCode = null;
        b.TrialId = null;

        Assert.Equal(Canonicalizer.ComputeSha256(a), Canonicalizer.ComputeSha256(b));

        b.DenialCode = ""; // empty string differs from null
        Assert.NotEqual(Canonicalizer.ComputeSha256(a), Canonicalizer.ComputeSha256(b));
    }

    [Fact]
    public void UnsupportedContractVersion_IsRejectedByVersionCheck()
    {
        Assert.True(ValidationAccessPayloadContractVersions.IsSupported("ValidationAccessPayload/v1"));
        Assert.False(ValidationAccessPayloadContractVersions.IsSupported("ValidationAccessPayload/v999"));
        Assert.False(ValidationAccessPayloadContractVersions.IsSupported(null));
        Assert.False(ValidationAccessPayloadContractVersions.IsSupported(""));
    }

    internal static ValidationCandleAccessAudit NewAudit()
    {
        var accessed = new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc).AddTicks(123450);
        return new ValidationCandleAccessAudit
        {
            AccessEventId = Guid.NewGuid(),
            ScopeExecutionId = new Guid("11111111-1111-1111-1111-111111111111"),
            ScopeSequenceNumber = 1,
            ValidationExperimentId = 42,
            TrialId = 7,
            TrialNumber = 3,
            CallerComponent = "StrategyLabRunner",
            AccessPurpose = "EvaluationRange",
            RequestedStartUtc = accessed.AddDays(-2),
            RequestedEndUtc = accessed,
            RequestedCandleCount = 100,
            ReturnedStartUtc = accessed.AddDays(-2),
            ReturnedEndUtc = accessed,
            ReturnedCandleCount = 100,
            MinimumReturnedTimestampUtc = accessed.AddDays(-2),
            MaximumReturnedTimestampUtc = accessed,
            CandleContentFingerprint = "ABCD1234",
            AccessedAtUtc = accessed,
            WasDenied = false,
            DenialCode = null,
            DenialReason = null,
            CorrelationId = "corr-1",
            DatasetPartition = "Training",
            RecorderVersion = ValidationCandleAccessRecorder.RecorderVersion,
            FlushAttemptCount = 1,
            PersistedAtUtc = accessed,
            CreatedAtUtc = accessed
        };
    }

    private static ValidationCandleAccessAudit CloneAudit(ValidationCandleAccessAudit source) => new()
    {
        AccessEventId = source.AccessEventId,
        ScopeExecutionId = source.ScopeExecutionId,
        ScopeSequenceNumber = source.ScopeSequenceNumber,
        ValidationExperimentId = source.ValidationExperimentId,
        TrialId = source.TrialId,
        TrialNumber = source.TrialNumber,
        CallerComponent = source.CallerComponent,
        AccessPurpose = source.AccessPurpose,
        RequestedStartUtc = source.RequestedStartUtc,
        RequestedEndUtc = source.RequestedEndUtc,
        RequestedCandleCount = source.RequestedCandleCount,
        ReturnedStartUtc = source.ReturnedStartUtc,
        ReturnedEndUtc = source.ReturnedEndUtc,
        ReturnedCandleCount = source.ReturnedCandleCount,
        MinimumReturnedTimestampUtc = source.MinimumReturnedTimestampUtc,
        MaximumReturnedTimestampUtc = source.MaximumReturnedTimestampUtc,
        CandleContentFingerprint = source.CandleContentFingerprint,
        AccessedAtUtc = source.AccessedAtUtc,
        WasDenied = source.WasDenied,
        DenialCode = source.DenialCode,
        DenialReason = source.DenialReason,
        CorrelationId = source.CorrelationId,
        DatasetPartition = source.DatasetPartition,
        RecorderVersion = source.RecorderVersion,
        FlushAttemptCount = source.FlushAttemptCount,
        PersistedAtUtc = source.PersistedAtUtc,
        CreatedAtUtc = source.CreatedAtUtc
    };
}
