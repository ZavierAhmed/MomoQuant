using MomoQuant.Application.Reports;
using MomoQuant.Domain.Ai;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Execution;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Sessions;
using MomoQuant.Domain.Signals;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Application.Abstractions;

public interface IReportDataRepository
{
    Task<int> CountBacktestRunsAsync(ReportQueryFilter filter, CancellationToken cancellationToken = default);

    Task<int> CountPaperSessionsAsync(ReportQueryFilter filter, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Trade>> GetTradesAsync(ReportQueryFilter filter, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> GetOrdersAsync(ReportQueryFilter filter, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StrategySignal>> GetSignalsAsync(ReportQueryFilter filter, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RiskDecision>> GetRiskDecisionsAsync(ReportQueryFilter filter, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiDecision>> GetAiDecisionsAsync(ReportQueryFilter filter, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MissedOrder>> GetMissedOrdersAsync(ReportQueryFilter filter, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TradingSession>> GetTradingSessionsAsync(ReportQueryFilter filter, CancellationToken cancellationToken = default);

    Task<Dictionary<long, TradingMode>> GetSessionModesAsync(IReadOnlyCollection<long> sessionIds, CancellationToken cancellationToken = default);

    Task<PaperTradingSession?> GetPaperSessionAsync(long paperSessionId, CancellationToken cancellationToken = default);
}
