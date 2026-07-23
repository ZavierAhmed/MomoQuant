using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Reports.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Application.Reports;

public interface IPaperTradingReportService
{
    Task<ServiceResult<PaperSessionReportDto>> GetReportAsync(long paperSessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<EquityCurvePointDto>>> GetEquityCurveAsync(long paperSessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<DrawdownReportDto>> GetDrawdownAsync(long paperSessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<StrategyPerformanceReportDto>>> GetStrategyPerformanceAsync(long paperSessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<SymbolPerformanceReportDto>>> GetSymbolPerformanceAsync(long paperSessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<RiskRejectionReportDto>> GetRiskRejectionsAsync(long paperSessionId, ReportQuery query, CancellationToken cancellationToken = default);

    Task<ServiceResult<AiDecisionReportDto>> GetAiDecisionsAsync(long paperSessionId, ReportQuery query, CancellationToken cancellationToken = default);

    Task<ServiceResult<MissedOrderReportDto>> GetMissedOrdersAsync(long paperSessionId, ReportQuery query, CancellationToken cancellationToken = default);
}

public sealed class PaperTradingReportService : IPaperTradingReportService
{
    private readonly IPaperTradingSessionRepository _sessionRepository;
    private readonly IPaperAccountRepository _accountRepository;
    private readonly IPaperAccountSnapshotRepository _snapshotRepository;
    private readonly IReportDataRepository _reportDataRepository;
    private readonly IStrategyPerformanceReportService _strategyPerformanceReportService;
    private readonly ISymbolPerformanceReportService _symbolPerformanceReportService;
    private readonly IRiskReportService _riskReportService;
    private readonly IAiReportService _aiReportService;
    private readonly IExecutionReportService _executionReportService;

    public PaperTradingReportService(
        IPaperTradingSessionRepository sessionRepository,
        IPaperAccountRepository accountRepository,
        IPaperAccountSnapshotRepository snapshotRepository,
        IReportDataRepository reportDataRepository,
        IStrategyPerformanceReportService strategyPerformanceReportService,
        ISymbolPerformanceReportService symbolPerformanceReportService,
        IRiskReportService riskReportService,
        IAiReportService aiReportService,
        IExecutionReportService executionReportService)
    {
        _sessionRepository = sessionRepository;
        _accountRepository = accountRepository;
        _snapshotRepository = snapshotRepository;
        _reportDataRepository = reportDataRepository;
        _strategyPerformanceReportService = strategyPerformanceReportService;
        _symbolPerformanceReportService = symbolPerformanceReportService;
        _riskReportService = riskReportService;
        _aiReportService = aiReportService;
        _executionReportService = executionReportService;
    }

    public async Task<ServiceResult<PaperSessionReportDto>> GetReportAsync(
        long paperSessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdAsync(paperSessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<PaperSessionReportDto>.Fail("Paper session was not found.");
        }

        var account = await _accountRepository.GetByIdAsync(session.PaperAccountId, cancellationToken);
        var filter = BuildSessionFilter(session);
        var trades = await _reportDataRepository.GetTradesAsync(filter, cancellationToken);
        var orders = await _reportDataRepository.GetOrdersAsync(filter, cancellationToken);
        var signals = await _reportDataRepository.GetSignalsAsync(filter, cancellationToken);
        var missedOrders = await _reportDataRepository.GetMissedOrdersAsync(filter, cancellationToken);
        var riskDecisions = await _reportDataRepository.GetRiskDecisionsAsync(filter, cancellationToken);

        var closedTrades = trades.Where(trade => trade.Status == TradeStatus.Closed).ToList();
        var analysis = ReportMetrics.AnalyzeTrades(closedTrades);
        var snapshots = await _snapshotRepository.GetByAccountIdAsync(session.PaperAccountId, cancellationToken);
        var sessionSnapshots = snapshots.Where(snapshot => snapshot.PaperSessionId == paperSessionId).ToList();
        var maxDrawdown = sessionSnapshots.Count > 0 ? sessionSnapshots.Max(snapshot => snapshot.Drawdown) : 0m;
        var maxDrawdownPercent = sessionSnapshots.Count > 0 ? sessionSnapshots.Max(snapshot => snapshot.DrawdownPercent) : 0m;

        return ServiceResult<PaperSessionReportDto>.Ok(new PaperSessionReportDto
        {
            PaperSessionId = session.Id,
            PaperAccountId = session.PaperAccountId,
            Name = session.Name,
            Status = session.Status.ToString(),
            Mode = session.Mode.ToString(),
            InitialBalance = account?.InitialBalance ?? 0m,
            CurrentBalance = account?.CurrentBalance ?? 0m,
            CurrentEquity = account?.CurrentEquity ?? 0m,
            RealizedPnl = account?.TotalRealizedPnl ?? closedTrades.Sum(trade => trade.NetPnl),
            UnrealizedPnl = account?.TotalUnrealizedPnl ?? 0m,
            TotalFees = account?.TotalFees ?? closedTrades.Sum(trade => trade.Fees),
            MaxDrawdown = maxDrawdown,
            MaxDrawdownPercent = maxDrawdownPercent,
            TotalTrades = closedTrades.Count,
            WinningTrades = analysis.Winning,
            LosingTrades = analysis.Losing,
            WinRatePercent = ReportMetrics.CalculateWinRate(analysis.Winning, analysis.Losing, analysis.BreakEven),
            ProfitFactor = ReportMetrics.CalculateProfitFactor(analysis.GrossProfit, analysis.GrossLoss),
            TotalOrders = orders.Count,
            FilledOrders = orders.Count(order => order.Status == OrderStatus.Filled),
            MissedOrders = missedOrders.Count,
            RejectedSignals = riskDecisions.Count(decision => decision.Decision == RiskDecisionType.Rejected),
            GeneratedAtUtc = DateTime.UtcNow
        });
    }

    public async Task<ServiceResult<IReadOnlyList<EquityCurvePointDto>>> GetEquityCurveAsync(
        long paperSessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdAsync(paperSessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<IReadOnlyList<EquityCurvePointDto>>.Fail("Paper session was not found.");
        }

        var snapshots = await _snapshotRepository.GetByAccountIdAsync(session.PaperAccountId, cancellationToken);
        return ServiceResult<IReadOnlyList<EquityCurvePointDto>>.Ok(
            snapshots
                .Where(snapshot => snapshot.PaperSessionId == paperSessionId)
                .OrderBy(snapshot => snapshot.TimestampUtc)
                .Select(snapshot => new EquityCurvePointDto
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

    public async Task<ServiceResult<DrawdownReportDto>> GetDrawdownAsync(
        long paperSessionId,
        CancellationToken cancellationToken = default)
    {
        var equityResult = await GetEquityCurveAsync(paperSessionId, cancellationToken);
        if (!equityResult.Succeeded || equityResult.Data is null)
        {
            return ServiceResult<DrawdownReportDto>.Fail(equityResult.ErrorMessage!);
        }

        return ServiceResult<DrawdownReportDto>.Ok(ReportMetrics.CalculateDrawdown(equityResult.Data));
    }

    public Task<ServiceResult<IReadOnlyList<StrategyPerformanceReportDto>>> GetStrategyPerformanceAsync(
        long paperSessionId,
        CancellationToken cancellationToken = default) =>
        _strategyPerformanceReportService.GetForPaperSessionAsync(paperSessionId, cancellationToken);

    public Task<ServiceResult<IReadOnlyList<SymbolPerformanceReportDto>>> GetSymbolPerformanceAsync(
        long paperSessionId,
        CancellationToken cancellationToken = default) =>
        _symbolPerformanceReportService.GetForPaperSessionAsync(paperSessionId, cancellationToken);

    public Task<ServiceResult<RiskRejectionReportDto>> GetRiskRejectionsAsync(
        long paperSessionId,
        ReportQuery query,
        CancellationToken cancellationToken = default) =>
        _riskReportService.GetForPaperSessionAsync(paperSessionId, query, cancellationToken);

    public Task<ServiceResult<AiDecisionReportDto>> GetAiDecisionsAsync(
        long paperSessionId,
        ReportQuery query,
        CancellationToken cancellationToken = default) =>
        _aiReportService.GetForPaperSessionAsync(paperSessionId, query, cancellationToken);

    public Task<ServiceResult<MissedOrderReportDto>> GetMissedOrdersAsync(
        long paperSessionId,
        ReportQuery query,
        CancellationToken cancellationToken = default) =>
        _executionReportService.GetMissedOrdersForPaperSessionAsync(paperSessionId, query, cancellationToken);

    private static ReportQueryFilter BuildSessionFilter(Domain.PaperTrading.PaperTradingSession session) => new()
    {
        FromUtc = session.FromUtc ?? DateTime.MinValue,
        ToUtc = session.ToUtc ?? DateTime.UtcNow,
        TradingSessionId = session.TradingSessionId,
        Mode = TradingMode.Paper,
        Limit = 500
    };
}
