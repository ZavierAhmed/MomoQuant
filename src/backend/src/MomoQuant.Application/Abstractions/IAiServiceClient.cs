using MomoQuant.Application.Ai.Dtos;

namespace MomoQuant.Application.Abstractions;

public interface IAiServiceClient
{
    Task<AiClientResult<AiHealthDto>> GetHealthAsync(CancellationToken cancellationToken = default);

    Task<AiClientResult<DetectRegimeResponseDto>> DetectRegimeAsync(
        DetectRegimeRequestDto request,
        CancellationToken cancellationToken = default);

    Task<AiClientResult<ScoreConfidenceResponseDto>> ScoreConfidenceAsync(
        ScoreConfidenceRequestDto request,
        CancellationToken cancellationToken = default);

    Task<AiClientResult<DetectAnomalyResponseDto>> DetectAnomalyAsync(
        DetectAnomalyRequestDto request,
        CancellationToken cancellationToken = default);

    Task<AiClientResult<ExplainTradeResponseDto>> ExplainTradeAsync(
        ExplainTradeRequestDto request,
        CancellationToken cancellationToken = default);
}

public sealed class AiClientResult<T>
{
    public bool Succeeded { get; init; }
    public T? Data { get; init; }
    public bool UsedFallback { get; init; }
    public string? ErrorMessage { get; init; }

    public static AiClientResult<T> Ok(T data) => new() { Succeeded = true, Data = data };

    public static AiClientResult<T> Fail(string message) =>
        new() { Succeeded = false, ErrorMessage = message };
}
