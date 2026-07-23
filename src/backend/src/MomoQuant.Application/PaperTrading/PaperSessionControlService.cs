using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.LiveMarket.Dtos;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.PaperTrading.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Domain.Sessions;

namespace MomoQuant.Application.PaperTrading;

public interface IPaperSessionControlService
{
    Task<ServiceResult<PaperSessionControlResponse>> StartAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<PaperSessionControlResponse>> PauseAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<PaperSessionControlResponse>> ResumeAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<PaperSessionControlResponse>> StopAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<PaperSessionControlResponse>> TickAsync(long sessionId, CancellationToken cancellationToken = default);
}

public sealed class PaperSessionControlService : IPaperSessionControlService
{
    private readonly IPaperTradingSessionRepository _sessionRepository;
    private readonly ITradingSessionRepository _tradingSessionRepository;
    private readonly IPaperStateStore _stateStore;
    private readonly IPaperTradingEngine _paperEngine;
    private readonly IPaperPersistenceService _persistenceService;
    private readonly ILiveMarketConnectionManager _liveMarketConnectionManager;
    private readonly ILiveMarketBootstrapService _bootstrapService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;

    public PaperSessionControlService(
        IPaperTradingSessionRepository sessionRepository,
        ITradingSessionRepository tradingSessionRepository,
        IPaperStateStore stateStore,
        IPaperTradingEngine paperEngine,
        IPaperPersistenceService persistenceService,
        ILiveMarketConnectionManager liveMarketConnectionManager,
        ILiveMarketBootstrapService bootstrapService,
        ICurrentUserService currentUserService,
        IAuditService auditService)
    {
        _sessionRepository = sessionRepository;
        _tradingSessionRepository = tradingSessionRepository;
        _stateStore = stateStore;
        _paperEngine = paperEngine;
        _persistenceService = persistenceService;
        _liveMarketConnectionManager = liveMarketConnectionManager;
        _bootstrapService = bootstrapService;
        _currentUserService = currentUserService;
        _auditService = auditService;
    }

    public async Task<ServiceResult<PaperSessionControlResponse>> StartAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail("Paper session was not found.");
        }

        if (session.Status is not PaperSessionStatus.Created and not PaperSessionStatus.Paused)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail($"Paper session cannot start from status {session.Status}.", "status");
        }

        if (!_stateStore.TryGet(sessionId, out var state) || state is null)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail("Paper runtime state was not found. Recreate the session.");
        }

        if (session.Mode == PaperTradingMode.HistoricalPaper)
        {
            var beginResult = await BeginRunningAsync(session, state, cancellationToken);
            if (!beginResult.Succeeded)
            {
                return beginResult;
            }

            return await TickAsync(sessionId, cancellationToken);
        }

        if (session.Mode == PaperTradingMode.LivePaper)
        {
            var liveStartResult = await EnsureLivePaperReadyAsync(session, state, cancellationToken);
            if (!liveStartResult.Succeeded)
            {
                return liveStartResult;
            }

            return await BeginRunningAsync(session, state, cancellationToken);
        }

        return ServiceResult<PaperSessionControlResponse>.Fail("Only historical or live paper sessions can be started.", "mode");
    }

    public Task<ServiceResult<PaperSessionControlResponse>> PauseAsync(
        long sessionId,
        CancellationToken cancellationToken = default) =>
        ChangeStatusAsync(sessionId, PaperSessionStatus.Running, PaperSessionStatus.Paused, "PAPER_SESSION_PAUSED", cancellationToken);

    public async Task<ServiceResult<PaperSessionControlResponse>> ResumeAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail("Paper session was not found.");
        }

        if (session.Status != PaperSessionStatus.Paused)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail("Only paused paper sessions can be resumed.", "status");
        }

        if (!_stateStore.TryGet(sessionId, out var state) || state is null)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail("Paper runtime state was not found. Recreate the session.");
        }

        if (session.Mode == PaperTradingMode.LivePaper)
        {
            var liveStartResult = await EnsureLivePaperReadyAsync(session, state, cancellationToken);
            if (!liveStartResult.Succeeded)
            {
                return liveStartResult;
            }
        }

        var beginResult = await BeginRunningAsync(session, state, cancellationToken);
        if (!beginResult.Succeeded)
        {
            return beginResult;
        }

        return ServiceResult<PaperSessionControlResponse>.Ok(BuildResponse(session));
    }

    public async Task<ServiceResult<PaperSessionControlResponse>> StopAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail("Paper session was not found.");
        }

        if (session.Status is PaperSessionStatus.Stopped or PaperSessionStatus.Completed or PaperSessionStatus.Failed)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail($"Paper session is already {session.Status}.", "status");
        }

        if (_stateStore.TryGet(sessionId, out var state) && state is not null)
        {
            state.StopRequested = true;
            await _paperEngine.FinalizeSessionAsync(state, cancellationToken);
            await _persistenceService.SyncAccountAsync(state, cancellationToken);
            await UpdateTradingSessionStatusAsync(state.Context.TradingSessionId, TradingSessionStatus.Stopped, cancellationToken);
            _stateStore.Remove(sessionId);
        }

        _liveMarketConnectionManager.UnlinkSession(sessionId);

        session.Status = PaperSessionStatus.Stopped;
        session.StoppedAtUtc = DateTime.UtcNow;
        session.UpdatedAtUtc = DateTime.UtcNow;
        await _sessionRepository.UpdateAsync(session, cancellationToken);
        await _sessionRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync("PAPER_SESSION_STOPPED", nameof(PaperTradingSession), session.Id, _currentUserService.UserId, cancellationToken: cancellationToken);

        return ServiceResult<PaperSessionControlResponse>.Ok(BuildResponse(session));
    }

    public async Task<ServiceResult<PaperSessionControlResponse>> TickAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail("Paper session was not found.");
        }

        if (session.Mode != PaperTradingMode.HistoricalPaper)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail("Tick is only supported for historical paper sessions.", "mode");
        }

        if (session.Status != PaperSessionStatus.Running)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail("Paper session is not running.", "status");
        }

        if (!_stateStore.TryGet(sessionId, out var state) || state is null)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail("Paper runtime state was not found. Recreate the session.");
        }

        if (state.StopRequested)
        {
            return await StopAsync(sessionId, cancellationToken);
        }

        if (state.NextEvaluationIndex >= state.Dataset.EvaluationIndices.Count)
        {
            return await CompleteSessionAsync(session, state, cancellationToken);
        }

        try
        {
            var result = await _paperEngine.ProcessNextCandleAsync(state, cancellationToken);
            if (result is null)
            {
                return await CompleteSessionAsync(session, state, cancellationToken);
            }

            await _persistenceService.PersistCandleAsync(state, result.ProcessResult, cancellationToken);

            session.CurrentCandleIndex = result.Tick.EvaluationIndex;
            session.CurrentCandleTimeUtc = result.Tick.Candle.CloseTimeUtc;
            session.UpdatedAtUtc = DateTime.UtcNow;
            await _sessionRepository.UpdateAsync(session, cancellationToken);
            await _sessionRepository.SaveChangesAsync(cancellationToken);

            if (state.NextEvaluationIndex >= state.Dataset.EvaluationIndices.Count)
            {
                return await CompleteSessionAsync(session, state, cancellationToken);
            }

            return ServiceResult<PaperSessionControlResponse>.Ok(BuildResponse(session));
        }
        catch (Exception ex)
        {
            session.Status = PaperSessionStatus.Failed;
            session.ErrorMessage = ex.Message;
            session.UpdatedAtUtc = DateTime.UtcNow;
            await _sessionRepository.UpdateAsync(session, cancellationToken);
            await _sessionRepository.SaveChangesAsync(cancellationToken);
            _stateStore.Remove(sessionId);

            await _auditService.LogAsync("PAPER_SESSION_FAILED", nameof(PaperTradingSession), session.Id, _currentUserService.UserId, cancellationToken: cancellationToken);

            return ServiceResult<PaperSessionControlResponse>.Fail($"Paper session failed: {ex.Message}");
        }
    }

    private async Task<ServiceResult<PaperSessionControlResponse>> EnsureLivePaperReadyAsync(
        PaperTradingSession session,
        PaperSessionState state,
        CancellationToken cancellationToken)
    {
        if (!_liveMarketConnectionManager.IsAvailable)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail(
                "Live market provider is unavailable. LivePaper cannot start.",
                "mode");
        }

        foreach (var symbolId in state.Settings.SymbolIds)
        {
            foreach (var timeframe in state.Settings.Timeframes)
            {
                var bootstrapResult = await _bootstrapService.EnsureWarmupAsync(
                    session.ExchangeId,
                    symbolId,
                    timeframe,
                    cancellationToken);

                if (!bootstrapResult.Succeeded)
                {
                    return ServiceResult<PaperSessionControlResponse>.Fail(
                        bootstrapResult.ErrorMessage ?? "Failed to bootstrap recent market data.",
                        bootstrapResult.ErrorField);
                }

                var subscribeResult = await _liveMarketConnectionManager.SubscribeAsync(
                    new LiveMarketSubscribeRequest
                    {
                        ExchangeId = session.ExchangeId,
                        SymbolId = symbolId,
                        Timeframe = TimeframeParser.ToApiString(timeframe),
                        PaperSessionId = session.Id
                    },
                    cancellationToken);

                if (!subscribeResult.Succeeded)
                {
                    return ServiceResult<PaperSessionControlResponse>.Fail(
                        subscribeResult.ErrorMessage ?? "Failed to subscribe to live market data.",
                        subscribeResult.ErrorField);
                }

                _liveMarketConnectionManager.LinkSession(session.Id, symbolId, timeframe);
            }
        }

        if (!_liveMarketConnectionManager.IsConnected)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail(
                "Live market provider is unavailable. LivePaper cannot start.",
                "mode");
        }

        return ServiceResult<PaperSessionControlResponse>.Ok(BuildResponse(session));
    }

    private async Task<ServiceResult<PaperSessionControlResponse>> BeginRunningAsync(
        PaperTradingSession session,
        PaperSessionState state,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        session.Status = PaperSessionStatus.Running;
        session.StartedAtUtc ??= now;
        session.PausedAtUtc = null;
        session.UpdatedAtUtc = now;
        state.StopRequested = false;
        state.Session = session;

        await _sessionRepository.UpdateAsync(session, cancellationToken);
        await _sessionRepository.SaveChangesAsync(cancellationToken);
        await UpdateTradingSessionStatusAsync(state.Context.TradingSessionId, TradingSessionStatus.Running, cancellationToken);
        await _auditService.LogAsync("PAPER_SESSION_STARTED", nameof(PaperTradingSession), session.Id, _currentUserService.UserId, cancellationToken: cancellationToken);

        return ServiceResult<PaperSessionControlResponse>.Ok(BuildResponse(session));
    }

    private async Task<ServiceResult<PaperSessionControlResponse>> CompleteSessionAsync(
        PaperTradingSession session,
        PaperSessionState state,
        CancellationToken cancellationToken)
    {
        await _paperEngine.FinalizeSessionAsync(state, cancellationToken);
        await _persistenceService.SyncAccountAsync(state, cancellationToken);

        session.Status = PaperSessionStatus.Completed;
        session.CompletedAtUtc = DateTime.UtcNow;
        session.UpdatedAtUtc = DateTime.UtcNow;
        await _sessionRepository.UpdateAsync(session, cancellationToken);
        await _sessionRepository.SaveChangesAsync(cancellationToken);

        await UpdateTradingSessionStatusAsync(state.Context.TradingSessionId, TradingSessionStatus.Stopped, cancellationToken);
        _stateStore.Remove(session.Id);

        await _auditService.LogAsync("PAPER_SESSION_COMPLETED", nameof(PaperTradingSession), session.Id, _currentUserService.UserId, cancellationToken: cancellationToken);

        return ServiceResult<PaperSessionControlResponse>.Ok(BuildResponse(session));
    }

    private async Task<ServiceResult<PaperSessionControlResponse>> ChangeStatusAsync(
        long sessionId,
        PaperSessionStatus requiredStatus,
        PaperSessionStatus targetStatus,
        string auditAction,
        CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail("Paper session was not found.");
        }

        if (session.Status != requiredStatus)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail($"Paper session cannot change from status {session.Status}.", "status");
        }

        session.Status = targetStatus;
        session.PausedAtUtc = targetStatus == PaperSessionStatus.Paused ? DateTime.UtcNow : session.PausedAtUtc;
        session.UpdatedAtUtc = DateTime.UtcNow;
        await _sessionRepository.UpdateAsync(session, cancellationToken);
        await _sessionRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(auditAction, nameof(PaperTradingSession), session.Id, _currentUserService.UserId, cancellationToken: cancellationToken);

        return ServiceResult<PaperSessionControlResponse>.Ok(BuildResponse(session));
    }

    private async Task UpdateTradingSessionStatusAsync(
        long tradingSessionId,
        TradingSessionStatus status,
        CancellationToken cancellationToken)
    {
        var tradingSession = await _tradingSessionRepository.GetByIdAsync(tradingSessionId, cancellationToken);
        if (tradingSession is null)
        {
            return;
        }

        tradingSession.Status = status;
        tradingSession.UpdatedAtUtc = DateTime.UtcNow;
        if (status == TradingSessionStatus.Running)
        {
            tradingSession.StartedAtUtc ??= DateTime.UtcNow;
        }

        if (status == TradingSessionStatus.Stopped)
        {
            tradingSession.StoppedAtUtc = DateTime.UtcNow;
        }

        await _tradingSessionRepository.UpdateAsync(tradingSession, cancellationToken);
        await _tradingSessionRepository.SaveChangesAsync(cancellationToken);
    }

    private static PaperSessionControlResponse BuildResponse(PaperTradingSession session) => new()
    {
        PaperSessionId = session.Id,
        Status = session.Status.ToString(),
        CurrentCandleIndex = session.CurrentCandleIndex,
        TotalCandles = session.TotalCandles,
        CurrentCandleTimeUtc = session.CurrentCandleTimeUtc
    };
}
