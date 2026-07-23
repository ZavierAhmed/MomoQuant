namespace MomoQuant.Application.Exchanges.Dtos;

public sealed class ExchangeSymbolSummaryDto
{
    public required long Id { get; init; }
    public required string Symbol { get; init; }
    public required string DisplayName { get; init; }
    public required long ExchangeId { get; init; }
    public required string ExchangeName { get; init; }
    public required bool IsEnabled { get; init; }
}
