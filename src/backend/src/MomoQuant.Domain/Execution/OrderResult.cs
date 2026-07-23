namespace MomoQuant.Domain.Execution;

using MomoQuant.Domain.Enums;

public sealed class OrderResult
{
    public required bool Success { get; init; }
    public long? OrderId { get; init; }
    public string? ExternalOrderId { get; init; }
    public OrderStatus Status { get; init; }
    public string? FailureReason { get; init; }
}
