using System.Runtime.ExceptionServices;

namespace MomoQuant.Application.ValidationLab;

public sealed class ValidationTrainingScopeExecutionResult
{
    public ExceptionDispatchInfo? BodyException { get; init; }
    public ExceptionDispatchInfo? FlushException { get; init; }
    public ValidationTrainingFailurePhase BodyPhase { get; init; } = ValidationTrainingFailurePhase.TrialBody;
    public ValidationTrainingFailurePhase FlushPhase { get; init; } = ValidationTrainingFailurePhase.TrialScopeFlush;
    public bool FlushAttempted { get; init; }
    public bool BodySucceeded => BodyException is null;
    public bool FlushSucceeded => !FlushAttempted || FlushException is null;
    public bool IsSuccess => BodySucceeded && FlushSucceeded;

    public ValidationTrainingFailureAggregate ToFailureAggregate()
    {
        var aggregate = new ValidationTrainingFailureAggregate();
        if (BodyException is not null)
        {
            aggregate.ObserveDispatchInfo(BodyException, BodyPhase);
        }

        if (FlushException is not null)
        {
            aggregate.ObserveDispatchInfo(FlushException, FlushPhase);
        }

        return aggregate;
    }

    public void ThrowIfFailed()
    {
        if (IsSuccess)
        {
            return;
        }

        ToFailureAggregate().ThrowPrimary(BodyException, FlushException);
    }
}
