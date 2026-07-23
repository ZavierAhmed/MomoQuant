using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Infrastructure.Exchanges;

public sealed class FakeExchangeSymbolProvider : IExchangeSymbolProvider
{
    private static readonly IReadOnlyList<ExchangeSymbolDefinition> TopSymbols =
    [
        CreateDefinition("BTCUSDT", "BTC", "USDT", pricePrecision: 2, quantityPrecision: 3, minQty: 0.001m, minNotional: 5m, tickSize: 0.10m, stepSize: 0.001m),
        CreateDefinition("ETHUSDT", "ETH", "USDT", pricePrecision: 2, quantityPrecision: 3, minQty: 0.001m, minNotional: 5m, tickSize: 0.01m, stepSize: 0.001m),
        CreateDefinition("SOLUSDT", "SOL", "USDT", pricePrecision: 3, quantityPrecision: 1, minQty: 0.1m, minNotional: 5m, tickSize: 0.001m, stepSize: 0.1m),
        CreateDefinition("BNBUSDT", "BNB", "USDT", pricePrecision: 2, quantityPrecision: 2, minQty: 0.01m, minNotional: 5m, tickSize: 0.01m, stepSize: 0.01m),
        CreateDefinition("XRPUSDT", "XRP", "USDT", pricePrecision: 4, quantityPrecision: 1, minQty: 1m, minNotional: 5m, tickSize: 0.0001m, stepSize: 1m)
    ];

    public Task<IReadOnlyList<ExchangeSymbolDefinition>> GetSymbolsAsync(
        string exchangeCode,
        CancellationToken cancellationToken = default)
    {
        _ = exchangeCode;
        return Task.FromResult(TopSymbols);
    }

    private static ExchangeSymbolDefinition CreateDefinition(
        string symbol,
        string baseAsset,
        string quoteAsset,
        int pricePrecision,
        int quantityPrecision,
        decimal minQty,
        decimal minNotional,
        decimal tickSize,
        decimal stepSize) => new()
    {
        Symbol = symbol,
        BaseAsset = baseAsset,
        QuoteAsset = quoteAsset,
        ContractType = ContractType.Perpetual,
        PricePrecision = pricePrecision,
        QuantityPrecision = quantityPrecision,
        MinQty = minQty,
        MinNotional = minNotional,
        TickSize = tickSize,
        StepSize = stepSize,
        MakerFeeRate = 0.0002m,
        TakerFeeRate = 0.0004m
    };
}
