using MomoQuant.Domain.Backtesting;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.Sessions;
using MomoQuant.Domain.Signals;
using MomoQuant.Domain.Trades;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Abstractions;

public interface IBacktestRunRepository
{
    Task<BacktestRun?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<BacktestRun> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default);

    Task AddAsync(BacktestRun run, CancellationToken cancellationToken = default);

    Task UpdateAsync(BacktestRun run, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IBacktestResultRepository
{
    Task<BacktestResult?> GetByRunIdAsync(long backtestRunId, CancellationToken cancellationToken = default);

    Task AddAsync(BacktestResult result, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IBacktestEquityPointRepository
{
    Task<IReadOnlyList<BacktestEquityPoint>> GetByRunIdAsync(long backtestRunId, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyCollection<BacktestEquityPoint> points, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IBacktestStrategyResultRepository
{
    Task<IReadOnlyList<BacktestStrategyResult>> GetByRunIdAsync(long backtestRunId, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyCollection<BacktestStrategyResult> results, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IBacktestSymbolResultRepository
{
    Task<IReadOnlyList<BacktestSymbolResult>> GetByRunIdAsync(long backtestRunId, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyCollection<BacktestSymbolResult> results, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ITradingSessionRepository
{
    Task<TradingSession?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task AddAsync(TradingSession session, CancellationToken cancellationToken = default);

    Task UpdateAsync(TradingSession session, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface ITradeRepository
{
    Task<IReadOnlyList<Trade>> GetByTradingSessionIdAsync(long tradingSessionId, CancellationToken cancellationToken = default);

    Task AddAsync(Trade trade, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyCollection<Trade> trades, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IOrderRepository
{
    Task<IReadOnlyList<Order>> GetByTradingSessionIdAsync(long tradingSessionId, CancellationToken cancellationToken = default);

    Task AddAsync(Order order, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyCollection<Order> orders, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IOrderFillRepository
{
    Task<IReadOnlyList<OrderFill>> GetByTradingSessionIdAsync(long tradingSessionId, CancellationToken cancellationToken = default);

    Task AddAsync(OrderFill fill, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyCollection<OrderFill> fills, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IMissedOrderRepository
{
    Task<IReadOnlyList<MissedOrder>> GetByTradingSessionIdAsync(long tradingSessionId, CancellationToken cancellationToken = default);

    Task AddAsync(MissedOrder missedOrder, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyCollection<MissedOrder> missedOrders, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
