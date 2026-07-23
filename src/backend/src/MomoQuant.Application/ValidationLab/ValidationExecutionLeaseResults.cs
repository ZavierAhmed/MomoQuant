namespace MomoQuant.Application.ValidationLab;

public enum ValidationLeaseOperationStatus
{
    Succeeded = 0,
    Conflict = 1,
    NotFound = 2
}

public sealed record ValidationLeaseOperationResult(
    ValidationLeaseOperationStatus Status,
    string? Message = null)
{
    public bool Succeeded => Status == ValidationLeaseOperationStatus.Succeeded;

    public static ValidationLeaseOperationResult Ok() =>
        new(ValidationLeaseOperationStatus.Succeeded);

    public static ValidationLeaseOperationResult Conflict(string message) =>
        new(ValidationLeaseOperationStatus.Conflict, message);

    public static ValidationLeaseOperationResult NotFound(string message) =>
        new(ValidationLeaseOperationStatus.NotFound, message);
}
