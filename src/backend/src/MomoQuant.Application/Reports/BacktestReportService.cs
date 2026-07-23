using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Reports.Dtos;
using MomoQuant.Domain.Backtesting;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Application.Reports;

public interface IBacktestReportService
{
    Task<ServiceResult<BacktestReportDto>> GetReportAsync(long backtestRunId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<EquityCurvePointDto>>> GetEquityCurveAsync(long backtestRunId, CancellationToken cancellationToken = default);

    Task<ServiceResult<DrawdownReportDto>> GetDrawdownAsync(long backtestRunId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<StrategyPerformanceReportDto>>> GetStrategyPerformanceAsync(long backtestRunId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<SymbolPerformanceReportDto>>> GetSymbolPerformanceAsync(long backtestRunId, CancellationToken cancellationToken = default);

    Task<ServiceResult<RiskRejectionReportDto>> GetRiskRejectionsAsync(long backtestRunId, ReportQuery query, CancellationToken cancellationToken = default);

    Task<ServiceResult<AiDecisionReportDto>> GetAiDecisionsAsync(long backtestRunId, ReportQuery query, CancellationToken cancellationToken = default);

    Task<ServiceResult<MissedOrderReportDto>> GetMissedOrdersAsync(long backtestRunId, ReportQuery query, CancellationToken cancellationToken = default);
}

public sealed class BacktestReportService : IBacktestReportService
{
    private readonly IBacktestRunRepository _runRepository;
    private readonly IBacktestResultRepository _resultRepository;
    private readonly IBacktestEquityPointRepository _equityPointRepository;
    private readonly IBacktestStrategyResultRepository _strategyResultRepository;
    private readonly IBacktestSymbolResultRepository _symbolResultRepository;
    private readonly IStrategyRepository _strategyRepository;
    private readonly IRiskReportService _riskReportService;
    private readonly IAiReportService _aiReportService;
    private readonly IExecutionReportService _executionReportService;

    public BacktestReportService(
        IBacktestRunRepository runRepository,
        IBacktestResultRepository resultRepository,
        IBacktestEquityPointRepository equityPointRepository,
        IBacktestStrategyResultRepository strategyResultRepository,
        IBacktestSymbolResultRepository symbolResultRepository,
        IStrategyRepository strategyRepository,
        IRiskReportService riskReportService,
        IAiReportService aiReportService,
        IExecutionReportService executionReportService)
    {
        _runRepository = runRepository;
        _resultRepository = resultRepository;
        _equityPointRepository = equityPointRepository;
        _strategyResultRepository = strategyResultRepository;
        _symbolResultRepository = symbolResultRepository;
        _strategyRepository = strategyRepository;
        _riskReportService = riskReportService;
        _aiReportService = aiReportService;
        _executionReportService = executionReportService;
    }

    public async Task<ServiceResult<BacktestReportDto>> GetReportAsync(long backtestRunId, CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(backtestRunId, cancellationToken);
        if (run is null)
        {
            return ServiceResult<BacktestReportDto>.Fail("Backtest run was not found.");
        }

        var result = await _resultRepository.GetByRunIdAsync(backtestRunId, cancellationToken);
        if (result is null)
        {
            return ServiceResult<BacktestReportDto>.Fail("Backtest results were not found.");
        }

        return ServiceResult<BacktestReportDto>.Ok(MapReport(run, result));
    }

    public async Task<ServiceResult<IReadOnlyList<EquityCurvePointDto>>> GetEquityCurveAsync(
        long backtestRunId,
        CancellationToken cancellationToken = default)
    {
        if (await _runRepository.GetByIdAsync(backtestRunId, cancellationToken) is null)
        {
            return ServiceResult<IReadOnlyList<EquityCurvePointDto>>.Fail("Backtest run was not found.");
        }

        var points = await _equityPointRepository.GetByRunIdAsync(backtestRunId, cancellationToken);
        return ServiceResult<IReadOnlyList<EquityCurvePointDto>>.Ok(points.Select(point => new EquityCurvePointDto
        {
            TimestampUtc = point.TimestampUtc,
            Balance = point.Balance,
            Equity = point.Equity,
            Drawdown = point.Drawdown,
            DrawdownPercent = point.DrawdownPercent,
            OpenPositionCount = point.OpenPositionCount
        }).ToList());
    }

    public async Task<ServiceResult<DrawdownReportDto>> GetDrawdownAsync(long backtestRunId, CancellationToken cancellationToken = default)
    {
        var equityResult = await GetEquityCurveAsync(backtestRunId, cancellationToken);
        if (!equityResult.Succeeded || equityResult.Data is null)
        {
            return ServiceResult<DrawdownReportDto>.Fail(equityResult.ErrorMessage!);
        }

        return ServiceResult<DrawdownReportDto>.Ok(ReportMetrics.CalculateDrawdown(equityResult.Data));
    }

    public async Task<ServiceResult<IReadOnlyList<StrategyPerformanceReportDto>>> GetStrategyPerformanceAsync(
        long backtestRunId,
        CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(backtestRunId, cancellationToken);
        if (run is null)
        {
            return ServiceResult<IReadOnlyList<StrategyPerformanceReportDto>>.Fail("Backtest run was not found.");
        }

        var results = await _strategyResultRepository.GetByRunIdAsync(backtestRunId, cancellationToken);
        var strategies = await _strategyRepository.GetAllAsync(cancellationToken);
        var lookup = strategies.ToDictionary(strategy => strategy.Code, strategy => strategy);

        var dtos = results.Select(result =>
        {
            lookup.TryGetValue(result.StrategyCode, out var strategy);
            return new StrategyPerformanceReportDto
            {
                StrategyId = strategy?.Id ?? 0,
                StrategyCode = result.StrategyCode.ToCode(),
                StrategyName = strategy?.Name ?? result.StrategyCode.ToCode(),
                Mode = TradingMode.Backtest.ToString(),
                TotalSignals = result.TotalSignals,
                EntrySignals = result.TotalSignals,
                NoTradeSignals = 0,
                ApprovedSignals = result.ApprovedSignals,
                RejectedSignals = result.RejectedSignals,
                TotalTrades = result.TotalTrades,
                WinningTrades = result.WinningTrades,
                LosingTrades = result.LosingTrades,
                WinRatePercent = result.WinRatePercent,
                NetPnl = result.NetPnl,
                GrossProfit = result.NetPnl > 0 ? result.NetPnl : 0m,
                GrossLoss = result.NetPnl < 0 ? Math.Abs(result.NetPnl) : 0m,
                ProfitFactor = result.ProfitFactor,
                AveragePnl = result.TotalTrades > 0 ? result.NetPnl / result.TotalTrades : 0m,
                AverageConfidenceScore = result.AverageConfidenceScore,
                AverageRewardRisk = 0m,
                MaxDrawdownPercent = result.MaxDrawdownPercent,
                TotalFees = 0m,
                MissedOrders = 0
            };
        }).ToList();

        return ServiceResult<IReadOnlyList<StrategyPerformanceReportDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<IReadOnlyList<SymbolPerformanceReportDto>>> GetSymbolPerformanceAsync(
        long backtestRunId,
        CancellationToken cancellationToken = default)
    {
        if (await _runRepository.GetByIdAsync(backtestRunId, cancellationToken) is null)
        {
            return ServiceResult<IReadOnlyList<SymbolPerformanceReportDto>>.Fail("Backtest run was not found.");
        }

        var results = await _symbolResultRepository.GetByRunIdAsync(backtestRunId, cancellationToken);
        var dtos = results.Select(result => new SymbolPerformanceReportDto
        {
            SymbolId = result.SymbolId,
            Symbol = result.Symbol,
            Timeframe = TimeframeParser.ToApiString(result.Timeframe),
            Mode = TradingMode.Backtest.ToString(),
            TotalSignals = 0,
            TotalTrades = result.TotalTrades,
            WinningTrades = result.WinningTrades,
            LosingTrades = result.LosingTrades,
            WinRatePercent = result.WinRatePercent,
            NetPnl = result.NetPnl,
            GrossProfit = result.NetPnl > 0 ? result.NetPnl : 0m,
            GrossLoss = result.NetPnl < 0 ? Math.Abs(result.NetPnl) : 0m,
            ProfitFactor = result.ProfitFactor,
            AveragePnl = result.TotalTrades > 0 ? result.NetPnl / result.TotalTrades : 0m,
            MaxDrawdownPercent = result.MaxDrawdownPercent,
            TotalFees = result.TotalFees,
            MissedOrders = result.MissedOrders
        }).ToList();

        return ServiceResult<IReadOnlyList<SymbolPerformanceReportDto>>.Ok(dtos);
    }

    public Task<ServiceResult<RiskRejectionReportDto>> GetRiskRejectionsAsync(
        long backtestRunId,
        ReportQuery query,
        CancellationToken cancellationToken = default) =>
        _riskReportService.GetForBacktestAsync(backtestRunId, query, cancellationToken);

    public Task<ServiceResult<AiDecisionReportDto>> GetAiDecisionsAsync(
        long backtestRunId,
        ReportQuery query,
        CancellationToken cancellationToken = default) =>
        _aiReportService.GetForBacktestAsync(backtestRunId, query, cancellationToken);

    public Task<ServiceResult<MissedOrderReportDto>> GetMissedOrdersAsync(
        long backtestRunId,
        ReportQuery query,
        CancellationToken cancellationToken = default) =>
        _executionReportService.GetMissedOrdersForBacktestAsync(backtestRunId, query, cancellationToken);

    private static BacktestReportDto MapReport(BacktestRun run, BacktestResult result) => new()
    {
        BacktestRunId = run.Id,
        Name = run.Name,
        Status = run.Status.ToString(),
        FromUtc = run.StartDateUtc,
        ToUtc = run.EndDateUtc,
        InitialBalance = result.InitialBalance,
        FinalBalance = result.FinalBalance,
        NetPnl = result.NetPnl,
        NetPnlPercent = result.NetPnlPercent,
        GrossProfit = result.GrossProfit,
        GrossLoss = result.GrossLoss,
        ProfitFactor = result.ProfitFactor,
        MaxDrawdown = result.MaxDrawdown,
        MaxDrawdownPercent = result.MaxDrawdownPercent,
        TotalTrades = result.TotalTrades,
        WinningTrades = result.WinningTrades,
        LosingTrades = result.LosingTrades,
        BreakEvenTrades = result.BreakEvenTrades,
        WinRatePercent = result.WinRatePercent,
        AverageWin = result.AverageWin,
        AverageLoss = result.AverageLoss,
        LargestWin = result.LargestWin,
        LargestLoss = result.LargestLoss,
        AverageRewardRisk = result.AverageRewardRisk,
        TotalFees = result.TotalFees,
        TotalSignals = result.TotalSignals,
        ApprovedSignals = result.ApprovedSignals,
        RejectedSignals = result.RejectedSignals,
        MissedOrders = result.MissedOrders,
        FilledOrders = result.FilledOrders,
        CancelledOrders = result.CancelledOrders
    };
}
