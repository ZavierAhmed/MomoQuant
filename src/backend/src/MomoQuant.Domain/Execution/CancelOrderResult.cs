namespace MomoQuant.Domain.Execution;

public sealed class CancelOrderResult
{
    public required bool Success { get; init; }
    public string? FailureReason { get; init; }
}
