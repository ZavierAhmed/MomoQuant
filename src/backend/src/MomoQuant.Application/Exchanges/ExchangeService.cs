using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Exchanges.Dtos;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Exchanges;

public sealed class ExchangeService : IExchangeService
{
    private readonly IExchangeRepository _exchangeRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IExchangeConnectivityProvider _connectivityProvider;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;

    public ExchangeService(
        IExchangeRepository exchangeRepository,
        ISymbolRepository symbolRepository,
        IExchangeConnectivityProvider connectivityProvider,
        ICurrentUserService currentUserService,
        IAuditService auditService)
    {
        _exchangeRepository = exchangeRepository;
        _symbolRepository = symbolRepository;
        _connectivityProvider = connectivityProvider;
        _currentUserService = currentUserService;
        _auditService = auditService;
    }

    public async Task<ServiceResult<PagedResult<ExchangeDto>>> GetExchangesAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _exchangeRepository.GetPagedAsync(request, cancellationToken);

        return ServiceResult<PagedResult<ExchangeDto>>.Ok(new PagedResult<ExchangeDto>
        {
            Items = items.Select(MapToDto).ToList(),
            Page = Math.Max(request.Page, 1),
            PageSize = Math.Max(request.PageSize, 1),
            TotalCount = totalCount
        });
    }

    public async Task<ServiceResult<ExchangeDto>> GetExchangeByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var exchange = await _exchangeRepository.GetByIdAsync(id, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<ExchangeDto>.Fail("Exchange was not found.");
        }

        return ServiceResult<ExchangeDto>.Ok(MapToDto(exchange));
    }

    public async Task<ServiceResult<ExchangeDto>> CreateExchangeAsync(
        CreateExchangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = NormalizeCode(request.Code);
        if (await _exchangeRepository.CodeExistsAsync(code, cancellationToken: cancellationToken))
        {
            return ServiceResult<ExchangeDto>.Fail("Exchange code is already in use.", "code");
        }

        var now = DateTime.UtcNow;
        var exchange = new Exchange
        {
            Name = request.Name.Trim(),
            Code = code,
            BaseUrl = request.BaseUrl.Trim(),
            WebSocketUrl = request.WebSocketUrl.Trim(),
            IsActive = request.IsActive,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await _exchangeRepository.AddAsync(exchange, cancellationToken);
        await _exchangeRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "EXCHANGE_CREATED",
            nameof(Exchange),
            entityId: exchange.Id,
            userId: _currentUserService.UserId,
            newValueJson: $"{{\"code\":\"{exchange.Code}\",\"name\":\"{exchange.Name}\"}}",
            cancellationToken: cancellationToken);

        return ServiceResult<ExchangeDto>.Ok(MapToDto(exchange));
    }

    public async Task<ServiceResult<ExchangeDto>> UpdateExchangeAsync(
        long id,
        UpdateExchangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var exchange = await _exchangeRepository.GetByIdAsync(id, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<ExchangeDto>.Fail("Exchange was not found.");
        }

        var code = NormalizeCode(request.Code);
        if (await _exchangeRepository.CodeExistsAsync(code, id, cancellationToken))
        {
            return ServiceResult<ExchangeDto>.Fail("Exchange code is already in use.", "code");
        }

        var oldValue = $"{{\"code\":\"{exchange.Code}\",\"name\":\"{exchange.Name}\",\"isActive\":{exchange.IsActive.ToString().ToLowerInvariant()}}}";

        exchange.Name = request.Name.Trim();
        exchange.Code = code;
        exchange.BaseUrl = request.BaseUrl.Trim();
        exchange.WebSocketUrl = request.WebSocketUrl.Trim();
        exchange.IsActive = request.IsActive;
        exchange.UpdatedAtUtc = DateTime.UtcNow;

        await _exchangeRepository.UpdateAsync(exchange, cancellationToken);
        await _exchangeRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "EXCHANGE_UPDATED",
            nameof(Exchange),
            entityId: exchange.Id,
            userId: _currentUserService.UserId,
            oldValueJson: oldValue,
            newValueJson: $"{{\"code\":\"{exchange.Code}\",\"name\":\"{exchange.Name}\",\"isActive\":{exchange.IsActive.ToString().ToLowerInvariant()}}}",
            cancellationToken: cancellationToken);

        return ServiceResult<ExchangeDto>.Ok(MapToDto(exchange));
    }

    public async Task<ServiceResult<DeleteExchangeResultDto>> DeleteExchangeAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var exchange = await _exchangeRepository.GetByIdAsync(id, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<DeleteExchangeResultDto>.Fail("Exchange was not found.");
        }

        if (await _exchangeRepository.HasBlockingDependenciesAsync(id, cancellationToken))
        {
            return ServiceResult<DeleteExchangeResultDto>.Fail(
                "Exchange cannot be deleted because it has related market or trading data. Remove that data first.");
        }

        var symbolsDeleted = await _symbolRepository.DeleteByExchangeIdAsync(id, cancellationToken);
        var exchangeDeleted = await _exchangeRepository.DeleteAsync(id, cancellationToken);
        if (exchangeDeleted == 0)
        {
            return ServiceResult<DeleteExchangeResultDto>.Fail("Exchange was not found.");
        }

        await _exchangeRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "EXCHANGE_DELETED",
            nameof(Exchange),
            entityId: id,
            userId: _currentUserService.UserId,
            oldValueJson: $"{{\"code\":\"{exchange.Code}\",\"name\":\"{exchange.Name}\",\"symbolsDeleted\":{symbolsDeleted}}}",
            cancellationToken: cancellationToken);

        return ServiceResult<DeleteExchangeResultDto>.Ok(new DeleteExchangeResultDto
        {
            ExchangeId = id,
            ExchangeCode = exchange.Code,
            SymbolsDeleted = symbolsDeleted
        });
    }

    public async Task<ServiceResult<ExchangeConnectionTestDto>> TestConnectionAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var exchange = await _exchangeRepository.GetByIdAsync(id, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<ExchangeConnectionTestDto>.Fail("Exchange was not found.");
        }

        var result = await _connectivityProvider.TestConnectionAsync(
            exchange.Code,
            exchange.BaseUrl,
            exchange.WebSocketUrl,
            cancellationToken);

        await _auditService.LogAsync(
            "EXCHANGE_CONNECTIVITY_TESTED",
            nameof(Exchange),
            entityId: exchange.Id,
            userId: _currentUserService.UserId,
            newValueJson: $"{{\"restLatencyMs\":{result.RestLatencyMs},\"webSocketAvailable\":{result.WebSocketAvailable.ToString().ToLowerInvariant()}}}",
            cancellationToken: cancellationToken);

        return ServiceResult<ExchangeConnectionTestDto>.Ok(new ExchangeConnectionTestDto
        {
            RestLatencyMs = result.RestLatencyMs,
            WebSocketAvailable = result.WebSocketAvailable,
            Message = result.Message
        });
    }

    public async Task<ServiceResult<IReadOnlyList<ExchangeSymbolSummaryDto>>> GetEnabledSymbolsAsync(
        long exchangeId,
        CancellationToken cancellationToken = default)
    {
        var exchange = await _exchangeRepository.GetByIdAsync(exchangeId, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<IReadOnlyList<ExchangeSymbolSummaryDto>>.Fail("Exchange was not found.");
        }

        var symbols = await _symbolRepository.GetEnabledByExchangeIdAsync(exchangeId, cancellationToken);
        var summaries = symbols
            .GroupBy(symbol => symbol.SymbolName.Trim().ToUpperInvariant())
            .Select(group => group.OrderBy(symbol => symbol.Id).First())
            .Select(symbol => new ExchangeSymbolSummaryDto
            {
                Id = symbol.Id,
                Symbol = symbol.SymbolName,
                DisplayName = $"{symbol.SymbolName} — {exchange.Name}",
                ExchangeId = exchange.Id,
                ExchangeName = exchange.Name,
                IsEnabled = symbol.IsActive
            })
            .OrderBy(summary => summary.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ServiceResult<IReadOnlyList<ExchangeSymbolSummaryDto>>.Ok(summaries);
    }

    private static string NormalizeCode(string code) =>
        code.Trim().ToUpperInvariant();

    private static ExchangeDto MapToDto(Exchange exchange) => new()
    {
        Id = exchange.Id,
        Name = exchange.Name,
        Code = exchange.Code,
        BaseUrl = exchange.BaseUrl,
        WebSocketUrl = exchange.WebSocketUrl,
        IsActive = exchange.IsActive,
        CreatedAtUtc = exchange.CreatedAtUtc,
        UpdatedAtUtc = exchange.UpdatedAtUtc
    };
}
