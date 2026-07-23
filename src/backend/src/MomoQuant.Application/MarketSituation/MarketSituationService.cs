using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.MarketSituation.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.MarketSituation;

public interface IMarketSituationService
{
    Task<ServiceResult<MarketSituationDto>> GetCurrentAsync(
        long exchangeId,
        long symbolId,
        string timeframe,
        CancellationToken cancellationToken = default);
}

public sealed class MarketSituationService : IMarketSituationService
{
    private const int MinimumIndicatorCandles = 200;

    private readonly ISymbolRepository _symbolRepository;
    private readonly IExchangeRepository _exchangeRepository;
    private readonly ICandleRepository _candleRepository;
    private readonly IIndicatorSnapshotRepository _indicatorRepository;
    private readonly ILiveMarketSnapshotStore _liveSnapshotStore;
    private readonly ILiveMarketBootstrapService _bootstrapService;

    public MarketSituationService(
        ISymbolRepository symbolRepository,
        IExchangeRepository exchangeRepository,
        ICandleRepository candleRepository,
        IIndicatorSnapshotRepository indicatorRepository,
        ILiveMarketSnapshotStore liveSnapshotStore,
        ILiveMarketBootstrapService bootstrapService)
    {
        _symbolRepository = symbolRepository;
        _exchangeRepository = exchangeRepository;
        _candleRepository = candleRepository;
        _indicatorRepository = indicatorRepository;
        _liveSnapshotStore = liveSnapshotStore;
        _bootstrapService = bootstrapService;
    }

    public async Task<ServiceResult<MarketSituationDto>> GetCurrentAsync(
        long exchangeId,
        long symbolId,
        string timeframe,
        CancellationToken cancellationToken = default)
    {
        if (!TimeframeParser.TryParse(timeframe, out var parsedTimeframe))
        {
            return ServiceResult<MarketSituationDto>.Fail("Timeframe is invalid.", "timeframe");
        }

        var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
        if (symbol is null || symbol.ExchangeId != exchangeId)
        {
            return ServiceResult<MarketSituationDto>.Fail("Symbol was not found.", "symbolId");
        }

        var exchange = await _exchangeRepository.GetByIdAsync(exchangeId, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<MarketSituationDto>.Fail("Exchange was not found.", "exchangeId");
        }

        var liveSnapshot = _liveSnapshotStore.Get(symbolId, timeframe);
        var candle = await _candleRepository.GetLatestCandleAsync(symbolId, parsedTimeframe, cancellationToken);
        var storedCount = await _candleRepository.CountCandlesAsync(symbolId, parsedTimeframe, cancellationToken);
        IndicatorSnapshot? snapshot = null;
        if (candle is not null)
        {
            snapshot = await _indicatorRepository.GetByKeyAsync(symbolId, parsedTimeframe, candle.Id, cancellationToken);
        }

        var dataSource = ResolveDataSource(liveSnapshot, candle, storedCount, snapshot);
        LiveBootstrapResult? bootstrapResult = null;

        if (candle is null || storedCount < MinimumIndicatorCandles || snapshot is null)
        {
            var bootstrap = await _bootstrapService.EnsureWarmupAsync(
                exchangeId,
                symbolId,
                parsedTimeframe,
                cancellationToken);

            if (!bootstrap.Succeeded || bootstrap.Data is null)
            {
                if (liveSnapshot?.LatestPrice is not null)
                {
                    return ServiceResult<MarketSituationDto>.Ok(BuildFromLiveSnapshotOnly(
                        symbol,
                        exchange.Name,
                        timeframe,
                        liveSnapshot,
                        bootstrap.ErrorMessage));
                }

                return ServiceResult<MarketSituationDto>.Fail(
                    bootstrap.ErrorMessage
                    ?? $"Could not load recent market data for {symbol.SymbolName} {timeframe}. Check Binance public data connectivity.",
                    bootstrap.ErrorField ?? "candles");
            }

            bootstrapResult = bootstrap.Data;
            candle = await _candleRepository.GetLatestCandleAsync(symbolId, parsedTimeframe, cancellationToken);
            storedCount = bootstrapResult.CandleCountUsed;
            if (candle is not null)
            {
                snapshot = await _indicatorRepository.GetByKeyAsync(symbolId, parsedTimeframe, candle.Id, cancellationToken);
            }

            dataSource = bootstrapResult.DataSource == nameof(MarketSituationDataSource.BootstrapHistorical)
                ? nameof(MarketSituationDataSource.BootstrapHistorical)
                : nameof(MarketSituationDataSource.StoredHistorical);
        }

        if (candle is null)
        {
            return ServiceResult<MarketSituationDto>.Ok(BuildUnknown(
                symbol,
                exchange.Name,
                timeframe,
                nameof(MarketSituationDataSource.None),
                "No recent market data is available yet. The system will try to bootstrap recent Binance public candles."));
        }

        if (liveSnapshot?.CurrentCandle is { IsClosed: false })
        {
            dataSource = nameof(MarketSituationDataSource.LiveSnapshot);
        }
        else if (liveSnapshot?.LastClosedCandle is not null)
        {
            dataSource = nameof(MarketSituationDataSource.LatestClosedCandle);
        }

        var latestPrice = liveSnapshot?.LatestPrice ?? candle.Close;
        var analysis = Analyze(snapshot, candle, latestPrice);
        var summary = analysis.MarketRegime == MarketRegime.Unknown
            ? $"{symbol.SymbolName} {timeframe} market situation is unknown due to insufficient indicator data."
            : $"{symbol.SymbolName} {timeframe} is {analysis.MarketRegime.ToString().ToLowerInvariant()} with {analysis.MomentumState.ToString().ToLowerInvariant()} momentum.";

        return ServiceResult<MarketSituationDto>.Ok(new MarketSituationDto
        {
            ExchangeId = exchangeId,
            ExchangeName = exchange.Name,
            SymbolId = symbolId,
            Symbol = symbol.SymbolName,
            Timeframe = timeframe,
            DetectedAtUtc = DateTime.UtcNow,
            LatestPrice = latestPrice,
            MarketRegime = analysis.MarketRegime.ToString(),
            TrendDirection = analysis.TrendDirection.ToString(),
            VolatilityState = analysis.VolatilityState.ToString(),
            MomentumState = analysis.MomentumState.ToString(),
            VolumeState = analysis.VolumeState.ToString(),
            RiskState = analysis.RiskState,
            Summary = summary,
            Signals = analysis.Signals,
            Warnings = analysis.Warnings,
            DataSource = dataSource,
            LatestCandleTimeUtc = candle.CloseTimeUtc,
            CandleCountUsed = storedCount,
            IndicatorsAvailable = snapshot is not null
        });
    }

    private static string ResolveDataSource(
        LiveMarketSnapshot? liveSnapshot,
        Candle? candle,
        int storedCount,
        IndicatorSnapshot? snapshot)
    {
        if (liveSnapshot?.CurrentCandle is { IsClosed: false })
        {
            return nameof(MarketSituationDataSource.LiveSnapshot);
        }

        if (liveSnapshot?.LastClosedCandle is not null)
        {
            return nameof(MarketSituationDataSource.LatestClosedCandle);
        }

        if (candle is not null && storedCount > 0)
        {
            return snapshot is not null
                ? nameof(MarketSituationDataSource.StoredHistorical)
                : nameof(MarketSituationDataSource.None);
        }

        return nameof(MarketSituationDataSource.None);
    }

    private static MarketSituationDto BuildFromLiveSnapshotOnly(
        Domain.Exchanges.Symbol symbol,
        string exchangeName,
        string timeframe,
        LiveMarketSnapshot liveSnapshot,
        string? bootstrapWarning) => new()
    {
        ExchangeId = symbol.ExchangeId,
        ExchangeName = exchangeName,
        SymbolId = symbol.Id,
        Symbol = symbol.SymbolName,
        Timeframe = timeframe,
        DetectedAtUtc = DateTime.UtcNow,
        LatestPrice = liveSnapshot.LatestPrice,
        MarketRegime = MarketRegime.Unknown.ToString(),
        TrendDirection = TrendDirection.Unknown.ToString(),
        VolatilityState = VolatilityState.Normal.ToString(),
        MomentumState = MomentumState.Neutral.ToString(),
        VolumeState = VolumeState.Normal.ToString(),
        RiskState = "Unknown",
        Summary = $"{symbol.SymbolName} {timeframe} has live price updates but insufficient stored candles for indicator analysis.",
        Signals = [],
        Warnings =
        [
            bootstrapWarning
                ?? "No recent market data is available yet. The system will try to bootstrap recent Binance public candles."
        ],
        DataSource = nameof(MarketSituationDataSource.LiveSnapshot),
        LatestCandleTimeUtc = liveSnapshot.LastClosedCandleUtc ?? liveSnapshot.CurrentCandle?.CloseTimeUtc,
        CandleCountUsed = 0,
        IndicatorsAvailable = false
    };

    public static MarketSituationAnalysis Analyze(
        IndicatorSnapshot? snapshot,
        Candle? candle,
        decimal? latestPrice)
    {
        if (snapshot is null || candle is null)
        {
            return new MarketSituationAnalysis
            {
                MarketRegime = MarketRegime.Unknown,
                TrendDirection = TrendDirection.Unknown,
                VolatilityState = VolatilityState.Normal,
                MomentumState = MomentumState.Neutral,
                VolumeState = VolumeState.Normal,
                RiskState = "Unknown",
                Summary = "Insufficient market data to detect current situation.",
                Signals = ["No indicator snapshot available."],
                Warnings = ["Recalculate indicators or wait for live closed candles."]
            };
        }

        var regime = DetectRegime(snapshot, candle);
        var trend = DetectTrend(snapshot);
        var volatility = DetectVolatility(snapshot, candle);
        var momentum = DetectMomentum(snapshot);
        var volume = DetectVolume(snapshot, candle);
        var signals = BuildSignals(snapshot, candle, trend, momentum, volatility, volume);
        var warnings = new List<string>();

        if (regime == MarketRegime.Abnormal)
        {
            warnings.Add("Market appears abnormal. LivePaper trading is not recommended.");
        }

        var priceText = latestPrice?.ToString("0.########") ?? candle.Close.ToString("0.########");
        var summary =
            $"{candle.SymbolId} {TimeframeParser.ToApiString(candle.Timeframe)} is currently {regime.ToString().ToLowerInvariant()} with {momentum.ToString().ToLowerInvariant()} momentum and {volatility.ToString().ToLowerInvariant()} volatility. Latest price {priceText}.";

        return new MarketSituationAnalysis
        {
            MarketRegime = regime,
            TrendDirection = trend,
            VolatilityState = volatility,
            MomentumState = momentum,
            VolumeState = volume,
            RiskState = volatility >= VolatilityState.High ? "Elevated" : "Normal",
            Summary = summary,
            Signals = signals,
            Warnings = warnings
        };
    }

    private static MarketSituationDto BuildUnknown(
        Domain.Exchanges.Symbol symbol,
        string exchangeName,
        string timeframe,
        string dataSource,
        string summary) => new()
    {
        ExchangeId = symbol.ExchangeId,
        ExchangeName = exchangeName,
        SymbolId = symbol.Id,
        Symbol = symbol.SymbolName,
        Timeframe = timeframe,
        DetectedAtUtc = DateTime.UtcNow,
        LatestPrice = null,
        MarketRegime = MarketRegime.Unknown.ToString(),
        TrendDirection = TrendDirection.Unknown.ToString(),
        VolatilityState = VolatilityState.Normal.ToString(),
        MomentumState = MomentumState.Neutral.ToString(),
        VolumeState = VolumeState.Normal.ToString(),
        RiskState = "Unknown",
        Summary = summary,
        Signals = [],
        Warnings = ["Import historical candles, subscribe to live market data, or retry auto bootstrap."],
        DataSource = dataSource,
        LatestCandleTimeUtc = null,
        CandleCountUsed = 0,
        IndicatorsAvailable = false
    };

    private static MarketRegime DetectRegime(IndicatorSnapshot snapshot, Candle candle)
    {
        if (snapshot.Ema20 is null || snapshot.Ema50 is null || snapshot.Ema200 is null)
        {
            return MarketRegime.Unknown;
        }

        var atrPercent = snapshot.Atr14 is not null && candle.Close > 0
            ? snapshot.Atr14.Value / candle.Close * 100m
            : 0m;

        if (atrPercent > 4m)
        {
            return MarketRegime.HighVolatility;
        }

        if (atrPercent > 0 && atrPercent < 0.3m)
        {
            return MarketRegime.LowVolatility;
        }

        var emaSpread = Math.Abs(snapshot.Ema20.Value - snapshot.Ema50.Value) / candle.Close * 100m;
        if (emaSpread < 0.05m && snapshot.Rsi14 is >= 45m and <= 55m)
        {
            return MarketRegime.Choppy;
        }

        if ((snapshot.Ema20 > snapshot.Ema50 && snapshot.Ema50 > snapshot.Ema200)
            || (snapshot.Ema20 < snapshot.Ema50 && snapshot.Ema50 < snapshot.Ema200))
        {
            return MarketRegime.Trending;
        }

        if (snapshot.BollingerBandwidth20 is not null && snapshot.BollingerBandwidth20 < snapshot.BollingerMiddle20 * 0.01m)
        {
            return MarketRegime.Breakout;
        }

        if (snapshot.Rsi14 is < 25m or > 75m)
        {
            return MarketRegime.Reversal;
        }

        return MarketRegime.Ranging;
    }

    private static TrendDirection DetectTrend(IndicatorSnapshot snapshot)
    {
        if (snapshot.Ema20 is null || snapshot.Ema50 is null || snapshot.Ema200 is null)
        {
            return TrendDirection.Unknown;
        }

        if (snapshot.Ema20 > snapshot.Ema50 && snapshot.Ema50 > snapshot.Ema200)
        {
            return TrendDirection.Bullish;
        }

        if (snapshot.Ema20 < snapshot.Ema50 && snapshot.Ema50 < snapshot.Ema200)
        {
            return TrendDirection.Bearish;
        }

        return TrendDirection.Neutral;
    }

    private static VolatilityState DetectVolatility(IndicatorSnapshot snapshot, Candle candle)
    {
        if (snapshot.Atr14 is null || candle.Close <= 0)
        {
            return VolatilityState.Normal;
        }

        var atrPercent = snapshot.Atr14.Value / candle.Close * 100m;
        return atrPercent switch
        {
            > 4m => VolatilityState.Extreme,
            > 2m => VolatilityState.High,
            < 0.3m => VolatilityState.Low,
            _ => VolatilityState.Normal
        };
    }

    private static MomentumState DetectMomentum(IndicatorSnapshot snapshot)
    {
        if (snapshot.Rsi14 is null)
        {
            return MomentumState.Neutral;
        }

        if (snapshot.Rsi14 >= 70m)
        {
            return MomentumState.Overbought;
        }

        if (snapshot.Rsi14 <= 30m)
        {
            return MomentumState.Oversold;
        }

        if (snapshot.MacdHistogram is > 0)
        {
            return MomentumState.Bullish;
        }

        if (snapshot.MacdHistogram is < 0)
        {
            return MomentumState.Bearish;
        }

        return MomentumState.Neutral;
    }

    private static VolumeState DetectVolume(IndicatorSnapshot snapshot, Candle candle)
    {
        if (snapshot.VolumeSma20 is null || snapshot.VolumeSma20 <= 0)
        {
            return VolumeState.Normal;
        }

        var ratio = candle.Volume / snapshot.VolumeSma20.Value;
        return ratio switch
        {
            > 2.5m => VolumeState.Spike,
            > 1.5m => VolumeState.High,
            < 0.5m => VolumeState.Low,
            _ => VolumeState.Normal
        };
    }

    private static IReadOnlyList<string> BuildSignals(
        IndicatorSnapshot snapshot,
        Candle candle,
        TrendDirection trend,
        MomentumState momentum,
        VolatilityState volatility,
        VolumeState volume)
    {
        var signals = new List<string>
        {
            trend switch
            {
                TrendDirection.Bullish => "EMA alignment is bullish",
                TrendDirection.Bearish => "EMA alignment is bearish",
                _ => "EMA alignment is mixed"
            },
            momentum switch
            {
                MomentumState.Overbought => "RSI is overbought",
                MomentumState.Oversold => "RSI is oversold",
                _ => "RSI is neutral"
            },
            volatility switch
            {
                VolatilityState.High or VolatilityState.Extreme => "ATR is elevated",
                VolatilityState.Low => "ATR is low",
                _ => "ATR is normal"
            }
        };

        if (snapshot.Vwap is not null)
        {
            var distance = Math.Abs(candle.Close - snapshot.Vwap.Value) / candle.Close * 100m;
            signals.Add(distance < 0.2m ? "Price is near VWAP" : "Price is away from VWAP");
        }

        if (volume == VolumeState.Spike)
        {
            signals.Add("Volume spike detected");
        }

        return signals;
    }

    public sealed class MarketSituationAnalysis
    {
        public MarketRegime MarketRegime { get; init; }
        public TrendDirection TrendDirection { get; init; }
        public VolatilityState VolatilityState { get; init; }
        public MomentumState MomentumState { get; init; }
        public VolumeState VolumeState { get; init; }
        public required string RiskState { get; init; }
        public required string Summary { get; init; }
        public required IReadOnlyList<string> Signals { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }
    }
}
