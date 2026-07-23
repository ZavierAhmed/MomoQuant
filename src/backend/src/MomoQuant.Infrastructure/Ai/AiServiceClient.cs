using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Ai.Dtos;
using MomoQuant.Application.Options;

namespace MomoQuant.Infrastructure.Ai;

public sealed class AiServiceClient : IAiServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly AiIntegrationOptions _options;
    private readonly ILogger<AiServiceClient> _logger;

    public AiServiceClient(
        HttpClient httpClient,
        IOptions<AiIntegrationOptions> options,
        ILogger<AiServiceClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public Task<AiClientResult<AiHealthDto>> GetHealthAsync(CancellationToken cancellationToken = default) =>
        SendAsync<AiHealthDto>(HttpMethod.Get, "health", null, cancellationToken);

    public Task<AiClientResult<DetectRegimeResponseDto>> DetectRegimeAsync(
        DetectRegimeRequestDto request,
        CancellationToken cancellationToken = default) =>
        SendAsync<DetectRegimeResponseDto>(HttpMethod.Post, "api/v1/ai/regime/detect", request, cancellationToken);

    public Task<AiClientResult<ScoreConfidenceResponseDto>> ScoreConfidenceAsync(
        ScoreConfidenceRequestDto request,
        CancellationToken cancellationToken = default) =>
        SendAsync<ScoreConfidenceResponseDto>(HttpMethod.Post, "api/v1/ai/confidence/score", request, cancellationToken);

    public Task<AiClientResult<DetectAnomalyResponseDto>> DetectAnomalyAsync(
        DetectAnomalyRequestDto request,
        CancellationToken cancellationToken = default) =>
        SendAsync<DetectAnomalyResponseDto>(HttpMethod.Post, "api/v1/ai/anomaly/detect", request, cancellationToken);

    public Task<AiClientResult<ExplainTradeResponseDto>> ExplainTradeAsync(
        ExplainTradeRequestDto request,
        CancellationToken cancellationToken = default) =>
        SendAsync<ExplainTradeResponseDto>(HttpMethod.Post, "api/v1/ai/explain/trade", request, cancellationToken);

    private async Task<AiClientResult<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: JsonOptions);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "AI service call to {Path} failed with status {StatusCode}: {Body}",
                    path,
                    (int)response.StatusCode,
                    errorBody);

                return AiClientResult<T>.Fail($"AI service returned {(int)response.StatusCode}.");
            }

            var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return payload is null
                ? AiClientResult<T>.Fail("AI service returned an empty response.")
                : AiClientResult<T>.Ok(payload);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "AI service call to {Path} timed out after {TimeoutSeconds}s.", path, _options.TimeoutSeconds);
            return AiClientResult<T>.Fail("AI service request timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "AI service call to {Path} failed.", path);
            return AiClientResult<T>.Fail("AI service is unavailable.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error calling AI service at {Path}.", path);
            return AiClientResult<T>.Fail("AI service call failed.");
        }
    }
}
