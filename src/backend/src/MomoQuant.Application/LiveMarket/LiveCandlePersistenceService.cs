using MomoQuant.Application.Abstractions;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.LiveMarket;

public interface ILiveCandlePersistenceService
{
    Task<Candle?> PersistClosedCandleAsync(LiveCandleUpdate update, CancellationToken cancellationToken = default);
}

public sealed class LiveCandlePersistenceService : ILiveCandlePersistenceService
{
    private readonly ICandleRepository _candleRepository;

    public LiveCandlePersistenceService(ICandleRepository candleRepository)
    {
        _candleRepository = candleRepository;
    }

    public async Task<Candle?> PersistClosedCandleAsync(
        LiveCandleUpdate update,
        CancellationToken cancellationToken = default)
    {
        if (!update.IsClosed)
        {
            return null;
        }

        var existing = await _candleRepository.GetExistingOpenTimesAsync(
            update.ExchangeId,
            update.SymbolId,
            update.Timeframe,
            [update.OpenTimeUtc],
            cancellationToken);

        if (existing.Contains(update.OpenTimeUtc))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var candle = new Candle
        {
            ExchangeId = update.ExchangeId,
            SymbolId = update.SymbolId,
            Timeframe = update.Timeframe,
            OpenTimeUtc = update.OpenTimeUtc,
            CloseTimeUtc = update.CloseTimeUtc,
            Open = update.Open,
            High = update.High,
            Low = update.Low,
            Close = update.Close,
            Volume = update.Volume,
            QuoteVolume = update.QuoteVolume,
            TradeCount = update.TradeCount,
            IsClosed = true,
            CreatedAtUtc = now
        };

        await _candleRepository.AddRangeAsync([candle], cancellationToken);
        await _candleRepository.SaveChangesAsync(cancellationToken);
        return candle;
    }
}
