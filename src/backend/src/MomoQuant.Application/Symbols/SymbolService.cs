using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Symbols.Dtos;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Symbols;

public sealed class SymbolService : ISymbolService
{
    private readonly ISymbolRepository _symbolRepository;
    private readonly IExchangeRepository _exchangeRepository;
    private readonly IExchangeSymbolProvider _symbolProvider;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;

    public SymbolService(
        ISymbolRepository symbolRepository,
        IExchangeRepository exchangeRepository,
        IExchangeSymbolProvider symbolProvider,
        ICurrentUserService currentUserService,
        IAuditService auditService)
    {
        _symbolRepository = symbolRepository;
        _exchangeRepository = exchangeRepository;
        _symbolProvider = symbolProvider;
        _currentUserService = currentUserService;
        _auditService = auditService;
    }

    public async Task<ServiceResult<PagedResult<SymbolDto>>> GetSymbolsAsync(
        PagedRequest request,
        long? exchangeId = null,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _symbolRepository.GetPagedAsync(request, exchangeId, cancellationToken);
        var exchangeMap = await LoadExchangeMapAsync(cancellationToken);

        return ServiceResult<PagedResult<SymbolDto>>.Ok(new PagedResult<SymbolDto>
        {
            Items = items.Select(symbol => MapToDto(symbol, exchangeMap.GetValueOrDefault(symbol.ExchangeId))).ToList(),
            Page = Math.Max(request.Page, 1),
            PageSize = Math.Max(request.PageSize, 1),
            TotalCount = totalCount
        });
    }

    public async Task<ServiceResult<SymbolDto>> GetSymbolByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var symbol = await _symbolRepository.GetByIdAsync(id, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<SymbolDto>.Fail("Symbol was not found.");
        }

        var exchange = await _exchangeRepository.GetByIdAsync(symbol.ExchangeId, cancellationToken);
        return ServiceResult<SymbolDto>.Ok(MapToDto(symbol, exchange));
    }

    public async Task<ServiceResult<SymbolSyncResultDto>> SyncSymbolsAsync(
        SyncSymbolsRequest request,
        CancellationToken cancellationToken = default)
    {
        var exchange = await _exchangeRepository.GetByIdAsync(request.ExchangeId, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<SymbolSyncResultDto>.Fail("Exchange was not found.", "exchangeId");
        }

        var definitions = await _symbolProvider.GetSymbolsAsync(exchange.Code, cancellationToken);
        var createdCount = 0;
        var updatedCount = 0;
        var now = DateTime.UtcNow;

        foreach (var definition in definitions)
        {
            var symbolName = definition.Symbol.Trim().ToUpperInvariant();
            var existing = await _symbolRepository.GetByExchangeAndNameAsync(exchange.Id, symbolName, cancellationToken);

            if (existing is null)
            {
                await _symbolRepository.AddAsync(new Symbol
                {
                    ExchangeId = exchange.Id,
                    SymbolName = symbolName,
                    BaseAsset = definition.BaseAsset,
                    QuoteAsset = definition.QuoteAsset,
                    ContractType = definition.ContractType,
                    PricePrecision = definition.PricePrecision,
                    QuantityPrecision = definition.QuantityPrecision,
                    MinQty = definition.MinQty,
                    MinNotional = definition.MinNotional,
                    TickSize = definition.TickSize,
                    StepSize = definition.StepSize,
                    MakerFeeRate = definition.MakerFeeRate,
                    TakerFeeRate = definition.TakerFeeRate,
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                }, cancellationToken);

                createdCount++;
                continue;
            }

            existing.BaseAsset = definition.BaseAsset;
            existing.QuoteAsset = definition.QuoteAsset;
            existing.ContractType = definition.ContractType;
            existing.PricePrecision = definition.PricePrecision;
            existing.QuantityPrecision = definition.QuantityPrecision;
            existing.MinQty = definition.MinQty;
            existing.MinNotional = definition.MinNotional;
            existing.TickSize = definition.TickSize;
            existing.StepSize = definition.StepSize;
            existing.MakerFeeRate = definition.MakerFeeRate;
            existing.TakerFeeRate = definition.TakerFeeRate;
            existing.UpdatedAtUtc = now;

            await _symbolRepository.UpdateAsync(existing, cancellationToken);
            updatedCount++;
        }

        await _symbolRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "SYMBOLS_SYNCED",
            nameof(Symbol),
            entityId: exchange.Id,
            userId: _currentUserService.UserId,
            newValueJson: $"{{\"exchangeId\":{exchange.Id},\"createdCount\":{createdCount},\"updatedCount\":{updatedCount}}}",
            cancellationToken: cancellationToken);

        return ServiceResult<SymbolSyncResultDto>.Ok(new SymbolSyncResultDto
        {
            ExchangeId = exchange.Id,
            CreatedCount = createdCount,
            UpdatedCount = updatedCount,
            TotalCount = definitions.Count
        });
    }

    public async Task<ServiceResult<SymbolDto>> UpdateSymbolStatusAsync(
        long id,
        UpdateSymbolStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var symbol = await _symbolRepository.GetByIdAsync(id, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<SymbolDto>.Fail("Symbol was not found.");
        }

        var oldValue = $"{{\"isActive\":{symbol.IsActive.ToString().ToLowerInvariant()}}}";

        symbol.IsActive = request.IsActive;
        symbol.UpdatedAtUtc = DateTime.UtcNow;

        await _symbolRepository.UpdateAsync(symbol, cancellationToken);
        await _symbolRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "SYMBOL_STATUS_CHANGED",
            nameof(Symbol),
            entityId: symbol.Id,
            userId: _currentUserService.UserId,
            oldValueJson: oldValue,
            newValueJson: $"{{\"isActive\":{symbol.IsActive.ToString().ToLowerInvariant()}}}",
            cancellationToken: cancellationToken);

        return ServiceResult<SymbolDto>.Ok(MapToDto(symbol, await _exchangeRepository.GetByIdAsync(symbol.ExchangeId, cancellationToken)));
    }

    private async Task<Dictionary<long, Exchange>> LoadExchangeMapAsync(CancellationToken cancellationToken)
    {
        var (items, _) = await _exchangeRepository.GetPagedAsync(new PagedRequest { Page = 1, PageSize = 500 }, cancellationToken);
        return items.ToDictionary(exchange => exchange.Id);
    }

    private static SymbolDto MapToDto(Symbol symbol, Exchange? exchange) => new()
    {
        Id = symbol.Id,
        ExchangeId = symbol.ExchangeId,
        ExchangeName = exchange?.Name,
        ExchangeCode = exchange?.Code,
        Symbol = symbol.SymbolName,
        BaseAsset = symbol.BaseAsset,
        QuoteAsset = symbol.QuoteAsset,
        ContractType = symbol.ContractType,
        PricePrecision = symbol.PricePrecision,
        QuantityPrecision = symbol.QuantityPrecision,
        MinQty = symbol.MinQty,
        MinNotional = symbol.MinNotional,
        TickSize = symbol.TickSize,
        StepSize = symbol.StepSize,
        MakerFeeRate = symbol.MakerFeeRate,
        TakerFeeRate = symbol.TakerFeeRate,
        IsActive = symbol.IsActive,
        CreatedAtUtc = symbol.CreatedAtUtc,
        UpdatedAtUtc = symbol.UpdatedAtUtc
    };
}
