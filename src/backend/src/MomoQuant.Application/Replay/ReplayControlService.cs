using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Replay.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Replay;
using MomoQuant.Domain.Sessions;
using MomoQuant.Domain.Trades;

namespace MomoQuant.Application.Replay;

public interface IReplayControlService
{
    Task<ServiceResult<ReplayControlResponse>> StartAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<ReplayControlResponse>> PauseAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<ReplayControlResponse>> ResumeAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<ReplayControlResponse>> StopAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<ReplayControlResponse>> StepForwardAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<ReplayControlResponse>> StepBackwardAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<ReplayControlResponse>> UpdateSpeedAsync(long sessionId, UpdateReplaySpeedRequest request, CancellationToken cancellationToken = default);
}

public sealed class ReplayControlService : IReplayControlService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IReplaySessionRepository _replaySessionRepository;
    private readonly IReplayFrameRepository _frameRepository;
    private readonly ITradingSessionRepository _tradingSessionRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IReplayStateStore _stateStore;
    private readonly IReplayEngine _replayEngine;
    private readonly IReplayPersistenceService _persistenceService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;

    public ReplayControlService(
        IReplaySessionRepository replaySessionRepository,
        IReplayFrameRepository frameRepository,
        ITradingSessionRepository tradingSessionRepository,
        ISymbolRepository symbolRepository,
        IReplayStateStore stateStore,
        IReplayEngine replayEngine,
        IReplayPersistenceService persistenceService,
        ICurrentUserService currentUserService,
        IAuditService auditService)
    {
        _replaySessionRepository = replaySessionRepository;
        _frameRepository = frameRepository;
        _tradingSessionRepository = tradingSessionRepository;
        _symbolRepository = symbolRepository;
        _stateStore = stateStore;
        _replayEngine = replayEngine;
        _persistenceService = persistenceService;
        _currentUserService = currentUserService;
        _auditService = auditService;
    }

    public async Task<ServiceResult<ReplayControlResponse>> StartAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var sessionResult = await GetSessionAsync(sessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Data is null)
        {
            return ServiceResult<ReplayControlResponse>.Fail(sessionResult.ErrorMessage!);
        }

        var session = sessionResult.Data;
        if (!CanTransition(session.Status, ReplaySessionStatus.Running))
        {
            return ServiceResult<ReplayControlResponse>.Fail($"Replay session cannot start from status {session.Status}.", "status");
        }

        if (!_stateStore.TryGet(sessionId, out var state) || state is null)
        {
            return ServiceResult<ReplayControlResponse>.Fail("Replay runtime state was not found. Recreate the session.");
        }

        var now = DateTime.UtcNow;
        session.Status = ReplaySessionStatus.Running;
        session.StartedAtUtc ??= now;
        session.PausedAtUtc = null;
        session.UpdatedAtUtc = now;
        await UpdateSessionAsync(session, state, cancellationToken);

        await _auditService.LogAsync("REPLAY_STARTED", nameof(ReplaySession), session.Id, _currentUserService.UserId, cancellationToken: cancellationToken);

        if (session.CurrentFrameIndex < 0 && session.TotalFrames > 0)
        {
            return await StepForwardAsync(sessionId, cancellationToken);
        }

        return ServiceResult<ReplayControlResponse>.Ok(await BuildControlResponseAsync(session, state, null, cancellationToken));
    }

    public async Task<ServiceResult<ReplayControlResponse>> PauseAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var sessionResult = await GetSessionAsync(sessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Data is null)
        {
            return ServiceResult<ReplayControlResponse>.Fail(sessionResult.ErrorMessage!);
        }

        var session = sessionResult.Data;
        if (session.Status != ReplaySessionStatus.Running)
        {
            return ServiceResult<ReplayControlResponse>.Fail("Only running replay sessions can be paused.", "status");
        }

        if (!_stateStore.TryGet(sessionId, out var state) || state is null)
        {
            return ServiceResult<ReplayControlResponse>.Fail("Replay runtime state was not found.");
        }

        session.Status = ReplaySessionStatus.Paused;
        session.PausedAtUtc = DateTime.UtcNow;
        session.UpdatedAtUtc = DateTime.UtcNow;
        await UpdateSessionAsync(session, state, cancellationToken);

        await _auditService.LogAsync("REPLAY_PAUSED", nameof(ReplaySession), session.Id, _currentUserService.UserId, cancellationToken: cancellationToken);

        return ServiceResult<ReplayControlResponse>.Ok(await BuildControlResponseAsync(session, state, null, cancellationToken));
    }

    public async Task<ServiceResult<ReplayControlResponse>> ResumeAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var sessionResult = await GetSessionAsync(sessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Data is null)
        {
            return ServiceResult<ReplayControlResponse>.Fail(sessionResult.ErrorMessage!);
        }

        var session = sessionResult.Data;
        if (session.Status != ReplaySessionStatus.Paused)
        {
            return ServiceResult<ReplayControlResponse>.Fail("Only paused replay sessions can be resumed.", "status");
        }

        if (!_stateStore.TryGet(sessionId, out var state) || state is null)
        {
            return ServiceResult<ReplayControlResponse>.Fail("Replay runtime state was not found.");
        }

        session.Status = ReplaySessionStatus.Running;
        session.PausedAtUtc = null;
        session.UpdatedAtUtc = DateTime.UtcNow;
        await UpdateSessionAsync(session, state, cancellationToken);

        await _auditService.LogAsync("REPLAY_RESUMED", nameof(ReplaySession), session.Id, _currentUserService.UserId, cancellationToken: cancellationToken);

        return ServiceResult<ReplayControlResponse>.Ok(await BuildControlResponseAsync(session, state, null, cancellationToken));
    }

    public async Task<ServiceResult<ReplayControlResponse>> StopAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var sessionResult = await GetSessionAsync(sessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Data is null)
        {
            return ServiceResult<ReplayControlResponse>.Fail(sessionResult.ErrorMessage!);
        }

        var session = sessionResult.Data;
        if (session.Status is ReplaySessionStatus.Stopped or ReplaySessionStatus.Completed or ReplaySessionStatus.Failed)
        {
            return ServiceResult<ReplayControlResponse>.Fail($"Replay session is already {session.Status}.", "status");
        }

        _stateStore.TryGet(sessionId, out var state);

        session.Status = ReplaySessionStatus.Stopped;
        session.CompletedAtUtc = DateTime.UtcNow;
        session.UpdatedAtUtc = DateTime.UtcNow;
        await _replaySessionRepository.UpdateAsync(session, cancellationToken);
        await _replaySessionRepository.SaveChangesAsync(cancellationToken);

        if (state is not null)
        {
            await UpdateTradingSessionStatusAsync(state.Context.TradingSessionId, TradingSessionStatus.Stopped, state.Context.Balance, cancellationToken);
        }

        _stateStore.Remove(sessionId);

        await _auditService.LogAsync("REPLAY_STOPPED", nameof(ReplaySession), session.Id, _currentUserService.UserId, cancellationToken: cancellationToken);

        return ServiceResult<ReplayControlResponse>.Ok(new ReplayControlResponse
        {
            ReplaySessionId = session.Id,
            Status = session.Status.ToString(),
            CurrentFrameIndex = session.CurrentFrameIndex
        });
    }

    public async Task<ServiceResult<ReplayControlResponse>> StepForwardAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var sessionResult = await GetSessionAsync(sessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Data is null)
        {
            return ServiceResult<ReplayControlResponse>.Fail(sessionResult.ErrorMessage!);
        }

        var session = sessionResult.Data;
        if (session.Status != ReplaySessionStatus.Running)
        {
            return ServiceResult<ReplayControlResponse>.Fail("Replay session must be running to step forward.", "status");
        }

        if (!_stateStore.TryGet(sessionId, out var state) || state is null)
        {
            return ServiceResult<ReplayControlResponse>.Fail("Replay runtime state was not found.");
        }

        if (session.CurrentFrameIndex + 1 >= session.TotalFrames)
        {
            session.Status = ReplaySessionStatus.Completed;
            session.CompletedAtUtc = DateTime.UtcNow;
            session.UpdatedAtUtc = DateTime.UtcNow;
            await _replaySessionRepository.UpdateAsync(session, cancellationToken);
            await _replaySessionRepository.SaveChangesAsync(cancellationToken);
            await UpdateTradingSessionStatusAsync(state.Context.TradingSessionId, TradingSessionStatus.Completed, state.Context.Balance, cancellationToken);
            await _auditService.LogAsync("REPLAY_COMPLETED", nameof(ReplaySession), session.Id, _currentUserService.UserId, cancellationToken: cancellationToken);

            return ServiceResult<ReplayControlResponse>.Ok(new ReplayControlResponse
            {
                ReplaySessionId = session.Id,
                Status = session.Status.ToString(),
                CurrentFrameIndex = session.CurrentFrameIndex
            });
        }

        var context = state.Context;
        var signalCountBefore = context.Signals.Count;
        var aiCountBefore = context.AiDecisions.Count;
        var riskCountBefore = context.RiskDecisions.Count;
        var orderCountBefore = context.Orders.Count;
        var fillCountBefore = context.OrderFills.Count;
        var tradeCountBefore = context.Trades.Count;
        var missedCountBefore = context.MissedOrderLinks.Count;

        state.CurrentFrameIndex++;
        session.CurrentFrameIndex = state.CurrentFrameIndex;

        var stepResult = await _replayEngine.ProcessFrameAsync(state, cancellationToken);

        await _persistenceService.PersistStepEntitiesAsync(
            context,
            signalCountBefore,
            aiCountBefore,
            riskCountBefore,
            orderCountBefore,
            fillCountBefore,
            tradeCountBefore,
            missedCountBefore,
            cancellationToken);

        var frameEntity = ReplayMapper.ToEntity(session.Id, session.CurrentFrameIndex, stepResult, stepResult.StrategyResults);
        var existingFrame = await _frameRepository.GetTrackedBySessionAndIndexAsync(
            session.Id,
            session.CurrentFrameIndex,
            cancellationToken);

        if (existingFrame is null)
        {
            await _frameRepository.AddAsync(frameEntity, cancellationToken);
        }
        else
        {
            ReplayMapper.ApplyFrameData(existingFrame, frameEntity);
        }

        await _frameRepository.SaveChangesAsync(cancellationToken);

        session.CurrentCandleId = stepResult.Candle.Id;
        session.CurrentReplayTimeUtc = stepResult.Candle.CloseTimeUtc;
        session.CurrentBalance = stepResult.Balance;
        session.CurrentEquity = stepResult.Equity;
        session.UpdatedAtUtc = DateTime.UtcNow;

        if (session.CurrentFrameIndex + 1 >= session.TotalFrames)
        {
            session.Status = ReplaySessionStatus.Completed;
            session.CompletedAtUtc = DateTime.UtcNow;
            await UpdateTradingSessionStatusAsync(state.Context.TradingSessionId, TradingSessionStatus.Completed, state.Context.Balance, cancellationToken);
            await _auditService.LogAsync("REPLAY_COMPLETED", nameof(ReplaySession), session.Id, _currentUserService.UserId, cancellationToken: cancellationToken);
        }

        await UpdateSessionAsync(session, state, cancellationToken);

        var symbol = await _symbolRepository.GetByIdAsync(session.SymbolId, cancellationToken);
        var frameDto = ReplayMapper.MapFrame(session, symbol?.SymbolName ?? session.SymbolId.ToString(), stepResult, session.CurrentFrameIndex);

        return ServiceResult<ReplayControlResponse>.Ok(new ReplayControlResponse
        {
            ReplaySessionId = session.Id,
            Status = session.Status.ToString(),
            CurrentFrameIndex = session.CurrentFrameIndex,
            CurrentFrame = frameDto
        });
    }

    public async Task<ServiceResult<ReplayControlResponse>> StepBackwardAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var sessionResult = await GetSessionAsync(sessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Data is null)
        {
            return ServiceResult<ReplayControlResponse>.Fail(sessionResult.ErrorMessage!);
        }

        var session = sessionResult.Data;
        if (session.Status is not ReplaySessionStatus.Running and not ReplaySessionStatus.Paused)
        {
            return ServiceResult<ReplayControlResponse>.Fail("Replay session must be running or paused to step backward.", "status");
        }

        if (session.CurrentFrameIndex <= 0)
        {
            return ServiceResult<ReplayControlResponse>.Fail("Replay is already at the first frame.", "currentFrameIndex");
        }

        if (!_stateStore.TryGet(sessionId, out var template) || template is null)
        {
            return ServiceResult<ReplayControlResponse>.Fail("Replay runtime state was not found.");
        }

        var targetIndex = session.CurrentFrameIndex - 1;
        var rebuilt = await _replayEngine.RebuildToFrameAsync(template, targetIndex, cancellationToken);
        session.CurrentFrameIndex = targetIndex;
        session.CurrentBalance = rebuilt.Context.Balance;
        session.CurrentEquity = rebuilt.Context.CalculateEquity();
        session.CurrentCandleId = rebuilt.Dataset.Candles[rebuilt.Dataset.EvaluationIndices[targetIndex]].Id;
        session.CurrentReplayTimeUtc = rebuilt.Dataset.Candles[rebuilt.Dataset.EvaluationIndices[targetIndex]].CloseTimeUtc;
        session.UpdatedAtUtc = DateTime.UtcNow;
        _stateStore.Set(sessionId, rebuilt);

        await _frameRepository.DeleteAfterFrameIndexAsync(sessionId, targetIndex, cancellationToken);
        await _frameRepository.SaveChangesAsync(cancellationToken);

        await _replaySessionRepository.UpdateAsync(session, cancellationToken);
        await _replaySessionRepository.SaveChangesAsync(cancellationToken);

        var frame = await _frameRepository.GetBySessionAndIndexAsync(sessionId, targetIndex, cancellationToken);
        ReplayFrameDto? frameDto = null;
        if (frame is not null)
        {
            var symbol = await _symbolRepository.GetByIdAsync(session.SymbolId, cancellationToken);
            var candle = rebuilt.Dataset.Candles.First(item => item.Id == frame.CandleId);
            frameDto = ReplayMapper.MapFrameEntity(session, symbol?.SymbolName ?? session.SymbolId.ToString(), frame, candle, []);
        }

        return ServiceResult<ReplayControlResponse>.Ok(new ReplayControlResponse
        {
            ReplaySessionId = session.Id,
            Status = session.Status.ToString(),
            CurrentFrameIndex = session.CurrentFrameIndex,
            CurrentFrame = frameDto
        });
    }

    public async Task<ServiceResult<ReplayControlResponse>> UpdateSpeedAsync(
        long sessionId,
        UpdateReplaySpeedRequest request,
        CancellationToken cancellationToken = default)
    {
        var sessionResult = await GetSessionAsync(sessionId, cancellationToken);
        if (!sessionResult.Succeeded || sessionResult.Data is null)
        {
            return ServiceResult<ReplayControlResponse>.Fail(sessionResult.ErrorMessage!);
        }

        var session = sessionResult.Data;
        if (!ReplayMapper.TryParseSpeed(request.Speed, out var speed))
        {
            return ServiceResult<ReplayControlResponse>.Fail("Replay speed is invalid.", "speed");
        }

        session.Speed = speed;
        session.UpdatedAtUtc = DateTime.UtcNow;
        await _replaySessionRepository.UpdateAsync(session, cancellationToken);
        await _replaySessionRepository.SaveChangesAsync(cancellationToken);

        _stateStore.TryGet(sessionId, out var state);
        return ServiceResult<ReplayControlResponse>.Ok(await BuildControlResponseAsync(session, state, null, cancellationToken));
    }

    private async Task<ServiceResult<ReplaySession>> GetSessionAsync(long sessionId, CancellationToken cancellationToken)
    {
        var session = await _replaySessionRepository.GetByIdAsync(sessionId, cancellationToken);
        return session is null
            ? ServiceResult<ReplaySession>.Fail("Replay session was not found.")
            : ServiceResult<ReplaySession>.Ok(session);
    }

    private async Task UpdateSessionAsync(ReplaySession session, ReplayRuntimeState state, CancellationToken cancellationToken)
    {
        session.CurrentBalance = state.Context.Balance;
        session.CurrentEquity = state.Context.CalculateEquity();
        await _replaySessionRepository.UpdateAsync(session, cancellationToken);
        await _replaySessionRepository.SaveChangesAsync(cancellationToken);
        _stateStore.Set(session.Id, state);
    }

    private async Task<ReplayControlResponse> BuildControlResponseAsync(
        ReplaySession session,
        ReplayRuntimeState? state,
        ReplayFrameDto? frame,
        CancellationToken cancellationToken)
    {
        if (frame is null && session.CurrentFrameIndex >= 0)
        {
            var persisted = await _frameRepository.GetBySessionAndIndexAsync(session.Id, session.CurrentFrameIndex, cancellationToken);
            if (persisted is not null && state is not null)
            {
                var symbol = await _symbolRepository.GetByIdAsync(session.SymbolId, cancellationToken);
                var candle = state.Dataset.Candles.First(item => item.Id == persisted.CandleId);
                frame = ReplayMapper.MapFrameEntity(session, symbol?.SymbolName ?? session.SymbolId.ToString(), persisted, candle, []);
            }
        }

        return new ReplayControlResponse
        {
            ReplaySessionId = session.Id,
            Status = session.Status.ToString(),
            CurrentFrameIndex = session.CurrentFrameIndex,
            CurrentFrame = frame
        };
    }

    private async Task UpdateTradingSessionStatusAsync(
        long tradingSessionId,
        TradingSessionStatus status,
        decimal finalBalance,
        CancellationToken cancellationToken)
    {
        var tradingSession = await _tradingSessionRepository.GetByIdAsync(tradingSessionId, cancellationToken);
        if (tradingSession is null)
        {
            return;
        }

        tradingSession.Status = status;
        tradingSession.FinalBalance = finalBalance;
        tradingSession.StoppedAtUtc = DateTime.UtcNow;
        tradingSession.UpdatedAtUtc = DateTime.UtcNow;
        await _tradingSessionRepository.UpdateAsync(tradingSession, cancellationToken);
        await _tradingSessionRepository.SaveChangesAsync(cancellationToken);
    }

    private static bool CanTransition(ReplaySessionStatus current, ReplaySessionStatus next) =>
        current switch
        {
            ReplaySessionStatus.Created => next == ReplaySessionStatus.Running,
            ReplaySessionStatus.Paused => next == ReplaySessionStatus.Running,
            _ => false
        };
}
