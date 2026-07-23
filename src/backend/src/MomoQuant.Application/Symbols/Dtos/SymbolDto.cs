using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Symbols.Dtos;

public sealed class SymbolDto
{
    public required long Id { get; init; }
    public required long ExchangeId { get; init; }
    public string? ExchangeName { get; init; }
    public string? ExchangeCode { get; init; }
    public required string Symbol { get; init; }
    public required string BaseAsset { get; init; }
    public required string QuoteAsset { get; init; }
    public required ContractType ContractType { get; init; }
    public required int PricePrecision { get; init; }
    public required int QuantityPrecision { get; init; }
    public required decimal MinQty { get; init; }
    public required decimal MinNotional { get; init; }
    public required decimal TickSize { get; init; }
    public required decimal StepSize { get; init; }
    public required decimal MakerFeeRate { get; init; }
    public required decimal TakerFeeRate { get; init; }
    public required bool IsActive { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }
}
