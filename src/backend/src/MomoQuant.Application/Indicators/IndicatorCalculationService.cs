using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Indicators.Dtos;
using MomoQuant.Application.MarketData;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Indicators;

public sealed class IndicatorCalculationService : IIndicatorCalculationService
{
    private const int WarmUpCandleCount = 250;
    private const int UpsertBatchSize = 500;

    private readonly ICandleRepository _candleRepository;
    private readonly IIndicatorSnapshotRepository _snapshotRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;
    private readonly IMarketDataCoverageService? _coverageService;

    public IndicatorCalculationService(
        ICandleRepository candleRepository,
        IIndicatorSnapshotRepository snapshotRepository,
        ISymbolRepository symbolRepository,
        ICurrentUserService currentUserService,
        IAuditService auditService,
        IMarketDataCoverageService? coverageService = null)
    {
        _candleRepository = candleRepository;
        _snapshotRepository = snapshotRepository;
        _symbolRepository = symbolRepository;
        _currentUserService = currentUserService;
        _auditService = auditService;
        _coverageService = coverageService;
    }

    public async Task<ServiceResult<RecalculateIndicatorsResponse>> RecalculateAsync(
        RecalculateIndicatorsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TimeframeParser.TryParse(request.Timeframe, out var timeframe))
        {
            return ServiceResult<RecalculateIndicatorsResponse>.Fail("Timeframe is invalid.", "timeframe");
        }

        var fromUtc = NormalizeUtc(request.FromUtc);
        var toUtc = NormalizeUtc(request.ToUtc);
        if (fromUtc >= toUtc)
        {
            return ServiceResult<RecalculateIndicatorsResponse>.Fail("fromUtc must be earlier than toUtc.", "fromUtc");
        }

        var symbol = await _symbolRepository.GetByIdAsync(request.SymbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<RecalculateIndicatorsResponse>.Fail("Symbol was not found.", "symbolId");
        }

        if (request.AutoImportMissingCandles && _coverageService is not null)
        {
            var coverage = await _coverageService.EnsureTimeframesCoverageAsync(
                symbol.ExchangeId,
                symbol.Id,
                [TimeframeParser.ToApiString(timeframe)],
                fromUtc,
                toUtc,
                warmupCandles: WarmUpCandleCount,
                allowImport: true,
                cancellationToken);

            if (!coverage.Succeeded)
            {
                return ServiceResult<RecalculateIndicatorsResponse>.Fail(
                    coverage.ErrorMessage ?? "Candle coverage check failed.",
                    coverage.ErrorField);
            }
        }

        await _auditService.LogAsync(
            "INDICATOR_RECALCULATION_STARTED",
            nameof(IndicatorSnapshot),
            userId: _currentUserService.UserId,
            newValueJson: $"{{\"symbolId\":{symbol.Id},\"timeframe\":\"{TimeframeParser.ToApiString(timeframe)}\",\"fromUtc\":\"{fromUtc:O}\",\"toUtc\":\"{toUtc:O}\"}}",
            cancellationToken: cancellationToken);

        try
        {
            var candles = await _candleRepository.GetCandlesChronologicalAsync(
                symbol.Id,
                timeframe,
                fromUtc,
                toUtc,
                WarmUpCandleCount,
                cancellationToken);

            if (candles.Count == 0)
            {
                var emptyResponse = BuildResponse(symbol.Id, timeframe, fromUtc, toUtc, 0, 0, 0, "Completed");
                await LogCompletedAsync(symbol.Id, timeframe, emptyResponse, cancellationToken);
                return ServiceResult<RecalculateIndicatorsResponse>.Ok(emptyResponse);
            }

            var rangeStartIndex = FindRangeStartIndex(candles, fromUtc);
            var engine = new IndicatorCalculationEngine();
            var calculatedAtUtc = DateTime.UtcNow;
            var snapshotsToUpsert = new List<IndicatorSnapshot>();

            for (var index = 0; index < candles.Count; index++)
            {
                var candle = candles[index];
                if (candle.OpenTimeUtc < fromUtc)
                {
                    engine.CalculateSnapshot(candles, index, rangeStartIndex, timeframe, calculatedAtUtc);
                    continue;
                }

                snapshotsToUpsert.Add(engine.CalculateSnapshot(candles, index, rangeStartIndex, timeframe, calculatedAtUtc));
            }

            var inserted = 0;
            var updated = 0;

            for (var offset = 0; offset < snapshotsToUpsert.Count; offset += UpsertBatchSize)
            {
                var batch = snapshotsToUpsert.Skip(offset).Take(UpsertBatchSize).ToList();
                var candleIds = batch.Select(snapshot => snapshot.CandleId).ToList();
                var existing = await _snapshotRepository.GetByCandleIdsAsync(
                    symbol.Id,
                    timeframe,
                    candleIds,
                    cancellationToken);

                var toInsert = new List<IndicatorSnapshot>();
                var toUpdate = new List<IndicatorSnapshot>();

                foreach (var snapshot in batch)
                {
                    if (existing.TryGetValue(snapshot.CandleId, out var current))
                    {
                        ApplyCalculatedValues(current, snapshot, calculatedAtUtc);
                        toUpdate.Add(current);
                        updated++;
                        continue;
                    }

                    toInsert.Add(snapshot);
                    inserted++;
                }

                if (toInsert.Count > 0)
                {
                    await _snapshotRepository.AddRangeAsync(toInsert, cancellationToken);
                }

                if (toUpdate.Count > 0)
                {
                    await _snapshotRepository.UpdateRangeAsync(toUpdate, cancellationToken);
                }

                await _snapshotRepository.SaveChangesAsync(cancellationToken);
            }

            var response = BuildResponse(
                symbol.Id,
                timeframe,
                fromUtc,
                toUtc,
                snapshotsToUpsert.Count,
                inserted,
                updated,
                "Completed");

            await LogCompletedAsync(symbol.Id, timeframe, response, cancellationToken);
            return ServiceResult<RecalculateIndicatorsResponse>.Ok(response);
        }
        catch (Exception ex)
        {
            await _auditService.LogAsync(
                "INDICATOR_RECALCULATION_FAILED",
                nameof(IndicatorSnapshot),
                userId: _currentUserService.UserId,
                newValueJson: $"{{\"symbolId\":{symbol.Id},\"timeframe\":\"{TimeframeParser.ToApiString(timeframe)}\",\"error\":\"{EscapeJson(ex.Message)}\"}}",
                cancellationToken: cancellationToken);

            return ServiceResult<RecalculateIndicatorsResponse>.Fail("Indicator recalculation failed.");
        }
    }

    private async Task LogCompletedAsync(
        long symbolId,
        Timeframe timeframe,
        RecalculateIndicatorsResponse response,
        CancellationToken cancellationToken)
    {
        await _auditService.LogAsync(
            "INDICATOR_RECALCULATION_COMPLETED",
            nameof(IndicatorSnapshot),
            userId: _currentUserService.UserId,
            newValueJson: $"{{\"symbolId\":{symbolId},\"timeframe\":\"{TimeframeParser.ToApiString(timeframe)}\",\"candlesProcessed\":{response.CandlesProcessed},\"snapshotsInserted\":{response.SnapshotsInserted},\"snapshotsUpdated\":{response.SnapshotsUpdated}}}",
            cancellationToken: cancellationToken);
    }

    private static void ApplyCalculatedValues(
        IndicatorSnapshot target,
        IndicatorSnapshot source,
        DateTime calculatedAtUtc)
    {
        target.CalculatedAtUtc = calculatedAtUtc;
        target.Ema20 = source.Ema20;
        target.Ema50 = source.Ema50;
        target.Ema200 = source.Ema200;
        target.Vwap = source.Vwap;
        target.Rsi14 = source.Rsi14;
        target.Atr14 = source.Atr14;
        target.VolumeSma20 = source.VolumeSma20;
        target.SwingHigh = source.SwingHigh;
        target.SwingLow = source.SwingLow;
        target.MarketStructure = source.MarketStructure;
    }

    private static int FindRangeStartIndex(IReadOnlyList<Candle> candles, DateTime fromUtc)
    {
        for (var index = 0; index < candles.Count; index++)
        {
            if (candles[index].OpenTimeUtc >= fromUtc)
            {
                return index;
            }
        }

        return candles.Count;
    }

    private static RecalculateIndicatorsResponse BuildResponse(
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        int candlesProcessed,
        int snapshotsInserted,
        int snapshotsUpdated,
        string status) => new()
    {
        SymbolId = symbolId,
        Timeframe = TimeframeParser.ToApiString(timeframe),
        FromUtc = fromUtc,
        ToUtc = toUtc,
        CandlesProcessed = candlesProcessed,
        SnapshotsInserted = snapshotsInserted,
        SnapshotsUpdated = snapshotsUpdated,
        Status = status
    };

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
