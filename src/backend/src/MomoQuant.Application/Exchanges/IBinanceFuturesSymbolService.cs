using MomoQuant.Application.Common;
using MomoQuant.Application.Exchanges.Dtos;

namespace MomoQuant.Application.Exchanges;

public interface IBinanceFuturesSymbolService
{
    Task<ServiceResult<IReadOnlyList<BinanceFuturesDiscoveredSymbolDto>>> DiscoverAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AddBinanceFuturesSymbolsResultDto>> AddSymbolsAsync(
        AddBinanceFuturesSymbolsRequest request,
        CancellationToken cancellationToken = default);
}
