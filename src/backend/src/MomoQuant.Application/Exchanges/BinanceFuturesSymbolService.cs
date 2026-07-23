using Microsoft.Extensions.Logging;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Exchanges.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Exchanges;

namespace MomoQuant.Application.Exchanges;

public sealed class BinanceFuturesSymbolService : IBinanceFuturesSymbolService
{
    public const string BinanceFuturesCode = "BINANCE_FUTURES";

    // Binance USD-M Futures default fee tier (VIP 0): maker 0.02%, taker 0.04%.
    private const decimal DefaultMakerFeeRate = 0.0002m;
    private const decimal DefaultTakerFeeRate = 0.0004m;

    private readonly IBinanceFuturesSymbolDiscoveryService _discoveryService;
    private readonly IExchangeRepository _exchangeRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;
    private readonly ILogger<BinanceFuturesSymbolService> _logger;

    public BinanceFuturesSymbolService(
        IBinanceFuturesSymbolDiscoveryService discoveryService,
        IExchangeRepository exchangeRepository,
        ISymbolRepository symbolRepository,
        ICurrentUserService currentUserService,
        IAuditService auditService,
        ILogger<BinanceFuturesSymbolService> logger)
    {
        _discoveryService = discoveryService;
        _exchangeRepository = exchangeRepository;
        _symbolRepository = symbolRepository;
        _currentUserService = currentUserService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<ServiceResult<IReadOnlyList<BinanceFuturesDiscoveredSymbolDto>>> DiscoverAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var exchange = await _exchangeRepository.GetByCodeAsync(BinanceFuturesCode, cancellationToken);

        IReadOnlyList<BinanceFuturesDiscoveredSymbolDto> discovered;
        try
        {
            discovered = await _discoveryService.DiscoverTopSymbolsAsync(limit, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover Binance Futures symbols.");
            return ServiceResult<IReadOnlyList<BinanceFuturesDiscoveredSymbolDto>>.Fail(
                "Failed to fetch symbols from Binance. Please try again later.");
        }

        if (exchange is not null)
        {
            var existing = (await _symbolRepository.GetSymbolNamesByExchangeAsync(exchange.Id, cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var symbol in discovered)
            {
                symbol.AlreadyAdded = existing.Contains(symbol.Symbol);
            }
        }

        return ServiceResult<IReadOnlyList<BinanceFuturesDiscoveredSymbolDto>>.Ok(discovered);
    }

    public async Task<ServiceResult<AddBinanceFuturesSymbolsResultDto>> AddSymbolsAsync(
        AddBinanceFuturesSymbolsRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestedSymbols = (request.Symbols ?? [])
            .Select(symbol => symbol?.Trim().ToUpperInvariant() ?? string.Empty)
            .Where(symbol => symbol.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (requestedSymbols.Count == 0)
        {
            return ServiceResult<AddBinanceFuturesSymbolsResultDto>.Fail(
                "At least one symbol is required.", "symbols");
        }

        var exchange = await _exchangeRepository.GetByCodeAsync(BinanceFuturesCode, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<AddBinanceFuturesSymbolsResultDto>.Fail(
                "Binance Futures exchange was not found. Run the clean baseline reset first.", "exchange");
        }

        IReadOnlyList<BinanceFuturesDiscoveredSymbolDto> metadata;
        try
        {
            metadata = await _discoveryService.GetSymbolMetadataAsync(requestedSymbols, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Binance Futures symbol metadata.");
            return ServiceResult<AddBinanceFuturesSymbolsResultDto>.Fail(
                "Failed to fetch symbol metadata from Binance. Please try again later.");
        }

        var metadataMap = metadata.ToDictionary(symbol => symbol.Symbol, StringComparer.Ordinal);
        var existing = (await _symbolRepository.GetSymbolNamesByExchangeAsync(exchange.Id, cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = new List<string>();
        var skipped = new List<string>();
        var unknown = new List<string>();
        var now = DateTime.UtcNow;

        foreach (var symbolName in requestedSymbols)
        {
            if (!metadataMap.TryGetValue(symbolName, out var definition))
            {
                unknown.Add(symbolName);
                continue;
            }

            if (existing.Contains(symbolName))
            {
                skipped.Add(symbolName);
                continue;
            }

            await _symbolRepository.AddAsync(new Symbol
            {
                ExchangeId = exchange.Id,
                SymbolName = symbolName,
                BaseAsset = definition.BaseAsset,
                QuoteAsset = definition.QuoteAsset,
                ContractType = ContractType.Perpetual,
                PricePrecision = definition.PricePrecision,
                QuantityPrecision = definition.QuantityPrecision,
                MinQty = definition.MinQty,
                MinNotional = definition.MinNotional,
                TickSize = definition.TickSize,
                StepSize = definition.StepSize,
                MakerFeeRate = DefaultMakerFeeRate,
                TakerFeeRate = DefaultTakerFeeRate,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            }, cancellationToken);

            existing.Add(symbolName);
            added.Add(symbolName);
        }

        if (added.Count > 0)
        {
            await _symbolRepository.SaveChangesAsync(cancellationToken);
        }

        await _auditService.LogAsync(
            "BINANCE_FUTURES_SYMBOLS_ADDED",
            nameof(Symbol),
            entityId: exchange.Id,
            userId: _currentUserService.UserId,
            newValueJson: $"{{\"exchangeId\":{exchange.Id},\"addedCount\":{added.Count},\"skippedCount\":{skipped.Count},\"unknownCount\":{unknown.Count}}}",
            cancellationToken: cancellationToken);

        return ServiceResult<AddBinanceFuturesSymbolsResultDto>.Ok(new AddBinanceFuturesSymbolsResultDto
        {
            ExchangeId = exchange.Id,
            RequestedCount = requestedSymbols.Count,
            AddedCount = added.Count,
            SkippedCount = skipped.Count,
            AddedSymbols = added,
            SkippedSymbols = skipped,
            UnknownSymbols = unknown
        });
    }
}
