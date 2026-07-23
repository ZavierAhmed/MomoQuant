using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Research;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public static class ValidationTrainingFailureCodes
{
    public const string ValidationDataLeakage = "VALIDATION_DATA_LEAKAGE";
}

public sealed class ValidationTrainingFailureHandleResult
{
    public string ErrorCode { get; init; } = ValidationTrainingFailureCodes.ValidationDataLeakage;
    public string UserSafeErrorMessage { get; init; } = string.Empty;
}

/// <summary>
/// Owns production status transitions for training boundary / leakage failures.
/// </summary>
public interface IValidationTrainingFailureHandler
{
    /// <summary>
    /// Persists pending access evidence, marks trial/experiment failed for leakage,
    /// invalidates tentative selection, and writes safe operation-status diagnostics.
    /// Does not expose stack traces or candle contents in user-facing errors.
    /// </summary>
    Task<ValidationTrainingFailureHandleResult> HandleBoundaryFailureAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        IValidationTrainingCandleScope scope,
        Exception exception,
        string? optimizerInputFingerprint = null,
        string? leaseOwner = null,
        CancellationToken cancellationToken = default);
}

public sealed class ValidationTrainingFailureHandler : IValidationTrainingFailureHandler
{
    public const string UserSafeLeakageMessage =
        "Validation data leakage was detected during training. Training stopped and access evidence was recorded for audit.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IValidationCandleAccessRecorder _recorder;
    private readonly IValidationCandleAccessAuditRepository _audits;
    private readonly IValidationParameterTrialRepository _trials;
    private readonly IValidationExperimentRepository _experiments;
    private readonly IValidationLeakageAuditor _leakageAuditor;
    private readonly IResearchOperationStatusService _operationStatus;

    public ValidationTrainingFailureHandler(
        IValidationCandleAccessRecorder recorder,
        IValidationCandleAccessAuditRepository audits,
        IValidationParameterTrialRepository trials,
        IValidationExperimentRepository experiments,
        IValidationLeakageAuditor leakageAuditor,
        IResearchOperationStatusService operationStatus)
    {
        _recorder = recorder;
        _audits = audits;
        _trials = trials;
        _experiments = experiments;
        _leakageAuditor = leakageAuditor;
        _operationStatus = operationStatus;
    }

    public async Task<ValidationTrainingFailureHandleResult> HandleBoundaryFailureAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        IValidationTrainingCandleScope scope,
        Exception exception,
        string? optimizerInputFingerprint = null,
        string? leaseOwner = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentNullException.ThrowIfNull(trial);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(exception);

        var errorCode = ValidationTrainingFailureCodes.ValidationDataLeakage;
        var userSafe = UserSafeLeakageMessage;

        // 1. Persist pending access records (idempotent; may already be flushed by ExecuteTrialAsync).
        await _recorder.FlushAsync(scope, cancellationToken).ConfigureAwait(false);

        // 2. Mark trial LeakageFailed with code VALIDATION_DATA_LEAKAGE.
        trial.Status = ValidationTrialStatus.LeakageFailed;
        trial.ErrorMessage = userSafe;
        trial.CompletedAtUtc = DateTime.UtcNow;
        trial.DiagnosticWarningsJson = JsonSerializer.Serialize(
            new[] { new { code = errorCode, message = userSafe } },
            JsonOptions);
        await _trials.UpdateAsync(trial, cancellationToken).ConfigureAwait(false);

        // 3–4. Experiment leakage failure + invalidate tentative selection / ranking.
        InvalidateTentativeSelection(experiment);
        experiment.LeakageAuditStatus = ValidationLeakageAuditStatus.Failed;
        experiment.CurrentStage = "LeakageDetected";
        experiment.Status = ValidationExperimentStatus.Failed;
        experiment.ErrorMessage = userSafe;
        experiment.PrimaryFailureReason = errorCode;
        experiment.FailureReasonsJson = JsonSerializer.Serialize(new[] { errorCode }, JsonOptions);
        experiment.IsQualificationCapable = false;
        experiment.DecidedAtUtc = DateTime.UtcNow;
        experiment.UpdatedAtUtc = DateTime.UtcNow;

        AppendSafeDiagnostic(experiment, errorCode, userSafe);

        if (experiment.ValidationStartUtc is not null
            && experiment.TrainingStartUtc is not null
            && experiment.TrainingEndUtc is not null)
        {
            var audits = await _audits.GetByExperimentIdAsync(experiment.Id, cancellationToken)
                .ConfigureAwait(false);
            var leakage = _leakageAuditor.EvaluateFromAccessEvidence(
                audits,
                experiment.ValidationStartUtc.Value,
                experiment.TrainingStartUtc.Value,
                experiment.TrainingEndUtc.Value,
                optimizerInputFingerprint ?? string.Empty);
            experiment.LeakageAuditJson = _leakageAuditor.Serialize(leakage);
            experiment.LeakageAuditStatus = ValidationLeakageAuditStatus.Failed;
        }

        await _experiments.UpdateAsync(experiment, cancellationToken).ConfigureAwait(false);

        // 5. Persist safe operation-status diagnostics (no stack traces / candle contents).
        var progress = ValidationTrainingProgressCalculator.Calculate(
            experiment,
            await _trials.GetByExperimentIdAsync(experiment.Id, cancellationToken).ConfigureAwait(false),
            generatedTrialCount: experiment.MaximumTrials);
        await _operationStatus.SyncFromValidationTrainingAsync(
            experiment.Id,
            status: ValidationExperimentStatus.Failed.ToString(),
            stage: "LeakageDetected",
            progress,
            leaseOwner: leaseOwner,
            errorCode: errorCode,
            userSafeError: userSafe,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new ValidationTrainingFailureHandleResult
        {
            ErrorCode = errorCode,
            UserSafeErrorMessage = userSafe
        };
    }

    private static void InvalidateTentativeSelection(ValidationExperiment experiment)
    {
        experiment.SelectedTrialId = null;
        experiment.SelectedTrialNumber = null;
        experiment.SelectedTrialParameterSnapshotJson = null;
        experiment.SelectedTrialParameterFingerprint = null;
        experiment.TrainingStrategyLabRunId = null;
        experiment.ValidationStrategyLabRunId = null;
        experiment.FrozenStrategyParameterSnapshotJson = null;
        experiment.FrozenParameterFingerprint = null;
        experiment.FrozenAtUtc = null;
        experiment.SelectionIntegrityStatus = ValidationSelectionIntegrityStatus.NoEligibleTrial;
    }

    private static void AppendSafeDiagnostic(ValidationExperiment experiment, string code, string message)
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

        list.Add(new
        {
            code,
            message,
            atUtc = DateTime.UtcNow
        });
        experiment.DiagnosticsJson = JsonSerializer.Serialize(list, JsonOptions);
    }
}
