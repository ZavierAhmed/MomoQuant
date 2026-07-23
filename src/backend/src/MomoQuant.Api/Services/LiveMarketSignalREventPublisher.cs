using Microsoft.AspNetCore.SignalR;
using MomoQuant.Api.Hubs;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.LiveMarket.Dtos;
using MomoQuant.Application.MarketData;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Api.Services;

public sealed class LiveMarketSignalREventPublisher : ILiveMarketEventPublisher
{
    private readonly IHubContext<LiveMarketHub> _hubContext;

    public LiveMarketSignalREventPublisher(IHubContext<LiveMarketHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task PublishCandleUpdatedAsync(LiveCandleUpdate update, CancellationToken cancellationToken = default) =>
        _hubContext.Clients.All.SendAsync("LiveCandleUpdated", LiveMarketMapper.MapCandle(update), cancellationToken);

    public Task PublishCandleClosedAsync(LiveCandleUpdate update, CancellationToken cancellationToken = default) =>
        _hubContext.Clients.All.SendAsync("LiveCandleClosed", LiveMarketMapper.MapCandle(update), cancellationToken);

    public Task PublishSnapshotUpdatedAsync(LiveMarketSnapshot snapshot, CancellationToken cancellationToken = default) =>
        _hubContext.Clients.All.SendAsync("LiveMarketSnapshotUpdated", LiveMarketMapper.MapSnapshot(snapshot), cancellationToken);

    public Task PublishConnectionStatusAsync(LiveMarketConnectionStatus status, CancellationToken cancellationToken = default)
    {
        var eventName = status.Connected
            ? "LiveMarketConnected"
            : status.Subscriptions.Any(subscription => subscription.Status == LiveSubscriptionStatus.Reconnecting)
                ? "LiveMarketReconnecting"
                : "LiveMarketDisconnected";

        return _hubContext.Clients.All.SendAsync(eventName, LiveMarketMapper.MapStatus(status), cancellationToken);
    }

    public Task PublishPaperSessionUpdatedAsync(long symbolId, Timeframe timeframe, CancellationToken cancellationToken = default) =>
        _hubContext.Clients.All.SendAsync(
            "PaperSessionUpdated",
            new { symbolId, timeframe = TimeframeParser.ToApiString(timeframe), updatedAtUtc = DateTime.UtcNow },
            cancellationToken);
}
