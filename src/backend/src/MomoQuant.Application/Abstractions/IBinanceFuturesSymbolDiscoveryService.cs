using MomoQuant.Application.Exchanges.Dtos;

namespace MomoQuant.Application.Abstractions;

/// <summary>
/// Fetches public Binance USD-M Futures market metadata. Uses public market-data endpoints only
/// (exchangeInfo + 24hr ticker). Never calls private/account/order endpoints.
/// </summary>
public interface IBinanceFuturesSymbolDiscoveryService
{
    /// <summary>
    /// Returns the top tradable USD-M perpetual USDT symbols sorted by 24h quote volume descending.
    /// </summary>
    Task<IReadOnlyList<BinanceFuturesDiscoveredSymbolDto>> DiscoverTopSymbolsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns exchange metadata definitions for the requested symbols (used when adding symbols).
    /// </summary>
    Task<IReadOnlyList<BinanceFuturesDiscoveredSymbolDto>> GetSymbolMetadataAsync(
        IReadOnlyCollection<string> symbols,
        CancellationToken cancellationToken = default);
}
