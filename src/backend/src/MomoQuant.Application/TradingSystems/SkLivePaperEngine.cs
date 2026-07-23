using Microsoft.Extensions.Logging;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.LiveMarket.Dtos;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.TradingSystems.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.TradingSystems;

namespace MomoQuant.Application.TradingSystems;

public sealed class SkLivePaperEngine : ISkLivePaperEngine
{
    private readonly ISkSystemAnalysisService _analysisService;
    private readonly ISkLivePaperSessionRepository _sessionRepository;
    private readonly ISkLivePaperCandidateRepository _candidateRepository;
    private readonly ISkLivePaperTradeRepository _tradeRepository;
    private readonly ISkLivePaperEventRepository _eventRepository;
    private readonly SkLivePaperDiagnosticsStore _diagnosticsStore;
    private readonly ILogger<SkLivePaperEngine> _logger;

    public SkLivePaperEngine(
        ISkSystemAnalysisService analysisService,
        ISkLivePaperSessionRepository sessionRepository,
        ISkLivePaperCandidateRepository candidateRepository,
        ISkLivePaperTradeRepository tradeRepository,
        ISkLivePaperEventRepository eventRepository,
        SkLivePaperDiagnosticsStore diagnosticsStore,
        ILogger<SkLivePaperEngine> logger)
    {
        _analysisService = analysisService;
        _sessionRepository = sessionRepository;
        _candidateRepository = candidateRepository;
        _tradeRepository = tradeRepository;
        _eventRepository = eventRepository;
        _diagnosticsStore = diagnosticsStore;
        _logger = logger;
    }

    public async Task ProcessClosedCandleAsync(
        SkLivePaperSession session,
        Candle closedCandle,
        CancellationToken cancellationToken = default)
    {
        if (session.Status != SkLivePaperSessionStatus.Running)
        {
            return;
        }

        if (session.SymbolId != closedCandle.SymbolId)
        {
            return;
        }

        var diag = _diagnosticsStore.GetOrCreate(session.Id);
        diag.ClosedCandlesProcessed++;

        try
        {
            await CloseOpenTradesAsync(session, closedCandle, diag, cancellationToken);

            var analysisResult = await _analysisService.AnalyzeSnapshotAsync(new SkSystemAnalyzeRequest
            {
                ExchangeId = session.ExchangeId,
                SymbolId = session.SymbolId,
                PrimaryTimeframe = session.PrimaryTimeframe,
                HigherTimeframe = session.HigherTimeframe,
                LookbackCandles = SkLivePaperConstants.DefaultLookbackCandles,
                SwingSensitivity = "Balanced",
                DirectionMode = "Auto",
                UseAiSummary = false,
                ExplanationMode = "Beginner",
                AutoImportMissingCandles = true
            }, cancellationToken);

            if (!analysisResult.Succeeded || analysisResult.Data is null)
            {
                session.LastError = analysisResult.ErrorMessage;
                session.LastHeartbeatUtc = DateTime.UtcNow;
                await _sessionRepository.UpdateAsync(session, cancellationToken);
                await _sessionRepository.SaveChangesAsync(cancellationToken);
                return;
            }

            diag.SkAnalysesRun++;
            diag.LastAnalysis = analysisResult.Data;
            session.LastAnalyzedCandleUtc = closedCandle.CloseTimeUtc;
            session.LastHeartbeatUtc = DateTime.UtcNow;
            session.LastError = null;

            var openTrades = await _tradeRepository.GetOpenBySessionAsync(session.Id, cancellationToken);
            var tradesToday = await _tradeRepository.CountOpenedTodayAsync(session.Id, DateTime.UtcNow.Date, cancellationToken);

            foreach (var sequence in analysisResult.Data.Sequences.Where(s => s.EligibleForBestIdea))
            {
                var evaluation = SkLivePaperCandidateEvaluator.Evaluate(
                    session, sequence, analysisResult.Data, closedCandle, openTrades.Count, tradesToday);

                var candidateKey = $"{sequence.Id}-{closedCandle.CloseTimeUtc:yyyyMMddHHmm}";
                var existing = await _candidateRepository.GetByKeyAsync(session.Id, candidateKey, cancellationToken);
                if (existing is not null)
                {
                    continue;
                }

                var candidate = BuildCandidate(session, sequence, closedCandle, evaluation);
                await _candidateRepository.AddAsync(candidate, cancellationToken);
                await _candidateRepository.SaveChangesAsync(cancellationToken);
                diag.CandidatesDetected++;
                diag.LastCandidate = SkLivePaperSessionService.MapCandidate(candidate);

                if (evaluation.CanOpenTrade && evaluation.Sequence is not null)
                {
                    await OpenSimulatedTradeAsync(session, candidate, evaluation.Sequence, closedCandle, analysisResult.Data, diag, cancellationToken);
                }
                else if (!string.IsNullOrWhiteSpace(evaluation.RejectionReason))
                {
                    diag.CandidatesRejected++;
                    await LogEventAsync(session.Id, "CandidateRejected",
                        $"Simulated candidate rejected: {evaluation.RejectionReason}", cancellationToken);
                }
                else if (evaluation.WaitingForConfirmation)
                {
                    await LogEventAsync(session.Id, "CandidateDetected",
                        "Simulated candidate waiting for reaction confirmation.", cancellationToken);
                }
            }

            await _sessionRepository.UpdateAsync(session, cancellationToken);
            await _sessionRepository.SaveChangesAsync(cancellationToken);
            await LogEventAsync(session.Id, "SkAnalysisRun", "SK analysis completed on closed candle.", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SK LivePaper processing failed for session {SessionId}", session.Id);
            session.LastError = ex.Message;
            session.Status = SkLivePaperSessionStatus.Error;
            session.UpdatedAtUtc = DateTime.UtcNow;
            await _sessionRepository.UpdateAsync(session, cancellationToken);
            await _sessionRepository.SaveChangesAsync(cancellationToken);
            await LogEventAsync(session.Id, "Error", ex.Message, cancellationToken);
        }
    }

    private async Task CloseOpenTradesAsync(
        SkLivePaperSession session,
        Candle closedCandle,
        SkLivePaperSessionDiagnostics diag,
        CancellationToken cancellationToken)
    {
        var openTrades = await _tradeRepository.GetOpenBySessionAsync(session.Id, cancellationToken);
        foreach (var trade in openTrades)
        {
            var close = SkLivePaperTradeCloser.Evaluate(trade, closedCandle);
            if (close is null)
            {
                continue;
            }

            var (_, exitPrice, reason) = close.Value;
            var pnl = SkLivePaperTradeCloser.ComputePnl(trade, exitPrice);
            trade.Status = SkLivePaperTradeStatus.Closed;
            trade.ExitTimeUtc = closedCandle.CloseTimeUtc;
            trade.ExitPrice = exitPrice;
            trade.ExitReason = reason;
            trade.GrossPnl = pnl.GrossPnl;
            trade.Fees = pnl.Fees;
            trade.Slippage = pnl.Slippage;
            trade.NetPnl = pnl.NetPnl;
            trade.NetPnlPercent = pnl.NetPnlPercent;
            trade.UpdatedAtUtc = DateTime.UtcNow;
            session.CurrentBalance += trade.NetPnl;
            diag.PaperTradesClosed++;

            await _tradeRepository.UpdateAsync(trade, cancellationToken);
            await LogEventAsync(session.Id, "PaperTradeClosed",
                $"Simulated trade closed ({reason}). Net PnL {trade.NetPnl}.", cancellationToken);
        }

        if (openTrades.Count > 0)
        {
            await _tradeRepository.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task OpenSimulatedTradeAsync(
        SkLivePaperSession session,
        SkLivePaperCandidate candidate,
        SkSequenceDto sequence,
        Candle closedCandle,
        SkSystemAnalysisResultDto analysis,
        SkLivePaperSessionDiagnostics diag,
        CancellationToken cancellationToken)
    {
        var sizing = SkLivePaperPositionSizer.TrySize(
            session.CurrentBalance,
            session.RiskPerPaperTradePercent,
            closedCandle.Close,
            sequence.InvalidationLevel,
            session.SimulatedLeverage);

        if (!sizing.IsValid)
        {
            candidate.CandidateStatus = SkLivePaperCandidateStatus.Rejected;
            candidate.RejectionReason = SkLivePaperRejectionReasons.InvalidRiskReward;
            await _candidateRepository.UpdateAsync(candidate, cancellationToken);
            return;
        }

        var trade = new SkLivePaperTrade
        {
            SessionId = session.Id,
            SymbolId = session.SymbolId,
            Symbol = session.Symbol,
            Direction = sequence.Direction == "Upward" ? "Bullish" : "Bearish",
            Status = SkLivePaperTradeStatus.Open,
            EntryTimeUtc = closedCandle.CloseTimeUtc,
            EntryPrice = closedCandle.Close,
            Quantity = sizing.Quantity,
            SimulatedLeverage = session.SimulatedLeverage,
            MarginUsed = sizing.MarginUsed,
            NotionalValue = sizing.NotionalValue,
            StopLoss = sequence.InvalidationLevel,
            TakeProfit1 = sequence.Target1,
            TakeProfit2 = sequence.Target2,
            ClarityScore = sequence.ClarityScore,
            UsefulnessScore = sequence.UsefulnessScore,
            HtfDirection = analysis.ConceptAudit?.HtfDirection ?? "Unknown",
            LtfDirection = analysis.ConceptAudit?.LtfDirection ?? "Unknown",
            SimulationMode = SkLivePaperConstants.SimulationMode,
            CreatedAtUtc = DateTime.UtcNow
        };

        candidate.CandidateStatus = SkLivePaperCandidateStatus.ConvertedToPaperTrade;
        candidate.ConfirmedAtUtc = DateTime.UtcNow;
        trade.CandidateId = candidate.Id;

        await _tradeRepository.AddAsync(trade, cancellationToken);
        await _candidateRepository.UpdateAsync(candidate, cancellationToken);
        await _tradeRepository.SaveChangesAsync(cancellationToken);

        session.TradesOpenedToday++;
        session.TradesOpenedDayUtc = DateTime.UtcNow.Date;
        diag.PaperTradesOpened++;

        await LogEventAsync(session.Id, "PaperTradeOpened",
            $"Simulated paper trade opened at {trade.EntryPrice} (no real order).", cancellationToken);
    }

    private static SkLivePaperCandidate BuildCandidate(
        SkLivePaperSession session,
        SkSequenceDto sequence,
        Candle closedCandle,
        SkLivePaperEvaluationResult evaluation) => new()
    {
        SessionId = session.Id,
        SymbolId = session.SymbolId,
        Symbol = session.Symbol,
        HigherTimeframe = session.HigherTimeframe,
        PrimaryTimeframe = session.PrimaryTimeframe,
        Direction = sequence.Direction,
        SequenceStatus = sequence.SequenceStatus,
        ValidityStatus = sequence.ValidityStatus,
        UsefulnessStatus = sequence.UsefulnessStatus,
        ClarityScore = sequence.ClarityScore,
        UsefulnessScore = sequence.UsefulnessScore,
        ReactionZoneLow = sequence.CorrectionZoneLow,
        ReactionZoneHigh = sequence.CorrectionZoneHigh,
        StrongReactionZoneLow = sequence.StrongCorrectionZoneLow,
        StrongReactionZoneHigh = sequence.StrongCorrectionZoneHigh,
        InvalidationLevel = sequence.InvalidationLevel,
        Target1 = sequence.Target1,
        Target2 = sequence.Target2,
        CurrentPrice = closedCandle.Close,
        CandidateStatus = evaluation.CanOpenTrade
            ? SkLivePaperCandidateStatus.Confirmed
            : evaluation.WaitingForConfirmation
                ? SkLivePaperCandidateStatus.WaitingForReaction
                : SkLivePaperCandidateStatus.Rejected,
        RejectionReason = evaluation.RejectionReason,
        CandidateKey = $"{sequence.Id}-{closedCandle.CloseTimeUtc:yyyyMMddHHmm}",
        CreatedAtUtc = DateTime.UtcNow
    };

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
}

public sealed class SkLivePaperCandleHandler : ISkLivePaperCandleHandler
{
    private readonly ISkLivePaperSessionRepository _sessionRepository;
    private readonly ISkLivePaperEngine _engine;
    private readonly ILogger<SkLivePaperCandleHandler> _logger;

    public SkLivePaperCandleHandler(
        ISkLivePaperSessionRepository sessionRepository,
        ISkLivePaperEngine engine,
        ILogger<SkLivePaperCandleHandler> logger)
    {
        _sessionRepository = sessionRepository;
        _engine = engine;
        _logger = logger;
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

        var runningIds = await _sessionRepository.GetRunningSessionIdsAsync(cancellationToken);
        foreach (var sessionId in runningIds)
        {
            var session = await _sessionRepository.GetByIdAsync(sessionId, cancellationToken);
            if (session is null || session.Status != SkLivePaperSessionStatus.Running)
            {
                continue;
            }

            if (session.SymbolId != update.SymbolId ||
                !string.Equals(session.PrimaryTimeframe, TimeframeParser.ToApiString(update.Timeframe), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                await _engine.ProcessClosedCandleAsync(session, persistedCandle, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SK LivePaper candle handler failed for session {SessionId}", sessionId);
            }
        }
    }
}
