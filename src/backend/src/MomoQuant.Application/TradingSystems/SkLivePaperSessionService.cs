using Microsoft.Extensions.Logging;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.LiveMarket.Dtos;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.TradingSystems.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.TradingSystems;

namespace MomoQuant.Application.TradingSystems;

public interface ISkLivePaperSessionService
{
    SkLivePaperDefaultsDto GetDefaults();
    Task<ServiceResult<SkLivePaperSessionDto>> CreateSessionAsync(CreateSkLivePaperSessionRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<SkLivePaperSessionSummaryDto>>> ListSessionsAsync(int limit, CancellationToken cancellationToken = default);
    Task<ServiceResult<SkLivePaperStatusDto>> GetStatusAsync(long id, CancellationToken cancellationToken = default);
    Task<ServiceResult<SkLivePaperSessionDto>> StartAsync(long id, CancellationToken cancellationToken = default);
    Task<ServiceResult<SkLivePaperSessionDto>> PauseAsync(long id, CancellationToken cancellationToken = default);
    Task<ServiceResult<SkLivePaperSessionDto>> ResumeAsync(long id, CancellationToken cancellationToken = default);
    Task<ServiceResult<SkLivePaperSessionDto>> StopAsync(long id, CancellationToken cancellationToken = default);
    Task<ServiceResult<SkLivePaperTradeDto>> ManualCloseTradeAsync(long sessionId, long tradeId, CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<SkLivePaperCandidateDto>>> GetCandidatesAsync(long sessionId, int limit, CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<SkLivePaperTradeDto>>> GetTradesAsync(long sessionId, CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<SkLivePaperEventDto>>> GetEventsAsync(long sessionId, int limit, CancellationToken cancellationToken = default);
    Task<ServiceResult<SkLivePaperChartDto>> GetChartAsync(long sessionId, CancellationToken cancellationToken = default);
}

public interface ISkLivePaperEngine
{
    Task ProcessClosedCandleAsync(SkLivePaperSession session, Candle closedCandle, CancellationToken cancellationToken = default);
}

public interface ISkLivePaperCandleHandler
{
    Task HandleClosedCandleAsync(LiveCandleUpdate update, Candle? persistedCandle, CancellationToken cancellationToken = default);
}

public sealed class SkLivePaperSessionService : ISkLivePaperSessionService
{
    private readonly ISkLivePaperSessionRepository _sessionRepository;
    private readonly ISkLivePaperTradeRepository _tradeRepository;
    private readonly ISkLivePaperCandidateRepository _candidateRepository;
    private readonly ISkLivePaperEventRepository _eventRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IExchangeRepository _exchangeRepository;
    private readonly ILiveMarketConnectionManager _liveMarket;
    private readonly ISkSystemAnalysisService _skAnalysisService;
    private readonly SkLivePaperDiagnosticsStore _diagnosticsStore;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;

    public SkLivePaperSessionService(
        ISkLivePaperSessionRepository sessionRepository,
        ISkLivePaperTradeRepository tradeRepository,
        ISkLivePaperCandidateRepository candidateRepository,
        ISkLivePaperEventRepository eventRepository,
        ISymbolRepository symbolRepository,
        IExchangeRepository exchangeRepository,
        ILiveMarketConnectionManager liveMarket,
        ISkSystemAnalysisService skAnalysisService,
        SkLivePaperDiagnosticsStore diagnosticsStore,
        ICurrentUserService currentUserService,
        IAuditService auditService)
    {
        _sessionRepository = sessionRepository;
        _tradeRepository = tradeRepository;
        _candidateRepository = candidateRepository;
        _eventRepository = eventRepository;
        _symbolRepository = symbolRepository;
        _exchangeRepository = exchangeRepository;
        _liveMarket = liveMarket;
        _skAnalysisService = skAnalysisService;
        _diagnosticsStore = diagnosticsStore;
        _currentUserService = currentUserService;
        _auditService = auditService;
    }

    public SkLivePaperDefaultsDto GetDefaults() => new();

    public async Task<ServiceResult<SkLivePaperSessionDto>> CreateSessionAsync(
        CreateSkLivePaperSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var symbol = await _symbolRepository.GetByIdAsync(request.SymbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<SkLivePaperSessionDto>.Fail("Symbol was not found.", "symbolId");
        }

        var exchange = await _exchangeRepository.GetByIdAsync(request.ExchangeId, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<SkLivePaperSessionDto>.Fail("Exchange was not found.", "exchangeId");
        }

        if (!TimeframeParser.TryParse(request.PrimaryTimeframe, out _) ||
            !TimeframeParser.TryParse(request.HigherTimeframe, out _))
        {
            return ServiceResult<SkLivePaperSessionDto>.Fail("Invalid timeframe selection.", "timeframe");
        }

        var timeframeValidation = SkTimeframeValidation.ValidateSkPair(request.HigherTimeframe, request.PrimaryTimeframe);
        if (!timeframeValidation.Succeeded)
        {
            return ServiceResult<SkLivePaperSessionDto>.Fail(
                timeframeValidation.ErrorMessage ?? SkTimeframeValidation.HigherMustExceedPrimaryMessage,
                timeframeValidation.ErrorField ?? "timeframe");
        }

        var now = DateTime.UtcNow;
        var entity = new SkLivePaperSession
        {
            SessionName = string.IsNullOrWhiteSpace(request.SessionName) ? $"{symbol.SymbolName} SK LivePaper" : request.SessionName.Trim(),
            ExchangeId = exchange.Id,
            SymbolId = symbol.Id,
            Symbol = symbol.SymbolName,
            HigherTimeframe = request.HigherTimeframe,
            PrimaryTimeframe = request.PrimaryTimeframe,
            AdditionalTimeframesJson = SkSystemJson.Serialize(request.AdditionalTimeframes),
            StartingBalance = request.StartingBalance,
            CurrentBalance = request.StartingBalance,
            RiskPerPaperTradePercent = request.RiskPerPaperTradePercent,
            MaxPaperTradesPerDay = request.MaxPaperTradesPerDay,
            MaxOpenPaperPositions = request.MaxOpenPaperPositions,
            AllowLong = request.AllowLong,
            AllowShort = request.AllowShort,
            RequireHtfAgreement = request.RequireHtfAgreement,
            MinClarityScore = request.MinClarityScore,
            MinUsefulnessScore = request.MinUsefulnessScore,
            RequireReactionConfirmation = request.RequireReactionConfirmation,
            ConfirmationMode = request.ConfirmationMode,
            SimulatedLeverage = request.SimulatedLeverage,
            Status = SkLivePaperSessionStatus.Created,
            SimulationMode = SkLivePaperConstants.SimulationMode,
            CreatedByUserId = _currentUserService.UserId,
            CreatedAtUtc = now
        };

        await _sessionRepository.AddAsync(entity, cancellationToken);
        await _sessionRepository.SaveChangesAsync(cancellationToken);
        await LogEventAsync(entity.Id, "SessionCreated", "SK LivePaper session created (simulated only).", cancellationToken);

        await _auditService.LogAsync(
            "SK_LIVE_PAPER_SESSION_CREATED",
            nameof(SkLivePaperSession),
            entityId: entity.Id,
            userId: _currentUserService.UserId,
            cancellationToken: cancellationToken);

        return ServiceResult<SkLivePaperSessionDto>.Ok(MapSession(entity));
    }

    public async Task<ServiceResult<IReadOnlyList<SkLivePaperSessionSummaryDto>>> ListSessionsAsync(int limit, CancellationToken cancellationToken = default)
    {
        var sessions = await _sessionRepository.GetRecentAsync(limit, cancellationToken);
        var summaries = new List<SkLivePaperSessionSummaryDto>();
        foreach (var session in sessions)
        {
            var trades = await _tradeRepository.GetBySessionAsync(session.Id, cancellationToken);
            summaries.Add(new SkLivePaperSessionSummaryDto
            {
                Id = session.Id,
                SessionName = session.SessionName,
                Symbol = session.Symbol,
                Status = session.Status.ToString(),
                CurrentBalance = session.CurrentBalance,
                NetSimulatedPnl = session.CurrentBalance - session.StartingBalance,
                OpenTrades = trades.Count(t => t.Status == SkLivePaperTradeStatus.Open),
                ClosedTrades = trades.Count(t => t.Status == SkLivePaperTradeStatus.Closed),
                LastAnalyzedCandleUtc = session.LastAnalyzedCandleUtc,
                SimulationMode = session.SimulationMode
            });
        }

        return ServiceResult<IReadOnlyList<SkLivePaperSessionSummaryDto>>.Ok(summaries);
    }

    public async Task<ServiceResult<SkLivePaperStatusDto>> GetStatusAsync(long id, CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdAsync(id, cancellationToken);
        if (session is null)
        {
            return ServiceResult<SkLivePaperStatusDto>.Fail("Session was not found.", "id");
        }

        var trades = await _tradeRepository.GetBySessionAsync(id, cancellationToken);
        var diag = _diagnosticsStore.GetOrCreate(id);
        return ServiceResult<SkLivePaperStatusDto>.Ok(new SkLivePaperStatusDto
        {
            Session = MapSession(session),
            OpenTrades = trades.Count(t => t.Status == SkLivePaperTradeStatus.Open),
            ClosedTrades = trades.Count(t => t.Status == SkLivePaperTradeStatus.Closed),
            NetSimulatedPnl = session.CurrentBalance - session.StartingBalance,
            LastCandidate = diag.LastCandidate,
            Diagnostics = MapDiagnostics(session, diag),
            SafetyDisclaimer = GetDefaults().SafetyDisclaimer
        });
    }

    public async Task<ServiceResult<SkLivePaperSessionDto>> StartAsync(long id, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(id, cancellationToken);
        if (!session.Succeeded || session.Data is null)
        {
            return ServiceResult<SkLivePaperSessionDto>.Fail(session.ErrorMessage!, session.ErrorField);
        }

        var entity = session.Data;
        if (entity.Status is SkLivePaperSessionStatus.Running)
        {
            return ServiceResult<SkLivePaperSessionDto>.Ok(MapSession(entity));
        }

        if (!TimeframeParser.TryParse(entity.PrimaryTimeframe, out var primaryTf))
        {
            return ServiceResult<SkLivePaperSessionDto>.Fail("Invalid primary timeframe.", "primaryTimeframe");
        }

        if (!TimeframeParser.TryParse(entity.HigherTimeframe, out _))
        {
            return ServiceResult<SkLivePaperSessionDto>.Fail("Invalid higher timeframe.", "higherTimeframe");
        }

        var timeframeValidation = SkTimeframeValidation.ValidateSkPair(entity.HigherTimeframe, entity.PrimaryTimeframe);
        if (!timeframeValidation.Succeeded)
        {
            return ServiceResult<SkLivePaperSessionDto>.Fail(
                timeframeValidation.ErrorMessage ?? SkTimeframeValidation.HigherMustExceedPrimaryMessage,
                timeframeValidation.ErrorField ?? "timeframe");
        }

        var importResult = await _skAnalysisService.ImportRequiredDataAsync(new SkImportRequiredDataRequest
        {
            ExchangeId = entity.ExchangeId,
            SymbolId = entity.SymbolId,
            PrimaryTimeframe = entity.PrimaryTimeframe,
            HigherTimeframe = entity.HigherTimeframe,
            LookbackCandles = SkLivePaperConstants.DefaultLookbackCandles
        }, cancellationToken);

        if (!importResult.Succeeded)
        {
            entity.Status = SkLivePaperSessionStatus.Error;
            entity.LastError = importResult.ErrorMessage;
            await _sessionRepository.UpdateAsync(entity, cancellationToken);
            await _sessionRepository.SaveChangesAsync(cancellationToken);
            return ServiceResult<SkLivePaperSessionDto>.Fail(
                importResult.ErrorMessage ?? "Could not import required candle data.",
                importResult.ErrorField ?? "candles");
        }

        var subscribeHtf = await _liveMarket.SubscribeAsync(new LiveMarketSubscribeRequest
        {
            ExchangeId = entity.ExchangeId,
            SymbolId = entity.SymbolId,
            Timeframe = entity.HigherTimeframe
        }, cancellationToken);

        if (!subscribeHtf.Succeeded)
        {
            entity.Status = SkLivePaperSessionStatus.Error;
            entity.LastError = subscribeHtf.ErrorMessage;
            await _sessionRepository.UpdateAsync(entity, cancellationToken);
            await _sessionRepository.SaveChangesAsync(cancellationToken);
            return ServiceResult<SkLivePaperSessionDto>.Fail(subscribeHtf.ErrorMessage ?? "Could not subscribe to higher timeframe market data.");
        }

        var subscribe = await _liveMarket.SubscribeAsync(new LiveMarketSubscribeRequest
        {
            ExchangeId = entity.ExchangeId,
            SymbolId = entity.SymbolId,
            Timeframe = entity.PrimaryTimeframe
        }, cancellationToken);

        if (!subscribe.Succeeded)
        {
            entity.Status = SkLivePaperSessionStatus.Error;
            entity.LastError = subscribe.ErrorMessage;
            await _sessionRepository.UpdateAsync(entity, cancellationToken);
            await _sessionRepository.SaveChangesAsync(cancellationToken);
            return ServiceResult<SkLivePaperSessionDto>.Fail(subscribe.ErrorMessage ?? "Could not subscribe to live market data.");
        }

        entity.Status = SkLivePaperSessionStatus.Running;
        entity.StartedAtUtc ??= DateTime.UtcNow;
        entity.LastHeartbeatUtc = DateTime.UtcNow;
        entity.LastError = null;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _sessionRepository.UpdateAsync(entity, cancellationToken);
        await _sessionRepository.SaveChangesAsync(cancellationToken);

        var diag = _diagnosticsStore.GetOrCreate(id);
        diag.WebSocketStatus = _liveMarket.IsConnected ? "Connected" : "Disconnected";
        await LogEventAsync(id, "SessionStarted", "SK LivePaper session started (simulated only).", cancellationToken);

        return ServiceResult<SkLivePaperSessionDto>.Ok(MapSession(entity));
    }

    public async Task<ServiceResult<SkLivePaperSessionDto>> PauseAsync(long id, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(id, cancellationToken);
        if (!session.Succeeded || session.Data is null)
        {
            return ServiceResult<SkLivePaperSessionDto>.Fail(session.ErrorMessage!, session.ErrorField);
        }

        session.Data.Status = SkLivePaperSessionStatus.Paused;
        session.Data.UpdatedAtUtc = DateTime.UtcNow;
        await _sessionRepository.UpdateAsync(session.Data, cancellationToken);
        await _sessionRepository.SaveChangesAsync(cancellationToken);
        await LogEventAsync(id, "SessionPaused", "Session paused.", cancellationToken);
        return ServiceResult<SkLivePaperSessionDto>.Ok(MapSession(session.Data));
    }

    public async Task<ServiceResult<SkLivePaperSessionDto>> ResumeAsync(long id, CancellationToken cancellationToken = default) =>
        await StartAsync(id, cancellationToken);

    public async Task<ServiceResult<SkLivePaperSessionDto>> StopAsync(long id, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(id, cancellationToken);
        if (!session.Succeeded || session.Data is null)
        {
            return ServiceResult<SkLivePaperSessionDto>.Fail(session.ErrorMessage!, session.ErrorField);
        }

        var entity = session.Data;
        entity.Status = SkLivePaperSessionStatus.Stopped;
        entity.StoppedAtUtc = DateTime.UtcNow;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await _sessionRepository.UpdateAsync(entity, cancellationToken);
        await _sessionRepository.SaveChangesAsync(cancellationToken);
        _diagnosticsStore.Remove(id);
        await LogEventAsync(id, "SessionStopped", "Session stopped.", cancellationToken);
        return ServiceResult<SkLivePaperSessionDto>.Ok(MapSession(entity));
    }

    public async Task<ServiceResult<SkLivePaperTradeDto>> ManualCloseTradeAsync(
        long sessionId,
        long tradeId,
        CancellationToken cancellationToken = default)
    {
        var trades = await _tradeRepository.GetBySessionAsync(sessionId, cancellationToken);
        var trade = trades.FirstOrDefault(t => t.Id == tradeId && t.Status == SkLivePaperTradeStatus.Open);
        if (trade is null)
        {
            return ServiceResult<SkLivePaperTradeDto>.Fail("Open trade was not found.", "tradeId");
        }

        var session = await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<SkLivePaperTradeDto>.Fail("Session was not found.", "id");
        }

        var exitPrice = trade.EntryPrice;
        var pnl = SkLivePaperTradeCloser.ComputePnl(trade, exitPrice);
        trade.Status = SkLivePaperTradeStatus.Closed;
        trade.ExitTimeUtc = DateTime.UtcNow;
        trade.ExitPrice = exitPrice;
        trade.ExitReason = SkLivePaperTradeExitReason.ManualClose;
        trade.GrossPnl = pnl.GrossPnl;
        trade.Fees = pnl.Fees;
        trade.Slippage = pnl.Slippage;
        trade.NetPnl = pnl.NetPnl;
        trade.NetPnlPercent = pnl.NetPnlPercent;
        trade.UpdatedAtUtc = DateTime.UtcNow;
        session.CurrentBalance += trade.NetPnl;
        session.UpdatedAtUtc = DateTime.UtcNow;

        await _tradeRepository.UpdateAsync(trade, cancellationToken);
        await _sessionRepository.UpdateAsync(session, cancellationToken);
        await _tradeRepository.SaveChangesAsync(cancellationToken);
        await LogEventAsync(sessionId, "PaperTradeClosed", $"Simulated trade closed manually. Net PnL {trade.NetPnl}.", cancellationToken);

        return ServiceResult<SkLivePaperTradeDto>.Ok(MapTrade(trade));
    }

    public async Task<ServiceResult<IReadOnlyList<SkLivePaperCandidateDto>>> GetCandidatesAsync(long sessionId, int limit, CancellationToken cancellationToken = default)
    {
        var items = await _candidateRepository.GetBySessionAsync(sessionId, limit, cancellationToken);
        return ServiceResult<IReadOnlyList<SkLivePaperCandidateDto>>.Ok(items.Select(MapCandidate).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<SkLivePaperTradeDto>>> GetTradesAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var items = await _tradeRepository.GetBySessionAsync(sessionId, cancellationToken);
        return ServiceResult<IReadOnlyList<SkLivePaperTradeDto>>.Ok(items.Select(MapTrade).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<SkLivePaperEventDto>>> GetEventsAsync(long sessionId, int limit, CancellationToken cancellationToken = default)
    {
        var items = await _eventRepository.GetBySessionAsync(sessionId, limit, cancellationToken);
        return ServiceResult<IReadOnlyList<SkLivePaperEventDto>>.Ok(items.Select(e => new SkLivePaperEventDto
        {
            Id = e.Id,
            EventType = e.EventType,
            Message = e.Message,
            CreatedAtUtc = e.CreatedAtUtc
        }).ToList());
    }

    public async Task<ServiceResult<SkLivePaperChartDto>> GetChartAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<SkLivePaperChartDto>.Fail("Session was not found.", "id");
        }

        var diag = _diagnosticsStore.GetOrCreate(sessionId);
        var analysis = diag.LastAnalysis;
        var openTrades = await _tradeRepository.GetOpenBySessionAsync(sessionId, cancellationToken);

        return ServiceResult<SkLivePaperChartDto>.Ok(new SkLivePaperChartDto
        {
            SessionId = sessionId,
            Symbol = session.Symbol,
            PrimaryTimeframe = session.PrimaryTimeframe,
            HigherTimeframe = session.HigherTimeframe,
            CurrentPrice = analysis?.CurrentPrice ?? 0m,
            Candles = analysis?.Candles ?? [],
            ChartOverlays = analysis?.ChartOverlays ?? [],
            OpenTrades = openTrades.Select(MapTrade).ToList()
        });
    }

    private async Task<ServiceResult<SkLivePaperSession>> RequireSessionAsync(long id, CancellationToken cancellationToken)
    {
        var session = await _sessionRepository.GetByIdAsync(id, cancellationToken);
        return session is null
            ? ServiceResult<SkLivePaperSession>.Fail("Session was not found.", "id")
            : ServiceResult<SkLivePaperSession>.Ok(session);
    }

    private async Task LogEventAsync(long sessionId, string type, string message, CancellationToken cancellationToken)
    {
        await _eventRepository.AddAsync(new SkLivePaperEvent
        {
            SessionId = sessionId,
            EventType = type,
            Message = message,
            CreatedAtUtc = DateTime.UtcNow
        }, cancellationToken);
        await _eventRepository.SaveChangesAsync(cancellationToken);
    }

    internal static SkLivePaperSessionDto MapSession(SkLivePaperSession session) => new()
    {
        Id = session.Id,
        SessionName = session.SessionName,
        ExchangeId = session.ExchangeId,
        SymbolId = session.SymbolId,
        Symbol = session.Symbol,
        HigherTimeframe = session.HigherTimeframe,
        PrimaryTimeframe = session.PrimaryTimeframe,
        StartingBalance = session.StartingBalance,
        CurrentBalance = session.CurrentBalance,
        RiskPerPaperTradePercent = session.RiskPerPaperTradePercent,
        MaxPaperTradesPerDay = session.MaxPaperTradesPerDay,
        MaxOpenPaperPositions = session.MaxOpenPaperPositions,
        AllowLong = session.AllowLong,
        AllowShort = session.AllowShort,
        RequireHtfAgreement = session.RequireHtfAgreement,
        MinClarityScore = session.MinClarityScore,
        MinUsefulnessScore = session.MinUsefulnessScore,
        RequireReactionConfirmation = session.RequireReactionConfirmation,
        ConfirmationMode = session.ConfirmationMode,
        SimulatedLeverage = session.SimulatedLeverage,
        Status = session.Status.ToString(),
        SimulationMode = session.SimulationMode,
        StartedAtUtc = session.StartedAtUtc,
        StoppedAtUtc = session.StoppedAtUtc,
        LastHeartbeatUtc = session.LastHeartbeatUtc,
        LastAnalyzedCandleUtc = session.LastAnalyzedCandleUtc,
        LastError = session.LastError,
        CreatedAtUtc = session.CreatedAtUtc
    };

    internal static SkLivePaperCandidateDto MapCandidate(SkLivePaperCandidate candidate) => new()
    {
        Id = candidate.Id,
        SessionId = candidate.SessionId,
        Symbol = candidate.Symbol,
        Direction = candidate.Direction,
        SequenceStatus = candidate.SequenceStatus,
        ValidityStatus = candidate.ValidityStatus,
        UsefulnessStatus = candidate.UsefulnessStatus,
        ClarityScore = candidate.ClarityScore,
        UsefulnessScore = candidate.UsefulnessScore,
        ReactionZoneLow = candidate.ReactionZoneLow,
        ReactionZoneHigh = candidate.ReactionZoneHigh,
        InvalidationLevel = candidate.InvalidationLevel,
        Target1 = candidate.Target1,
        Target2 = candidate.Target2,
        CurrentPrice = candidate.CurrentPrice,
        CandidateStatus = candidate.CandidateStatus.ToString(),
        RejectionReason = candidate.RejectionReason,
        CreatedAtUtc = candidate.CreatedAtUtc
    };

    internal static SkLivePaperTradeDto MapTrade(SkLivePaperTrade trade) => new()
    {
        Id = trade.Id,
        SessionId = trade.SessionId,
        Symbol = trade.Symbol,
        Direction = trade.Direction,
        Status = trade.Status.ToString(),
        SimulationMode = trade.SimulationMode,
        EntryTimeUtc = trade.EntryTimeUtc,
        EntryPrice = trade.EntryPrice,
        Quantity = trade.Quantity,
        SimulatedLeverage = trade.SimulatedLeverage,
        MarginUsed = trade.MarginUsed,
        StopLoss = trade.StopLoss,
        TakeProfit1 = trade.TakeProfit1,
        TakeProfit2 = trade.TakeProfit2,
        ExitTimeUtc = trade.ExitTimeUtc,
        ExitPrice = trade.ExitPrice,
        ExitReason = trade.ExitReason?.ToString(),
        NetPnl = trade.NetPnl,
        NetPnlPercent = trade.NetPnlPercent
    };

    private static SkLivePaperDiagnosticsDto MapDiagnostics(SkLivePaperSession session, SkLivePaperSessionDiagnostics diag) => new()
    {
        WebSocketStatus = diag.WebSocketStatus,
        ClosedCandlesProcessed = diag.ClosedCandlesProcessed,
        SkAnalysesRun = diag.SkAnalysesRun,
        CandidatesDetected = diag.CandidatesDetected,
        CandidatesRejected = diag.CandidatesRejected,
        PaperTradesOpened = diag.PaperTradesOpened,
        PaperTradesClosed = diag.PaperTradesClosed,
        LastHeartbeatUtc = session.LastHeartbeatUtc,
        LastError = session.LastError
    };
}
