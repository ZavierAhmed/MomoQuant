using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.PaperTrading.Dtos;
using MomoQuant.Application.Strategies.FourHourRange;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.PaperTrading;

namespace MomoQuant.Application.PaperTrading;

public interface ILivePaperChartService
{
    Task<ServiceResult<LivePaperChartDto>> GetChartAsync(
        long sessionId,
        long? symbolId = null,
        string? timeframe = null,
        int limit = 300,
        CancellationToken cancellationToken = default);
}

public sealed class LivePaperChartService : ILivePaperChartService
{
    private readonly IPaperTradingSessionRepository _sessionRepository;
    private readonly IPaperStateStore _stateStore;
    private readonly ICandleRepository _candleRepository;
    private readonly IIndicatorSnapshotRepository _indicatorSnapshotRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IExchangeRepository _exchangeRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly ITradeRepository _tradeRepository;
    private readonly IMissedOrderRepository _missedOrderRepository;
    private readonly IRiskDecisionRepository _riskDecisionRepository;
    private readonly IAiDecisionRepository _aiDecisionRepository;
    private readonly ILiveMarketSnapshotStore _liveSnapshotStore;
    private readonly ILiveMarketConnectionManager _liveMarketConnectionManager;
    private readonly IFourHourRangeService _fourHourRangeService;

    public LivePaperChartService(
        IPaperTradingSessionRepository sessionRepository,
        IPaperStateStore stateStore,
        ICandleRepository candleRepository,
        IIndicatorSnapshotRepository indicatorSnapshotRepository,
        ISymbolRepository symbolRepository,
        IExchangeRepository exchangeRepository,
        IOrderRepository orderRepository,
        ITradeRepository tradeRepository,
        IMissedOrderRepository missedOrderRepository,
        IRiskDecisionRepository riskDecisionRepository,
        IAiDecisionRepository aiDecisionRepository,
        ILiveMarketSnapshotStore liveSnapshotStore,
        ILiveMarketConnectionManager liveMarketConnectionManager,
        IFourHourRangeService fourHourRangeService)
    {
        _sessionRepository = sessionRepository;
        _stateStore = stateStore;
        _candleRepository = candleRepository;
        _indicatorSnapshotRepository = indicatorSnapshotRepository;
        _symbolRepository = symbolRepository;
        _exchangeRepository = exchangeRepository;
        _orderRepository = orderRepository;
        _tradeRepository = tradeRepository;
        _missedOrderRepository = missedOrderRepository;
        _riskDecisionRepository = riskDecisionRepository;
        _aiDecisionRepository = aiDecisionRepository;
        _liveSnapshotStore = liveSnapshotStore;
        _liveMarketConnectionManager = liveMarketConnectionManager;
        _fourHourRangeService = fourHourRangeService;
    }

    public async Task<ServiceResult<LivePaperChartDto>> GetChartAsync(
        long sessionId,
        long? symbolId = null,
        string? timeframe = null,
        int limit = 300,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<LivePaperChartDto>.Fail("Paper session was not found.");
        }

        if (session.Mode != PaperTradingMode.LivePaper)
        {
            return ServiceResult<LivePaperChartDto>.Fail("Live chart is only available for LivePaper sessions.", "mode");
        }

        _stateStore.TryGet(sessionId, out var state);
        var symbolIds = PaperConfigParser.ResolveSymbolIds(session, state);
        var timeframes = PaperConfigParser.ResolveTimeframes(session, state);

        var resolvedSymbolId = symbolId ?? symbolIds.FirstOrDefault();
        if (resolvedSymbolId == 0)
        {
            return ServiceResult<LivePaperChartDto>.Fail("No symbol is configured for this LivePaper session.", "symbolId");
        }

        Timeframe resolvedTimeframe;
        if (!string.IsNullOrWhiteSpace(timeframe))
        {
            if (!TimeframeParser.TryParse(timeframe, out resolvedTimeframe))
            {
                return ServiceResult<LivePaperChartDto>.Fail("Timeframe is invalid.", "timeframe");
            }
        }
        else if (timeframes.Count > 0)
        {
            resolvedTimeframe = timeframes[0];
        }
        else
        {
            return ServiceResult<LivePaperChartDto>.Fail("No timeframe is configured for this LivePaper session.", "timeframe");
        }

        var symbol = await _symbolRepository.GetByIdAsync(resolvedSymbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<LivePaperChartDto>.Fail("Symbol was not found.", "symbolId");
        }

        var exchange = await _exchangeRepository.GetByIdAsync(session.ExchangeId, cancellationToken);
        var tf = TimeframeParser.ToApiString(resolvedTimeframe);
        var candleLimit = Math.Clamp(limit, 50, 500);

        var orderedCandles = (await _candleRepository.GetRecentCandlesAsync(
            resolvedSymbolId,
            resolvedTimeframe,
            DateTime.UtcNow.AddMinutes(1),
            candleLimit,
            cancellationToken)).ToList();
        var candleDtos = orderedCandles.Select(candle => new LivePaperChartCandleDto
        {
            CandleId = candle.Id,
            Time = candle.CloseTimeUtc,
            OpenTimeUtc = candle.OpenTimeUtc,
            CloseTimeUtc = candle.CloseTimeUtc,
            Open = candle.Open,
            High = candle.High,
            Low = candle.Low,
            Close = candle.Close,
            Volume = candle.Volume,
            IsClosed = candle.IsClosed,
            IsForming = false
        }).ToList();

        var indicatorDtos = new List<LivePaperChartIndicatorDto>();
        foreach (var candle in orderedCandles)
        {
            var snapshot = await _indicatorSnapshotRepository.GetByKeyAsync(
                resolvedSymbolId,
                resolvedTimeframe,
                candle.Id,
                cancellationToken);

            if (snapshot is null)
            {
                continue;
            }

            indicatorDtos.Add(new LivePaperChartIndicatorDto
            {
                Time = candle.CloseTimeUtc,
                Ema20 = snapshot.Ema20,
                Ema50 = snapshot.Ema50,
                Ema200 = snapshot.Ema200,
                Vwap = snapshot.Vwap
            });
        }

        var liveSnapshot = _liveSnapshotStore.Get(resolvedSymbolId, tf);
        LivePaperChartCandleDto? currentCandle = null;
        if (liveSnapshot?.CurrentCandle is not null)
        {
            var live = liveSnapshot.CurrentCandle;
            currentCandle = new LivePaperChartCandleDto
            {
                Time = live.CloseTimeUtc,
                OpenTimeUtc = live.OpenTimeUtc,
                CloseTimeUtc = live.CloseTimeUtc,
                Open = live.Open,
                High = live.High,
                Low = live.Low,
                Close = live.Close,
                Volume = live.Volume,
                IsClosed = live.IsClosed,
                IsForming = !live.IsClosed
            };

            // Replace or append forming candle on chart series.
            var existingIndex = candleDtos.FindIndex(item => item.OpenTimeUtc == live.OpenTimeUtc);
            if (existingIndex >= 0)
            {
                candleDtos[existingIndex] = currentCandle;
            }
            else if (!live.IsClosed)
            {
                candleDtos.Add(currentCandle);
            }
        }

        var orders = await _orderRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        var trades = await _tradeRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        var missed = await _missedOrderRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        var riskDecisions = await _riskDecisionRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        var aiDecisions = await _aiDecisionRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);

        var orderMarkers = orders
            .Where(order => order.Mode == TradingMode.Paper && order.SymbolId == resolvedSymbolId)
            .Select(order => new LivePaperChartMarkerDto
            {
                Time = order.FilledAtUtc ?? order.RequestedAtUtc,
                Type = "Order",
                Side = order.Side.ToString(),
                Price = order.Price,
                Label = $"{order.Side} {order.OrderType}",
                Color = order.Side == OrderSide.Buy ? "#22c55e" : "#ef4444"
            })
            .ToList();

        var tradeMarkers = trades
            .Where(trade => trade.SymbolId == resolvedSymbolId)
            .SelectMany(trade =>
            {
                var markers = new List<LivePaperChartMarkerDto>
                {
                    new()
                    {
                        Time = trade.OpenedAtUtc,
                        Type = "TradeEntry",
                        Side = trade.Direction.ToString(),
                        Price = trade.EntryPrice,
                        Label = "Entry",
                        Color = "#38bdf8"
                    }
                };

                if (trade.ClosedAtUtc is not null && trade.ExitPrice is not null)
                {
                    markers.Add(new LivePaperChartMarkerDto
                    {
                        Time = trade.ClosedAtUtc.Value,
                        Type = "TradeExit",
                        Side = trade.Direction.ToString(),
                        Price = trade.ExitPrice,
                        Label = "Exit",
                        Color = "#a78bfa"
                    });
                }

                return markers;
            })
            .ToList();

        var missedMarkers = missed
            .Where(item => item.SymbolId == resolvedSymbolId)
            .Select(item => new LivePaperChartMarkerDto
            {
                Time = item.ExpiredAtUtc,
                Type = "MissedOrder",
                Side = "None",
                Price = item.RequestedPrice,
                Label = item.Reason.ToString(),
                Color = "#f59e0b"
            })
            .ToList();

        var riskMarkers = riskDecisions
            .Where(item => item.SymbolId == resolvedSymbolId)
            .Select(item => new LivePaperChartMarkerDto
            {
                Time = item.CreatedAtUtc,
                Type = "Risk",
                Side = item.Decision.ToString(),
                Price = null,
                Label = item.Reason ?? item.Decision.ToString(),
                Color = "#fb7185"
            })
            .ToList();

        var aiMarkers = aiDecisions
            .Where(item => item.SymbolId == resolvedSymbolId)
            .Select(item => new LivePaperChartMarkerDto
            {
                Time = item.CreatedAtUtc,
                Type = "Ai",
                Side = item.TradeAllowed ? "Allow" : "Block",
                Price = null,
                Label = $"AI {item.ConfidenceScore:0.#}",
                Color = item.TradeAllowed ? "#34d399" : "#f87171"
            })
            .ToList();

        var isSubscribed = _liveMarketConnectionManager.IsSubscribed(resolvedSymbolId, resolvedTimeframe);
        var rangeLevels = BuildRangeLevels(resolvedSymbolId, resolvedTimeframe, orderedCandles, currentCandle);

        return ServiceResult<LivePaperChartDto>.Ok(new LivePaperChartDto
        {
            SessionId = session.Id,
            Mode = session.Mode.ToString(),
            Symbol = symbol.SymbolName,
            Exchange = exchange?.Name ?? string.Empty,
            Timeframe = tf,
            Connected = isSubscribed && _liveMarketConnectionManager.IsConnected,
            LatestPrice = liveSnapshot?.LatestPrice,
            LastLiveUpdateUtc = liveSnapshot?.LastLiveUpdateUtc ?? liveSnapshot?.LastUpdateUtc,
            LastClosedCandleUtc = liveSnapshot?.LastClosedCandleUtc,
            LastProcessedCandleUtc = session.CurrentCandleTimeUtc,
            Candles = candleDtos,
            Indicators = indicatorDtos,
            CurrentCandle = currentCandle,
            OrderMarkers = orderMarkers,
            TradeMarkers = tradeMarkers,
            RiskMarkers = riskMarkers,
            AiMarkers = aiMarkers,
            MissedOrderMarkers = missedMarkers,
            RangeLevels = rangeLevels
        });
    }

    private IReadOnlyList<LivePaperChartRangeLevelDto> BuildRangeLevels(
        long symbolId,
        Timeframe timeframe,
        IReadOnlyList<Candle> closedCandles,
        LivePaperChartCandleDto? currentCandle)
    {
        var anchorTimeUtc = currentCandle?.CloseTimeUtc ?? closedCandles.LastOrDefault()?.CloseTimeUtc;
        if (anchorTimeUtc is null)
        {
            return [];
        }

        var range = _fourHourRangeService.GetRangeForCandle(
            symbolId,
            timeframe,
            anchorTimeUtc.Value,
            closedCandles);

        if (!range.IsValid || range.RangeHigh is null || range.RangeLow is null)
        {
            return [];
        }

        return
        [
            new LivePaperChartRangeLevelDto
            {
                Label = "NY 4H High",
                Price = range.RangeHigh.Value,
                StartUtc = range.RangeStartUtc,
                EndUtc = range.NewYorkDayEndUtc,
                Color = "#f59e0b"
            },
            new LivePaperChartRangeLevelDto
            {
                Label = "NY 4H Low",
                Price = range.RangeLow.Value,
                StartUtc = range.RangeStartUtc,
                EndUtc = range.NewYorkDayEndUtc,
                Color = "#06b6d4"
            }
        ];
    }
}
