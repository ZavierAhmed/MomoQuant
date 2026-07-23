using Microsoft.Extensions.Options;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData.Dtos;
using MomoQuant.Application.Options;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.MarketData;

public sealed class MarketDataService : IMarketDataService
{
    private const int DefaultCandleLimit = 500;
    private const int MaxCandleLimit = 5000;
    private const int InsertBatchSize = 500;

    private readonly ICandleRepository _candleRepository;
    private readonly IMarketDataImportRepository _importRepository;
    private readonly IExchangeRepository _exchangeRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IHistoricalCandleProvider _historicalCandleProvider;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;
    private readonly MarketDataSettings _settings;

    public MarketDataService(
        ICandleRepository candleRepository,
        IMarketDataImportRepository importRepository,
        IExchangeRepository exchangeRepository,
        ISymbolRepository symbolRepository,
        IHistoricalCandleProvider historicalCandleProvider,
        ICurrentUserService currentUserService,
        IAuditService auditService,
        IOptions<MarketDataSettings> settings)
    {
        _candleRepository = candleRepository;
        _importRepository = importRepository;
        _exchangeRepository = exchangeRepository;
        _symbolRepository = symbolRepository;
        _historicalCandleProvider = historicalCandleProvider;
        _currentUserService = currentUserService;
        _auditService = auditService;
        _settings = settings.Value;
    }

    public async Task<ServiceResult<IReadOnlyList<CandleDto>>> GetCandlesAsync(
        long symbolId,
        string timeframe,
        DateTime? fromUtc,
        DateTime? toUtc,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        if (!TimeframeParser.TryParse(timeframe, out var parsedTimeframe))
        {
            return ServiceResult<IReadOnlyList<CandleDto>>.Fail("Timeframe is invalid.", "timeframe");
        }

        var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<IReadOnlyList<CandleDto>>.Fail("Symbol was not found.", "symbolId");
        }

        if (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value >= toUtc.Value)
        {
            return ServiceResult<IReadOnlyList<CandleDto>>.Fail("fromUtc must be earlier than toUtc.", "fromUtc");
        }

        var effectiveLimit = Math.Clamp(limit ?? DefaultCandleLimit, 1, MaxCandleLimit);
        var candles = await _candleRepository.GetCandlesAsync(
            symbolId,
            parsedTimeframe,
            NormalizeUtc(fromUtc),
            NormalizeUtc(toUtc),
            effectiveLimit,
            cancellationToken);

        return ServiceResult<IReadOnlyList<CandleDto>>.Ok(candles.Select(MapToDto).ToList());
    }

    public async Task<ServiceResult<MarketDataImportDto>> ImportCandlesAsync(
        ImportCandlesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TimeframeParser.TryParse(request.Timeframe, out var timeframe))
        {
            return ServiceResult<MarketDataImportDto>.Fail("Timeframe is invalid.", "timeframe");
        }

        var fromUtc = NormalizeUtc(request.FromUtc);
        var toUtc = NormalizeUtc(request.ToUtc);
        if (fromUtc >= toUtc)
        {
            return ServiceResult<MarketDataImportDto>.Fail("fromUtc must be earlier than toUtc.", "fromUtc");
        }

        var exchange = await _exchangeRepository.GetByIdAsync(request.ExchangeId, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<MarketDataImportDto>.Fail("Exchange was not found.", "exchangeId");
        }

        var symbol = await _symbolRepository.GetByIdAsync(request.SymbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<MarketDataImportDto>.Fail("Symbol was not found.", "symbolId");
        }

        if (symbol.ExchangeId != exchange.Id)
        {
            return ServiceResult<MarketDataImportDto>.Fail("Symbol does not belong to the specified exchange.", "symbolId");
        }

        if (IsBinanceProvider())
        {
            var timeframeValue = TimeframeParser.ToApiString(timeframe);
            if (!_settings.Binance.AllowedIntervals.Contains(timeframeValue, StringComparer.OrdinalIgnoreCase))
            {
                return ServiceResult<MarketDataImportDto>.Fail(
                    $"Timeframe '{timeframeValue}' is not supported for Binance import.",
                    "timeframe");
            }

            if (!_settings.Binance.AllowedSymbols.Contains(symbol.SymbolName, StringComparer.OrdinalIgnoreCase))
            {
                return ServiceResult<MarketDataImportDto>.Fail(
                    $"Symbol '{symbol.SymbolName}' is not supported for Binance import.",
                    "symbolId");
            }

            var maxDays = Math.Max(_settings.Binance.MaxDaysPerImport, 1);
            if ((toUtc - fromUtc).TotalDays > maxDays)
            {
                return ServiceResult<MarketDataImportDto>.Fail(
                    $"Import range exceeds the maximum of {maxDays} days for Binance imports.",
                    "toUtc");
            }
        }

        var now = DateTime.UtcNow;
        var import = new MarketDataImport
        {
            ExchangeId = exchange.Id,
            SymbolId = symbol.Id,
            Timeframe = timeframe,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Status = MarketDataImportStatus.Pending,
            StartedAtUtc = now,
            CreatedByUserId = _currentUserService.UserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await _importRepository.AddAsync(import, cancellationToken);
        await _importRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "CANDLE_IMPORT_STARTED",
            nameof(MarketDataImport),
            entityId: import.Id,
            userId: _currentUserService.UserId,
            newValueJson: $"{{\"exchangeId\":{exchange.Id},\"symbolId\":{symbol.Id},\"timeframe\":\"{TimeframeParser.ToApiString(timeframe)}\",\"fromUtc\":\"{fromUtc:O}\",\"toUtc\":\"{toUtc:O}\"}}",
            cancellationToken: cancellationToken);

        try
        {
            import.Status = MarketDataImportStatus.Running;
            import.UpdatedAtUtc = DateTime.UtcNow;
            await _importRepository.UpdateAsync(import, cancellationToken);
            await _importRepository.SaveChangesAsync(cancellationToken);

            var definitions = await _historicalCandleProvider.GetCandlesAsync(
                exchange.Code,
                symbol.SymbolName,
                timeframe,
                fromUtc,
                toUtc,
                cancellationToken);

            import.TotalReceived = definitions.Count;

            var openTimes = definitions.Select(definition => definition.OpenTimeUtc).ToList();
            var existingOpenTimes = await _candleRepository.GetExistingOpenTimesAsync(
                exchange.Id,
                symbol.Id,
                timeframe,
                openTimes,
                cancellationToken);

            var candlesToInsert = new List<Candle>();
            foreach (var definition in definitions)
            {
                if (existingOpenTimes.Contains(definition.OpenTimeUtc))
                {
                    continue;
                }

                candlesToInsert.Add(new Candle
                {
                    ExchangeId = exchange.Id,
                    SymbolId = symbol.Id,
                    Timeframe = timeframe,
                    OpenTimeUtc = definition.OpenTimeUtc,
                    CloseTimeUtc = definition.CloseTimeUtc,
                    Open = definition.Open,
                    High = definition.High,
                    Low = definition.Low,
                    Close = definition.Close,
                    Volume = definition.Volume,
                    QuoteVolume = definition.QuoteVolume,
                    TradeCount = definition.TradeCount,
                    IsClosed = definition.CloseTimeUtc <= now,
                    CreatedAtUtc = now
                });
            }

            for (var offset = 0; offset < candlesToInsert.Count; offset += InsertBatchSize)
            {
                var batch = candlesToInsert.Skip(offset).Take(InsertBatchSize).ToList();
                var batchOpenTimes = batch.Select(candle => candle.OpenTimeUtc).ToList();
                var stillExisting = await _candleRepository.GetExistingOpenTimesAsync(
                    exchange.Id,
                    symbol.Id,
                    timeframe,
                    batchOpenTimes,
                    cancellationToken);

                var freshBatch = batch
                    .Where(candle => !stillExisting.Contains(candle.OpenTimeUtc))
                    .ToList();

                if (freshBatch.Count == 0)
                {
                    continue;
                }

                await _candleRepository.AddRangeAsync(freshBatch, cancellationToken);
                await _candleRepository.SaveChangesAsync(cancellationToken);
                import.InsertedCount += freshBatch.Count;
            }

            import.SkippedDuplicateCount = import.TotalReceived - import.InsertedCount;
            import.Status = MarketDataImportStatus.Completed;
            import.CompletedAtUtc = DateTime.UtcNow;
            import.UpdatedAtUtc = import.CompletedAtUtc;

            await _importRepository.UpdateAsync(import, cancellationToken);
            await _importRepository.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync(
                "CANDLE_IMPORT_COMPLETED",
                nameof(MarketDataImport),
                entityId: import.Id,
                userId: _currentUserService.UserId,
                newValueJson: $"{{\"totalReceived\":{import.TotalReceived},\"insertedCount\":{import.InsertedCount},\"skippedDuplicateCount\":{import.SkippedDuplicateCount}}}",
                cancellationToken: cancellationToken);

            return ServiceResult<MarketDataImportDto>.Ok(MapImportToDto(import));
        }
        catch (Exception ex)
        {
            import.Status = MarketDataImportStatus.Failed;
            import.ErrorMessage = ex.Message;
            import.CompletedAtUtc = DateTime.UtcNow;
            import.UpdatedAtUtc = import.CompletedAtUtc;

            await _importRepository.UpdateAsync(import, cancellationToken);
            await _importRepository.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync(
                "CANDLE_IMPORT_FAILED",
                nameof(MarketDataImport),
                entityId: import.Id,
                userId: _currentUserService.UserId,
                newValueJson: $"{{\"error\":\"{EscapeJson(ex.Message)}\"}}",
                cancellationToken: cancellationToken);

            return ServiceResult<MarketDataImportDto>.Fail("Candle import failed.");
        }
    }

    public async Task<ServiceResult<MarketDataImportDto>> GetImportStatusAsync(
        long importId,
        CancellationToken cancellationToken = default)
    {
        var import = await _importRepository.GetByIdAsync(importId, cancellationToken);
        if (import is null)
        {
            return ServiceResult<MarketDataImportDto>.Fail("Import was not found.");
        }

        return ServiceResult<MarketDataImportDto>.Ok(MapImportToDto(import));
    }

    public async Task<ServiceResult<IReadOnlyList<MarketDataImportDto>>> GetRecentImportsAsync(
        int? limit,
        CancellationToken cancellationToken = default)
    {
        var imports = await _importRepository.GetRecentAsync(limit ?? 20, cancellationToken);
        return ServiceResult<IReadOnlyList<MarketDataImportDto>>.Ok(imports.Select(MapImportToDto).ToList());
    }

    public async Task<ServiceResult<MarketSnapshotDto>> GetMarketSnapshotAsync(
        long symbolId,
        string timeframe,
        CancellationToken cancellationToken = default)
    {
        if (!TimeframeParser.TryParse(timeframe, out var parsedTimeframe))
        {
            return ServiceResult<MarketSnapshotDto>.Fail("Timeframe is invalid.", "timeframe");
        }

        var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<MarketSnapshotDto>.Fail("Symbol was not found.", "symbolId");
        }

        var latestCandle = await _candleRepository.GetLatestCandleAsync(symbolId, parsedTimeframe, cancellationToken);
        var candleCount = await _candleRepository.CountCandlesAsync(symbolId, parsedTimeframe, cancellationToken);
        var latestCandleDto = latestCandle is null ? null : MapToDto(latestCandle);

        return ServiceResult<MarketSnapshotDto>.Ok(new MarketSnapshotDto
        {
            SymbolId = symbol.Id,
            Symbol = symbol.SymbolName,
            Timeframe = TimeframeParser.ToApiString(parsedTimeframe),
            LatestCandle = latestCandleDto,
            LatestPrice = latestCandle?.Close,
            LatestUpdateTimeUtc = latestCandle?.CloseTimeUtc,
            CandleCountAvailable = candleCount,
            IndicatorsAvailable = false,
            Spread = null
        });
    }

    public async Task<ServiceResult<MarketDataQualityDto>> GetDataQualityAsync(
        long exchangeId,
        long symbolId,
        string timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        if (!TimeframeParser.TryParse(timeframe, out var parsedTimeframe))
        {
            return ServiceResult<MarketDataQualityDto>.Fail("Timeframe is invalid.", "timeframe");
        }

        var normalizedFromUtc = NormalizeUtc(fromUtc);
        var normalizedToUtc = NormalizeUtc(toUtc);
        if (normalizedFromUtc >= normalizedToUtc)
        {
            return ServiceResult<MarketDataQualityDto>.Fail("fromUtc must be earlier than toUtc.", "fromUtc");
        }

        var exchange = await _exchangeRepository.GetByIdAsync(exchangeId, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<MarketDataQualityDto>.Fail("Exchange was not found.", "exchangeId");
        }

        var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<MarketDataQualityDto>.Fail("Symbol was not found.", "symbolId");
        }

        if (symbol.ExchangeId != exchange.Id)
        {
            return ServiceResult<MarketDataQualityDto>.Fail("Symbol does not belong to the specified exchange.", "symbolId");
        }

        var openTimes = await _candleRepository.GetOpenTimesInRangeAsync(
            exchangeId,
            symbolId,
            parsedTimeframe,
            normalizedFromUtc,
            normalizedToUtc,
            cancellationToken);

        var duplicateCandles = await _candleRepository.CountDuplicateKeysInRangeAsync(
            exchangeId,
            symbolId,
            parsedTimeframe,
            normalizedFromUtc,
            normalizedToUtc,
            cancellationToken);

        var totalCandles = openTimes.Count;
        var expectedCandles = CalculateExpectedCandles(normalizedFromUtc, normalizedToUtc, parsedTimeframe);
        var uniqueCandles = totalCandles - duplicateCandles;
        var missingCandles = Math.Max(0, expectedCandles - uniqueCandles);
        var coveragePercent = expectedCandles == 0
            ? 100m
            : Math.Round(uniqueCandles * 100m / expectedCandles, 2);

        return ServiceResult<MarketDataQualityDto>.Ok(new MarketDataQualityDto
        {
            ExchangeId = exchangeId,
            SymbolId = symbolId,
            Timeframe = TimeframeParser.ToApiString(parsedTimeframe),
            FromUtc = normalizedFromUtc,
            ToUtc = normalizedToUtc,
            TotalCandles = totalCandles,
            ExpectedCandles = expectedCandles,
            MissingCandles = missingCandles,
            DuplicateCandles = duplicateCandles,
            FirstOpenTimeUtc = openTimes.Count > 0 ? openTimes[0] : null,
            LastOpenTimeUtc = openTimes.Count > 0 ? openTimes[^1] : null,
            CoveragePercent = coveragePercent,
            Gaps = BuildQualityGaps(openTimes, parsedTimeframe, normalizedFromUtc, normalizedToUtc)
        });
    }

    public Task<ServiceResult<MarketDataSettingsDto>> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult(ServiceResult<MarketDataSettingsDto>.Ok(new MarketDataSettingsDto
        {
            HistoricalProvider = _settings.HistoricalProvider
        }));
    }

    private bool IsBinanceProvider() =>
        string.Equals(_settings.HistoricalProvider, "Binance", StringComparison.OrdinalIgnoreCase);

    private static int CalculateExpectedCandles(DateTime fromUtc, DateTime toUtc, Timeframe timeframe)
    {
        var intervalMinutes = (int)timeframe;
        if (intervalMinutes <= 0 || toUtc <= fromUtc)
        {
            return 0;
        }

        return (int)((toUtc - fromUtc).TotalMinutes / intervalMinutes);
    }

    private static IReadOnlyList<MarketDataQualityGapDto> BuildQualityGaps(
        IReadOnlyList<DateTime> openTimes,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var gaps = new List<MarketDataQualityGapDto>();
        var interval = TimeSpan.FromMinutes((int)timeframe);

        if (openTimes.Count == 0)
        {
            var missingCount = CalculateExpectedCandles(fromUtc, toUtc, timeframe);
            if (missingCount > 0)
            {
                gaps.Add(new MarketDataQualityGapDto
                {
                    FromUtc = fromUtc,
                    ToUtc = toUtc,
                    MissingCount = missingCount
                });
            }

            return gaps;
        }

        if (openTimes[0] > fromUtc)
        {
            gaps.Add(new MarketDataQualityGapDto
            {
                FromUtc = fromUtc,
                ToUtc = openTimes[0],
                MissingCount = CalculateExpectedCandles(fromUtc, openTimes[0], timeframe)
            });
        }

        for (var index = 0; index < openTimes.Count - 1; index++)
        {
            var expectedNext = openTimes[index] + interval;
            var actualNext = openTimes[index + 1];
            if (actualNext > expectedNext)
            {
                gaps.Add(new MarketDataQualityGapDto
                {
                    FromUtc = expectedNext,
                    ToUtc = actualNext,
                    MissingCount = CalculateExpectedCandles(expectedNext, actualNext, timeframe)
                });
            }
        }

        var rangeEnd = openTimes[^1] + interval;
        if (rangeEnd < toUtc)
        {
            gaps.Add(new MarketDataQualityGapDto
            {
                FromUtc = rangeEnd,
                ToUtc = toUtc,
                MissingCount = CalculateExpectedCandles(rangeEnd, toUtc, timeframe)
            });
        }

        return gaps;
    }

    private static CandleDto MapToDto(Candle candle) => new()
    {
        Id = candle.Id,
        ExchangeId = candle.ExchangeId,
        SymbolId = candle.SymbolId,
        Timeframe = TimeframeParser.ToApiString(candle.Timeframe),
        OpenTimeUtc = candle.OpenTimeUtc,
        CloseTimeUtc = candle.CloseTimeUtc,
        Open = candle.Open,
        High = candle.High,
        Low = candle.Low,
        Close = candle.Close,
        Volume = candle.Volume,
        QuoteVolume = candle.QuoteVolume,
        TradeCount = candle.TradeCount,
        IsClosed = candle.IsClosed,
        CreatedAtUtc = candle.CreatedAtUtc
    };

    private static MarketDataImportDto MapImportToDto(MarketDataImport import) => new()
    {
        ImportId = import.Id,
        ExchangeId = import.ExchangeId,
        SymbolId = import.SymbolId,
        Timeframe = TimeframeParser.ToApiString(import.Timeframe),
        FromUtc = import.FromUtc,
        ToUtc = import.ToUtc,
        Status = import.Status,
        TotalReceived = import.TotalReceived,
        InsertedCount = import.InsertedCount,
        SkippedDuplicateCount = import.SkippedDuplicateCount,
        ErrorMessage = import.ErrorMessage,
        StartedAtUtc = import.StartedAtUtc,
        CompletedAtUtc = import.CompletedAtUtc
    };

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue ? NormalizeUtc(value.Value) : null;

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
