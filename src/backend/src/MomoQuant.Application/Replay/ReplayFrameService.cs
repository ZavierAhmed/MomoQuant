using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Replay.Dtos;
using MomoQuant.Domain.Ai;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Replay;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Signals;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Application.Replay;

public interface IReplayFrameService
{
    Task<ServiceResult<ReplayFrameDto>> GetCurrentFrameAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<ReplayFrameDto>>> GetFramesAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<ReplaySignalDto>>> GetSignalsAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<ReplayOrderDto>>> GetOrdersAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<ReplayTradeDto>>> GetTradesAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<ReplayMissedOrderDto>>> GetMissedOrdersAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<ReplayRiskDecisionDto>>> GetRiskDecisionsAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<ReplayAiDecisionDto>>> GetAiDecisionsAsync(long sessionId, CancellationToken cancellationToken = default);
}

public sealed class ReplayFrameService : IReplayFrameService
{
    private readonly IReplaySessionRepository _replaySessionRepository;
    private readonly IReplayFrameRepository _frameRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly ICandleRepository _candleRepository;
    private readonly IStrategySignalRepository _signalRepository;
    private readonly IStrategyRepository _strategyRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly ITradeRepository _tradeRepository;
    private readonly IMissedOrderRepository _missedOrderRepository;
    private readonly IRiskDecisionRepository _riskDecisionRepository;
    private readonly IAiDecisionRepository _aiDecisionRepository;
    private readonly IReplayStateStore _stateStore;
    private readonly IIndicatorSnapshotRepository _indicatorSnapshotRepository;

    public ReplayFrameService(
        IReplaySessionRepository replaySessionRepository,
        IReplayFrameRepository frameRepository,
        ISymbolRepository symbolRepository,
        ICandleRepository candleRepository,
        IStrategySignalRepository signalRepository,
        IStrategyRepository strategyRepository,
        IOrderRepository orderRepository,
        ITradeRepository tradeRepository,
        IMissedOrderRepository missedOrderRepository,
        IRiskDecisionRepository riskDecisionRepository,
        IAiDecisionRepository aiDecisionRepository,
        IReplayStateStore stateStore,
        IIndicatorSnapshotRepository indicatorSnapshotRepository)
    {
        _replaySessionRepository = replaySessionRepository;
        _frameRepository = frameRepository;
        _symbolRepository = symbolRepository;
        _candleRepository = candleRepository;
        _signalRepository = signalRepository;
        _strategyRepository = strategyRepository;
        _orderRepository = orderRepository;
        _tradeRepository = tradeRepository;
        _missedOrderRepository = missedOrderRepository;
        _riskDecisionRepository = riskDecisionRepository;
        _aiDecisionRepository = aiDecisionRepository;
        _stateStore = stateStore;
        _indicatorSnapshotRepository = indicatorSnapshotRepository;
    }

    public async Task<ServiceResult<ReplayFrameDto>> GetCurrentFrameAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _replaySessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<ReplayFrameDto>.Fail("Replay session was not found.");
        }

        if (session.CurrentFrameIndex < 0)
        {
            return ServiceResult<ReplayFrameDto>.Fail("Replay has not advanced to a frame yet.");
        }

        var frame = await _frameRepository.GetBySessionAndIndexAsync(sessionId, session.CurrentFrameIndex, cancellationToken);
        if (frame is null)
        {
            return ServiceResult<ReplayFrameDto>.Fail("Current replay frame was not found.");
        }

        var symbol = await _symbolRepository.GetByIdAsync(session.SymbolId, cancellationToken);
        var candle = await _candleRepository.GetByIdAsync(frame.CandleId, cancellationToken);
        if (candle is null)
        {
            return ServiceResult<ReplayFrameDto>.Fail("Replay candle was not found.");
        }

        var indicator = await ResolveIndicatorSnapshotAsync(sessionId, session, candle, cancellationToken);

        return ServiceResult<ReplayFrameDto>.Ok(
            ReplayMapper.MapFrameEntity(session, symbol?.SymbolName ?? session.SymbolId.ToString(), frame, candle, [], indicator));
    }

    public async Task<ServiceResult<IReadOnlyList<ReplayFrameDto>>> GetFramesAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(sessionId, cancellationToken);
        if (!session.Succeeded || session.Data is null)
        {
            return ServiceResult<IReadOnlyList<ReplayFrameDto>>.Fail(session.ErrorMessage!);
        }

        var replaySession = await _replaySessionRepository.GetByIdAsync(sessionId, cancellationToken);
        var symbol = await _symbolRepository.GetByIdAsync(replaySession!.SymbolId, cancellationToken);
        var frames = await _frameRepository.GetBySessionIdAsync(sessionId, cancellationToken);
        var results = new List<ReplayFrameDto>();

        foreach (var frame in frames)
        {
            var candle = await _candleRepository.GetByIdAsync(frame.CandleId, cancellationToken);
            if (candle is null)
            {
                continue;
            }

            results.Add(ReplayMapper.MapFrameEntity(
                replaySession,
                symbol?.SymbolName ?? replaySession.SymbolId.ToString(),
                frame,
                candle,
                [],
                await ResolveIndicatorSnapshotAsync(sessionId, replaySession, candle, cancellationToken)));
        }

        return ServiceResult<IReadOnlyList<ReplayFrameDto>>.Ok(results);
    }

    public async Task<ServiceResult<IReadOnlyList<ReplaySignalDto>>> GetSignalsAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(sessionId, cancellationToken);
        if (!session.Succeeded)
        {
            return ServiceResult<IReadOnlyList<ReplaySignalDto>>.Fail(session.ErrorMessage!);
        }

        var replaySession = session.Data!;
        var signals = await _signalRepository.GetByTradingSessionIdAsync(replaySession.TradingSessionId, cancellationToken);
        var strategies = await _strategyRepository.GetAllAsync(cancellationToken);
        var strategyLookup = strategies.ToDictionary(strategy => strategy.Id, strategy => strategy.Code.ToCode());

        var dtos = signals
            .Select(signal => ReplayMapper.MapSignal(signal, strategyLookup.GetValueOrDefault(signal.StrategyId, signal.StrategyId.ToString())))
            .ToList();

        return ServiceResult<IReadOnlyList<ReplaySignalDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<IReadOnlyList<ReplayOrderDto>>> GetOrdersAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(sessionId, cancellationToken);
        if (!session.Succeeded)
        {
            return ServiceResult<IReadOnlyList<ReplayOrderDto>>.Fail(session.ErrorMessage!);
        }

        var orders = await _orderRepository.GetByTradingSessionIdAsync(session.Data!.TradingSessionId, cancellationToken);
        var dtos = orders
            .Where(order => order.Mode == TradingMode.Replay)
            .Select(order => new ReplayOrderDto
            {
                Id = order.Id,
                Mode = order.Mode.ToString(),
                Side = order.Side.ToString(),
                OrderType = order.OrderType.ToString(),
                Status = order.Status.ToString(),
                Price = order.Price,
                Quantity = order.Quantity,
                IsPostOnly = order.IsPostOnly
            })
            .ToList();

        return ServiceResult<IReadOnlyList<ReplayOrderDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<IReadOnlyList<ReplayTradeDto>>> GetTradesAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(sessionId, cancellationToken);
        if (!session.Succeeded)
        {
            return ServiceResult<IReadOnlyList<ReplayTradeDto>>.Fail(session.ErrorMessage!);
        }

        var trades = await _tradeRepository.GetByTradingSessionIdAsync(session.Data!.TradingSessionId, cancellationToken);
        var dtos = trades.Select(trade => new ReplayTradeDto
        {
            Id = trade.Id,
            Direction = trade.Direction.ToString(),
            EntryPrice = trade.EntryPrice,
            ExitPrice = trade.ExitPrice,
            Quantity = trade.Quantity,
            Status = trade.Status.ToString(),
            CloseReason = trade.CloseReason?.ToString(),
            NetPnl = trade.NetPnl,
            Fees = trade.Fees
        }).ToList();

        return ServiceResult<IReadOnlyList<ReplayTradeDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<IReadOnlyList<ReplayMissedOrderDto>>> GetMissedOrdersAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(sessionId, cancellationToken);
        if (!session.Succeeded)
        {
            return ServiceResult<IReadOnlyList<ReplayMissedOrderDto>>.Fail(session.ErrorMessage!);
        }

        var missedOrders = await _missedOrderRepository.GetByTradingSessionIdAsync(session.Data!.TradingSessionId, cancellationToken);
        var dtos = missedOrders.Select(order => new ReplayMissedOrderDto
        {
            Id = order.Id,
            RequestedPrice = order.RequestedPrice,
            Reason = order.Reason.ToString(),
            ExpiredAtUtc = order.ExpiredAtUtc
        }).ToList();

        return ServiceResult<IReadOnlyList<ReplayMissedOrderDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<IReadOnlyList<ReplayRiskDecisionDto>>> GetRiskDecisionsAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(sessionId, cancellationToken);
        if (!session.Succeeded)
        {
            return ServiceResult<IReadOnlyList<ReplayRiskDecisionDto>>.Fail(session.ErrorMessage!);
        }

        var decisions = await _riskDecisionRepository.GetByTradingSessionIdAsync(session.Data!.TradingSessionId, cancellationToken);
        var dtos = decisions.Select(decision => new ReplayRiskDecisionDto
        {
            Id = decision.Id,
            Decision = decision.Decision.ToString(),
            Reason = decision.Reason,
            RejectedRuleKey = decision.RejectedRuleKey,
            PositionSize = decision.PositionSize,
            StopLoss = decision.StopLoss,
            TakeProfit = decision.TakeProfit
        }).ToList();

        return ServiceResult<IReadOnlyList<ReplayRiskDecisionDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<IReadOnlyList<ReplayAiDecisionDto>>> GetAiDecisionsAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(sessionId, cancellationToken);
        if (!session.Succeeded)
        {
            return ServiceResult<IReadOnlyList<ReplayAiDecisionDto>>.Fail(session.ErrorMessage!);
        }

        var decisions = await _aiDecisionRepository.GetByTradingSessionIdAsync(session.Data!.TradingSessionId, cancellationToken);
        var dtos = decisions.Select(decision => new ReplayAiDecisionDto
        {
            Id = decision.Id,
            MarketRegime = decision.MarketRegime.ToString(),
            ConfidenceScore = decision.ConfidenceScore,
            Classification = decision.ConfidenceClassification ?? string.Empty,
            TradeAllowed = decision.TradeAllowed,
            Summary = decision.Summary ?? string.Empty,
            Explanation = decision.Explanation ?? string.Empty
        }).ToList();

        return ServiceResult<IReadOnlyList<ReplayAiDecisionDto>>.Ok(dtos);
    }

    private async Task<IndicatorSnapshot?> ResolveIndicatorSnapshotAsync(
        long sessionId,
        ReplaySession session,
        Candle candle,
        CancellationToken cancellationToken)
    {
        if (_stateStore.TryGet(sessionId, out var state)
            && state?.Dataset.IndicatorSnapshots.TryGetValue(candle.Id, out var runtimeSnapshot) == true)
        {
            return runtimeSnapshot;
        }

        return await _indicatorSnapshotRepository.GetByKeyAsync(
            session.SymbolId,
            session.Timeframe,
            candle.Id,
            cancellationToken);
    }

    private async Task<ServiceResult<ReplaySession>> RequireSessionAsync(long sessionId, CancellationToken cancellationToken)
    {
        var session = await _replaySessionRepository.GetByIdAsync(sessionId, cancellationToken);
        return session is null
            ? ServiceResult<ReplaySession>.Fail("Replay session was not found.")
            : ServiceResult<ReplaySession>.Ok(session);
    }
}
