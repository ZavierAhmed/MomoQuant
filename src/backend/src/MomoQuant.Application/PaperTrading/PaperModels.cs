using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Backtesting.Simulation;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Domain.Risk;

namespace MomoQuant.Application.PaperTrading;

public sealed class PaperSessionSettings
{
    public required decimal MakerFeeRate { get; init; }
    public required decimal TakerFeeRate { get; init; }
    public required int OrderExpiryCandles { get; init; }
    public required bool UseAiScoring { get; init; }
    public bool StrictAiRequired { get; init; }
    public required decimal MinConfidenceScore { get; init; }
    public required decimal SlippagePercent { get; init; }
    public required Domain.Enums.ExecutionMode ExecutionMode { get; init; }
    public required IReadOnlyList<long> StrategyIds { get; init; }
    public required IReadOnlyList<long> SymbolIds { get; init; }
    public required IReadOnlyList<Domain.Enums.Timeframe> Timeframes { get; init; }
    public long? ParameterSetId { get; init; }
}

public sealed class PaperSessionState
{
    public required PaperTradingSession Session { get; set; }
    public required PaperAccount Account { get; set; }
    public required PaperSessionSettings Settings { get; init; }
    public required BacktestContext Context { get; init; }
    public BacktestDataset Dataset { get; set; } = null!;
    public required IReadOnlyList<PreparedStrategy> Strategies { get; init; }
    public required IReadOnlyList<RiskRule> RiskRules { get; init; }
    public IReadOnlyDictionary<long, IReadOnlyDictionary<string, string>>? FrozenStrategyParameters { get; init; }
    public int NextEvaluationIndex { get; set; }
    public bool StopRequested { get; set; }
    public long? LastProcessedCandleId { get; set; }
    public DateTime? LastProcessedCandleTimeUtc { get; set; }
}

public interface IPaperStateStore
{
    bool TryGet(long sessionId, out PaperSessionState? state);

    void Set(long sessionId, PaperSessionState state);

    void Remove(long sessionId);
}

public sealed class PaperStateStore : IPaperStateStore
{
    private readonly Dictionary<long, PaperSessionState> _states = new();

    public bool TryGet(long sessionId, out PaperSessionState? state)
    {
        if (_states.TryGetValue(sessionId, out var existing))
        {
            state = existing;
            return true;
        }

        state = null;
        return false;
    }

    public void Set(long sessionId, PaperSessionState state) => _states[sessionId] = state;

    public void Remove(long sessionId) => _states.Remove(sessionId);
}

public interface ILiveMarketDataProvider
{
    Task<LiveMarketTick?> GetLatestTickAsync(
        long exchangeId,
        long symbolId,
        Domain.Enums.Timeframe timeframe,
        CancellationToken cancellationToken = default);
}

public sealed class LiveMarketDataProviderAdapter : ILiveMarketDataProvider
{
    private readonly ILiveMarketSnapshotStore _snapshotStore;
    private readonly ICandleRepository _candleRepository;

    public LiveMarketDataProviderAdapter(
        ILiveMarketSnapshotStore snapshotStore,
        ICandleRepository candleRepository)
    {
        _snapshotStore = snapshotStore;
        _candleRepository = candleRepository;
    }

    public async Task<LiveMarketTick?> GetLatestTickAsync(
        long exchangeId,
        long symbolId,
        Domain.Enums.Timeframe timeframe,
        CancellationToken cancellationToken = default)
    {
        var snapshot = _snapshotStore.Get(symbolId, TimeframeParser.ToApiString(timeframe));
        var candle = await _candleRepository.GetLatestCandleAsync(symbolId, timeframe, cancellationToken);
        if (candle is null && snapshot?.LatestPrice is null)
        {
            return null;
        }

        if (candle is null && snapshot?.LastClosedCandle is not null)
        {
            candle = new Domain.MarketData.Candle
            {
                ExchangeId = exchangeId,
                SymbolId = symbolId,
                Timeframe = timeframe,
                OpenTimeUtc = snapshot.LastClosedCandle.OpenTimeUtc,
                CloseTimeUtc = snapshot.LastClosedCandle.CloseTimeUtc,
                Open = snapshot.LastClosedCandle.Open,
                High = snapshot.LastClosedCandle.High,
                Low = snapshot.LastClosedCandle.Low,
                Close = snapshot.LastClosedCandle.Close,
                Volume = snapshot.LastClosedCandle.Volume,
                IsClosed = true,
                CreatedAtUtc = DateTime.UtcNow
            };
        }

        return candle is null ? null : new LiveMarketTick { Candle = candle };
    }
}

public sealed class LiveMarketTick
{
    public required Domain.MarketData.Candle Candle { get; init; }
    public Domain.Indicators.IndicatorSnapshot? IndicatorSnapshot { get; init; }
}

public sealed class LiveMarketDataProviderNotImplemented : ILiveMarketDataProvider
{
    public Task<LiveMarketTick?> GetLatestTickAsync(
        long exchangeId,
        long symbolId,
        Domain.Enums.Timeframe timeframe,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<LiveMarketTick?>(null);
}

public interface IPaperExecutionProvider
{
    ISimulatedExecutionProvider SimulatedExecution { get; }
}

public sealed class PaperExecutionProvider : IPaperExecutionProvider
{
    public PaperExecutionProvider(ISimulatedExecutionProvider simulatedExecution) =>
        SimulatedExecution = simulatedExecution;

    public ISimulatedExecutionProvider SimulatedExecution { get; }
}

public sealed class PaperTradingTick
{
    public required Domain.MarketData.Candle Candle { get; init; }
    public int EvaluationIndex { get; init; }
}

public sealed class PaperTradingDecisionResult
{
    public required PaperTradingTick Tick { get; init; }
    public required CandleProcessResult ProcessResult { get; init; }
    public decimal Balance { get; init; }
    public decimal Equity { get; init; }
    public decimal Drawdown { get; init; }
    public decimal DrawdownPercent { get; init; }
}
