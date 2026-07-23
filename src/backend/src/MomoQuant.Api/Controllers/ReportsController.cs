using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application.Reports;
using MomoQuant.Application.Reports.Dtos;
using IReportsBacktestService = MomoQuant.Application.Reports.IBacktestReportService;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/reports")]
public sealed class ReportsController : ApiControllerBase
{
    private readonly IReportingService _reportingService;
    private readonly IReportsBacktestService _backtestReportService;
    private readonly IPaperTradingReportService _paperReportService;
    private readonly IStrategyPerformanceReportService _strategyPerformanceReportService;
    private readonly ISymbolPerformanceReportService _symbolPerformanceReportService;
    private readonly IRiskReportService _riskReportService;
    private readonly IAiReportService _aiReportService;
    private readonly IExecutionReportService _executionReportService;

    public ReportsController(
        IReportingService reportingService,
        IReportsBacktestService backtestReportService,
        IPaperTradingReportService paperReportService,
        IStrategyPerformanceReportService strategyPerformanceReportService,
        ISymbolPerformanceReportService symbolPerformanceReportService,
        IRiskReportService riskReportService,
        IAiReportService aiReportService,
        IExecutionReportService executionReportService)
    {
        _reportingService = reportingService;
        _backtestReportService = backtestReportService;
        _paperReportService = paperReportService;
        _strategyPerformanceReportService = strategyPerformanceReportService;
        _symbolPerformanceReportService = symbolPerformanceReportService;
        _riskReportService = riskReportService;
        _aiReportService = aiReportService;
        _executionReportService = executionReportService;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _reportingService.GetOverviewAsync(query, cancellationToken));

    [HttpGet("backtests/{backtestRunId:long}")]
    public async Task<IActionResult> GetBacktestReport(long backtestRunId, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _backtestReportService.GetReportAsync(backtestRunId, cancellationToken));

    [HttpGet("backtests/{backtestRunId:long}/equity-curve")]
    public async Task<IActionResult> GetBacktestEquityCurve(long backtestRunId, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _backtestReportService.GetEquityCurveAsync(backtestRunId, cancellationToken));

    [HttpGet("backtests/{backtestRunId:long}/drawdown")]
    public async Task<IActionResult> GetBacktestDrawdown(long backtestRunId, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _backtestReportService.GetDrawdownAsync(backtestRunId, cancellationToken));

    [HttpGet("backtests/{backtestRunId:long}/strategy-performance")]
    public async Task<IActionResult> GetBacktestStrategyPerformance(long backtestRunId, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _backtestReportService.GetStrategyPerformanceAsync(backtestRunId, cancellationToken));

    [HttpGet("backtests/{backtestRunId:long}/symbol-performance")]
    public async Task<IActionResult> GetBacktestSymbolPerformance(long backtestRunId, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _backtestReportService.GetSymbolPerformanceAsync(backtestRunId, cancellationToken));

    [HttpGet("backtests/{backtestRunId:long}/risk-rejections")]
    public async Task<IActionResult> GetBacktestRiskRejections(long backtestRunId, [FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _backtestReportService.GetRiskRejectionsAsync(backtestRunId, query, cancellationToken));

    [HttpGet("backtests/{backtestRunId:long}/ai-decisions")]
    public async Task<IActionResult> GetBacktestAiDecisions(long backtestRunId, [FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _backtestReportService.GetAiDecisionsAsync(backtestRunId, query, cancellationToken));

    [HttpGet("backtests/{backtestRunId:long}/missed-orders")]
    public async Task<IActionResult> GetBacktestMissedOrders(long backtestRunId, [FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _backtestReportService.GetMissedOrdersAsync(backtestRunId, query, cancellationToken));

    [HttpGet("paper/sessions/{paperSessionId:long}")]
    public async Task<IActionResult> GetPaperSessionReport(long paperSessionId, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _paperReportService.GetReportAsync(paperSessionId, cancellationToken));

    [HttpGet("paper/sessions/{paperSessionId:long}/equity-curve")]
    public async Task<IActionResult> GetPaperEquityCurve(long paperSessionId, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _paperReportService.GetEquityCurveAsync(paperSessionId, cancellationToken));

    [HttpGet("paper/sessions/{paperSessionId:long}/drawdown")]
    public async Task<IActionResult> GetPaperDrawdown(long paperSessionId, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _paperReportService.GetDrawdownAsync(paperSessionId, cancellationToken));

    [HttpGet("paper/sessions/{paperSessionId:long}/strategy-performance")]
    public async Task<IActionResult> GetPaperStrategyPerformance(long paperSessionId, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _paperReportService.GetStrategyPerformanceAsync(paperSessionId, cancellationToken));

    [HttpGet("paper/sessions/{paperSessionId:long}/symbol-performance")]
    public async Task<IActionResult> GetPaperSymbolPerformance(long paperSessionId, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _paperReportService.GetSymbolPerformanceAsync(paperSessionId, cancellationToken));

    [HttpGet("paper/sessions/{paperSessionId:long}/risk-rejections")]
    public async Task<IActionResult> GetPaperRiskRejections(long paperSessionId, [FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _paperReportService.GetRiskRejectionsAsync(paperSessionId, query, cancellationToken));

    [HttpGet("paper/sessions/{paperSessionId:long}/ai-decisions")]
    public async Task<IActionResult> GetPaperAiDecisions(long paperSessionId, [FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _paperReportService.GetAiDecisionsAsync(paperSessionId, query, cancellationToken));

    [HttpGet("paper/sessions/{paperSessionId:long}/missed-orders")]
    public async Task<IActionResult> GetPaperMissedOrders(long paperSessionId, [FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _paperReportService.GetMissedOrdersAsync(paperSessionId, query, cancellationToken));

    [HttpGet("strategies")]
    public async Task<IActionResult> GetStrategyPerformance([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _strategyPerformanceReportService.GetAsync(query, cancellationToken));

    [HttpGet("symbols")]
    public async Task<IActionResult> GetSymbolPerformance([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _symbolPerformanceReportService.GetAsync(query, cancellationToken));

    [HttpGet("risk")]
    public async Task<IActionResult> GetRiskReport([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _riskReportService.GetAsync(query, cancellationToken));

    [HttpGet("ai")]
    public async Task<IActionResult> GetAiReport([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _aiReportService.GetAsync(query, cancellationToken));

    [HttpGet("execution")]
    public async Task<IActionResult> GetExecutionReport([FromQuery] ReportQuery query, CancellationToken cancellationToken) =>
        await ExecuteAsync(() => _executionReportService.GetAsync(query, cancellationToken));

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<Application.Common.ServiceResult<T>>> action)
    {
        var result = await action();
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Report request failed.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }
}
