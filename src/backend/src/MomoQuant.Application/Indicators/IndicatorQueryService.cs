using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Indicators.Dtos;
using MomoQuant.Application.MarketData;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;

namespace MomoQuant.Application.Indicators;

public sealed class IndicatorQueryService : IIndicatorQueryService
{
    private readonly IIndicatorSnapshotRepository _snapshotRepository;

    public IndicatorQueryService(IIndicatorSnapshotRepository snapshotRepository)
    {
        _snapshotRepository = snapshotRepository;
    }

    public async Task<ServiceResult<IndicatorSnapshotDto>> GetSnapshotAsync(
        long symbolId,
        string timeframe,
        long candleId,
        CancellationToken cancellationToken = default)
    {
        if (!TimeframeParser.TryParse(timeframe, out var parsedTimeframe))
        {
            return ServiceResult<IndicatorSnapshotDto>.Fail("Timeframe is invalid.", "timeframe");
        }

        var snapshot = await _snapshotRepository.GetByKeyAsync(symbolId, parsedTimeframe, candleId, cancellationToken);
        if (snapshot is null)
        {
            return ServiceResult<IndicatorSnapshotDto>.Fail("Indicator snapshot was not found.");
        }

        return ServiceResult<IndicatorSnapshotDto>.Ok(MapToDto(snapshot));
    }

    internal static IndicatorSnapshotDto MapToDto(IndicatorSnapshot snapshot) => new()
    {
        Id = snapshot.Id,
        SymbolId = snapshot.SymbolId,
        Timeframe = TimeframeParser.ToApiString(snapshot.Timeframe),
        CandleId = snapshot.CandleId,
        CalculatedAtUtc = snapshot.CalculatedAtUtc,
        Ema20 = snapshot.Ema20,
        Ema50 = snapshot.Ema50,
        Ema200 = snapshot.Ema200,
        Vwap = snapshot.Vwap,
        Rsi14 = snapshot.Rsi14,
        Atr14 = snapshot.Atr14,
        VolumeSma20 = snapshot.VolumeSma20,
        SwingHigh = snapshot.SwingHigh,
        SwingLow = snapshot.SwingLow,
        MarketStructure = snapshot.MarketStructure,
        BollingerMiddle20 = snapshot.BollingerMiddle20,
        BollingerUpper20 = snapshot.BollingerUpper20,
        BollingerLower20 = snapshot.BollingerLower20,
        BollingerBandwidth20 = snapshot.BollingerBandwidth20,
        DonchianHigh20 = snapshot.DonchianHigh20,
        DonchianLow20 = snapshot.DonchianLow20,
        MacdLine = snapshot.MacdLine,
        MacdSignal = snapshot.MacdSignal,
        MacdHistogram = snapshot.MacdHistogram,
        Supertrend = snapshot.Supertrend,
        SupertrendDirection = snapshot.SupertrendDirection,
        SupportLevel = snapshot.SupportLevel,
        ResistanceLevel = snapshot.ResistanceLevel,
        CreatedAtUtc = snapshot.CreatedAtUtc
    };
}
