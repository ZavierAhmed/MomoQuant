using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Reports;
using MomoQuant.Domain.Ai;
using MomoQuant.Domain.Backtesting;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Sessions;
using MomoQuant.Domain.Signals;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Persistence.Repositories;

public sealed class ReportDataRepository : IReportDataRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public ReportDataRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<int> CountBacktestRunsAsync(ReportQueryFilter filter, CancellationToken cancellationToken = default) =>
        _dbContext.BacktestRuns.AsNoTracking()
            .Where(run => run.CreatedAtUtc >= filter.FromUtc && run.CreatedAtUtc <= filter.ToUtc)
            .CountAsync(cancellationToken);

    public Task<int> CountPaperSessionsAsync(ReportQueryFilter filter, CancellationToken cancellationToken = default) =>
        _dbContext.PaperTradingSessions.AsNoTracking()
            .Where(session => session.CreatedAtUtc >= filter.FromUtc && session.CreatedAtUtc <= filter.ToUtc)
            .CountAsync(cancellationToken);

    public async Task<IReadOnlyList<Trade>> GetTradesAsync(ReportQueryFilter filter, CancellationToken cancellationToken = default)
    {
        var query = BuildTradeQuery(filter);
        return await query.OrderByDescending(trade => trade.CreatedAtUtc).Take(filter.Limit * 10).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(ReportQueryFilter filter, CancellationToken cancellationToken = default)
    {
        var query = BuildOrderQuery(filter);
        return await query.OrderByDescending(order => order.RequestedAtUtc).Take(filter.Limit * 10).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StrategySignal>> GetSignalsAsync(ReportQueryFilter filter, CancellationToken cancellationToken = default)
    {
        var query = BuildSignalQuery(filter);
        return await query.OrderByDescending(signal => signal.CreatedAtUtc).Take(filter.Limit * 10).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RiskDecision>> GetRiskDecisionsAsync(ReportQueryFilter filter, CancellationToken cancellationToken = default)
    {
        var query = BuildRiskDecisionQuery(filter);
        return await query.OrderByDescending(decision => decision.CreatedAtUtc).Take(filter.Limit * 10).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiDecision>> GetAiDecisionsAsync(ReportQueryFilter filter, CancellationToken cancellationToken = default)
    {
        var query = BuildAiDecisionQuery(filter);
        return await query.OrderByDescending(decision => decision.CreatedAtUtc).Take(filter.Limit * 10).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MissedOrder>> GetMissedOrdersAsync(ReportQueryFilter filter, CancellationToken cancellationToken = default)
    {
        var query = BuildMissedOrderQuery(filter);
        return await query.OrderByDescending(order => order.CreatedAtUtc).Take(filter.Limit * 10).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TradingSession>> GetTradingSessionsAsync(
        ReportQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.TradingSessions.AsNoTracking()
            .Where(session => session.CreatedAtUtc >= filter.FromUtc && session.CreatedAtUtc <= filter.ToUtc);

        if (filter.Mode.HasValue)
        {
            query = query.Where(session => session.Mode == filter.Mode.Value);
        }

        if (filter.TradingSessionId.HasValue)
        {
            query = query.Where(session => session.Id == filter.TradingSessionId.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<long, TradingMode>> GetSessionModesAsync(
        IReadOnlyCollection<long> sessionIds,
        CancellationToken cancellationToken = default)
    {
        if (sessionIds.Count == 0)
        {
            return new Dictionary<long, TradingMode>();
        }

        return await _dbContext.TradingSessions.AsNoTracking()
            .Where(session => sessionIds.Contains(session.Id))
            .ToDictionaryAsync(session => session.Id, session => session.Mode, cancellationToken);
    }

    public Task<PaperTradingSession?> GetPaperSessionAsync(long paperSessionId, CancellationToken cancellationToken = default) =>
        _dbContext.PaperTradingSessions.AsNoTracking()
            .FirstOrDefaultAsync(session => session.Id == paperSessionId, cancellationToken);

    private IQueryable<Trade> BuildTradeQuery(ReportQueryFilter filter)
    {
        var query = from trade in _dbContext.Trades.AsNoTracking()
                    join session in _dbContext.TradingSessions.AsNoTracking() on trade.TradingSessionId equals session.Id
                    where trade.CreatedAtUtc >= filter.FromUtc && trade.CreatedAtUtc <= filter.ToUtc
                    select trade;

        if (filter.TradingSessionId.HasValue)
        {
            query = query.Where(trade => trade.TradingSessionId == filter.TradingSessionId.Value);
        }

        if (filter.SymbolId.HasValue)
        {
            query = query.Where(trade => trade.SymbolId == filter.SymbolId.Value);
        }

        if (filter.StrategyId.HasValue)
        {
            query = query.Where(trade => trade.StrategyId == filter.StrategyId.Value);
        }

        if (filter.Mode.HasValue)
        {
            query = from trade in query
                    join session in _dbContext.TradingSessions.AsNoTracking() on trade.TradingSessionId equals session.Id
                    where session.Mode == filter.Mode.Value
                    select trade;
        }

        return query;
    }

    private IQueryable<Order> BuildOrderQuery(ReportQueryFilter filter)
    {
        var query = _dbContext.Orders.AsNoTracking()
            .Where(order => order.CreatedAtUtc >= filter.FromUtc && order.CreatedAtUtc <= filter.ToUtc);

        if (filter.TradingSessionId.HasValue)
        {
            query = query.Where(order => order.TradingSessionId == filter.TradingSessionId.Value);
        }

        if (filter.SymbolId.HasValue)
        {
            query = query.Where(order => order.SymbolId == filter.SymbolId.Value);
        }

        if (filter.Mode.HasValue)
        {
            query = query.Where(order => order.Mode == filter.Mode.Value);
        }

        return query;
    }

    private IQueryable<StrategySignal> BuildSignalQuery(ReportQueryFilter filter)
    {
        var query = from signal in _dbContext.StrategySignals.AsNoTracking()
                    where signal.CreatedAtUtc >= filter.FromUtc && signal.CreatedAtUtc <= filter.ToUtc
                    select signal;

        if (filter.TradingSessionId.HasValue)
        {
            query = query.Where(signal => signal.TradingSessionId == filter.TradingSessionId.Value);
        }

        if (filter.SymbolId.HasValue)
        {
            query = query.Where(signal => signal.SymbolId == filter.SymbolId.Value);
        }

        if (filter.StrategyId.HasValue)
        {
            query = query.Where(signal => signal.StrategyId == filter.StrategyId.Value);
        }

        if (filter.Timeframe.HasValue)
        {
            query = query.Where(signal => signal.Timeframe == filter.Timeframe.Value);
        }

        if (filter.Mode.HasValue)
        {
            query = from signal in query
                    join session in _dbContext.TradingSessions.AsNoTracking() on signal.TradingSessionId equals session.Id
                    where session.Mode == filter.Mode.Value
                    select signal;
        }

        return query;
    }

    private IQueryable<RiskDecision> BuildRiskDecisionQuery(ReportQueryFilter filter)
    {
        var query = _dbContext.RiskDecisions.AsNoTracking()
            .Where(decision => decision.CreatedAtUtc >= filter.FromUtc && decision.CreatedAtUtc <= filter.ToUtc);

        if (filter.TradingSessionId.HasValue)
        {
            query = query.Where(decision => decision.TradingSessionId == filter.TradingSessionId.Value);
        }

        if (filter.SymbolId.HasValue)
        {
            query = query.Where(decision => decision.SymbolId == filter.SymbolId.Value);
        }

        if (filter.Mode.HasValue)
        {
            query = from decision in query
                    join session in _dbContext.TradingSessions.AsNoTracking() on decision.TradingSessionId equals session.Id
                    where session.Mode == filter.Mode.Value
                    select decision;
        }

        return query;
    }

    private IQueryable<AiDecision> BuildAiDecisionQuery(ReportQueryFilter filter)
    {
        var query = _dbContext.AiDecisions.AsNoTracking()
            .Where(decision => decision.CreatedAtUtc >= filter.FromUtc && decision.CreatedAtUtc <= filter.ToUtc);

        if (filter.TradingSessionId.HasValue)
        {
            query = query.Where(decision => decision.TradingSessionId == filter.TradingSessionId.Value);
        }

        if (filter.SymbolId.HasValue)
        {
            query = query.Where(decision => decision.SymbolId == filter.SymbolId.Value);
        }

        if (filter.Timeframe.HasValue)
        {
            query = query.Where(decision => decision.Timeframe == filter.Timeframe.Value);
        }

        if (filter.MarketRegime.HasValue)
        {
            query = query.Where(decision => decision.MarketRegime == filter.MarketRegime.Value);
        }

        if (filter.Mode.HasValue)
        {
            query = from decision in query
                    join session in _dbContext.TradingSessions.AsNoTracking() on decision.TradingSessionId equals session.Id
                    where session.Mode == filter.Mode.Value
                    select decision;
        }

        return query;
    }

    private IQueryable<MissedOrder> BuildMissedOrderQuery(ReportQueryFilter filter)
    {
        var query = _dbContext.MissedOrders.AsNoTracking()
            .Where(order => order.CreatedAtUtc >= filter.FromUtc && order.CreatedAtUtc <= filter.ToUtc);

        if (filter.TradingSessionId.HasValue)
        {
            query = query.Where(order => order.TradingSessionId == filter.TradingSessionId.Value);
        }

        if (filter.SymbolId.HasValue)
        {
            query = query.Where(order => order.SymbolId == filter.SymbolId.Value);
        }

        if (filter.Mode.HasValue)
        {
            query = from order in query
                    join session in _dbContext.TradingSessions.AsNoTracking() on order.TradingSessionId equals session.Id
                    where session.Mode == filter.Mode.Value
                    select order;
        }

        return query;
    }
}
