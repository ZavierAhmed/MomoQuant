using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Abstractions;

public interface IExchangeSymbolProvider
{
    Task<IReadOnlyList<ExchangeSymbolDefinition>> GetSymbolsAsync(
        string exchangeCode,
        CancellationToken cancellationToken = default);
}

public sealed class ExchangeSymbolDefinition
{
    public required string Symbol { get; init; }
    public required string BaseAsset { get; init; }
    public required string QuoteAsset { get; init; }
    public ContractType ContractType { get; init; } = ContractType.Perpetual;
    public int PricePrecision { get; init; }
    public int QuantityPrecision { get; init; }
    public decimal MinQty { get; init; }
    public decimal MinNotional { get; init; }
    public decimal TickSize { get; init; }
    public decimal StepSize { get; init; }
    public decimal MakerFeeRate { get; init; }
    public decimal TakerFeeRate { get; init; }
}
