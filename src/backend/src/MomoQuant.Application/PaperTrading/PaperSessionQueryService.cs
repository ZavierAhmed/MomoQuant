using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.PaperTrading.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Domain.Signals;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Application.PaperTrading;

public interface IPaperSessionQueryService
{
    Task<ServiceResult<PaperSessionStatusDto>> GetStatusAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<PaperOrderDto>>> GetOrdersAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<PaperFillDto>>> GetFillsAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<PaperPositionDto>>> GetPositionsAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<PaperTradeDto>>> GetTradesAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<PaperMissedOrderDto>>> GetMissedOrdersAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<PaperEquityPointDto>>> GetEquityCurveAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<PaperSignalDto>>> GetSignalsAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<PaperRiskDecisionDto>>> GetRiskDecisionsAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<PaperAiDecisionDto>>> GetAiDecisionsAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<PaperSessionLiveStatusDto>> GetLiveStatusAsync(long sessionId, CancellationToken cancellationToken = default);
}

public sealed class PaperSessionQueryService : IPaperSessionQueryService
{
    private readonly IPaperTradingSessionRepository _sessionRepository;
    private readonly IPaperAccountRepository _accountRepository;
    private readonly IPaperAccountSnapshotRepository _snapshotRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderFillRepository _orderFillRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly ITradeRepository _tradeRepository;
    private readonly IMissedOrderRepository _missedOrderRepository;
    private readonly IStrategySignalRepository _signalRepository;
    private readonly IRiskDecisionRepository _riskDecisionRepository;
    private readonly IAiDecisionRepository _aiDecisionRepository;
    private readonly IPaperStateStore _stateStore;
    private readonly ILiveMarketConnectionManager _liveMarketConnectionManager;
    private readonly ILiveMarketSnapshotStore _liveSnapshotStore;
    private readonly ISymbolRepository _symbolRepository;

    public PaperSessionQueryService(
        IPaperTradingSessionRepository sessionRepository,
        IPaperAccountRepository accountRepository,
        IPaperAccountSnapshotRepository snapshotRepository,
        IOrderRepository orderRepository,
        IOrderFillRepository orderFillRepository,
        IPositionRepository positionRepository,
        ITradeRepository tradeRepository,
        IMissedOrderRepository missedOrderRepository,
        IStrategySignalRepository signalRepository,
        IRiskDecisionRepository riskDecisionRepository,
        IAiDecisionRepository aiDecisionRepository,
        IPaperStateStore stateStore,
        ILiveMarketConnectionManager liveMarketConnectionManager,
        ILiveMarketSnapshotStore liveSnapshotStore,
        ISymbolRepository symbolRepository)
    {
        _sessionRepository = sessionRepository;
        _accountRepository = accountRepository;
        _snapshotRepository = snapshotRepository;
        _orderRepository = orderRepository;
        _orderFillRepository = orderFillRepository;
        _positionRepository = positionRepository;
        _tradeRepository = tradeRepository;
        _missedOrderRepository = missedOrderRepository;
        _signalRepository = signalRepository;
        _riskDecisionRepository = riskDecisionRepository;
        _aiDecisionRepository = aiDecisionRepository;
        _stateStore = stateStore;
        _liveMarketConnectionManager = liveMarketConnectionManager;
        _liveSnapshotStore = liveSnapshotStore;
        _symbolRepository = symbolRepository;
    }

    public async Task<ServiceResult<PaperSessionStatusDto>> GetStatusAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<PaperSessionStatusDto>.Fail("Paper session was not found.");
        }

        var processedCandles = Math.Max(session.CurrentCandleIndex + 1, 0);
        var account = await _accountRepository.GetByIdAsync(session.PaperAccountId, cancellationToken);
        var openPositions = await _positionRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        var orders = await _orderRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        var trades = await _tradeRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        var missedOrders = await _missedOrderRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);

        if (session.Mode == PaperTradingMode.LivePaper)
        {
            return ServiceResult<PaperSessionStatusDto>.Ok(
                await BuildLivePaperStatusAsync(session, account, openPositions, orders, trades, missedOrders, processedCandles, cancellationToken));
        }

        var progressPercent = session.TotalCandles > 0
            ? Math.Round((decimal)processedCandles / session.TotalCandles * 100m, 2)
            : 0m;

        var warnings = new List<string>();
        if (session.TotalCandles == 0)
        {
            warnings.Add("No candles are configured for this paper session.");
        }

        if (session.Status == PaperSessionStatus.Running
            && session.TotalCandles > 0
            && session.CurrentCandleIndex >= session.TotalCandles - 1)
        {
            warnings.Add("Paper session has reached the final candle.");
        }

        return ServiceResult<PaperSessionStatusDto>.Ok(new PaperSessionStatusDto
        {
            SessionId = session.Id,
            PaperSessionId = session.Id,
            Status = session.Status.ToString(),
            Mode = session.Mode.ToString(),
            CurrentCandleIndex = session.CurrentCandleIndex,
            ProcessedCandles = processedCandles,
            TotalCandles = session.TotalCandles,
            ProgressPercent = progressPercent,
            ProgressLabel = null,
            CurrentCandleTimeUtc = session.CurrentCandleTimeUtc,
            CurrentBalance = account?.CurrentBalance ?? 0m,
            CurrentEquity = account?.CurrentEquity ?? 0m,
            OpenPositionCount = (openPositions ?? []).Count(position => position.Status == PositionStatus.Open),
            OrdersCount = (orders ?? []).Count(order => order.Mode == TradingMode.Paper),
            TradesCount = trades?.Count ?? 0,
            MissedOrdersCount = missedOrders?.Count ?? 0,
            LastUpdatedAtUtc = session.UpdatedAtUtc ?? session.CreatedAtUtc,
            Warnings = warnings
        });
    }

    private async Task<PaperSessionStatusDto> BuildLivePaperStatusAsync(
        PaperTradingSession session,
        PaperAccount? account,
        IReadOnlyList<Position> openPositions,
        IReadOnlyList<Order> orders,
        IReadOnlyList<Trade> trades,
        IReadOnlyList<MissedOrder> missedOrders,
        int processedCandles,
        CancellationToken cancellationToken)
    {
        _stateStore.TryGet(session.Id, out var state);
        var symbolIds = PaperConfigParser.ResolveSymbolIds(session, state);
        var timeframes = PaperConfigParser.ResolveTimeframes(session, state);

        var symbolStatuses = await BuildSymbolLiveStatusesAsync(symbolIds, timeframes, session, cancellationToken);
        var primary = symbolStatuses.FirstOrDefault();
        var sessionConnected = primary is { IsSubscribed: true } && _liveMarketConnectionManager.IsConnected;
        var warnings = BuildLivePaperWarnings(sessionConnected, primary, symbolIds, timeframes);

        return new PaperSessionStatusDto
        {
            SessionId = session.Id,
            PaperSessionId = session.Id,
            Status = session.Status.ToString(),
            Mode = session.Mode.ToString(),
            CurrentCandleIndex = session.CurrentCandleIndex,
            ProcessedCandles = Math.Max(session.CurrentCandleIndex + 1, 0),
            TotalCandles = null,
            ProgressPercent = null,
            ProgressLabel = "Live",
            CurrentCandleTimeUtc = primary?.CurrentCandleOpenTimeUtc ?? primary?.LastClosedCandleUtc ?? session.CurrentCandleTimeUtc,
            CurrentBalance = account?.CurrentBalance ?? 0m,
            CurrentEquity = account?.CurrentEquity ?? 0m,
            OpenPositionCount = (openPositions ?? []).Count(position => position.Status == PositionStatus.Open),
            OrdersCount = (orders ?? []).Count(order => order.Mode == TradingMode.Paper),
            TradesCount = trades?.Count ?? 0,
            MissedOrdersCount = missedOrders?.Count ?? 0,
            LastUpdatedAtUtc = session.UpdatedAtUtc ?? session.CreatedAtUtc,
            Connected = sessionConnected,
            LastLiveUpdateUtc = primary?.LastLiveUpdateUtc,
            LastClosedCandleUtc = primary?.LastClosedCandleUtc,
            LastProcessedCandleUtc = session.CurrentCandleTimeUtc,
            LatestPrice = primary?.LatestPrice,
            SubscribedSymbols = symbolStatuses.Select(status => status.Symbol).Distinct().ToList(),
            SubscribedTimeframes = symbolStatuses.Select(status => status.Timeframe).Distinct().ToList(),
            Warnings = warnings
        };
    }

    private List<string> BuildLivePaperWarnings(
        bool sessionConnected,
        PaperSymbolLiveStatusDto? primary,
        IReadOnlyList<long> symbolIds,
        IReadOnlyList<Timeframe> timeframes)
    {
        var warnings = new List<string>();

        if (symbolIds.Count == 0 || timeframes.Count == 0)
        {
            warnings.Add("Waiting for live market subscription data.");
            return warnings;
        }

        if (primary is { IsSubscribed: false })
        {
            warnings.Add(
                $"Live market provider connected, but session is not subscribed to {primary.Symbol} {primary.Timeframe}.");
            return warnings;
        }

        if (!sessionConnected)
        {
            warnings.Add("Live market connection is not active.");
            return warnings;
        }

        if (!string.IsNullOrWhiteSpace(primary?.StreamWarning))
        {
            warnings.Add(primary.StreamWarning);
            return warnings;
        }

        if (primary?.LastLiveUpdateUtc is null)
        {
            warnings.Add("Waiting for first live candle update.");
        }
        else if (primary.LastClosedCandleUtc is null)
        {
            warnings.Add($"Live price is updating. Waiting for the current {primary.Timeframe} candle to close before strategy evaluation.");
        }
        else if (primary.LastProcessedCandleUtc is null)
        {
            warnings.Add("Waiting for next closed live candle.");
        }

        return warnings;
    }

    private async Task<IReadOnlyList<PaperSymbolLiveStatusDto>> BuildSymbolLiveStatusesAsync(
        IReadOnlyList<long> symbolIds,
        IReadOnlyList<Timeframe> timeframes,
        PaperTradingSession session,
        CancellationToken cancellationToken)
    {
        var symbolStatuses = new List<PaperSymbolLiveStatusDto>();
        var diagnostics = _liveMarketConnectionManager.GetDiagnostics();

        foreach (var symbolId in symbolIds)
        {
            var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
            if (symbol is null)
            {
                continue;
            }

            foreach (var timeframe in timeframes)
            {
                var tf = TimeframeParser.ToApiString(timeframe);
                var liveSnapshot = _liveSnapshotStore.Get(symbolId, tf);
                var isSubscribed = _liveMarketConnectionManager.IsSubscribed(symbolId, timeframe);
                var streamName = $"{symbol.SymbolName.ToLowerInvariant()}@kline_{tf}";
                var streamDiag = diagnostics.Subscriptions.FirstOrDefault(item =>
                    item.SymbolId == symbolId
                    && string.Equals(item.Timeframe, tf, StringComparison.OrdinalIgnoreCase));

                symbolStatuses.Add(new PaperSymbolLiveStatusDto
                {
                    Symbol = symbol.SymbolName,
                    Timeframe = tf,
                    LastLiveUpdateUtc = liveSnapshot?.LastLiveUpdateUtc ?? liveSnapshot?.LastUpdateUtc,
                    LastClosedCandleUtc = liveSnapshot?.LastClosedCandleUtc,
                    LastProcessedCandleUtc = session.CurrentCandleTimeUtc,
                    LatestPrice = liveSnapshot?.LatestPrice,
                    CurrentCandleOpenTimeUtc = liveSnapshot?.OpenTimeUtc ?? liveSnapshot?.CurrentCandle?.OpenTimeUtc,
                    CurrentCandleCloseTimeUtc = liveSnapshot?.CloseTimeUtc ?? liveSnapshot?.CurrentCandle?.CloseTimeUtc,
                    IsSubscribed = isSubscribed,
                    StreamName = streamName,
                    StreamWarning = streamDiag?.Warning
                });
            }
        }

        return symbolStatuses;
    }

    public async Task<ServiceResult<IReadOnlyList<PaperOrderDto>>> GetOrdersAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<IReadOnlyList<PaperOrderDto>>.Fail("Paper session was not found.");
        }

        var orders = await _orderRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        return ServiceResult<IReadOnlyList<PaperOrderDto>>.Ok(
            orders.Where(order => order.Mode == TradingMode.Paper).Select(MapOrder).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<PaperFillDto>>> GetFillsAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<IReadOnlyList<PaperFillDto>>.Fail("Paper session was not found.");
        }

        var fills = await _orderFillRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        return ServiceResult<IReadOnlyList<PaperFillDto>>.Ok(fills.Select(MapFill).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<PaperPositionDto>>> GetPositionsAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<IReadOnlyList<PaperPositionDto>>.Fail("Paper session was not found.");
        }

        var positions = await _positionRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        return ServiceResult<IReadOnlyList<PaperPositionDto>>.Ok(positions.Select(MapPosition).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<PaperTradeDto>>> GetTradesAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<IReadOnlyList<PaperTradeDto>>.Fail("Paper session was not found.");
        }

        var trades = await _tradeRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        return ServiceResult<IReadOnlyList<PaperTradeDto>>.Ok(trades.Select(MapTrade).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<PaperMissedOrderDto>>> GetMissedOrdersAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<IReadOnlyList<PaperMissedOrderDto>>.Fail("Paper session was not found.");
        }

        var missed = await _missedOrderRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        return ServiceResult<IReadOnlyList<PaperMissedOrderDto>>.Ok(missed.Select(MapMissedOrder).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<PaperEquityPointDto>>> GetEquityCurveAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<IReadOnlyList<PaperEquityPointDto>>.Fail("Paper session was not found.");
        }

        var snapshots = await _snapshotRepository.GetByAccountIdAsync(session.PaperAccountId, cancellationToken);
        return ServiceResult<IReadOnlyList<PaperEquityPointDto>>.Ok(
            snapshots
                .Where(snapshot => snapshot.PaperSessionId == sessionId)
                .OrderBy(snapshot => snapshot.TimestampUtc)
                .Select(snapshot => new PaperEquityPointDto
                {
                    TimestampUtc = snapshot.TimestampUtc,
                    Balance = snapshot.Balance,
                    Equity = snapshot.Equity,
                    Drawdown = snapshot.Drawdown,
                    DrawdownPercent = snapshot.DrawdownPercent,
                    OpenPositionCount = snapshot.OpenPositionCount
                })
                .ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<PaperSignalDto>>> GetSignalsAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<IReadOnlyList<PaperSignalDto>>.Fail("Paper session was not found.");
        }

        var signals = await _signalRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        return ServiceResult<IReadOnlyList<PaperSignalDto>>.Ok(signals.Select(MapSignal).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<PaperRiskDecisionDto>>> GetRiskDecisionsAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<IReadOnlyList<PaperRiskDecisionDto>>.Fail("Paper session was not found.");
        }

        var decisions = await _riskDecisionRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        return ServiceResult<IReadOnlyList<PaperRiskDecisionDto>>.Ok(decisions.Select(MapRiskDecision).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<PaperAiDecisionDto>>> GetAiDecisionsAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<IReadOnlyList<PaperAiDecisionDto>>.Fail("Paper session was not found.");
        }

        var decisions = await _aiDecisionRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        return ServiceResult<IReadOnlyList<PaperAiDecisionDto>>.Ok(decisions.Select(MapAiDecision).ToList());
    }

    private Task<PaperTradingSession?> GetSessionAsync(long sessionId, CancellationToken cancellationToken) =>
        _sessionRepository.GetByIdAsync(sessionId, cancellationToken);

    private static PaperOrderDto MapOrder(Order order) => new()
    {
        Id = order.Id,
        SymbolId = order.SymbolId,
        Mode = order.Mode.ToString(),
        Side = order.Side.ToString(),
        OrderType = order.OrderType.ToString(),
        Price = order.Price,
        Quantity = order.Quantity,
        Status = order.Status.ToString(),
        RequestedAtUtc = order.RequestedAtUtc,
        FilledAtUtc = order.FilledAtUtc
    };

    private static PaperFillDto MapFill(OrderFill fill) => new()
    {
        Id = fill.Id,
        OrderId = fill.OrderId,
        FillPrice = fill.FillPrice,
        FillQuantity = fill.FillQuantity,
        Fee = fill.Fee,
        LiquidityType = fill.LiquidityType.ToString(),
        FilledAtUtc = fill.FilledAtUtc
    };

    private static PaperPositionDto MapPosition(Position position) => new()
    {
        Id = position.Id,
        SymbolId = position.SymbolId,
        Direction = position.Direction.ToString(),
        Quantity = position.Quantity,
        AverageEntryPrice = position.AverageEntryPrice,
        MarkPrice = position.MarkPrice,
        UnrealizedPnl = position.UnrealizedPnl,
        Status = position.Status.ToString(),
        OpenedAtUtc = position.OpenedAtUtc
    };

    private static PaperTradeDto MapTrade(Trade trade) => new()
    {
        Id = trade.Id,
        SymbolId = trade.SymbolId,
        Direction = trade.Direction.ToString(),
        EntryPrice = trade.EntryPrice,
        ExitPrice = trade.ExitPrice,
        Quantity = trade.Quantity,
        Status = trade.Status.ToString(),
        NetPnl = trade.NetPnl,
        Fees = trade.Fees,
        OpenedAtUtc = trade.OpenedAtUtc,
        ClosedAtUtc = trade.ClosedAtUtc
    };

    private static PaperMissedOrderDto MapMissedOrder(MissedOrder missed) => new()
    {
        Id = missed.Id,
        SymbolId = missed.SymbolId,
        RequestedPrice = missed.RequestedPrice,
        Reason = missed.Reason.ToString(),
        ExpiredAtUtc = missed.ExpiredAtUtc
    };

    private static PaperSignalDto MapSignal(StrategySignal signal) => new()
    {
        Id = signal.Id,
        StrategyId = signal.StrategyId,
        SymbolId = signal.SymbolId,
        Timeframe = signal.Timeframe.ToString(),
        SignalType = signal.SignalType.ToString(),
        Direction = signal.Direction.ToString(),
        Strength = signal.Strength,
        Reason = signal.Reason,
        CreatedAtUtc = signal.CreatedAtUtc
    };

    private static PaperRiskDecisionDto MapRiskDecision(Domain.Risk.RiskDecision decision) => new()
    {
        Id = decision.Id,
        SymbolId = decision.SymbolId,
        Decision = decision.Decision.ToString(),
        Reason = decision.Reason,
        RejectedRuleKey = decision.RejectedRuleKey,
        CreatedAtUtc = decision.CreatedAtUtc
    };

    private static PaperAiDecisionDto MapAiDecision(Domain.Ai.AiDecision decision) => new()
    {
        Id = decision.Id,
        SymbolId = decision.SymbolId,
        Timeframe = decision.Timeframe.ToString(),
        MarketRegime = decision.MarketRegime.ToString(),
        ConfidenceScore = decision.ConfidenceScore,
        TradeAllowed = decision.TradeAllowed,
        Summary = decision.Summary,
        CreatedAtUtc = decision.CreatedAtUtc
    };

    public async Task<ServiceResult<PaperSessionLiveStatusDto>> GetLiveStatusAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<PaperSessionLiveStatusDto>.Fail("Paper session was not found.");
        }

        if (session.Mode != PaperTradingMode.LivePaper)
        {
            return ServiceResult<PaperSessionLiveStatusDto>.Fail("Live status is only available for LivePaper sessions.", "mode");
        }

        var account = await _accountRepository.GetByIdAsync(session.PaperAccountId, cancellationToken);
        var openPositions = await _positionRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        var orders = await _orderRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        var trades = await _tradeRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        var missedOrders = await _missedOrderRepository.GetByTradingSessionIdAsync(session.TradingSessionId, cancellationToken);
        var processedCandles = Math.Max(session.CurrentCandleIndex + 1, 0);

        _stateStore.TryGet(sessionId, out var state);
        var symbolIds = PaperConfigParser.ResolveSymbolIds(session, state);
        var timeframes = PaperConfigParser.ResolveTimeframes(session, state);
        var symbolStatuses = await BuildSymbolLiveStatusesAsync(symbolIds, timeframes, session, cancellationToken);
        var primary = symbolStatuses.FirstOrDefault();
        var sessionConnected = primary is { IsSubscribed: true } && _liveMarketConnectionManager.IsConnected;
        var warnings = BuildLivePaperWarnings(sessionConnected, primary, symbolIds, timeframes);

        return ServiceResult<PaperSessionLiveStatusDto>.Ok(new PaperSessionLiveStatusDto
        {
            SessionId = session.Id,
            Status = session.Status.ToString(),
            Mode = session.Mode.ToString(),
            Connected = sessionConnected,
            ProgressLabel = "Live",
            ProcessedCandles = processedCandles,
            TotalCandles = null,
            ProgressPercent = null,
            CurrentCandleTimeUtc = primary?.CurrentCandleOpenTimeUtc ?? primary?.LastClosedCandleUtc ?? session.CurrentCandleTimeUtc,
            LastLiveUpdateUtc = primary?.LastLiveUpdateUtc,
            LastClosedCandleUtc = primary?.LastClosedCandleUtc,
            LastProcessedCandleUtc = session.CurrentCandleTimeUtc,
            LatestPrice = primary?.LatestPrice,
            SymbolStatuses = symbolStatuses,
            CurrentBalance = account?.CurrentBalance ?? 0m,
            CurrentEquity = account?.CurrentEquity ?? 0m,
            OpenPositionCount = (openPositions ?? []).Count(position => position.Status == PositionStatus.Open),
            OrdersCount = (orders ?? []).Count(order => order.Mode == TradingMode.Paper),
            TradesCount = trades?.Count ?? 0,
            MissedOrdersCount = missedOrders?.Count ?? 0,
            Warnings = warnings
        });
    }
}

