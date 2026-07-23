using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Common;
using MomoQuant.Application.Replay;
using MomoQuant.Application.Replay.Dtos;
using MomoQuant.Application.Trading;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/replay/sessions")]
public sealed class ReplayController : ApiControllerBase
{
    private readonly IReplaySessionService _sessionService;
    private readonly IReplayControlService _controlService;
    private readonly IReplayFrameService _frameService;
    private readonly IReplayChartService _chartService;
    private readonly IPipelineDiagnosticsService _pipelineDiagnosticsService;

    public ReplayController(
        IReplaySessionService sessionService,
        IReplayControlService controlService,
        IReplayFrameService frameService,
        IReplayChartService chartService,
        IPipelineDiagnosticsService pipelineDiagnosticsService)
    {
        _sessionService = sessionService;
        _controlService = controlService;
        _frameService = frameService;
        _chartService = chartService;
        _pipelineDiagnosticsService = pipelineDiagnosticsService;
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> CreateSession([FromBody] CreateReplaySessionRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        try
        {
            request = TimeframeRequestNormalizer.NormalizeReplay(request);
        }
        catch (ArgumentException ex)
        {
            return FailResponse(ex.Message, StatusCodes.Status400BadRequest);
        }

        var result = await _sessionService.CreateAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            var statusCode = result.ErrorField is not null
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status500InternalServerError;

            return FailResponse(result.ErrorMessage ?? "Failed to create replay session.", statusCode);
        }

        return OkResponse(result.Data);
    }

    [HttpGet]
    public async Task<IActionResult> GetSessions([FromQuery] PagedRequest request, CancellationToken cancellationToken)
    {
        var result = await _sessionService.GetPagedAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Failed to load replay sessions.");
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetSession(long id, CancellationToken cancellationToken)
    {
        var result = await _sessionService.GetByIdAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Replay session was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpPost("{id:long}/start")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> Start(long id, CancellationToken cancellationToken) =>
        await ExecuteControlAsync(id, _controlService.StartAsync, cancellationToken);

    [HttpPost("{id:long}/pause")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> Pause(long id, CancellationToken cancellationToken) =>
        await ExecuteControlAsync(id, _controlService.PauseAsync, cancellationToken);

    [HttpPost("{id:long}/resume")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> Resume(long id, CancellationToken cancellationToken) =>
        await ExecuteControlAsync(id, _controlService.ResumeAsync, cancellationToken);

    [HttpPost("{id:long}/stop")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> Stop(long id, CancellationToken cancellationToken) =>
        await ExecuteControlAsync(id, _controlService.StopAsync, cancellationToken);

    [HttpPost("{id:long}/step-forward")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> StepForward(long id, CancellationToken cancellationToken) =>
        await ExecuteControlAsync(id, _controlService.StepForwardAsync, cancellationToken);

    [HttpPost("{id:long}/step-backward")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> StepBackward(long id, CancellationToken cancellationToken) =>
        await ExecuteControlAsync(id, _controlService.StepBackwardAsync, cancellationToken);

    [HttpPut("{id:long}/speed")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> UpdateSpeed(long id, [FromBody] UpdateReplaySpeedRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationFailResponse();
        }

        var result = await _controlService.UpdateSpeedAsync(id, request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Failed to update replay speed.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/diagnostics")]
    public async Task<IActionResult> GetDiagnostics(long id, CancellationToken cancellationToken)
    {
        var result = await _pipelineDiagnosticsService.GetForReplaySessionAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Replay session diagnostics were not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/chart")]
    public async Task<IActionResult> GetChart(
        long id,
        [FromQuery] int? upToFrameIndex,
        [FromQuery] int? fromFrameIndex,
        [FromQuery] int? toFrameIndex,
        [FromQuery] bool includeFutureContext = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _chartService.GetChartAsync(
            id,
            new ReplayChartQuery
            {
                UpToFrameIndex = upToFrameIndex,
                FromFrameIndex = fromFrameIndex,
                ToFrameIndex = toFrameIndex,
                IncludeFutureContext = includeFutureContext
            },
            cancellationToken);

        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Replay chart data was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/chart-window")]
    public async Task<IActionResult> GetChartWindow(
        long id,
        [FromQuery] int currentFrameIndex,
        [FromQuery] int candlesBefore = 150,
        [FromQuery] int candlesAfter = 0,
        [FromQuery] bool includeFutureContext = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _chartService.GetChartAsync(
            id,
            new ReplayChartQuery
            {
                CurrentFrameIndex = currentFrameIndex,
                CandlesBefore = candlesBefore,
                CandlesAfter = candlesAfter,
                IncludeFutureContext = includeFutureContext
            },
            cancellationToken);

        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Replay chart data was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/current-frame")]
    public async Task<IActionResult> GetCurrentFrame(long id, CancellationToken cancellationToken)
    {
        var result = await _frameService.GetCurrentFrameAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Current replay frame was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/frames")]
    public async Task<IActionResult> GetFrames(long id, CancellationToken cancellationToken)
    {
        var result = await _frameService.GetFramesAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Replay frames were not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("{id:long}/signals")]
    public async Task<IActionResult> GetSignals(long id, CancellationToken cancellationToken) =>
        await ExecuteListAsync(id, _frameService.GetSignalsAsync, cancellationToken);

    [HttpGet("{id:long}/orders")]
    public async Task<IActionResult> GetOrders(long id, CancellationToken cancellationToken) =>
        await ExecuteListAsync(id, _frameService.GetOrdersAsync, cancellationToken);

    [HttpGet("{id:long}/trades")]
    public async Task<IActionResult> GetTrades(long id, CancellationToken cancellationToken) =>
        await ExecuteListAsync(id, _frameService.GetTradesAsync, cancellationToken);

    [HttpGet("{id:long}/missed-orders")]
    public async Task<IActionResult> GetMissedOrders(long id, CancellationToken cancellationToken) =>
        await ExecuteListAsync(id, _frameService.GetMissedOrdersAsync, cancellationToken);

    [HttpGet("{id:long}/risk-decisions")]
    public async Task<IActionResult> GetRiskDecisions(long id, CancellationToken cancellationToken) =>
        await ExecuteListAsync(id, _frameService.GetRiskDecisionsAsync, cancellationToken);

    [HttpGet("{id:long}/ai-decisions")]
    public async Task<IActionResult> GetAiDecisions(long id, CancellationToken cancellationToken) =>
        await ExecuteListAsync(id, _frameService.GetAiDecisionsAsync, cancellationToken);

    private async Task<IActionResult> ExecuteControlAsync(
        long id,
        Func<long, CancellationToken, Task<Application.Common.ServiceResult<ReplayControlResponse>>> action,
        CancellationToken cancellationToken)
    {
        var result = await action(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Replay control action failed.", StatusCodes.Status400BadRequest);
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
            return FailResponse(result.ErrorMessage ?? "Replay data was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }
}
