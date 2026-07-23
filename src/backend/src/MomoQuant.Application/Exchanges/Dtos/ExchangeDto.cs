namespace MomoQuant.Application.Exchanges.Dtos;

public sealed class ExchangeDto
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    public required string Code { get; init; }
    public required string BaseUrl { get; init; }
    public required string WebSocketUrl { get; init; }
    public required bool IsActive { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }
}
