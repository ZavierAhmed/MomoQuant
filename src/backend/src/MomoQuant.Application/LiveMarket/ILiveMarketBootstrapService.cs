using MomoQuant.Application.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.LiveMarket;

public enum MarketSituationDataSource
{
    None,
    LiveSnapshot,
    LatestClosedCandle,
    StoredHistorical,
    BootstrapHistorical
}

public sealed class LiveBootstrapResult
{
    public required string DataSource { get; init; }
    public int CandleCountUsed { get; init; }
    public bool IndicatorsAvailable { get; init; }
    public DateTime? LatestCandleTimeUtc { get; init; }
    public int CandlesInserted { get; init; }
}

public interface ILiveMarketBootstrapService
{
    Task<ServiceResult<LiveBootstrapResult>> EnsureWarmupAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        CancellationToken cancellationToken = default);
}
