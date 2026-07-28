using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Milestone 23.0E2C3A / E2C3A1 — fail-closed wrappers for authoritative audit qualification
/// evaluation and the repository reads required to reach those gates.
/// Never exposes raw exception, SQL, connection, or payload details to callers.
/// </summary>
public static class ValidationAuthoritativeEvaluationSafety
{
    public static ValidationTrainingFailurePhase ClassifyExceptionPhase(Exception exception) =>
        exception is ValidationAuditCompletenessVerificationException
            ? ValidationTrainingFailurePhase.CompletenessVerification
            : ValidationTrainingFailurePhase.AuditFinalization;

    public static ValidationTrainingFailureAggregate ObserveEvaluatorException(
        ValidationExperiment experiment,
        Exception exception,
        ValidationTrainingFailurePhase? phase = null)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentNullException.ThrowIfNull(exception);

        var aggregate = ValidationTrainingFailurePersistence.MergeExisting(experiment.FailureReasonsJson);
        aggregate.Observe(exception, phase ?? ClassifyExceptionPhase(exception));
        return aggregate;
    }

    /// <summary>
    /// Repository / evidence-loading failures always classify as AuditDurability / AuditFinalization.
    /// Phase is structural — never derived from exception messages.
    /// </summary>
    public static ValidationTrainingFailureAggregate ObserveRepositoryException(
        ValidationExperiment experiment,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentNullException.ThrowIfNull(exception);

        var aggregate = ValidationTrainingFailurePersistence.MergeExisting(experiment.FailureReasonsJson);
        aggregate.Observe(
            exception,
            ValidationTrainingFailurePhase.AuditFinalization,
            ValidationTrainingFailureHandler.UserSafeAuditPersistenceMessage);
        return aggregate;
    }

    public static async Task<(bool Succeeded, ValidationAuthoritativeAuditQualificationResult? Evaluation, ValidationTrainingFailureAggregate? FailureAggregate)> TryEvaluateTrialAsync(
        IValidationAuthoritativeAuditQualificationEvaluator evaluator,
        ValidationExperiment experiment,
        ValidationParameterTrial trial,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentNullException.ThrowIfNull(trial);

        try
        {
            var evaluation = await evaluator.EvaluateTrialAsync(experiment, trial, cancellationToken)
                .ConfigureAwait(false);
            return (true, evaluation, null);
        }
        catch (Exception ex)
        {
            return (false, null, ObserveEvaluatorException(experiment, ex));
        }
    }

    public static async Task<(bool Succeeded, IReadOnlyList<ValidationAuthoritativeAuditQualificationResult>? Results, ValidationTrainingFailureAggregate? FailureAggregate)> TryRevalidatePopulationAsync(
        IValidationAuthoritativeAuditQualificationEvaluator evaluator,
        ValidationExperiment experiment,
        IList<ValidationParameterTrial> trials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentNullException.ThrowIfNull(trials);

        try
        {
            var results = await evaluator.RevalidatePopulationAsync(experiment, trials, cancellationToken)
                .ConfigureAwait(false);
            return (true, results, null);
        }
        catch (Exception ex)
        {
            return (false, null, ObserveEvaluatorException(experiment, ex));
        }
    }

    public static async Task<(bool Succeeded, IReadOnlyList<ValidationParameterTrial>? Trials, ValidationTrainingFailureAggregate? FailureAggregate)> TryGetTrialsByExperimentIdAsync(
        IValidationParameterTrialRepository trials,
        ValidationExperiment experiment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trials);
        ArgumentNullException.ThrowIfNull(experiment);

        try
        {
            var loaded = await trials.GetByExperimentIdAsync(experiment.Id, cancellationToken)
                .ConfigureAwait(false);
            return (true, loaded, null);
        }
        catch (Exception ex)
        {
            return (false, null, ObserveRepositoryException(experiment, ex));
        }
    }

    public static async Task<(bool Succeeded, ValidationParameterTrial? Trial, ValidationTrainingFailureAggregate? FailureAggregate)> TryGetTrialByFingerprintAsync(
        IValidationParameterTrialRepository trials,
        ValidationExperiment experiment,
        string parameterFingerprint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trials);
        ArgumentNullException.ThrowIfNull(experiment);

        try
        {
            var trial = await trials.GetByExperimentAndFingerprintAsync(
                    experiment.Id, parameterFingerprint, cancellationToken)
                .ConfigureAwait(false);
            return (true, trial, null);
        }
        catch (Exception ex)
        {
            return (false, null, ObserveRepositoryException(experiment, ex));
        }
    }
}
