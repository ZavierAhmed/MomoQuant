namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Thrown when training code requests candles at or beyond ValidationStartUtc.
/// </summary>
public sealed class ValidationDataLeakageException : Exception
{
    public long ValidationExperimentId { get; }
    public DateTime? RequestedStartUtc { get; }
    public DateTime? RequestedEndUtc { get; }
    public DateTime ValidationBoundaryUtc { get; }
    public string CallerComponent { get; }

    public ValidationDataLeakageException(
        long validationExperimentId,
        DateTime validationBoundaryUtc,
        string callerComponent,
        DateTime? requestedStartUtc,
        DateTime? requestedEndUtc,
        string message)
        : base(message)
    {
        ValidationExperimentId = validationExperimentId;
        ValidationBoundaryUtc = DateTime.SpecifyKind(validationBoundaryUtc, DateTimeKind.Utc);
        CallerComponent = callerComponent;
        RequestedStartUtc = requestedStartUtc;
        RequestedEndUtc = requestedEndUtc;
    }
}
