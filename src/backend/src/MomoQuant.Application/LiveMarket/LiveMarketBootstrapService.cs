using Microsoft.Extensions.Options;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Indicators;
using MomoQuant.Application.Indicators.Dtos;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Options;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.LiveMarket;

public sealed class LiveMarketBootstrapService : ILiveMarketBootstrapService
{
    private const int MinimumWarmupCandles = 200;

    private readonly IExchangeRepository _exchangeRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly ICandleRepository _candleRepository;
    private readonly IIndicatorSnapshotRepository _indicatorSnapshotRepository;
    private readonly IHistoricalCandleProvider _historicalCandleProvider;
    private readonly IIndicatorCalculationService _indicatorCalculationService;
    private readonly MarketDataSettings _settings;

    public LiveMarketBootstrapService(
        IExchangeRepository exchangeRepository,
        ISymbolRepository symbolRepository,
        ICandleRepository candleRepository,
        IIndicatorSnapshotRepository indicatorSnapshotRepository,
        IHistoricalCandleProvider historicalCandleProvider,
        IIndicatorCalculationService indicatorCalculationService,
        IOptions<MarketDataSettings> settings)
    {
        _exchangeRepository = exchangeRepository;
        _symbolRepository = symbolRepository;
        _candleRepository = candleRepository;
        _indicatorSnapshotRepository = indicatorSnapshotRepository;
        _historicalCandleProvider = historicalCandleProvider;
        _indicatorCalculationService = indicatorCalculationService;
        _settings = settings.Value;
    }

    public async Task<ServiceResult<LiveBootstrapResult>> EnsureWarmupAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        CancellationToken cancellationToken = default)
    {
        var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
        if (symbol is null || symbol.ExchangeId != exchangeId)
        {
            return ServiceResult<LiveBootstrapResult>.Fail("Symbol was not found.", "symbolId");
        }

        var exchange = await _exchangeRepository.GetByIdAsync(exchangeId, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<LiveBootstrapResult>.Fail("Exchange was not found.", "exchangeId");
        }

        var warmupTarget = Math.Clamp(_settings.LiveBootstrap.WarmupCandles, MinimumWarmupCandles, _settings.LiveBootstrap.MaxBootstrapCandles);
        var storedCount = await _candleRepository.CountCandlesAsync(symbolId, timeframe, cancellationToken);
        var latest = await _candleRepository.GetLatestCandleAsync(symbolId, timeframe, cancellationToken);
        var hasIndicators = latest is not null
            && await _indicatorSnapshotRepository.GetByKeyAsync(symbolId, timeframe, latest.Id, cancellationToken) is not null;

        if (storedCount >= warmupTarget && hasIndicators)
        {
            return ServiceResult<LiveBootstrapResult>.Ok(new LiveBootstrapResult
            {
                DataSource = nameof(MarketSituationDataSource.StoredHistorical),
                CandleCountUsed = storedCount,
                IndicatorsAvailable = true,
                LatestCandleTimeUtc = latest?.CloseTimeUtc,
                CandlesInserted = 0
            });
        }

        if (storedCount >= MinimumWarmupCandles && latest is not null && !hasIndicators)
        {
            var recalcFrom = latest.OpenTimeUtc.AddMinutes(-(int)timeframe * warmupTarget);
            await _indicatorCalculationService.RecalculateAsync(new RecalculateIndicatorsRequest
            {
                SymbolId = symbolId,
                Timeframe = TimeframeParser.ToApiString(timeframe),
                FromUtc = recalcFrom,
                ToUtc = latest.CloseTimeUtc
            }, cancellationToken);

            hasIndicators = await _indicatorSnapshotRepository.GetByKeyAsync(symbolId, timeframe, latest.Id, cancellationToken) is not null;
            return ServiceResult<LiveBootstrapResult>.Ok(new LiveBootstrapResult
            {
                DataSource = nameof(MarketSituationDataSource.StoredHistorical),
                CandleCountUsed = storedCount,
                IndicatorsAvailable = hasIndicators,
                LatestCandleTimeUtc = latest.CloseTimeUtc,
                CandlesInserted = 0
            });
        }

        if (!_settings.LiveBootstrap.AllowAutoBootstrap)
        {
            return ServiceResult<LiveBootstrapResult>.Fail(
                $"No recent market data is available yet for {symbol.SymbolName} {TimeframeParser.ToApiString(timeframe)}. Enable auto bootstrap or import candles first.",
                "candles");
        }

        try
        {
            var candlesNeeded = Math.Max(warmupTarget - storedCount, 0);
            if (candlesNeeded == 0)
            {
                candlesNeeded = warmupTarget;
            }
            var toUtc = DateTime.UtcNow;
            var lookbackMinutes = (int)timeframe * candlesNeeded;
            var fromUtc = toUtc.AddMinutes(-lookbackMinutes);

            var definitions = await _historicalCandleProvider.GetCandlesAsync(
                exchange.Code,
                symbol.SymbolName,
                timeframe,
                fromUtc,
                toUtc,
                cancellationToken);

            if (definitions.Count == 0)
            {
                return ServiceResult<LiveBootstrapResult>.Fail(
                    $"Could not load recent market data for {symbol.SymbolName} {TimeframeParser.ToApiString(timeframe)}. Check Binance public data connectivity.",
                    "candles");
            }

            var openTimes = definitions.Select(definition => definition.OpenTimeUtc).ToList();
            var existingOpenTimes = await _candleRepository.GetExistingOpenTimesAsync(
                exchange.Id,
                symbol.Id,
                timeframe,
                openTimes,
                cancellationToken);

            var now = DateTime.UtcNow;
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
                    IsClosed = definition.IsClosed,
                    CreatedAtUtc = now
                });
            }

            if (candlesToInsert.Count > 0)
            {
                await _candleRepository.AddRangeAsync(candlesToInsert, cancellationToken);
                await _candleRepository.SaveChangesAsync(cancellationToken);
            }

            latest = await _candleRepository.GetLatestCandleAsync(symbolId, timeframe, cancellationToken);
            if (latest is null)
            {
                return ServiceResult<LiveBootstrapResult>.Fail(
                    $"Could not load recent market data for {symbol.SymbolName} {TimeframeParser.ToApiString(timeframe)}. Check Binance public data connectivity.",
                    "candles");
            }

            var recalcFrom = latest.OpenTimeUtc.AddMinutes(-(int)timeframe * warmupTarget);
            await _indicatorCalculationService.RecalculateAsync(new RecalculateIndicatorsRequest
            {
                SymbolId = symbolId,
                Timeframe = TimeframeParser.ToApiString(timeframe),
                FromUtc = recalcFrom,
                ToUtc = latest.CloseTimeUtc
            }, cancellationToken);

            storedCount = await _candleRepository.CountCandlesAsync(symbolId, timeframe, cancellationToken);
            hasIndicators = await _indicatorSnapshotRepository.GetByKeyAsync(symbolId, timeframe, latest.Id, cancellationToken) is not null;

            return ServiceResult<LiveBootstrapResult>.Ok(new LiveBootstrapResult
            {
                DataSource = nameof(MarketSituationDataSource.BootstrapHistorical),
                CandleCountUsed = storedCount,
                IndicatorsAvailable = hasIndicators,
                LatestCandleTimeUtc = latest.CloseTimeUtc,
                CandlesInserted = candlesToInsert.Count
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<LiveBootstrapResult>.Fail(
                $"Could not load recent market data for {symbol.SymbolName} {TimeframeParser.ToApiString(timeframe)}. Check Binance public data connectivity. ({ex.Message})",
                "candles");
        }
    }
}
