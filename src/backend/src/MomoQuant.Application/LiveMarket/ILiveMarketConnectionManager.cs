using MomoQuant.Application.Common;
using MomoQuant.Application.LiveMarket.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.LiveMarket;

public interface ILiveMarketConnectionManager
{
    bool IsAvailable { get; }

    bool IsConnected { get; }

    LiveMarketConnectionStatus GetStatus();

    LiveMarketDiagnosticsDto GetDiagnostics();

    bool IsSubscribed(long symbolId, Timeframe timeframe);

    void LinkSession(long sessionId, long symbolId, Timeframe timeframe);

    void UnlinkSession(long sessionId);

    Task<ServiceResult<LiveMarketStatusDto>> SubscribeAsync(
        LiveMarketSubscribeRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<LiveMarketStatusDto>> UnsubscribeAsync(
        LiveMarketSubscribeRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<LiveMarketStatusDto>> ReconnectAsync(CancellationToken cancellationToken = default);

    event Action<LiveCandleUpdate>? CandleUpdated;

    event Action<LiveCandleUpdate>? CandleClosed;

    event Action<LiveMarketConnectionStatus>? ConnectionStatusChanged;
}

public interface ILiveMarketEventPublisher
{
    Task PublishCandleUpdatedAsync(LiveCandleUpdate update, CancellationToken cancellationToken = default);

    Task PublishCandleClosedAsync(LiveCandleUpdate update, CancellationToken cancellationToken = default);

    Task PublishSnapshotUpdatedAsync(LiveMarketSnapshot snapshot, CancellationToken cancellationToken = default);

    Task PublishConnectionStatusAsync(LiveMarketConnectionStatus status, CancellationToken cancellationToken = default);

    Task PublishPaperSessionUpdatedAsync(long symbolId, Timeframe timeframe, CancellationToken cancellationToken = default);
}

public sealed class NullLiveMarketEventPublisher : ILiveMarketEventPublisher
{
    public Task PublishCandleUpdatedAsync(LiveCandleUpdate update, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task PublishCandleClosedAsync(LiveCandleUpdate update, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task PublishSnapshotUpdatedAsync(LiveMarketSnapshot snapshot, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task PublishConnectionStatusAsync(LiveMarketConnectionStatus status, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task PublishPaperSessionUpdatedAsync(long symbolId, Timeframe timeframe, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
