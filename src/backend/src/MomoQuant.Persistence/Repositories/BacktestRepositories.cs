using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Backtesting;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.Sessions;
using MomoQuant.Domain.Trades;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Persistence.Repositories;

public sealed class BacktestRunRepository : IBacktestRunRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public BacktestRunRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<BacktestRun?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.BacktestRuns.AsNoTracking().FirstOrDefaultAsync(run => run.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<BacktestRun> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var query = _dbContext.BacktestRuns.AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(run => run.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task AddAsync(BacktestRun run, CancellationToken cancellationToken = default)
    {
        _dbContext.BacktestRuns.Add(run);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(BacktestRun run, CancellationToken cancellationToken = default)
    {
        _dbContext.BacktestRuns.Update(run);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class BacktestResultRepository : IBacktestResultRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public BacktestResultRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<BacktestResult?> GetByRunIdAsync(long backtestRunId, CancellationToken cancellationToken = default) =>
        _dbContext.BacktestResults.AsNoTracking()
            .FirstOrDefaultAsync(result => result.BacktestRunId == backtestRunId, cancellationToken);

    public Task AddAsync(BacktestResult result, CancellationToken cancellationToken = default)
    {
        _dbContext.BacktestResults.Add(result);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class BacktestEquityPointRepository : IBacktestEquityPointRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public BacktestEquityPointRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<IReadOnlyList<BacktestEquityPoint>> GetByRunIdAsync(long backtestRunId, CancellationToken cancellationToken = default) =>
        _dbContext.BacktestEquityPoints.AsNoTracking()
            .Where(point => point.BacktestRunId == backtestRunId)
            .OrderBy(point => point.TimestampUtc)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<BacktestEquityPoint>)task.Result, cancellationToken);

    public Task AddRangeAsync(IReadOnlyCollection<BacktestEquityPoint> points, CancellationToken cancellationToken = default)
    {
        _dbContext.BacktestEquityPoints.AddRange(points);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class BacktestStrategyResultRepository : IBacktestStrategyResultRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public BacktestStrategyResultRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<IReadOnlyList<BacktestStrategyResult>> GetByRunIdAsync(long backtestRunId, CancellationToken cancellationToken = default) =>
        _dbContext.BacktestStrategyResults.AsNoTracking()
            .Where(result => result.BacktestRunId == backtestRunId)
            .OrderBy(result => result.StrategyCode)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<BacktestStrategyResult>)task.Result, cancellationToken);

    public Task AddRangeAsync(IReadOnlyCollection<BacktestStrategyResult> results, CancellationToken cancellationToken = default)
    {
        _dbContext.BacktestStrategyResults.AddRange(results);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class BacktestSymbolResultRepository : IBacktestSymbolResultRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public BacktestSymbolResultRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<IReadOnlyList<BacktestSymbolResult>> GetByRunIdAsync(long backtestRunId, CancellationToken cancellationToken = default) =>
        _dbContext.BacktestSymbolResults.AsNoTracking()
            .Where(result => result.BacktestRunId == backtestRunId)
            .OrderBy(result => result.Symbol)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<BacktestSymbolResult>)task.Result, cancellationToken);

    public Task AddRangeAsync(IReadOnlyCollection<BacktestSymbolResult> results, CancellationToken cancellationToken = default)
    {
        _dbContext.BacktestSymbolResults.AddRange(results);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class TradingSessionRepository : ITradingSessionRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public TradingSessionRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<TradingSession?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.TradingSessions.AsNoTracking().FirstOrDefaultAsync(session => session.Id == id, cancellationToken);

    public Task AddAsync(TradingSession session, CancellationToken cancellationToken = default)
    {
        _dbContext.TradingSessions.Add(session);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(TradingSession session, CancellationToken cancellationToken = default)
    {
        _dbContext.TradingSessions.Update(session);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class TradeRepository : ITradeRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public TradeRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<IReadOnlyList<Trade>> GetByTradingSessionIdAsync(long tradingSessionId, CancellationToken cancellationToken = default) =>
        _dbContext.Trades.AsNoTracking()
            .Where(trade => trade.TradingSessionId == tradingSessionId)
            .OrderByDescending(trade => trade.OpenedAtUtc)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<Trade>)task.Result, cancellationToken);

    public Task AddAsync(Trade trade, CancellationToken cancellationToken = default)
    {
        _dbContext.Trades.Add(trade);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IReadOnlyCollection<Trade> trades, CancellationToken cancellationToken = default)
    {
        _dbContext.Trades.AddRange(trades);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class OrderRepository : IOrderRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public OrderRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<IReadOnlyList<Order>> GetByTradingSessionIdAsync(long tradingSessionId, CancellationToken cancellationToken = default) =>
        _dbContext.Orders.AsNoTracking()
            .Where(order => order.TradingSessionId == tradingSessionId)
            .OrderByDescending(order => order.RequestedAtUtc)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<Order>)task.Result, cancellationToken);

    public Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        _dbContext.Orders.Add(order);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IReadOnlyCollection<Order> orders, CancellationToken cancellationToken = default)
    {
        _dbContext.Orders.AddRange(orders);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class OrderFillRepository : IOrderFillRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public OrderFillRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<IReadOnlyList<OrderFill>> GetByTradingSessionIdAsync(long tradingSessionId, CancellationToken cancellationToken = default) =>
        _dbContext.OrderFills.AsNoTracking()
            .Join(
                _dbContext.Orders.AsNoTracking().Where(order => order.TradingSessionId == tradingSessionId),
                fill => fill.OrderId,
                order => order.Id,
                (fill, _) => fill)
            .OrderByDescending(fill => fill.FilledAtUtc)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<OrderFill>)task.Result, cancellationToken);

    public Task AddAsync(OrderFill fill, CancellationToken cancellationToken = default)
    {
        _dbContext.OrderFills.Add(fill);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IReadOnlyCollection<OrderFill> fills, CancellationToken cancellationToken = default)
    {
        _dbContext.OrderFills.AddRange(fills);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class MissedOrderRepository : IMissedOrderRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public MissedOrderRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public Task<IReadOnlyList<MissedOrder>> GetByTradingSessionIdAsync(long tradingSessionId, CancellationToken cancellationToken = default) =>
        _dbContext.MissedOrders.AsNoTracking()
            .Where(order => order.TradingSessionId == tradingSessionId)
            .OrderByDescending(order => order.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<MissedOrder>)task.Result, cancellationToken);

    public Task AddAsync(MissedOrder missedOrder, CancellationToken cancellationToken = default)
    {
        _dbContext.MissedOrders.Add(missedOrder);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(IReadOnlyCollection<MissedOrder> missedOrders, CancellationToken cancellationToken = default)
    {
        _dbContext.MissedOrders.AddRange(missedOrders);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
