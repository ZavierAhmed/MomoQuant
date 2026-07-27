using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Milestone 23.0E2C3 — typed result of authoritative audit qualification evaluation.
/// Cached trial/experiment status fields are never sufficient proof by themselves.
/// </summary>
public sealed class ValidationAuthoritativeAuditQualificationResult
{
    public bool IsApplicable { get; init; }
    public long TrialId { get; init; }
    public Guid? AuditExecutionId { get; init; }
    public Guid? ScopeExecutionId { get; init; }
    public int? AttemptNumber { get; init; }
    public ValidationAuditExecutionStatus? AuthoritativeStatus { get; init; }
    public ValidationAuditCompletionStatus TrialAuditCompletionStatus { get; init; }
    public ValidationAuditCompletenessCode CompletenessCode { get; init; }
    public bool IsQualificationEligible { get; init; }
    public string? UserSafeBlockingReason { get; init; }
    public ValidationAuditCompletenessResult? Completeness { get; init; }

    public static ValidationAuthoritativeAuditQualificationResult NotApplicable(long trialId) => new()
    {
        IsApplicable = false,
        TrialId = trialId,
        CompletenessCode = ValidationAuditCompletenessCode.HistoricalNotEvaluated,
        IsQualificationEligible = false,
        UserSafeBlockingReason = null
    };

    public static ValidationAuthoritativeAuditQualificationResult Blocked(
        long trialId,
        ValidationAuditCompletenessCode code,
        string userSafeReason,
        Guid? auditExecutionId = null,
        Guid? scopeExecutionId = null,
        int? attemptNumber = null,
        ValidationAuditExecutionStatus? authoritativeStatus = null,
        ValidationAuditCompletionStatus trialAuditCompletionStatus = ValidationAuditCompletionStatus.NotEvaluated,
        ValidationAuditCompletenessResult? completeness = null) =>
        new()
        {
            IsApplicable = true,
            TrialId = trialId,
            AuditExecutionId = auditExecutionId,
            ScopeExecutionId = scopeExecutionId,
            AttemptNumber = attemptNumber,
            AuthoritativeStatus = authoritativeStatus,
            TrialAuditCompletionStatus = trialAuditCompletionStatus,
            CompletenessCode = code,
            IsQualificationEligible = false,
            UserSafeBlockingReason = userSafeReason,
            Completeness = completeness
        };
}

public interface IValidationAuthoritativeAuditQualificationEvaluator
{
    /// <summary>
    /// Reloads durable authoritative audit evidence for a trial and verifier-confirms completion.
    /// Returns NotApplicable when the experiment type does not require training-audit qualification.
    /// </summary>
    Task<ValidationAuthoritativeAuditQualificationResult> EvaluateTrialAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revalidates trials that claim completed/passed status, mutates ineligible markers in memory,
    /// and returns whether any applicable trial remains qualification-eligible after verification.
    /// </summary>
    Task<IReadOnlyList<ValidationAuthoritativeAuditQualificationResult>> RevalidatePopulationAsync(
        ValidationExperiment experiment,
        IList<ValidationParameterTrial> trials,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Single production evaluator for Milestone 23.0E2C3 qualification gates.
/// Does not duplicate completeness logic — delegates to <see cref="IValidationAuditCompletenessVerifier"/>.
/// </summary>
public sealed class ValidationAuthoritativeAuditQualificationEvaluator
    : IValidationAuthoritativeAuditQualificationEvaluator
{
    public const string RankIneligibleReasonCode = "VALIDATION_AUDIT_AUTHORITATIVE_INCOMPLETE";
    public const string UserSafeIncompleteMessage =
        "Authoritative validation audit evidence is incomplete or not currently verifier-confirmed. Ranking, selection, freeze, and qualification remain blocked.";

    private readonly IValidationAuditExecutionRepository _executions;
    private readonly IValidationAuditBatchRepository _batches;
    private readonly IValidationCandleAccessAuditRepository _accessAudits;
    private readonly IValidationAuditCompletenessVerifier _verifier;

    public ValidationAuthoritativeAuditQualificationEvaluator(
        IValidationAuditExecutionRepository executions,
        IValidationAuditBatchRepository batches,
        IValidationCandleAccessAuditRepository accessAudits,
        IValidationAuditCompletenessVerifier verifier)
    {
        _executions = executions;
        _batches = batches;
        _accessAudits = accessAudits;
        _verifier = verifier;
    }

    public async Task<ValidationAuthoritativeAuditQualificationResult> EvaluateTrialAsync(
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentNullException.ThrowIfNull(trial);

        if (!IsTrainingAuditQualificationApplicable(experiment))
        {
            return ValidationAuthoritativeAuditQualificationResult.NotApplicable(trial.Id);
        }

        if (trial.AuthoritativeAuditExecutionId is null)
        {
            var code = trial.AuditCompletionStatus == ValidationAuditCompletionStatus.NotEvaluated
                ? ValidationAuditCompletenessCode.HistoricalNotEvaluated
                : ValidationAuditCompletenessCode.ExecutionMissing;
            return ValidationAuthoritativeAuditQualificationResult.Blocked(
                trial.Id,
                code,
                UserSafeIncompleteMessage,
                trialAuditCompletionStatus: trial.AuditCompletionStatus);
        }

        var execution = await _executions
            .GetByAuditExecutionIdAsync(trial.AuthoritativeAuditExecutionId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (execution is null)
        {
            return ValidationAuthoritativeAuditQualificationResult.Blocked(
                trial.Id,
                ValidationAuditCompletenessCode.ExecutionMissing,
                UserSafeIncompleteMessage,
                auditExecutionId: trial.AuthoritativeAuditExecutionId,
                trialAuditCompletionStatus: trial.AuditCompletionStatus);
        }

        if (execution.ValidationTrialId != trial.Id
            || execution.ValidationExperimentId != experiment.Id)
        {
            return ValidationAuthoritativeAuditQualificationResult.Blocked(
                trial.Id,
                ValidationAuditCompletenessCode.ScopeIdentityMismatch,
                UserSafeIncompleteMessage,
                auditExecutionId: execution.AuditExecutionId,
                scopeExecutionId: execution.ScopeExecutionId,
                attemptNumber: execution.AttemptNumber,
                authoritativeStatus: execution.Status,
                trialAuditCompletionStatus: trial.AuditCompletionStatus);
        }

        if (!string.Equals(
                execution.AuditContractVersion,
                ValidationAuditExecution.ContractVersionV1,
                StringComparison.Ordinal))
        {
            return ValidationAuthoritativeAuditQualificationResult.Blocked(
                trial.Id,
                ValidationAuditCompletenessCode.NotAuthoritative,
                UserSafeIncompleteMessage,
                auditExecutionId: execution.AuditExecutionId,
                scopeExecutionId: execution.ScopeExecutionId,
                attemptNumber: execution.AttemptNumber,
                authoritativeStatus: execution.Status,
                trialAuditCompletionStatus: trial.AuditCompletionStatus);
        }

        if (execution.Status is ValidationAuditExecutionStatus.Superseded
            or ValidationAuditExecutionStatus.Failed)
        {
            return ValidationAuthoritativeAuditQualificationResult.Blocked(
                trial.Id,
                execution.Status == ValidationAuditExecutionStatus.Superseded
                    ? ValidationAuditCompletenessCode.Superseded
                    : ValidationAuditCompletenessCode.RecoveryRequired,
                UserSafeIncompleteMessage,
                auditExecutionId: execution.AuditExecutionId,
                scopeExecutionId: execution.ScopeExecutionId,
                attemptNumber: execution.AttemptNumber,
                authoritativeStatus: execution.Status,
                trialAuditCompletionStatus: trial.AuditCompletionStatus);
        }

        var batches = await _batches
            .GetByAuditExecutionIdAsync(execution.AuditExecutionId, cancellationToken)
            .ConfigureAwait(false);
        var accessRows = (await _accessAudits
                .GetByExperimentIdAsync(experiment.Id, cancellationToken)
                .ConfigureAwait(false))
            .Where(r => r.ScopeExecutionId == execution.ScopeExecutionId)
            .ToList();

        var completeness = _verifier.Verify(trial, execution, batches, accessRows);
        if (!completeness.IsComplete || !completeness.IsAuthoritative)
        {
            return ValidationAuthoritativeAuditQualificationResult.Blocked(
                trial.Id,
                completeness.CompletionCode,
                UserSafeIncompleteMessage,
                auditExecutionId: execution.AuditExecutionId,
                scopeExecutionId: execution.ScopeExecutionId,
                attemptNumber: execution.AttemptNumber,
                authoritativeStatus: execution.Status,
                trialAuditCompletionStatus: trial.AuditCompletionStatus,
                completeness: completeness);
        }

        return new ValidationAuthoritativeAuditQualificationResult
        {
            IsApplicable = true,
            TrialId = trial.Id,
            AuditExecutionId = execution.AuditExecutionId,
            ScopeExecutionId = execution.ScopeExecutionId,
            AttemptNumber = execution.AttemptNumber,
            AuthoritativeStatus = execution.Status,
            TrialAuditCompletionStatus = trial.AuditCompletionStatus,
            CompletenessCode = completeness.CompletionCode,
            IsQualificationEligible = true,
            Completeness = completeness
        };
    }

    public async Task<IReadOnlyList<ValidationAuthoritativeAuditQualificationResult>> RevalidatePopulationAsync(
        ValidationExperiment experiment,
        IList<ValidationParameterTrial> trials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentNullException.ThrowIfNull(trials);

        var results = new List<ValidationAuthoritativeAuditQualificationResult>(trials.Count);
        if (!IsTrainingAuditQualificationApplicable(experiment))
        {
            foreach (var trial in trials)
            {
                results.Add(ValidationAuthoritativeAuditQualificationResult.NotApplicable(trial.Id));
            }

            return results;
        }

        foreach (var trial in trials)
        {
            var result = await EvaluateTrialAsync(experiment, trial, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            ApplyPopulationMarker(trial, result);
        }

        return results;
    }

    public static bool IsTrainingAuditQualificationApplicable(ValidationExperiment experiment) =>
        experiment.ExperimentType != ValidationExperimentType.ValidateExistingFrozenConfiguration;

    /// <summary>
    /// Cached-field gate used by ranking/selection population filters.
    /// Necessary but not sufficient — production paths must also revalidate via the evaluator.
    /// </summary>
    public static bool MeetsCachedAuditEligibilityFields(ValidationParameterTrial trial) =>
        trial.Status == ValidationTrialStatus.Completed
        && string.Equals(trial.GuardrailDecision, "Passed", StringComparison.OrdinalIgnoreCase)
        && trial.AuthoritativeAuditExecutionId is not null
        && trial.AuditCompletionStatus == ValidationAuditCompletionStatus.Complete;

    public static bool IsGuardrailPassedCompleted(ValidationParameterTrial trial) =>
        trial.Status == ValidationTrialStatus.Completed
        && string.Equals(trial.GuardrailDecision, "Passed", StringComparison.OrdinalIgnoreCase);

    public static void ApplyPopulationMarker(
        ValidationParameterTrial trial,
        ValidationAuthoritativeAuditQualificationResult result)
    {
        if (!result.IsApplicable)
        {
            return;
        }

        if (result.IsQualificationEligible)
        {
            return;
        }

        trial.Rank = null;
        if (trial.TrialRankEligibility == ValidationTrialRankEligibility.Eligible)
        {
            trial.TrialRankEligibility = ValidationTrialRankEligibility.Ineligible;
        }

        if (trial.AuditCompletionStatus == ValidationAuditCompletionStatus.Complete)
        {
            trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired;
        }

        ValidationTrainingFailurePersistence.AppendRankIneligibleReasons(
            trial,
            [RankIneligibleReasonCode, result.CompletenessCode.ToString()]);
    }
}
