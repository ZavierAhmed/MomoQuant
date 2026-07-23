namespace MomoQuant.Application.Exchanges.Dtos;

public sealed class BinanceFuturesDiscoveredSymbolDto
{
    public int Rank { get; set; }
    public required string Symbol { get; init; }
    public required string BaseAsset { get; init; }
    public required string QuoteAsset { get; init; }
    public string ContractType { get; init; } = "PERPETUAL";
    public string Status { get; init; } = "TRADING";
    public string MarginAsset { get; init; } = "USDT";
    public int PricePrecision { get; init; }
    public int QuantityPrecision { get; init; }
    public decimal TickSize { get; init; }
    public decimal StepSize { get; init; }
    public decimal MinQty { get; init; }
    public decimal MinNotional { get; init; }
    public decimal LastPrice { get; init; }
    public decimal PriceChangePercent24h { get; init; }
    public decimal QuoteVolume24h { get; init; }
    public long Trades24h { get; init; }
    public bool AlreadyAdded { get; set; }
}

public sealed class AddBinanceFuturesSymbolsRequest
{
    public List<string> Symbols { get; set; } = [];
}

public sealed class AddBinanceFuturesSymbolsResultDto
{
    public required long ExchangeId { get; init; }
    public required int RequestedCount { get; init; }
    public required int AddedCount { get; init; }
    public required int SkippedCount { get; init; }
    public IReadOnlyList<string> AddedSymbols { get; init; } = [];
    public IReadOnlyList<string> SkippedSymbols { get; init; } = [];
    public IReadOnlyList<string> UnknownSymbols { get; init; } = [];
}
