using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Indicators;
using MomoQuant.Application.MarketData;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.LiveMarket;

public interface ILiveIndicatorUpdateService
{
    Task<IndicatorSnapshot?> UpdateForClosedCandleAsync(
        long symbolId,
        Timeframe timeframe,
        long candleId,
        CancellationToken cancellationToken = default);
}

public sealed class LiveIndicatorUpdateService : ILiveIndicatorUpdateService
{
    private const int WarmUpCandleCount = 250;

    private readonly ICandleRepository _candleRepository;
    private readonly IIndicatorSnapshotRepository _snapshotRepository;

    public LiveIndicatorUpdateService(
        ICandleRepository candleRepository,
        IIndicatorSnapshotRepository snapshotRepository)
    {
        _candleRepository = candleRepository;
        _snapshotRepository = snapshotRepository;
    }

    public async Task<IndicatorSnapshot?> UpdateForClosedCandleAsync(
        long symbolId,
        Timeframe timeframe,
        long candleId,
        CancellationToken cancellationToken = default)
    {
        var target = await _candleRepository.GetByIdAsync(candleId, cancellationToken);
        if (target is null)
        {
            return null;
        }

        var candles = await _candleRepository.GetRecentCandlesAsync(
            symbolId,
            timeframe,
            target.OpenTimeUtc,
            WarmUpCandleCount,
            cancellationToken);

        if (candles.Count == 0)
        {
            return null;
        }

        var chronological = candles.OrderBy(candle => candle.OpenTimeUtc).ToList();
        var targetIndex = chronological.FindIndex(candle => candle.Id == candleId);
        if (targetIndex < 0)
        {
            return null;
        }

        var rangeStartIndex = 0;
        var engine = new IndicatorCalculationEngine();
        var calculatedAtUtc = DateTime.UtcNow;
        IndicatorSnapshot? latest = null;

        for (var index = 0; index <= targetIndex; index++)
        {
            latest = engine.CalculateSnapshot(chronological, index, rangeStartIndex, timeframe, calculatedAtUtc);
        }

        if (latest is null)
        {
            return null;
        }

        var existing = await _snapshotRepository.GetByKeyAsync(symbolId, timeframe, candleId, cancellationToken);
        if (existing is null)
        {
            await _snapshotRepository.AddRangeAsync([latest], cancellationToken);
        }
        else
        {
            ApplyCalculatedValues(existing, latest, calculatedAtUtc);
            await _snapshotRepository.UpdateRangeAsync([existing], cancellationToken);
        }

        await _snapshotRepository.SaveChangesAsync(cancellationToken);
        return existing ?? latest;
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
        target.BollingerMiddle20 = source.BollingerMiddle20;
        target.BollingerUpper20 = source.BollingerUpper20;
        target.BollingerLower20 = source.BollingerLower20;
        target.BollingerBandwidth20 = source.BollingerBandwidth20;
        target.DonchianHigh20 = source.DonchianHigh20;
        target.DonchianLow20 = source.DonchianLow20;
        target.MacdLine = source.MacdLine;
        target.MacdSignal = source.MacdSignal;
        target.MacdHistogram = source.MacdHistogram;
        target.Supertrend = source.Supertrend;
        target.SupertrendDirection = source.SupertrendDirection;
        target.SupportLevel = source.SupportLevel;
        target.ResistanceLevel = source.ResistanceLevel;
    }
}
