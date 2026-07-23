namespace MomoQuant.Application.Abstractions;

public interface IExchangeConnectivityProvider
{
    Task<ExchangeConnectivityResult> TestConnectionAsync(
        string exchangeCode,
        string baseUrl,
        string webSocketUrl,
        CancellationToken cancellationToken = default);
}

public sealed class ExchangeConnectivityResult
{
    public required bool Success { get; init; }
    public int RestLatencyMs { get; init; }
    public bool WebSocketAvailable { get; init; }
    public string? Message { get; init; }
}
