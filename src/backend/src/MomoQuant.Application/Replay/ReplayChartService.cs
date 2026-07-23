using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Replay.Dtos;
using MomoQuant.Application.Strategies.FourHourRange;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Replay;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Application.Replay;

public interface IReplayChartService
{
    Task<ServiceResult<ReplayChartDto>> GetChartAsync(
        long sessionId,
        ReplayChartQuery query,
        CancellationToken cancellationToken = default);
}

public sealed class ReplayChartService : IReplayChartService
{
    private const int DefaultLookbackFrames = 150;
    private const int DefaultFutureContextFrames = 25;

    private readonly IReplaySessionRepository _sessionRepository;
    private readonly IReplayFrameRepository _frameRepository;
    private readonly IReplayDataLoader _dataLoader;
    private readonly IReplayStateStore _stateStore;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IExchangeRepository _exchangeRepository;
    private readonly IIndicatorSnapshotRepository _indicatorSnapshotRepository;
    private readonly IRiskDecisionRepository _riskDecisionRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly ITradeRepository _tradeRepository;
    private readonly IMissedOrderRepository _missedOrderRepository;
    private readonly IFourHourRangeService _fourHourRangeService;

    public ReplayChartService(
        IReplaySessionRepository sessionRepository,
        IReplayFrameRepository frameRepository,
        IReplayDataLoader dataLoader,
        IReplayStateStore stateStore,
        ISymbolRepository symbolRepository,
        IExchangeRepository exchangeRepository,
        IIndicatorSnapshotRepository indicatorSnapshotRepository,
        IRiskDecisionRepository riskDecisionRepository,
        IOrderRepository orderRepository,
        ITradeRepository tradeRepository,
        IMissedOrderRepository missedOrderRepository,
        IFourHourRangeService fourHourRangeService)
    {
        _sessionRepository = sessionRepository;
        _frameRepository = frameRepository;
        _dataLoader = dataLoader;
        _stateStore = stateStore;
        _symbolRepository = symbolRepository;
        _exchangeRepository = exchangeRepository;
        _indicatorSnapshotRepository = indicatorSnapshotRepository;
        _riskDecisionRepository = riskDecisionRepository;
        _orderRepository = orderRepository;
        _tradeRepository = tradeRepository;
        _missedOrderRepository = missedOrderRepository;
        _fourHourRangeService = fourHourRangeService;
    }

    public async Task<ServiceResult<ReplayChartDto>> GetChartAsync(
        long sessionId,
        ReplayChartQuery query,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<ReplayChartDto>.Fail("Replay session was not found.");
        }

        var symbol = await _symbolRepository.GetByIdAsync(session.SymbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<ReplayChartDto>.Fail("Replay symbol was not found.");
        }

        var exchange = await _exchangeRepository.GetByIdAsync(session.ExchangeId, cancellationToken);
        var dataset = await ResolveDatasetAsync(session, cancellationToken);
        if (dataset is null || dataset.EvaluationIndices.Count == 0)
        {
            return ServiceResult<ReplayChartDto>.Fail("Replay candle data was not found.");
        }

        var strictMode = !query.IncludeFutureContext;
        var currentFrame = query.CurrentFrameIndex ?? session.CurrentFrameIndex;
        var totalFrames = session.TotalFrames;
        var processedUpTo = ResolveProcessedUpToFrame(query.UpToFrameIndex, currentFrame, totalFrames, strictMode);

        var anchorFrame = query.CurrentFrameIndex ?? (strictMode
            ? Math.Max(processedUpTo, 0)
            : Math.Max(currentFrame, 0));

        var lookback = query.CandlesBefore ?? DefaultLookbackFrames;
        var futureFrames = query.CandlesAfter ?? (query.IncludeFutureContext ? DefaultFutureContextFrames : 0);

        var windowStart = query.FromFrameIndex ?? Math.Max(0, anchorFrame - lookback);
        var windowEnd = query.ToFrameIndex ?? (strictMode
            ? Math.Max(processedUpTo, anchorFrame < 0 ? 0 : processedUpTo)
            : Math.Min(totalFrames - 1, anchorFrame + futureFrames));

        windowStart = Math.Clamp(windowStart, 0, totalFrames - 1);
        windowEnd = Math.Clamp(windowEnd, windowStart, totalFrames - 1);

        var candleDtos = new List<ReplayChartCandleDto>();
        var indicatorDtos = new List<ReplayChartIndicatorDto>();
        var candleIds = new List<long>();
        var indicatorsMissing = false;

        for (var frameIndex = windowStart; frameIndex <= windowEnd; frameIndex++)
        {
            if (frameIndex >= dataset.EvaluationIndices.Count)
            {
                break;
            }

            var candle = dataset.Candles[dataset.EvaluationIndices[frameIndex]];
            var isFuture = strictMode && frameIndex > processedUpTo;
            candleDtos.Add(new ReplayChartCandleDto
            {
                FrameIndex = frameIndex,
                CandleId = candle.Id,
                Time = candle.CloseTimeUtc,
                Open = candle.Open,
                High = candle.High,
                Low = candle.Low,
                Close = candle.Close,
                Volume = candle.Volume,
                IsFutureContext = isFuture
            });
            candleIds.Add(candle.Id);

            if (!dataset.IndicatorSnapshots.TryGetValue(candle.Id, out var snapshot))
            {
                if (frameIndex <= processedUpTo)
                {
                    indicatorsMissing = true;
                }

                continue;
            }

            indicatorDtos.Add(MapIndicator(frameIndex, candle, snapshot));
        }

        if (indicatorsMissing && indicatorDtos.Count == 0)
        {
            var snapshots = await _indicatorSnapshotRepository.GetByCandleIdsAsync(
                session.SymbolId,
                session.Timeframe,
                candleIds,
                cancellationToken);

            foreach (var candleDto in candleDtos.Where(item => !item.IsFutureContext))
            {
                if (snapshots.TryGetValue(candleDto.CandleId, out var snapshot))
                {
                    var candle = dataset.Candles.First(item => item.Id == candleDto.CandleId);
                    indicatorDtos.Add(MapIndicator(candleDto.FrameIndex, candle, snapshot));
                }
                else if (candleDto.FrameIndex <= processedUpTo)
                {
                    indicatorsMissing = true;
                }
            }
        }

        var persistedFrames = await _frameRepository.GetBySessionIdAsync(sessionId, cancellationToken);
        var markerFrames = persistedFrames
            .Where(frame => frame.FrameIndex >= windowStart && frame.FrameIndex <= Math.Max(processedUpTo, -1))
            .OrderBy(frame => frame.FrameIndex)
            .ToList();

        var riskLookup = await BuildRiskLookupAsync(session.TradingSessionId, markerFrames, cancellationToken);
        var orderLookup = await BuildOrderLookupAsync(session.TradingSessionId, markerFrames, cancellationToken);
        var tradeLookup = await BuildTradeLookupAsync(session.TradingSessionId, markerFrames, cancellationToken);
        var missedLookup = await BuildMissedLookupAsync(session.TradingSessionId, markerFrames, cancellationToken);

        var strategyMarkers = new List<ReplayChartStrategyMarkerDto>();
        var riskMarkers = new List<ReplayChartRiskMarkerDto>();
        var executionMarkers = new List<ReplayChartExecutionMarkerDto>();

        foreach (var frame in markerFrames)
        {
            if (frame.FrameIndex >= dataset.EvaluationIndices.Count)
            {
                continue;
            }

            var candle = dataset.Candles[dataset.EvaluationIndices[frame.FrameIndex]];
            var price = candle.Close;

            foreach (var strategy in ReplayMapper.DeserializeStrategyResultDtos(frame.StrategyResultsJson))
            {
                if (string.Equals(strategy.SignalType, nameof(SignalType.NoTrade), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                strategyMarkers.Add(new ReplayChartStrategyMarkerDto
                {
                    FrameIndex = frame.FrameIndex,
                    Time = candle.CloseTimeUtc,
                    StrategyCode = strategy.StrategyCode,
                    SignalType = strategy.SignalType,
                    Direction = strategy.Direction,
                    Price = strategy.EntryPrice ?? price,
                    Reason = strategy.Reason
                });
            }

            if (frame.RiskDecisionId is long riskId && riskLookup.TryGetValue(riskId, out var risk))
            {
                riskMarkers.Add(new ReplayChartRiskMarkerDto
                {
                    FrameIndex = frame.FrameIndex,
                    Time = candle.CloseTimeUtc,
                    Decision = risk.Decision.ToString(),
                    Price = price,
                    RejectedRuleKey = risk.RejectedRuleKey,
                    Reason = risk.Reason
                });
            }

            if (frame.OrderId is long orderId && orderLookup.TryGetValue(orderId, out var order))
            {
                executionMarkers.Add(new ReplayChartExecutionMarkerDto
                {
                    FrameIndex = frame.FrameIndex,
                    Time = candle.CloseTimeUtc,
                    Type = order.Status == OrderStatus.Filled ? "OrderFilled" : "OrderPlaced",
                    Direction = order.Side == OrderSide.Buy ? nameof(TradeDirection.Long) : nameof(TradeDirection.Short),
                    Price = order.Price,
                    Label = order.Status == OrderStatus.Filled ? "Simulated fill" : "Simulated order"
                });
            }

            if (frame.MissedOrderId is long missedId && missedLookup.TryGetValue(missedId, out var missed))
            {
                executionMarkers.Add(new ReplayChartExecutionMarkerDto
                {
                    FrameIndex = frame.FrameIndex,
                    Time = candle.CloseTimeUtc,
                    Type = "MissedOrder",
                    Direction = "None",
                    Price = missed.RequestedPrice,
                    Label = "Missed"
                });
            }

            if (frame.TradeId is long tradeId && tradeLookup.TryGetValue(tradeId, out var trade))
            {
                executionMarkers.Add(new ReplayChartExecutionMarkerDto
                {
                    FrameIndex = frame.FrameIndex,
                    Time = candle.CloseTimeUtc,
                    Type = trade.Status == TradeStatus.Closed ? "TradeExit" : "TradeEntry",
                    Direction = trade.Direction.ToString(),
                    Price = trade.ExitPrice ?? trade.EntryPrice,
                    Label = trade.Status == TradeStatus.Closed ? "Trade exit" : "Trade entry",
                    Pnl = trade.Status == TradeStatus.Closed ? trade.NetPnl : null
                });
            }
        }

        var rangeLevels = BuildRangeLevels(session, dataset, processedUpTo);

        return ServiceResult<ReplayChartDto>.Ok(new ReplayChartDto
        {
            ReplaySessionId = session.Id,
            Symbol = symbol.SymbolName,
            Exchange = exchange?.Name ?? session.ExchangeId.ToString(),
            Timeframe = TimeframeParser.ToApiString(session.Timeframe),
            CurrentFrameIndex = currentFrame,
            TotalFrames = totalFrames,
            StrictReplayMode = strictMode,
            IndicatorsMissing = indicatorsMissing,
            IndicatorWarning = indicatorsMissing
                ? "Indicator snapshots are missing for this range. Recalculate indicators before replaying."
                : null,
            Candles = candleDtos,
            Indicators = indicatorDtos,
            StrategyMarkers = strategyMarkers,
            RiskMarkers = riskMarkers,
            ExecutionMarkers = executionMarkers,
            RangeLevels = rangeLevels
        });
    }

    private IReadOnlyList<ReplayChartRangeLevelDto> BuildRangeLevels(
        ReplaySession session,
        Backtesting.BacktestDataset dataset,
        int processedUpTo)
    {
        if (processedUpTo < 0 || processedUpTo >= dataset.EvaluationIndices.Count)
        {
            return [];
        }

        var candleIndex = dataset.EvaluationIndices[processedUpTo];
        var current = dataset.Candles[candleIndex];
        var visibleCandles = dataset.Candles
            .Where(candle => candle.SymbolId == session.SymbolId &&
                candle.Timeframe == session.Timeframe &&
                candle.CloseTimeUtc <= current.CloseTimeUtc)
            .ToList();

        var range = _fourHourRangeService.GetRangeForCandle(
            session.SymbolId,
            session.Timeframe,
            current.CloseTimeUtc,
            visibleCandles);

        if (!range.IsValid || range.RangeHigh is null || range.RangeLow is null)
        {
            return [];
        }

        return
        [
            new ReplayChartRangeLevelDto
            {
                Label = "NY 4H High",
                Price = range.RangeHigh.Value,
                StartUtc = range.RangeStartUtc,
                EndUtc = range.NewYorkDayEndUtc,
                Color = "#f59e0b"
            },
            new ReplayChartRangeLevelDto
            {
                Label = "NY 4H Low",
                Price = range.RangeLow.Value,
                StartUtc = range.RangeStartUtc,
                EndUtc = range.NewYorkDayEndUtc,
                Color = "#06b6d4"
            }
        ];
    }

    private async Task<Backtesting.BacktestDataset?> ResolveDatasetAsync(ReplaySession session, CancellationToken cancellationToken)
    {
        if (_stateStore.TryGet(session.Id, out var state) && state is not null)
        {
            return state.Dataset;
        }

        return await _dataLoader.LoadAsync(
            session.ExchangeId,
            session.SymbolId,
            session.Timeframe,
            session.FromUtc,
            session.ToUtc,
            cancellationToken);
    }

    private static int ResolveProcessedUpToFrame(int? upToFrameIndex, int currentFrame, int totalFrames, bool strictMode)
    {
        if (strictMode && currentFrame < 0)
        {
            return -1;
        }

        var maxProcessed = strictMode ? currentFrame : totalFrames - 1;
        if (!upToFrameIndex.HasValue)
        {
            return maxProcessed;
        }

        return Math.Clamp(upToFrameIndex.Value, -1, maxProcessed);
    }

    private static ReplayChartIndicatorDto MapIndicator(int frameIndex, Candle candle, IndicatorSnapshot snapshot) => new()
    {
        FrameIndex = frameIndex,
        CandleId = candle.Id,
        Time = candle.CloseTimeUtc,
        Ema20 = snapshot.Ema20,
        Ema50 = snapshot.Ema50,
        Ema200 = snapshot.Ema200,
        Vwap = snapshot.Vwap,
        Rsi14 = snapshot.Rsi14,
        Atr14 = snapshot.Atr14,
        VolumeSma20 = snapshot.VolumeSma20,
        SwingHigh = snapshot.SwingHigh is > 0,
        SwingLow = snapshot.SwingLow is > 0,
        MarketStructure = snapshot.MarketStructure.ToString()
    };

    private async Task<Dictionary<long, RiskDecision>> BuildRiskLookupAsync(
        long tradingSessionId,
        IReadOnlyList<ReplayFrame> frames,
        CancellationToken cancellationToken)
    {
        var ids = frames.Where(frame => frame.RiskDecisionId.HasValue).Select(frame => frame.RiskDecisionId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var decisions = await _riskDecisionRepository.GetByTradingSessionIdAsync(tradingSessionId, cancellationToken);
        return decisions.Where(decision => ids.Contains(decision.Id)).ToDictionary(decision => decision.Id);
    }

    private async Task<Dictionary<long, Order>> BuildOrderLookupAsync(
        long tradingSessionId,
        IReadOnlyList<ReplayFrame> frames,
        CancellationToken cancellationToken)
    {
        var ids = frames.Where(frame => frame.OrderId.HasValue).Select(frame => frame.OrderId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var orders = await _orderRepository.GetByTradingSessionIdAsync(tradingSessionId, cancellationToken);
        return orders.Where(order => ids.Contains(order.Id)).ToDictionary(order => order.Id);
    }

    private async Task<Dictionary<long, Trade>> BuildTradeLookupAsync(
        long tradingSessionId,
        IReadOnlyList<ReplayFrame> frames,
        CancellationToken cancellationToken)
    {
        var ids = frames.Where(frame => frame.TradeId.HasValue).Select(frame => frame.TradeId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var trades = await _tradeRepository.GetByTradingSessionIdAsync(tradingSessionId, cancellationToken);
        return trades.Where(trade => ids.Contains(trade.Id)).ToDictionary(trade => trade.Id);
    }

    private async Task<Dictionary<long, MissedOrder>> BuildMissedLookupAsync(
        long tradingSessionId,
        IReadOnlyList<ReplayFrame> frames,
        CancellationToken cancellationToken)
    {
        var ids = frames.Where(frame => frame.MissedOrderId.HasValue).Select(frame => frame.MissedOrderId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var missed = await _missedOrderRepository.GetByTradingSessionIdAsync(tradingSessionId, cancellationToken);
        return missed.Where(order => ids.Contains(order.Id)).ToDictionary(order => order.Id);
    }
}
