namespace MomoQuant.Shared.Contracts;

public sealed class ApiError
{
    public string Field { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
