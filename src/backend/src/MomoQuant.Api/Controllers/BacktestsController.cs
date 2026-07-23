using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Backtesting.Dtos;
using MomoQuant.Application.Common;
using MomoQuant.Application.Trading;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/backtests")]
public sealed class BacktestsController : ApiControllerBase
{
    private readonly IBacktestRunner _backtestRunner;
    private readonly IBacktestReportService _reportService;
    private readonly IPipelineDiagnosticsService _pipelineDiagnosticsService;

    public BacktestsController(
        IBacktestRunner backtestRunner,
        IBacktestReportService reportService,
        IPipelineDiagnosticsService pipelineDiagnosticsService)
    {
        _backtestRunner = backtestRunner;
        _reportService = reportService;
        _pipelineDiagnosticsService = pipelineDiagnosticsService;
    }

    [HttpPost("run")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> RunBacktest([FromBody] RunBacktestRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        try
        {
            request = TimeframeRequestNormalizer.Normalize(request);
        }
        catch (ArgumentException ex)
        {
            return FailResponse(ex.Message, StatusCodes.Status400BadRequest);
        }

        var result = await _backtestRunner.RunAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorField is not null
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status500InternalServerError;

            return FailResponse(result.ErrorMessage ?? "Backtest run failed.", statusCode);
        }

        return OkResponse(result.Data);
    }

    [HttpGet]
    public async Task<IActionResult> GetBacktests([FromQuery] PagedRequest request, CancellationToken cancellationToken)
    {
        var result = await _reportService.GetRunsAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Failed to load backtests.");
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetBacktest(long id, CancellationToken cancellationToken)
    {
        var result = await _reportService.GetRunByIdAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Backtest run was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/diagnostics")]
    public async Task<IActionResult> GetDiagnostics(long id, CancellationToken cancellationToken)
    {
        var result = await _pipelineDiagnosticsService.GetForBacktestAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Backtest diagnostics were not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/results")]
    public async Task<IActionResult> GetResults(long id, CancellationToken cancellationToken)
    {
        var result = await _reportService.GetResultsAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Backtest results were not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/trades")]
    public async Task<IActionResult> GetTrades(long id, CancellationToken cancellationToken)
    {
        var result = await _reportService.GetTradesAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Backtest trades were not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/orders")]
    public async Task<IActionResult> GetOrders(long id, CancellationToken cancellationToken)
    {
        var result = await _reportService.GetOrdersAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Backtest orders were not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/missed-orders")]
    public async Task<IActionResult> GetMissedOrders(long id, CancellationToken cancellationToken)
    {
        var result = await _reportService.GetMissedOrdersAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Backtest missed orders were not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/equity-curve")]
    public async Task<IActionResult> GetEquityCurve(long id, CancellationToken cancellationToken)
    {
        var result = await _reportService.GetEquityCurveAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Backtest equity curve was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/strategy-breakdown")]
    public async Task<IActionResult> GetStrategyBreakdown(long id, CancellationToken cancellationToken)
    {
        var result = await _reportService.GetStrategyBreakdownAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Backtest strategy breakdown was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/symbol-breakdown")]
    public async Task<IActionResult> GetSymbolBreakdown(long id, CancellationToken cancellationToken)
    {
        var result = await _reportService.GetSymbolBreakdownAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Backtest symbol breakdown was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }
}
