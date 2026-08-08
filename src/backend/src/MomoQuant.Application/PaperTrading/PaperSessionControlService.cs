using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Audit;
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
    private readonly IPaperDeploymentQualificationVerifier? _deploymentVerifier;
    private readonly IPaperSessionRelationalCoordinator? _relationalCoordinator;
    private readonly IRequiredAuditWriter? _requiredAuditWriter;
    private readonly ILogger<PaperSessionControlService> _logger;

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
        : this(
            sessionRepository,
            tradingSessionRepository,
            stateStore,
            paperEngine,
            persistenceService,
            liveMarketConnectionManager,
            bootstrapService,
            currentUserService,
            auditService,
            null,
            null,
            null,
            null)
    {
    }

    public PaperSessionControlService(
        IPaperTradingSessionRepository sessionRepository,
        ITradingSessionRepository tradingSessionRepository,
        IPaperStateStore stateStore,
        IPaperTradingEngine paperEngine,
        IPaperPersistenceService persistenceService,
        ILiveMarketConnectionManager liveMarketConnectionManager,
        ILiveMarketBootstrapService bootstrapService,
        ICurrentUserService currentUserService,
        IAuditService auditService,
        IPaperDeploymentQualificationVerifier? deploymentVerifier,
        IPaperSessionRelationalCoordinator? relationalCoordinator,
        IRequiredAuditWriter? requiredAuditWriter = null,
        ILogger<PaperSessionControlService>? logger = null)
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
        _deploymentVerifier = deploymentVerifier;
        _relationalCoordinator = relationalCoordinator;
        _requiredAuditWriter = requiredAuditWriter;
        _logger = logger ?? NullLogger<PaperSessionControlService>.Instance;
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

        if (session.UseClass == PaperSessionUseClass.DeploymentSimulation)
        {
            return await ExecuteDeploymentBeginAsync(
                sessionId,
                session.Status,
                "Start",
                cancellationToken);
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

        if (session.UseClass == PaperSessionUseClass.DeploymentSimulation)
        {
            return await ExecuteDeploymentBeginAsync(
                sessionId,
                PaperSessionStatus.Paused,
                "Resume",
                cancellationToken);
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

    private async Task<ServiceResult<PaperSessionControlResponse>> ExecuteDeploymentBeginAsync(
        long sessionId,
        PaperSessionStatus requiredStatus,
        string phase,
        CancellationToken cancellationToken)
    {
        if (_deploymentVerifier is null || _relationalCoordinator is null || _requiredAuditWriter is null)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail(
                "Deployment-simulation runtime verification is unavailable.",
                _requiredAuditWriter is null
                    ? AuditEvidenceCodes.Unavailable
                    : PaperDeploymentQualificationCodes.NotQualified);
        }

        if (!_stateStore.TryGet(sessionId, out var state) || state is null)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail(
                "Paper runtime state was not found. Recreate the session.");
        }

        ServiceResult<DeploymentActivationCommit> durable;
        try
        {
            durable = await _relationalCoordinator.ExecuteSerializedAsync(
                sessionId,
                async (session, token) =>
                {
                if (session is null)
                {
                    return ServiceResult<DeploymentActivationCommit>.Fail("Paper session was not found.");
                }

                if (session.UseClass != PaperSessionUseClass.DeploymentSimulation)
                {
                    return ServiceResult<DeploymentActivationCommit>.Fail(
                        "The durable paper-session use class changed during runtime control.",
                        PaperDeploymentQualificationCodes.BindingConflict);
                }

                if (session.Status != requiredStatus)
                {
                    return ServiceResult<DeploymentActivationCommit>.Fail(
                        $"Paper session cannot {phase.ToLowerInvariant()} from status {session.Status}.",
                        "status");
                }

                var binding = BuildStoredBinding(session);
                if (binding is null)
                {
                    return ServiceResult<DeploymentActivationCommit>.Fail(
                        "The durable deployment-simulation binding is incomplete.",
                        PaperDeploymentQualificationCodes.BindingConflict);
                }

                var verification = await _deploymentVerifier.VerifyAsync(
                    binding.ParameterSetId,
                    binding.StrategyId,
                    binding.SymbolId,
                    binding.Timeframe,
                    binding,
                    token);
                if (!verification.Succeeded)
                {
                    return ServiceResult<DeploymentActivationCommit>.Fail(
                        verification.ErrorMessage ?? "Deployment qualification verification failed.",
                        verification.ErrorCode ?? PaperDeploymentQualificationCodes.NotQualified);
                }

                var tradingSession = await _tradingSessionRepository
                    .GetByIdAsync(session.TradingSessionId, token)
                    .ConfigureAwait(false);
                if (tradingSession is null)
                {
                    return ServiceResult<DeploymentActivationCommit>.Fail(
                        "The durable trading session was not found.",
                        PaperDeploymentQualificationCodes.BindingConflict);
                }

                session.QualificationVerifiedAtUtc = verification.VerifiedAtUtc;
                session.Status = PaperSessionStatus.Running;
                session.StartedAtUtc ??= verification.VerifiedAtUtc;
                session.PausedAtUtc = null;
                session.UpdatedAtUtc = verification.VerifiedAtUtc;
                await _sessionRepository.UpdateAsync(session, token);
                tradingSession.Status = TradingSessionStatus.Running;
                tradingSession.StartedAtUtc ??= verification.VerifiedAtUtc;
                tradingSession.UpdatedAtUtc = verification.VerifiedAtUtc;
                await _tradingSessionRepository.UpdateAsync(tradingSession, token);

                _requiredAuditWriter.AttachRequired(
                    BuildQualificationAuditRequest(session, phase, verification),
                    token);
                _requiredAuditWriter.AttachRequired(
                    BuildTransitionAuditRequest(
                        phase == "Resume"
                            ? RequiredAuditActions.PaperSessionResumed
                            : RequiredAuditActions.PaperSessionStarted,
                        session,
                        phase,
                        verification),
                    token);
                await _sessionRepository.SaveChangesAsync(token);

                var frozenParameters = new Dictionary<long, IReadOnlyDictionary<string, string>>
                {
                    [verification.StrategyId] = verification.FrozenParameters
                };
                return ServiceResult<DeploymentActivationCommit>.Ok(
                    new DeploymentActivationCommit(session.Id, frozenParameters));
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AuditEvidenceException ex)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail(
                "Required deployment audit evidence could not be committed.",
                ex.Code);
        }

        if (!durable.Succeeded)
        {
            return ServiceResult<PaperSessionControlResponse>.Fail(durable.ErrorMessage!, durable.ErrorField);
        }

        // The coordinator has committed and cleared its tracker. The reload is part of the
        // post-commit activation phase: failure here must compensate the committed Running state.
        var activation = durable.Data!;
        var previouslySubscribed = CaptureExistingSubscriptions(state);
        PaperTradingSession? committedSession = null;
        try
        {
            committedSession = await _sessionRepository
                .GetByIdAsync(sessionId, cancellationToken)
                .ConfigureAwait(false);
            if (committedSession is null || committedSession.Status != PaperSessionStatus.Running)
            {
                throw new InvalidOperationException("The committed deployment session could not be reloaded.");
            }

            state.FrozenStrategyParameters = activation.FrozenStrategyParameters;
            state.StopRequested = false;
            state.Session = committedSession;
            _stateStore.Set(sessionId, state);

            var readiness = await EnsureLivePaperReadyAsync(committedSession, state, cancellationToken);
            if (!readiness.Succeeded)
            {
                await CleanupActivationAsync(committedSession, state, previouslySubscribed).ConfigureAwait(false);
                return await CompensateActivationFailureAsync(sessionId, phase, CancellationToken.None);
            }

            return ServiceResult<PaperSessionControlResponse>.Ok(BuildResponse(committedSession));
        }
        catch (OperationCanceledException)
        {
            if (committedSession is not null)
            {
                await CleanupActivationAsync(committedSession, state, previouslySubscribed).ConfigureAwait(false);
            }
            else
            {
                RemoveRuntimeActivation(sessionId);
            }

            await CompensateActivationFailureAsync(sessionId, phase, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch
        {
            if (committedSession is not null)
            {
                await CleanupActivationAsync(committedSession, state, previouslySubscribed).ConfigureAwait(false);
            }
            else
            {
                RemoveRuntimeActivation(sessionId);
            }

            return await CompensateActivationFailureAsync(sessionId, phase, CancellationToken.None);
        }
    }

    private static PaperDeploymentStoredBinding? BuildStoredBinding(PaperTradingSession session)
    {
        if (session.ParameterSetId is null
            || session.BoundStrategyId is null
            || session.BoundSymbolId is null
            || string.IsNullOrWhiteSpace(session.BoundTimeframe)
            || session.QualificationSourceExperimentId is null
            || session.QualificationSourceTrialId is null
            || string.IsNullOrWhiteSpace(session.QualificationParameterFingerprint)
            || string.IsNullOrWhiteSpace(session.QualificationEvidenceVersion))
        {
            return null;
        }

        return new PaperDeploymentStoredBinding(
            session.ParameterSetId.Value,
            session.BoundStrategyId.Value,
            session.BoundSymbolId.Value,
            session.BoundTimeframe,
            session.QualificationSourceExperimentId.Value,
            session.QualificationSourceTrialId.Value,
            session.QualificationParameterFingerprint,
            session.QualificationEvidenceVersion);
    }

    private RequiredAuditRequest BuildQualificationAuditRequest(
        PaperTradingSession session,
        string phase,
        PaperDeploymentQualificationResult verification) =>
        new(
            RequiredAuditActions.PaperDeploymentQualificationVerified,
            nameof(PaperTradingSession),
            session.Id,
            _currentUserService.UserId,
            session.TradingSessionId,
            LogSeverity.Info,
            new PaperQualificationAuditMetadata(
                session.Id,
                session.TradingSessionId,
                phase,
                verification.ParameterSetId,
                verification.StrategyId,
                verification.SymbolId,
                verification.Timeframe,
                verification.SourceExperimentId,
                verification.SourceTrialId,
                verification.ParameterFingerprint,
                verification.EvidenceVersion,
                verification.VerifiedAtUtc),
            verification.VerifiedAtUtc);

    private RequiredAuditRequest BuildTransitionAuditRequest(
        string action,
        PaperTradingSession session,
        string phase,
        PaperDeploymentQualificationResult verification) =>
        new(
            action,
            nameof(PaperTradingSession),
            session.Id,
            _currentUserService.UserId,
            session.TradingSessionId,
            LogSeverity.Info,
            new PaperSessionTransitionAuditMetadata(
                session.Id,
                session.TradingSessionId,
                phase,
                verification.ParameterSetId,
                verification.StrategyId,
                verification.SymbolId,
                verification.Timeframe,
                verification.SourceExperimentId,
                verification.SourceTrialId,
                verification.ParameterFingerprint,
                verification.EvidenceVersion,
                verification.VerifiedAtUtc),
            verification.VerifiedAtUtc);

    private HashSet<(long SymbolId, Timeframe Timeframe)> CaptureExistingSubscriptions(PaperSessionState state) =>
        state.Settings.SymbolIds
            .SelectMany(symbolId => state.Settings.Timeframes.Select(timeframe => (symbolId, timeframe)))
            .Where(item => _liveMarketConnectionManager.IsSubscribed(item.symbolId, item.timeframe))
            .ToHashSet();

    private void RemoveRuntimeActivation(long sessionId)
    {
        _liveMarketConnectionManager.UnlinkSession(sessionId);
        _stateStore.Remove(sessionId);
    }

    private async Task CleanupActivationAsync(
        PaperTradingSession session,
        PaperSessionState state,
        HashSet<(long SymbolId, Timeframe Timeframe)> previouslySubscribed)
    {
        _liveMarketConnectionManager.UnlinkSession(session.Id);
        _stateStore.Remove(session.Id);

        foreach (var symbolId in state.Settings.SymbolIds)
        {
            foreach (var timeframe in state.Settings.Timeframes)
            {
                if (previouslySubscribed.Contains((symbolId, timeframe)))
                {
                    continue;
                }

                try
                {
                    var diagnostics = _liveMarketConnectionManager.GetDiagnostics();
                    var timeframeValue = TimeframeParser.ToApiString(timeframe);
                    var hasOtherLinks = diagnostics.Subscriptions.Any(item =>
                        item.SymbolId == symbolId
                        && string.Equals(item.Timeframe, timeframeValue, StringComparison.OrdinalIgnoreCase)
                        && item.LinkedSessionIds.Count > 0);
                    if (!hasOtherLinks && _liveMarketConnectionManager.IsSubscribed(symbolId, timeframe))
                    {
                        await _liveMarketConnectionManager.UnsubscribeAsync(
                            new LiveMarketSubscribeRequest
                            {
                                ExchangeId = session.ExchangeId,
                                SymbolId = symbolId,
                                Timeframe = timeframeValue,
                                PaperSessionId = session.Id
                            },
                            CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch
                {
                    _logger.LogCritical(
                        "Deployment runtime cleanup could not fully remove a subscription for paper session {PaperSessionId}.",
                        session.Id);
                }
            }
        }
    }

    private async Task<ServiceResult<PaperSessionControlResponse>> CompensateActivationFailureAsync(
        long sessionId,
        string phase,
        CancellationToken cancellationToken)
    {
        try
        {
            var compensation = await _relationalCoordinator!.ExecuteSerializedAsync(
                sessionId,
                async (session, token) =>
                {
                    if (session is null)
                    {
                        return ServiceResult<PaperSessionControlResponse>.Fail(
                            "The deployment session could not be marked failed.",
                            PaperDeploymentQualificationCodes.RuntimeActivationFailed);
                    }

                    var tradingSession = await _tradingSessionRepository
                        .GetByIdAsync(session.TradingSessionId, token)
                        .ConfigureAwait(false);
                    if (tradingSession is null)
                    {
                        throw new InvalidOperationException("Durable trading session missing during activation compensation.");
                    }

                    var now = DateTime.UtcNow;
                    session.Status = PaperSessionStatus.Failed;
                    session.ErrorMessage = PaperDeploymentQualificationCodes.RuntimeActivationFailed;
                    session.UpdatedAtUtc = now;
                    tradingSession.Status = TradingSessionStatus.Failed;
                    tradingSession.StoppedAtUtc = now;
                    tradingSession.UpdatedAtUtc = now;
                    await _sessionRepository.UpdateAsync(session, token);
                    await _tradingSessionRepository.UpdateAsync(tradingSession, token);
                    _requiredAuditWriter!.AttachRequired(
                        new RequiredAuditRequest(
                            RequiredAuditActions.PaperSessionFailed,
                            nameof(PaperTradingSession),
                            session.Id,
                            _currentUserService.UserId,
                            session.TradingSessionId,
                            LogSeverity.Error,
                            new PaperSessionFailureAuditMetadata(
                                session.Id,
                                session.TradingSessionId,
                                "Activation",
                                PaperDeploymentQualificationCodes.RuntimeActivationFailed),
                            now),
                        token);
                    await _sessionRepository.SaveChangesAsync(token);
                    return ServiceResult<PaperSessionControlResponse>.Ok(BuildResponse(session));
                },
                cancellationToken);
            if (!compensation.Succeeded)
            {
                return compensation;
            }

            return ServiceResult<PaperSessionControlResponse>.Fail(
                "Deployment runtime activation failed after the durable transition.",
                PaperDeploymentQualificationCodes.RuntimeActivationFailed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AuditEvidenceException)
        {
            _logger.LogCritical(
                "Required activation-failure audit evidence was unavailable for paper session {PaperSessionId}; no runtime remains active.",
                sessionId);
            return ServiceResult<PaperSessionControlResponse>.Fail(
                "Required deployment audit evidence could not be committed.",
                AuditEvidenceCodes.Unavailable);
        }
        catch
        {
            _logger.LogCritical(
                "Deployment activation compensation could not be committed for paper session {PaperSessionId}; no runtime remains active.",
                sessionId);
            return ServiceResult<PaperSessionControlResponse>.Fail(
                "Required deployment audit evidence could not be committed.",
                AuditEvidenceCodes.Unavailable);
        }
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

    private sealed record DeploymentActivationCommit(
        long PaperSessionId,
        IReadOnlyDictionary<long, IReadOnlyDictionary<string, string>> FrozenStrategyParameters);
}
