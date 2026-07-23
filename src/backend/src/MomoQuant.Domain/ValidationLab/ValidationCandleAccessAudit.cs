using MomoQuant.Domain.Common;

namespace MomoQuant.Domain.ValidationLab;

/// <summary>
/// Persisted evidence of an actual candle access during Validation Laboratory training.
/// </summary>
public class ValidationCandleAccessAudit : Entity
{
    /// <summary>
    /// Durable idempotency key generated once when the in-memory access event is created.
    /// Never regenerated on flush/retry.
    /// </summary>
    public Guid AccessEventId { get; set; }

    public Guid ScopeExecutionId { get; set; }

    public long ValidationExperimentId { get; set; }
    public long? TrialId { get; set; }
    public int? TrialNumber { get; set; }
    public string CallerComponent { get; set; } = string.Empty;
    public DateTime? RequestedStartUtc { get; set; }
    public DateTime? RequestedEndUtc { get; set; }
    public DateTime? ReturnedStartUtc { get; set; }
    public DateTime? ReturnedEndUtc { get; set; }
    public int ReturnedCandleCount { get; set; }
    public DateTime? MinimumReturnedTimestampUtc { get; set; }
    public DateTime? MaximumReturnedTimestampUtc { get; set; }
    public string? CandleContentFingerprint { get; set; }
    public DateTime AccessedAtUtc { get; set; }
    public bool WasDenied { get; set; }
    public string? DenialReason { get; set; }
    public int FlushAttemptCount { get; set; }
    public DateTime? PersistedAtUtc { get; set; }
    public string RecorderVersion { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
