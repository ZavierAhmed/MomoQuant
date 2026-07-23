using MomoQuant.Application.Common;
using MomoQuant.Application.Symbols.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Symbols;

public interface ISymbolService
{
    Task<ServiceResult<PagedResult<SymbolDto>>> GetSymbolsAsync(
        PagedRequest request,
        long? exchangeId = null,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SymbolDto>> GetSymbolByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<SymbolSyncResultDto>> SyncSymbolsAsync(
        SyncSymbolsRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SymbolDto>> UpdateSymbolStatusAsync(
        long id,
        UpdateSymbolStatusRequest request,
        CancellationToken cancellationToken = default);
}
