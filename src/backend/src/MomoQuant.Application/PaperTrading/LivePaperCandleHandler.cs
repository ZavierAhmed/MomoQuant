using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.PaperTrading;

namespace MomoQuant.Application.PaperTrading;

public interface ILivePaperCandleHandler
{
    Task HandleClosedCandleAsync(
        LiveCandleUpdate update,
        Candle? persistedCandle,
        CancellationToken cancellationToken = default);
}

public sealed class LivePaperCandleHandler : ILivePaperCandleHandler
{
    private readonly IPaperStateStore _stateStore;
    private readonly IPaperTradingSessionRepository _sessionRepository;
    private readonly IBacktestDataLoader _dataLoader;
    private readonly IPaperTradingEngine _paperEngine;
    private readonly IPaperPersistenceService _persistenceService;

    public LivePaperCandleHandler(
        IPaperStateStore stateStore,
        IPaperTradingSessionRepository sessionRepository,
        IBacktestDataLoader dataLoader,
        IPaperTradingEngine paperEngine,
        IPaperPersistenceService persistenceService)
    {
        _stateStore = stateStore;
        _sessionRepository = sessionRepository;
        _dataLoader = dataLoader;
        _paperEngine = paperEngine;
        _persistenceService = persistenceService;
    }

    public async Task HandleClosedCandleAsync(
        LiveCandleUpdate update,
        Candle? persistedCandle,
        CancellationToken cancellationToken = default)
    {
        if (persistedCandle is null)
        {
            return;
        }

        var runningSessions = await _sessionRepository.GetRunningSessionIdsAsync(cancellationToken);
        foreach (var sessionId in runningSessions)
        {
            if (!_stateStore.TryGet(sessionId, out var state) || state is null)
            {
                continue;
            }

            if (state.Session.Mode != PaperTradingMode.LivePaper
                || state.Session.Status != PaperSessionStatus.Running)
            {
                continue;
            }

            if (!state.Settings.SymbolIds.Contains(update.SymbolId)
                || !state.Settings.Timeframes.Contains(update.Timeframe))
            {
                continue;
            }

            if (state.LastProcessedCandleId == persistedCandle.Id
                || state.LastProcessedCandleTimeUtc == persistedCandle.CloseTimeUtc)
            {
                continue;
            }

            await ProcessSessionCandleAsync(state, persistedCandle, cancellationToken);
        }
    }

    private async Task ProcessSessionCandleAsync(
        PaperSessionState state,
        Candle closedCandle,
        CancellationToken cancellationToken)
    {
        var fromUtc = closedCandle.OpenTimeUtc.AddDays(-30);
        var dataset = await _dataLoader.LoadSymbolTimeframeAsync(
            state.Session.ExchangeId,
            closedCandle.SymbolId,
            closedCandle.Timeframe,
            fromUtc,
            closedCandle.CloseTimeUtc,
            warmUpCount: 600,
            cancellationToken);

        if (dataset is null || dataset.EvaluationIndices.Count == 0)
        {
            return;
        }

        state.Dataset = dataset;
        state.NextEvaluationIndex = dataset.EvaluationIndices.Count - 1;

        var result = await _paperEngine.ProcessNextCandleAsync(state, cancellationToken);
        if (result is null)
        {
            return;
        }

        state.LastProcessedCandleId = closedCandle.Id;
        state.LastProcessedCandleTimeUtc = closedCandle.CloseTimeUtc;
        state.Session.CurrentCandleIndex = state.NextEvaluationIndex;
        state.Session.CurrentCandleTimeUtc = closedCandle.CloseTimeUtc;
        state.Session.UpdatedAtUtc = DateTime.UtcNow;

        await _persistenceService.PersistCandleAsync(state, result.ProcessResult, cancellationToken);
        await _persistenceService.SyncAccountAsync(state, cancellationToken);
        await _sessionRepository.UpdateAsync(state.Session, cancellationToken);
        await _sessionRepository.SaveChangesAsync(cancellationToken);
    }
}
