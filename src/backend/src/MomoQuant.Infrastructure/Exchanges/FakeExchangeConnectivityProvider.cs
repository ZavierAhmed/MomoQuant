using MomoQuant.Application.Abstractions;

namespace MomoQuant.Infrastructure.Exchanges;

public sealed class FakeExchangeConnectivityProvider : IExchangeConnectivityProvider
{
    public Task<ExchangeConnectivityResult> TestConnectionAsync(
        string exchangeCode,
        string baseUrl,
        string webSocketUrl,
        CancellationToken cancellationToken = default)
    {
        _ = exchangeCode;
        _ = baseUrl;
        _ = webSocketUrl;

        return Task.FromResult(new ExchangeConnectivityResult
        {
            Success = true,
            RestLatencyMs = 25,
            WebSocketAvailable = true,
            Message = "Simulated connectivity test succeeded."
        });
    }
}
