namespace MomoQuant.Application.Common;

public sealed class ServiceResult<T>
{
    public bool Succeeded { get; init; }
    public T? Data { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorField { get; init; }

    public static ServiceResult<T> Ok(T data) => new() { Succeeded = true, Data = data };

    public static ServiceResult<T> Fail(string message, string? field = null) =>
        new() { Succeeded = false, ErrorMessage = message, ErrorField = field };
}
