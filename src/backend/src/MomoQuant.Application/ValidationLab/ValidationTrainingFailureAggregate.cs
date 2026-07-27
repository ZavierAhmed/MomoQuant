using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public enum ValidationTrainingFailurePrecedence
{
    Boundary = 1,
    AuditDurability = 2,
    TrialExecution = 3,
    Cleanup = 4
}

public enum ValidationTrainingFailureCategory
{
    Boundary,
    AuditDurability,
    TrialExecution,
    Cleanup
}

public enum ValidationTrainingFailurePhase
{
    TrialBody,
    TrialScopeFlush,
    OuterScopeFlush,
    AuditFinalization,
    CompletenessVerification,
    TrialStatusPersistence,
    ExperimentStatusPersistence,
    OperationStatusSync,
    LeaseHeartbeat,
    LeaseRelease,
    ScopeDisposal
}

public sealed class ValidationTrainingFailureRecord
{
    public string Code { get; init; } = string.Empty;
    public ValidationTrainingFailureCategory Category { get; init; }
    public ValidationTrainingFailurePrecedence Precedence { get; init; }
    public ValidationTrainingFailurePhase Phase { get; init; }
    public string UserSafeMessage { get; init; } = string.Empty;
    public string? ExceptionType { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public bool IsQualificationBlocking { get; init; }

    [JsonIgnore]
    public ExceptionDispatchInfo? DispatchInfo { get; init; }

    /// <summary>Stable logical identity: precedence + code + phase.</summary>
    [JsonIgnore]
    public string LogicalIdentity => $"{(int)Precedence}:{Code}:{Phase}";

    public static ValidationTrainingFailureRecord FromException(
        Exception exception,
        ValidationTrainingFailurePhase phase,
        string? userSafeMessage = null,
        DateTime? occurredAtUtc = null,
        ExceptionDispatchInfo? dispatchInfo = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var (code, category, precedence, blocking, safeMessage) = MapException(exception, phase, userSafeMessage);
        return new ValidationTrainingFailureRecord
        {
            Code = code,
            Category = category,
            Precedence = precedence,
            Phase = phase,
            UserSafeMessage = safeMessage,
            ExceptionType = exception.GetType().Name,
            OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow,
            IsQualificationBlocking = blocking,
            DispatchInfo = dispatchInfo ?? ExceptionDispatchInfo.Capture(exception)
        };
    }

    internal static (string Code, ValidationTrainingFailureCategory Category, ValidationTrainingFailurePrecedence Precedence, bool Blocking, string SafeMessage) MapException(
        Exception exception,
        ValidationTrainingFailurePhase phase,
        string? userSafeMessage)
    {
        // Explicit typed failures always win over phase heuristics.
        switch (exception)
        {
            case ValidationTrainingBoundaryException boundary:
                return (
                    string.IsNullOrWhiteSpace(boundary.ErrorCode)
                        ? ValidationTrainingFailureCodes.ValidationDataLeakage
                        : boundary.ErrorCode,
                    ValidationTrainingFailureCategory.Boundary,
                    ValidationTrainingFailurePrecedence.Boundary,
                    true,
                    userSafeMessage ?? ValidationTrainingFailureHandler.UserSafeLeakageMessage);
            case ValidationAccessEvidencePersistenceException:
                return (
                    ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed,
                    ValidationTrainingFailureCategory.AuditDurability,
                    ValidationTrainingFailurePrecedence.AuditDurability,
                    true,
                    userSafeMessage ?? ValidationTrainingFailureHandler.UserSafeAuditPersistenceMessage);
            case ValidationAuditExecutionIdentityMismatchException identity:
                return (
                    identity.ErrorCode,
                    ValidationTrainingFailureCategory.AuditDurability,
                    ValidationTrainingFailurePrecedence.AuditDurability,
                    true,
                    userSafeMessage ?? identity.SafeMessage);
            case ValidationAuditExecutionException audit:
                return (
                    string.IsNullOrWhiteSpace(audit.ErrorCode)
                        ? ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed
                        : audit.ErrorCode,
                    ValidationTrainingFailureCategory.AuditDurability,
                    ValidationTrainingFailurePrecedence.AuditDurability,
                    true,
                    userSafeMessage ?? ValidationTrainingFailureHandler.UserSafeAuditPersistenceMessage);
            case ValidationTrainingInsufficientWarmupException warmup:
                return (
                    ValidationTrainingFailureCodes.InsufficientWarmup,
                    ValidationTrainingFailureCategory.TrialExecution,
                    ValidationTrainingFailurePrecedence.TrialExecution,
                    true,
                    userSafeMessage
                    ?? $"Insufficient warm-up candles (available={warmup.AvailableWarmupCandleCount}, required={warmup.RequiredWarmupCandleCount}).");
        }

        // Cancellation retains phase-aware semantics (not always cleanup).
        if (exception is OperationCanceledException)
        {
            return ClassifyByPhase(
                phase,
                userSafeMessage ?? "Training operation was cancelled.",
                cancellation: true);
        }

        return ClassifyByPhase(
            phase,
            userSafeMessage ?? DefaultSafeMessageForPhase(phase),
            cancellation: false);
    }

    private static (string Code, ValidationTrainingFailureCategory Category, ValidationTrainingFailurePrecedence Precedence, bool Blocking, string SafeMessage) ClassifyByPhase(
        ValidationTrainingFailurePhase phase,
        string safeMessage,
        bool cancellation)
    {
        if (IsAuditDurabilityPhase(phase))
        {
            return (
                ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed,
                ValidationTrainingFailureCategory.AuditDurability,
                ValidationTrainingFailurePrecedence.AuditDurability,
                true,
                safeMessage);
        }

        if (IsCleanupPhase(phase))
        {
            return (
                ValidationTrainingFailureCodes.TrainingCleanupFailed,
                ValidationTrainingFailureCategory.Cleanup,
                ValidationTrainingFailurePrecedence.Cleanup,
                false,
                safeMessage);
        }

        // TrialBody (and any unspecified non-cleanup phase) => trial execution.
        return (
            cancellation
                ? ValidationTrainingFailureCodes.TrialExecutionFailed
                : ValidationTrainingFailureCodes.TrialExecutionFailed,
            ValidationTrainingFailureCategory.TrialExecution,
            ValidationTrainingFailurePrecedence.TrialExecution,
            false,
            safeMessage);
    }

    public static bool IsAuditDurabilityPhase(ValidationTrainingFailurePhase phase) =>
        phase is ValidationTrainingFailurePhase.TrialScopeFlush
            or ValidationTrainingFailurePhase.OuterScopeFlush
            or ValidationTrainingFailurePhase.AuditFinalization
            or ValidationTrainingFailurePhase.CompletenessVerification;

    public static bool IsCleanupPhase(ValidationTrainingFailurePhase phase) =>
        phase is ValidationTrainingFailurePhase.TrialStatusPersistence
            or ValidationTrainingFailurePhase.ExperimentStatusPersistence
            or ValidationTrainingFailurePhase.OperationStatusSync
            or ValidationTrainingFailurePhase.LeaseHeartbeat
            or ValidationTrainingFailurePhase.LeaseRelease
            or ValidationTrainingFailurePhase.ScopeDisposal;

    private static string DefaultSafeMessageForPhase(ValidationTrainingFailurePhase phase) =>
        IsCleanupPhase(phase)
            ? ValidationTrainingFailureHandler.UserSafeCleanupMessage
            : IsAuditDurabilityPhase(phase)
                ? ValidationTrainingFailureHandler.UserSafeAuditPersistenceMessage
                : "Validation training trial execution failed.";
}

public sealed class ValidationTrainingFailureAggregate
{
    private readonly List<ValidationTrainingFailureRecord> _failures = new();

    public IReadOnlyList<ValidationTrainingFailureRecord> AllFailures =>
        _failures
            .OrderBy(f => f.Precedence)
            .ThenBy(f => f.OccurredAtUtc)
            .ToList();

    public ValidationTrainingFailureRecord? PrimaryFailure => AllFailures.FirstOrDefault();

    public bool HasBoundaryFailure =>
        _failures.Any(f => f.Category == ValidationTrainingFailureCategory.Boundary);

    public bool HasAuditDurabilityFailure =>
        _failures.Any(f => f.Category == ValidationTrainingFailureCategory.AuditDurability);

    public bool HasTrialExecutionFailure =>
        _failures.Any(f => f.Category == ValidationTrainingFailureCategory.TrialExecution);

    public bool HasCleanupFailure =>
        _failures.Any(f => f.Category == ValidationTrainingFailureCategory.Cleanup);

    public bool IsQualificationBlocking => _failures.Any(f => f.IsQualificationBlocking);

    public bool HasAnyFailure => _failures.Count > 0;

    public void Observe(
        Exception exception,
        ValidationTrainingFailurePhase phase,
        string? userSafeMessage = null,
        DateTime? occurredAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Observe(ValidationTrainingFailureRecord.FromException(exception, phase, userSafeMessage, occurredAtUtc));
    }

    public void Observe(ValidationTrainingFailureRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (_failures.Any(f => f.LogicalIdentity == record.LogicalIdentity))
        {
            return;
        }

        _failures.Add(record);
    }

    public void ObserveDispatchInfo(
        ExceptionDispatchInfo dispatchInfo,
        ValidationTrainingFailurePhase phase,
        string? userSafeMessage = null)
    {
        ArgumentNullException.ThrowIfNull(dispatchInfo);
        Observe(ValidationTrainingFailureRecord.FromException(
            dispatchInfo.SourceException,
            phase,
            userSafeMessage,
            dispatchInfo: dispatchInfo));
    }

    public void MergeFrom(ValidationTrainingFailureAggregate? other)
    {
        if (other is null)
        {
            return;
        }

        foreach (var failure in other.AllFailures)
        {
            Observe(failure);
        }
    }

    public void MergeFromExistingJson(string? failureReasonsJson)
    {
        foreach (var record in ValidationTrainingFailureJson.ParseRecords(failureReasonsJson))
        {
            Observe(record);
        }
    }

    /// <summary>Rethrows the exception belonging to the selected primary record.</summary>
    public ExceptionDispatchInfo? SelectPrimaryDispatchInfo() =>
        PrimaryFailure?.DispatchInfo;

    public void ThrowPrimary()
    {
        var dispatch = SelectPrimaryDispatchInfo();
        if (dispatch is not null)
        {
            dispatch.Throw();
        }
    }

    [Obsolete("Use ThrowPrimary() which rethrows the DispatchInfo associated with the primary record.")]
    public void ThrowPrimary(
        ExceptionDispatchInfo? bodyException,
        ExceptionDispatchInfo? flushException,
        ExceptionDispatchInfo? cleanupException = null)
    {
        var primary = SelectPrimaryDispatchInfo();
        if (primary is not null)
        {
            primary.Throw();
        }

        (bodyException ?? flushException ?? cleanupException)?.Throw();
    }
}

public static class ValidationTrainingFailureCodes
{
    public const string ValidationDataLeakage = "VALIDATION_DATA_LEAKAGE";
    public const string ValidationAccessAuditPersistenceFailed = "VALIDATION_ACCESS_AUDIT_PERSISTENCE_FAILED";
    public const string InsufficientWarmup = "VALIDATION_INSUFFICIENT_WARMUP";
    public const string TrialExecutionFailed = "VALIDATION_TRIAL_EXECUTION_FAILED";
    public const string TrainingCleanupFailed = "VALIDATION_TRAINING_CLEANUP_FAILED";
}

public static class ValidationTrainingFailureJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string SerializeRecords(IReadOnlyList<ValidationTrainingFailureRecord> records) =>
        JsonSerializer.Serialize(records.OrderBy(r => r.Precedence).ThenBy(r => r.OccurredAtUtc), Options);

    public static IReadOnlyList<ValidationTrainingFailureRecord> ParseRecords(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ValidationTrainingFailureRecord>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<ValidationTrainingFailureRecord>();
            }

            var list = new List<ValidationTrainingFailureRecord>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var code = element.GetString();
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        continue;
                    }

                    list.Add(LegacyCodeToRecord(code));
                    continue;
                }

                if (element.ValueKind == JsonValueKind.Object)
                {
                    var record = JsonSerializer.Deserialize<ValidationTrainingFailureRecord>(element.GetRawText(), Options);
                    if (record is not null && !string.IsNullOrWhiteSpace(record.Code))
                    {
                        list.Add(record);
                    }
                }
            }

            return list;
        }
        catch
        {
            return
            [
                new ValidationTrainingFailureRecord
                {
                    Code = ValidationTrainingFailureCodes.TrainingCleanupFailed,
                    Category = ValidationTrainingFailureCategory.Cleanup,
                    Precedence = ValidationTrainingFailurePrecedence.Cleanup,
                    Phase = ValidationTrainingFailurePhase.ExperimentStatusPersistence,
                    UserSafeMessage = "Stored failure reasons could not be parsed; training remains non-qualified.",
                    OccurredAtUtc = DateTime.UtcNow,
                    IsQualificationBlocking = true
                }
            ];
        }
    }

    private static ValidationTrainingFailureRecord LegacyCodeToRecord(string code, string? message = null)
    {
        var (category, precedence, blocking, safeMessage, phase) = code switch
        {
            ValidationTrainingFailureCodes.ValidationDataLeakage => (
                ValidationTrainingFailureCategory.Boundary,
                ValidationTrainingFailurePrecedence.Boundary,
                true,
                message ?? ValidationTrainingFailureHandler.UserSafeLeakageMessage,
                ValidationTrainingFailurePhase.TrialBody),
            ValidationTrainingFailureCodes.ValidationAccessAuditPersistenceFailed => (
                ValidationTrainingFailureCategory.AuditDurability,
                ValidationTrainingFailurePrecedence.AuditDurability,
                true,
                message ?? ValidationTrainingFailureHandler.UserSafeAuditPersistenceMessage,
                ValidationTrainingFailurePhase.AuditFinalization),
            ValidationTrainingFailureCodes.InsufficientWarmup => (
                ValidationTrainingFailureCategory.TrialExecution,
                ValidationTrainingFailurePrecedence.TrialExecution,
                true,
                message ?? "Insufficient warm-up candles for training.",
                ValidationTrainingFailurePhase.TrialBody),
            ValidationTrainingFailureCodes.TrainingCleanupFailed => (
                ValidationTrainingFailureCategory.Cleanup,
                ValidationTrainingFailurePrecedence.Cleanup,
                false,
                message ?? ValidationTrainingFailureHandler.UserSafeCleanupMessage,
                ValidationTrainingFailurePhase.LeaseRelease),
            _ => (
                ValidationTrainingFailureCategory.TrialExecution,
                ValidationTrainingFailurePrecedence.TrialExecution,
                false,
                message ?? "Validation training failed.",
                ValidationTrainingFailurePhase.TrialBody)
        };

        return new ValidationTrainingFailureRecord
        {
            Code = code,
            Category = category,
            Precedence = precedence,
            Phase = phase,
            UserSafeMessage = safeMessage,
            OccurredAtUtc = DateTime.UtcNow,
            IsQualificationBlocking = blocking
        };
    }
}

public static class ValidationTrainingFailurePersistence
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Merges only authoritative <see cref="ValidationExperiment.FailureReasonsJson"/>.
    /// DiagnosticsJson is a presentation channel and must not seed the aggregate.
    /// </summary>
    public static ValidationTrainingFailureAggregate MergeExisting(string? failureReasonsJson)
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        aggregate.MergeFromExistingJson(failureReasonsJson);
        return aggregate;
    }

    [Obsolete("DiagnosticsJson must not seed the authoritative aggregate. Use MergeExisting(failureReasonsJson).")]
    public static ValidationTrainingFailureAggregate MergeExisting(
        string? failureReasonsJson,
        string? diagnosticsJson)
    {
        _ = diagnosticsJson;
        return MergeExisting(failureReasonsJson);
    }

    public static void ApplyToExperiment(ValidationExperiment experiment, ValidationTrainingFailureAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentNullException.ThrowIfNull(aggregate);

        var wasIneligible = experiment.IsQualificationCapable == false;
        var merged = MergeExisting(experiment.FailureReasonsJson);
        merged.MergeFrom(aggregate);

        var ordered = merged.AllFailures;
        experiment.PrimaryFailureReason = ordered.FirstOrDefault()?.Code;
        experiment.FailureReasonsJson = ValidationTrainingFailureJson.SerializeRecords(ordered);

        // Any durable failure (including cleanup-only) leaves the experiment non-qualified.
        // Never flip an already-ineligible experiment back to capable.
        if (wasIneligible || merged.HasAnyFailure || merged.IsQualificationBlocking)
        {
            experiment.IsQualificationCapable = false;
        }

        foreach (var failure in ordered)
        {
            AppendSafeDiagnostic(experiment, failure.Code, failure.UserSafeMessage, failure.OccurredAtUtc);
        }
    }

    public static void ApplyTrialWarnings(ValidationParameterTrial trial, ValidationTrainingFailureAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(trial);
        ArgumentNullException.ThrowIfNull(aggregate);

        var existing = ValidationTrainingFailureJson.ParseRecords(trial.DiagnosticWarningsJson);
        var merged = new ValidationTrainingFailureAggregate();
        foreach (var record in existing)
        {
            merged.Observe(record);
        }

        merged.MergeFrom(aggregate);
        trial.DiagnosticWarningsJson = ValidationTrainingFailureJson.SerializeRecords(merged.AllFailures);
    }

    public static void AppendRankIneligibleReasons(
        ValidationParameterTrial trial,
        IReadOnlyCollection<string> codes)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(trial.RankIneligibleReasonsJson))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<string[]>(trial.RankIneligibleReasonsJson);
                if (existing is not null)
                {
                    foreach (var code in existing)
                    {
                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            set.Add(code);
                        }
                    }
                }
            }
            catch
            {
                // ignore malformed
            }
        }

        foreach (var code in codes)
        {
            if (!string.IsNullOrWhiteSpace(code))
            {
                set.Add(code);
            }
        }

        trial.RankIneligibleReasonsJson = JsonSerializer.Serialize(set.OrderBy(c => c, StringComparer.Ordinal), JsonOptions);
    }

    private static void AppendSafeDiagnostic(
        ValidationExperiment experiment,
        string code,
        string message,
        DateTime occurredAtUtc)
    {
        var list = new List<object>();
        try
        {
            var existing = JsonSerializer.Deserialize<List<JsonElement>>(
                string.IsNullOrWhiteSpace(experiment.DiagnosticsJson) ? "[]" : experiment.DiagnosticsJson);
            if (existing is not null)
            {
                foreach (var el in existing)
                {
                    list.Add(JsonSerializer.Deserialize<object>(el.GetRawText())!);
                }
            }
        }
        catch
        {
            // start fresh
        }

        if (list.Any(item => item is JsonElement el
                && el.ValueKind == JsonValueKind.Object
                && el.TryGetProperty("code", out var codeEl)
                && string.Equals(codeEl.GetString(), code, StringComparison.Ordinal)))
        {
            experiment.DiagnosticsJson = JsonSerializer.Serialize(list, JsonOptions);
            return;
        }

        list.Add(new
        {
            code,
            message,
            atUtc = occurredAtUtc
        });
        experiment.DiagnosticsJson = JsonSerializer.Serialize(list, JsonOptions);
    }
}
