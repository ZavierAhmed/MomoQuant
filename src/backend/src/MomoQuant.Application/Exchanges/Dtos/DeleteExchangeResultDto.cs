namespace MomoQuant.Application.Exchanges.Dtos;

public sealed class DeleteExchangeResultDto
{
    public required long ExchangeId { get; init; }

    public required string ExchangeCode { get; init; }

    public required int SymbolsDeleted { get; init; }
}
