namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Authoritative outcome for lease release, heartbeat, and related cleanup work.
/// Callers must not treat a failed outcome as successful cleanup.
/// </summary>
public sealed class ValidationTrainingCleanupOutcome
{
    public bool Succeeded { get; init; }
    public string? ErrorCode { get; init; }
    public string? UserSafeErrorMessage { get; init; }
    public ValidationTrainingFailureAggregate Aggregate { get; init; } = new();

    public static ValidationTrainingCleanupOutcome Ok(
        ValidationTrainingFailureAggregate? aggregate = null) =>
        new()
        {
            Succeeded = true,
            Aggregate = aggregate ?? new ValidationTrainingFailureAggregate()
        };

    public static ValidationTrainingCleanupOutcome Failed(
        string errorCode,
        string userSafeErrorMessage,
        ValidationTrainingFailureAggregate aggregate) =>
        new()
        {
            Succeeded = false,
            ErrorCode = errorCode,
            UserSafeErrorMessage = userSafeErrorMessage,
            Aggregate = aggregate
        };
}
