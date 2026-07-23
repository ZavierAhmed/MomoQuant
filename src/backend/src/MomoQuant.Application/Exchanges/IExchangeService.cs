using MomoQuant.Application.Common;
using MomoQuant.Application.Exchanges.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Exchanges;

public interface IExchangeService
{
    Task<ServiceResult<PagedResult<ExchangeDto>>> GetExchangesAsync(PagedRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<ExchangeDto>> GetExchangeByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<ServiceResult<ExchangeDto>> CreateExchangeAsync(CreateExchangeRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<ExchangeDto>> UpdateExchangeAsync(long id, UpdateExchangeRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<DeleteExchangeResultDto>> DeleteExchangeAsync(long id, CancellationToken cancellationToken = default);
    Task<ServiceResult<ExchangeConnectionTestDto>> TestConnectionAsync(long id, CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<ExchangeSymbolSummaryDto>>> GetEnabledSymbolsAsync(long exchangeId, CancellationToken cancellationToken = default);
}
