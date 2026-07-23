using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Common;
using MomoQuant.Application.TradingSystems;
using MomoQuant.Application.TradingSystems.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

/// <summary>
/// SK LivePaper simulation endpoints. Simulated orders only — never real execution.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/trading-systems/sk/livepaper")]
public sealed class SkLivePaperController : ApiControllerBase
{
    private readonly ISkLivePaperSessionService _service;

    public SkLivePaperController(ISkLivePaperSessionService service) => _service = service;

    [HttpGet("defaults")]
    public IActionResult GetDefaults() => OkResponse(_service.GetDefaults());

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession([FromBody] CreateSkLivePaperSessionRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.CreateSessionAsync(request, cancellationToken);
        return FromResult(result);
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> ListSessions([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var result = await _service.ListSessionsAsync(limit, cancellationToken);
        return OkResponse(result.Data!);
    }

    [HttpGet("sessions/{id:long}")]
    public async Task<IActionResult> GetStatus(long id, CancellationToken cancellationToken)
    {
        var result = await _service.GetStatusAsync(id, cancellationToken);
        return FromResult(result);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPost("sessions/{id:long}/start")]
    public async Task<IActionResult> Start(long id, CancellationToken cancellationToken) =>
        FromResult(await _service.StartAsync(id, cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPost("sessions/{id:long}/pause")]
    public async Task<IActionResult> Pause(long id, CancellationToken cancellationToken) =>
        FromResult(await _service.PauseAsync(id, cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPost("sessions/{id:long}/resume")]
    public async Task<IActionResult> Resume(long id, CancellationToken cancellationToken) =>
        FromResult(await _service.ResumeAsync(id, cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPost("sessions/{id:long}/stop")]
    public async Task<IActionResult> Stop(long id, CancellationToken cancellationToken) =>
        FromResult(await _service.StopAsync(id, cancellationToken));

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPost("sessions/{id:long}/manual-close/{tradeId:long}")]
    public async Task<IActionResult> ManualClose(long id, long tradeId, CancellationToken cancellationToken) =>
        FromResult(await _service.ManualCloseTradeAsync(id, tradeId, cancellationToken));

    [HttpGet("sessions/{id:long}/candidates")]
    public async Task<IActionResult> GetCandidates(long id, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        var result = await _service.GetCandidatesAsync(id, limit, cancellationToken);
        return OkResponse(result.Data!);
    }

    [HttpGet("sessions/{id:long}/trades")]
    public async Task<IActionResult> GetTrades(long id, CancellationToken cancellationToken)
    {
        var result = await _service.GetTradesAsync(id, cancellationToken);
        return OkResponse(result.Data!);
    }

    [HttpGet("sessions/{id:long}/events")]
    public async Task<IActionResult> GetEvents(long id, [FromQuery] int limit = 200, CancellationToken cancellationToken = default)
    {
        var result = await _service.GetEventsAsync(id, limit, cancellationToken);
        return OkResponse(result.Data!);
    }

    [HttpGet("sessions/{id:long}/chart")]
    public async Task<IActionResult> GetChart(long id, CancellationToken cancellationToken)
    {
        var result = await _service.GetChartAsync(id, cancellationToken);
        return FromResult(result);
    }

    private IActionResult FromResult<T>(ServiceResult<T> result)
    {
        if (!result.Succeeded || result.Data is null)
        {
            var errors = result.ErrorField is null
                ? null
                : new List<ApiError>
                {
                    new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." }
                };

            return FailResponse(result.ErrorMessage ?? "Request failed.", StatusCodes.Status400BadRequest, errors);
        }

        return OkResponse(result.Data);
    }
}
