namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Thrown when training code requests candles at or beyond ValidationStartUtc.
/// </summary>
public sealed class ValidationDataLeakageException : ValidationTrainingBoundaryException
{
    public ValidationDataLeakageException(
        long validationExperimentId,
        DateTime validationBoundaryUtc,
        string callerComponent,
        DateTime? requestedStartUtc,
        DateTime? requestedEndUtc,
        string message)
        : base(
            validationExperimentId,
            validationBoundaryUtc,
            callerComponent,
            requestedStartUtc,
            requestedEndUtc,
            message)
    {
    }
}
