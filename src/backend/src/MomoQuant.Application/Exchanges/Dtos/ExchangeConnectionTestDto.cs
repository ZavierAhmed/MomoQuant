namespace MomoQuant.Application.Exchanges.Dtos;

public sealed class ExchangeConnectionTestDto
{
    public required int RestLatencyMs { get; init; }
    public required bool WebSocketAvailable { get; init; }
    public string? Message { get; init; }
}
