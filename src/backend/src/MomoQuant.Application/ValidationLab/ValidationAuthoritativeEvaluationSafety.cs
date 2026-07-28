using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Milestone 23.0E2C3A — fail-closed wrappers for authoritative audit qualification evaluation.
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
}
