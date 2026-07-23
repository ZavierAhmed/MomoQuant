using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.PaperTrading;
using MomoQuant.Application.PaperTrading.Dtos;
using MomoQuant.Application.Trading;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
public sealed class PaperTradingController : ApiControllerBase
{
    private readonly IPaperAccountService _accountService;
    private readonly IPaperSessionService _sessionService;
    private readonly IPaperSessionControlService _controlService;
    private readonly IPaperSessionQueryService _queryService;
    private readonly ILivePaperChartService _livePaperChartService;
    private readonly IPipelineDiagnosticsService _pipelineDiagnosticsService;

    public PaperTradingController(
        IPaperAccountService accountService,
        IPaperSessionService sessionService,
        IPaperSessionControlService controlService,
        IPaperSessionQueryService queryService,
        ILivePaperChartService livePaperChartService,
        IPipelineDiagnosticsService pipelineDiagnosticsService)
    {
        _accountService = accountService;
        _sessionService = sessionService;
        _controlService = controlService;
        _queryService = queryService;
        _livePaperChartService = livePaperChartService;
        _pipelineDiagnosticsService = pipelineDiagnosticsService;
    }

    [HttpGet("api/v1/paper/accounts")]
    public async Task<IActionResult> GetAccounts([FromQuery] PagedRequest request, CancellationToken cancellationToken)
    {
        var result = await _accountService.GetPagedAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Failed to load paper accounts.");
        }

        return OkResponse(result.Data);
    }

    [HttpGet("api/v1/paper/accounts/{id:long}")]
    public async Task<IActionResult> GetAccount(long id, CancellationToken cancellationToken)
    {
        var result = await _accountService.GetByIdAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Paper account was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpPost("api/v1/paper/accounts")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> CreateAccount([FromBody] CreatePaperAccountRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _accountService.CreateAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Failed to create paper account.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }

    [HttpPut("api/v1/paper/accounts/{id:long}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> UpdateAccount(long id, [FromBody] UpdatePaperAccountRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _accountService.UpdateAsync(id, request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Failed to update paper account.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }

    [HttpPost("api/v1/paper/accounts/{id:long}/reset")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> ResetAccount(long id, CancellationToken cancellationToken)
    {
        var result = await _accountService.ResetAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Failed to reset paper account.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("api/v1/paper/accounts/{id:long}/snapshots")]
    public async Task<IActionResult> GetAccountSnapshots(long id, CancellationToken cancellationToken)
    {
        var result = await _accountService.GetSnapshotsAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Paper account snapshots were not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpPost("api/v1/paper/sessions")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> CreateSession([FromBody] CreatePaperSessionRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _sessionService.CreateAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Failed to create paper session.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("api/v1/paper/sessions")]
    public async Task<IActionResult> GetSessions([FromQuery] PagedRequest request, CancellationToken cancellationToken)
    {
        var result = await _sessionService.GetPagedAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Failed to load paper sessions.");
        }

        return OkResponse(result.Data);
    }

    [HttpGet("api/v1/paper/sessions/{id:long}")]
    public async Task<IActionResult> GetSession(long id, CancellationToken cancellationToken)
    {
        var result = await _sessionService.GetByIdAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Paper session was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpPost("api/v1/paper/sessions/{id:long}/start")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> StartSession(long id, CancellationToken cancellationToken) =>
        await ExecuteControlAsync(id, _controlService.StartAsync, cancellationToken);

    [HttpPost("api/v1/paper/sessions/{id:long}/pause")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> PauseSession(long id, CancellationToken cancellationToken) =>
        await ExecuteControlAsync(id, _controlService.PauseAsync, cancellationToken);

    [HttpPost("api/v1/paper/sessions/{id:long}/resume")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> ResumeSession(long id, CancellationToken cancellationToken) =>
        await ExecuteControlAsync(id, _controlService.ResumeAsync, cancellationToken);

    [HttpPost("api/v1/paper/sessions/{id:long}/stop")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> StopSession(long id, CancellationToken cancellationToken) =>
        await ExecuteControlAsync(id, _controlService.StopAsync, cancellationToken);

    [HttpPost("api/v1/paper/sessions/{id:long}/tick")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> TickSession(long id, CancellationToken cancellationToken) =>
        await ExecuteControlAsync(id, _controlService.TickAsync, cancellationToken);

    [HttpGet("api/v1/paper/sessions/{id:long}/diagnostics")]
    public async Task<IActionResult> GetSessionDiagnostics(long id, CancellationToken cancellationToken)
    {
        var result = await _pipelineDiagnosticsService.GetForPaperSessionAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Paper session diagnostics were not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("api/v1/paper/sessions/{id:long}/status")]
    public async Task<IActionResult> GetSessionStatus(long id, CancellationToken cancellationToken)
    {
        var result = await _queryService.GetStatusAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Paper session status was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("api/v1/paper/sessions/{id:long}/live-status")]
    public async Task<IActionResult> GetSessionLiveStatus(long id, CancellationToken cancellationToken)
    {
        var result = await _queryService.GetLiveStatusAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Paper session live status was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("api/v1/paper/sessions/{id:long}/live-chart")]
    public async Task<IActionResult> GetSessionLiveChart(
        long id,
        [FromQuery] long? symbolId,
        [FromQuery] string? timeframe,
        [FromQuery] int limit = 300,
        CancellationToken cancellationToken = default)
    {
        var result = await _livePaperChartService.GetChartAsync(id, symbolId, timeframe, limit, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "LivePaper chart was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("api/v1/paper/sessions/{id:long}/orders")]
    public async Task<IActionResult> GetOrders(long id, CancellationToken cancellationToken) =>
        await ExecuteListAsync(id, _queryService.GetOrdersAsync, cancellationToken);

    [HttpGet("api/v1/paper/sessions/{id:long}/fills")]
    public async Task<IActionResult> GetFills(long id, CancellationToken cancellationToken) =>
        await ExecuteListAsync(id, _queryService.GetFillsAsync, cancellationToken);

    [HttpGet("api/v1/paper/sessions/{id:long}/positions")]
    public async Task<IActionResult> GetPositions(long id, CancellationToken cancellationToken) =>
        await ExecuteListAsync(id, _queryService.GetPositionsAsync, cancellationToken);

    [HttpGet("api/v1/paper/sessions/{id:long}/trades")]
    public async Task<IActionResult> GetTrades(long id, CancellationToken cancellationToken) =>
        await ExecuteListAsync(id, _queryService.GetTradesAsync, cancellationToken);

    [HttpGet("api/v1/paper/sessions/{id:long}/missed-orders")]
    public async Task<IActionResult> GetMissedOrders(long id, CancellationToken cancellationToken) =>
        await ExecuteListAsync(id, _queryService.GetMissedOrdersAsync, cancellationToken);

    [HttpGet("api/v1/paper/sessions/{id:long}/equity-curve")]
    public async Task<IActionResult> GetEquityCurve(long id, CancellationToken cancellationToken) =>
        await ExecuteListAsync(id, _queryService.GetEquityCurveAsync, cancellationToken);

    [HttpGet("api/v1/paper/sessions/{id:long}/signals")]
    public async Task<IActionResult> GetSignals(long id, CancellationToken cancellationToken) =>
        await ExecuteListAsync(id, _queryService.GetSignalsAsync, cancellationToken);

    [HttpGet("api/v1/paper/sessions/{id:long}/risk-decisions")]
    public async Task<IActionResult> GetRiskDecisions(long id, CancellationToken cancellationToken) =>
        await ExecuteListAsync(id, _queryService.GetRiskDecisionsAsync, cancellationToken);

    [HttpGet("api/v1/paper/sessions/{id:long}/ai-decisions")]
    public async Task<IActionResult> GetAiDecisions(long id, CancellationToken cancellationToken) =>
        await ExecuteListAsync(id, _queryService.GetAiDecisionsAsync, cancellationToken);

    private async Task<IActionResult> ExecuteControlAsync(
        long id,
        Func<long, CancellationToken, Task<Application.Common.ServiceResult<PaperSessionControlResponse>>> action,
        CancellationToken cancellationToken)
    {
        var result = await action(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Paper session control action failed.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }

    private async Task<IActionResult> ExecuteListAsync<T>(
        long id,
        Func<long, CancellationToken, Task<Application.Common.ServiceResult<IReadOnlyList<T>>>> action,
        CancellationToken cancellationToken)
    {
        var result = await action(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Paper session data was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }
}
