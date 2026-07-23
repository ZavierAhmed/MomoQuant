using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.LiveMarket.Dtos;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Authorize]
public sealed class LiveMarketController : ApiControllerBase
{
    private readonly ILiveMarketConnectionManager _connectionManager;
    private readonly ILiveMarketSnapshotStore _snapshotStore;

    public LiveMarketController(
        ILiveMarketConnectionManager connectionManager,
        ILiveMarketSnapshotStore snapshotStore)
    {
        _connectionManager = connectionManager;
        _snapshotStore = snapshotStore;
    }

    [HttpGet("api/v1/live-market/status")]
    public IActionResult GetStatus()
    {
        return OkResponse(LiveMarketMapper.MapStatus(_connectionManager.GetStatus()));
    }

    [HttpGet("api/v1/live-market/diagnostics")]
    public IActionResult GetDiagnostics()
    {
        return OkResponse(_connectionManager.GetDiagnostics());
    }

    [HttpGet("api/v1/live-market/snapshots")]
    public IActionResult GetSnapshots()
    {
        var snapshots = _snapshotStore.GetAll().Select(LiveMarketMapper.MapSnapshot).ToList();
        return OkResponse(snapshots);
    }

    [HttpGet("api/v1/live-market/snapshots/{symbolId:long}")]
    public IActionResult GetSnapshot(long symbolId, [FromQuery] string timeframe = "3m")
    {
        var snapshot = _snapshotStore.Get(symbolId, timeframe);
        if (snapshot is null)
        {
            return FailResponse("Live market snapshot was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(LiveMarketMapper.MapSnapshot(snapshot));
    }

    [HttpPost("api/v1/live-market/subscribe")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> Subscribe([FromBody] LiveMarketSubscribeRequest request, CancellationToken cancellationToken)
    {
        var result = await _connectionManager.SubscribeAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Failed to subscribe.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }

    [HttpPost("api/v1/live-market/unsubscribe")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> Unsubscribe([FromBody] LiveMarketSubscribeRequest request, CancellationToken cancellationToken)
    {
        var result = await _connectionManager.UnsubscribeAsync(request, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Failed to unsubscribe.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }

    [HttpPost("api/v1/live-market/reconnect")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    public async Task<IActionResult> Reconnect(CancellationToken cancellationToken)
    {
        var result = await _connectionManager.ReconnectAsync(cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Failed to reconnect.", StatusCodes.Status400BadRequest);
        }

        return OkResponse(result.Data);
    }
}
