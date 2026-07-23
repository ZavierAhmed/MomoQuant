using System.Text.Json;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public interface IValidationLeakageAuditor
{
    ValidationLeakageAuditReport BuildPassed(
        DateTime? maxTimestampAccessed,
        DateTime validationStartUtc,
        DateTime trainingStartUtc,
        DateTime trainingEndUtc,
        string optimizerInputFingerprint);

    ValidationLeakageAuditReport Evaluate(
        DateTime? maxTimestampAccessedByOptimizer,
        DateTime validationStartUtc,
        DateTime trainingStartUtc,
        DateTime trainingEndUtc,
        string optimizerInputFingerprint,
        IReadOnlyList<LeakageTrialAccess>? trialAccesses = null);

    /// <summary>
    /// Leakage Passed/Failed must be computed from persisted access evidence, never from expected TrainingEndUtc alone.
    /// </summary>
    ValidationLeakageAuditReport EvaluateFromAccessEvidence(
        IReadOnlyList<ValidationCandleAccessAudit> accessAudits,
        DateTime validationStartUtc,
        DateTime trainingStartUtc,
        DateTime trainingEndUtc,
        string optimizerInputFingerprint);

    string Serialize(ValidationLeakageAuditReport report);
}

public sealed class ValidationLeakageAuditor : IValidationLeakageAuditor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ValidationLeakageAuditReport BuildPassed(
        DateTime? maxTimestampAccessed,
        DateTime validationStartUtc,
        DateTime trainingStartUtc,
        DateTime trainingEndUtc,
        string optimizerInputFingerprint) =>
        Evaluate(maxTimestampAccessed, validationStartUtc, trainingStartUtc, trainingEndUtc, optimizerInputFingerprint);

    public ValidationLeakageAuditReport Evaluate(
        DateTime? maxTimestampAccessedByOptimizer,
        DateTime validationStartUtc,
        DateTime trainingStartUtc,
        DateTime trainingEndUtc,
        string optimizerInputFingerprint,
        IReadOnlyList<LeakageTrialAccess>? trialAccesses = null)
    {
        var valStart = DateTime.SpecifyKind(validationStartUtc, DateTimeKind.Utc);
        ValidationLeakageAuditStatus status;
        string? reason = null;

        if (maxTimestampAccessedByOptimizer is null && (trialAccesses is null || trialAccesses.Count == 0))
        {
            status = ValidationLeakageAuditStatus.NotAvailable;
            reason = "No optimizer candle-access timestamps were recorded.";
        }
        else
        {
            var maxTs = maxTimestampAccessedByOptimizer
                ?? trialAccesses!.Max(t => t.MaximumCandleTimestampUtc);
            maxTs = DateTime.SpecifyKind(maxTs, DateTimeKind.Utc);
            if (maxTs < valStart)
            {
                status = ValidationLeakageAuditStatus.Passed;
                reason = "MaximumTimestampAccessedByOptimizer < ValidationStartUtc.";
            }
            else
            {
                status = ValidationLeakageAuditStatus.Failed;
                reason = "ValidationDataLeakageDetected: MaximumTimestampAccessedByOptimizer >= ValidationStartUtc.";
            }
        }

        return new ValidationLeakageAuditReport
        {
            Status = status,
            MaximumTimestampAccessedByOptimizer = maxTimestampAccessedByOptimizer,
            ValidationStartUtc = valStart,
            TrainingStartUtc = DateTime.SpecifyKind(trainingStartUtc, DateTimeKind.Utc),
            TrainingEndUtc = DateTime.SpecifyKind(trainingEndUtc, DateTimeKind.Utc),
            OptimizerInputFingerprint = optimizerInputFingerprint,
            TrialAccesses = trialAccesses ?? [],
            Reason = reason,
            BlocksFreezeOrPassed = status == ValidationLeakageAuditStatus.Failed,
            AccessEvidenceCount = trialAccesses?.Count ?? 0,
            DeniedAccessCount = 0
        };
    }

    public ValidationLeakageAuditReport EvaluateFromAccessEvidence(
        IReadOnlyList<ValidationCandleAccessAudit> accessAudits,
        DateTime validationStartUtc,
        DateTime trainingStartUtc,
        DateTime trainingEndUtc,
        string optimizerInputFingerprint)
    {
        var valStart = DateTime.SpecifyKind(validationStartUtc, DateTimeKind.Utc);
        if (accessAudits.Count == 0)
        {
            return new ValidationLeakageAuditReport
            {
                Status = ValidationLeakageAuditStatus.NotAvailable,
                ValidationStartUtc = valStart,
                TrainingStartUtc = DateTime.SpecifyKind(trainingStartUtc, DateTimeKind.Utc),
                TrainingEndUtc = DateTime.SpecifyKind(trainingEndUtc, DateTimeKind.Utc),
                OptimizerInputFingerprint = optimizerInputFingerprint,
                Reason = "No persisted candle-access evidence was recorded.",
                BlocksFreezeOrPassed = false,
                AccessEvidenceCount = 0,
                DeniedAccessCount = 0
            };
        }

        var denied = accessAudits.Where(a => a.WasDenied).ToList();
        var allowedMax = accessAudits
            .Where(a => !a.WasDenied && a.MaximumReturnedTimestampUtc is not null)
            .Select(a => DateTime.SpecifyKind(a.MaximumReturnedTimestampUtc!.Value, DateTimeKind.Utc))
            .DefaultIfEmpty()
            .Max();

        DateTime? maxAccessed = allowedMax == default ? null : allowedMax;

        ValidationLeakageAuditStatus status;
        string? reason;
        if (denied.Count > 0)
        {
            status = ValidationLeakageAuditStatus.Failed;
            reason = denied[0].DenialReason
                     ?? "ValidationDataLeakageDetected: prohibited candle access was attempted.";
        }
        else if (maxAccessed is null)
        {
            status = ValidationLeakageAuditStatus.NotAvailable;
            reason = "Access audits exist but no returned candle timestamps were recorded.";
        }
        else if (maxAccessed.Value < valStart)
        {
            status = ValidationLeakageAuditStatus.Passed;
            reason = "Persisted access evidence: MaximumReturnedTimestampUtc < ValidationStartUtc and no denials.";
        }
        else
        {
            status = ValidationLeakageAuditStatus.Failed;
            reason = "ValidationDataLeakageDetected: persisted MaximumReturnedTimestampUtc >= ValidationStartUtc.";
        }

        var trialAccesses = accessAudits
            .Where(a => a.TrialNumber is not null)
            .GroupBy(a => a.TrialNumber!.Value)
            .Select(g =>
            {
                var max = g.Where(x => x.MaximumReturnedTimestampUtc is not null)
                    .Select(x => x.MaximumReturnedTimestampUtc!.Value)
                    .DefaultIfEmpty(g.Min(x => x.RequestedStartUtc ?? trainingStartUtc))
                    .Max();
                return new LeakageTrialAccess
                {
                    TrialNumber = g.Key,
                    RangeStartUtc = g.Min(x => x.RequestedStartUtc ?? trainingStartUtc),
                    RangeEndUtc = g.Max(x => x.RequestedEndUtc ?? trainingEndUtc),
                    MaximumCandleTimestampUtc = DateTime.SpecifyKind(max, DateTimeKind.Utc),
                    SegmentType = "Training",
                    MetricSources = "PersistedCandleAccessAudit"
                };
            })
            .OrderBy(t => t.TrialNumber)
            .ToList();

        return new ValidationLeakageAuditReport
        {
            Status = status,
            MaximumTimestampAccessedByOptimizer = maxAccessed,
            ValidationStartUtc = valStart,
            TrainingStartUtc = DateTime.SpecifyKind(trainingStartUtc, DateTimeKind.Utc),
            TrainingEndUtc = DateTime.SpecifyKind(trainingEndUtc, DateTimeKind.Utc),
            OptimizerInputFingerprint = optimizerInputFingerprint,
            TrialAccesses = trialAccesses,
            Reason = reason,
            BlocksFreezeOrPassed = status == ValidationLeakageAuditStatus.Failed,
            AccessEvidenceCount = accessAudits.Count,
            DeniedAccessCount = denied.Count
        };
    }

    public string Serialize(ValidationLeakageAuditReport report) =>
        JsonSerializer.Serialize(report, JsonOptions);

    public static ValidationLeakageAuditReport? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ValidationLeakageAuditReport>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class ValidationLeakageAuditReport
{
    public ValidationLeakageAuditStatus Status { get; init; }
    public DateTime? MaximumTimestampAccessedByOptimizer { get; init; }
    public DateTime ValidationStartUtc { get; init; }
    public DateTime TrainingStartUtc { get; init; }
    public DateTime TrainingEndUtc { get; init; }
    public string OptimizerInputFingerprint { get; init; } = string.Empty;
    public IReadOnlyList<LeakageTrialAccess> TrialAccesses { get; init; } = [];
    public string? Reason { get; init; }
    public bool BlocksFreezeOrPassed { get; init; }
    public int AccessEvidenceCount { get; init; }
    public int DeniedAccessCount { get; init; }
}

public sealed class LeakageTrialAccess
{
    public int TrialNumber { get; init; }
    public DateTime RangeStartUtc { get; init; }
    public DateTime RangeEndUtc { get; init; }
    public DateTime MaximumCandleTimestampUtc { get; init; }
    public string SegmentType { get; init; } = "Training";
    public string MetricSources { get; init; } = "StrategyLabRaw";
}
