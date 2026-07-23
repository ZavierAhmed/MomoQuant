using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.StrategyBenchmarks;
using MomoQuant.Application.StrategyBenchmarks.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/strategy-benchmarks")]
public sealed class StrategyBenchmarksController : ApiControllerBase
{
    private readonly IStrategyBenchmarkService _benchmarkService;

    public StrategyBenchmarksController(IStrategyBenchmarkService benchmarkService)
    {
        _benchmarkService = benchmarkService;
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> Create([FromBody] CreateStrategyBenchmarkRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _benchmarkService.CreateAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Failed to create strategy benchmark.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data, "Strategy benchmark created.");
    }

    [HttpPost("preflight")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> Preflight([FromBody] StrategyBenchmarkPreflightRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _benchmarkService.PreflightAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Benchmark preflight failed.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PagedRequest request, CancellationToken cancellationToken)
    {
        var result = await _benchmarkService.GetPagedAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Failed to load strategy benchmarks.");
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken cancellationToken)
    {
        var result = await _benchmarkService.GetByIdAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Strategy benchmark was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/progress")]
    public async Task<IActionResult> GetProgress(long id, CancellationToken cancellationToken)
    {
        var result = await _benchmarkService.GetProgressAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Strategy benchmark progress was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/report")]
    public async Task<IActionResult> GetReport(long id, CancellationToken cancellationToken)
    {
        var result = await _benchmarkService.GetReportAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Strategy benchmark report was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/run-items")]
    public async Task<IActionResult> GetRunItems(long id, CancellationToken cancellationToken)
    {
        var result = await _benchmarkService.GetRunItemsAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Strategy benchmark run items were not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/diagnostics")]
    public async Task<IActionResult> GetDiagnostics(long id, CancellationToken cancellationToken)
    {
        var result = await _benchmarkService.GetDiagnosticsAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Strategy benchmark diagnostics were not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpPost("{id:long}/cancel")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public Task<IActionResult> Cancel(long id, CancellationToken cancellationToken) =>
        ExecuteControlAsync(id, _benchmarkService.CancelAsync, cancellationToken);

    [HttpPost("{id:long}/resume")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public Task<IActionResult> Resume(long id, CancellationToken cancellationToken) =>
        ExecuteControlAsync(id, _benchmarkService.ResumeAsync, cancellationToken);

    [HttpPost("{id:long}/restart")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public Task<IActionResult> Restart(long id, CancellationToken cancellationToken) =>
        ExecuteControlAsync(id, _benchmarkService.RestartAsync, cancellationToken);

    [HttpPost("{id:long}/retry-failed")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public Task<IActionResult> RetryFailed(long id, CancellationToken cancellationToken) =>
        ExecuteControlAsync(id, _benchmarkService.RetryFailedAsync, cancellationToken);

    [HttpPost("{id:long}/mark-stalled-failed")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public Task<IActionResult> MarkStalledFailed(long id, CancellationToken cancellationToken) =>
        ExecuteControlAsync(id, _benchmarkService.MarkStalledFailedAsync, cancellationToken);

    private async Task<IActionResult> ExecuteControlAsync(
        long id,
        Func<long, CancellationToken, Task<Application.Common.ServiceResult<StrategyBenchmarkRunDto>>> action,
        CancellationToken cancellationToken)
    {
        var result = await action(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Benchmark control action failed.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }
}
