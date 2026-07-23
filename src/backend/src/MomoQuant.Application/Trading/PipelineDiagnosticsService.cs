using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.PaperTrading;
using MomoQuant.Application.Replay;
using MomoQuant.Application.Trading.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Trading;

public interface IPipelineDiagnosticsService
{
    Task<ServiceResult<PipelineDiagnosticsDto>> GetForBacktestAsync(long backtestRunId, CancellationToken cancellationToken = default);

    Task<ServiceResult<PipelineDiagnosticsDto>> GetForPaperSessionAsync(long paperSessionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<PipelineDiagnosticsDto>> GetForReplaySessionAsync(long replaySessionId, CancellationToken cancellationToken = default);
}

public sealed class PipelineDiagnosticsService : IPipelineDiagnosticsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IBacktestRunRepository _backtestRunRepository;
    private readonly IPaperTradingSessionRepository _paperSessionRepository;
    private readonly IReplaySessionRepository _replaySessionRepository;
    private readonly ICandleRepository _candleRepository;
    private readonly IIndicatorSnapshotRepository _indicatorSnapshotRepository;
    private readonly IRiskRuleRepository _riskRuleRepository;
    private readonly IStrategySignalRepository _signalRepository;
    private readonly IRiskDecisionRepository _riskDecisionRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderFillRepository _orderFillRepository;
    private readonly IMissedOrderRepository _missedOrderRepository;
    private readonly ITradeRepository _tradeRepository;
    private readonly IAiDecisionRepository _aiDecisionRepository;
    private readonly IReplayStateStore _replayStateStore;
    private readonly IPaperStateStore _paperStateStore;

    public PipelineDiagnosticsService(
        IBacktestRunRepository backtestRunRepository,
        IPaperTradingSessionRepository paperSessionRepository,
        IReplaySessionRepository replaySessionRepository,
        ICandleRepository candleRepository,
        IIndicatorSnapshotRepository indicatorSnapshotRepository,
        IRiskRuleRepository riskRuleRepository,
        IStrategySignalRepository signalRepository,
        IRiskDecisionRepository riskDecisionRepository,
        IOrderRepository orderRepository,
        IOrderFillRepository orderFillRepository,
        IMissedOrderRepository missedOrderRepository,
        ITradeRepository tradeRepository,
        IAiDecisionRepository aiDecisionRepository,
        IReplayStateStore replayStateStore,
        IPaperStateStore paperStateStore)
    {
        _backtestRunRepository = backtestRunRepository;
        _paperSessionRepository = paperSessionRepository;
        _replaySessionRepository = replaySessionRepository;
        _candleRepository = candleRepository;
        _indicatorSnapshotRepository = indicatorSnapshotRepository;
        _riskRuleRepository = riskRuleRepository;
        _signalRepository = signalRepository;
        _riskDecisionRepository = riskDecisionRepository;
        _orderRepository = orderRepository;
        _orderFillRepository = orderFillRepository;
        _missedOrderRepository = missedOrderRepository;
        _tradeRepository = tradeRepository;
        _aiDecisionRepository = aiDecisionRepository;
        _replayStateStore = replayStateStore;
        _paperStateStore = paperStateStore;
    }

    public async Task<ServiceResult<PipelineDiagnosticsDto>> GetForBacktestAsync(long backtestRunId, CancellationToken cancellationToken = default)
    {
        var run = await _backtestRunRepository.GetByIdAsync(backtestRunId, cancellationToken);
        if (run is null)
        {
            return ServiceResult<PipelineDiagnosticsDto>.Fail("Backtest run was not found.");
        }

        var sessionMin = ParseSessionMinConfidence(run.SettingsJson);
        var riskRules = await _riskRuleRepository.GetByProfileIdAsync(run.RiskProfileId, cancellationToken);
        var effectiveMin = TradingPipelineConfidence.ResolveEffectiveMinimum(sessionMin, riskRules);

        var candleCount = await CountCandlesAsync(run.ExchangeId, run.SymbolId, run.Timeframe, run.StartDateUtc, run.EndDateUtc, cancellationToken);
        var indicatorCount = await CountIndicatorsAsync(run.SymbolId, run.Timeframe, run.StartDateUtc, run.EndDateUtc, cancellationToken);

        var persistedCounters = TryReadPersistedCounters(run.SettingsJson);
        return ServiceResult<PipelineDiagnosticsDto>.Ok(await BuildFromSessionAsync(
            run.TradingSessionId,
            null,
            persistedCounters,
            candleCount,
            indicatorCount,
            effectiveMin,
            run.UseAiScoring,
            cancellationToken));
    }

    public async Task<ServiceResult<PipelineDiagnosticsDto>> GetForPaperSessionAsync(long paperSessionId, CancellationToken cancellationToken = default)
    {
        var session = await _paperSessionRepository.GetByIdAsync(paperSessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<PipelineDiagnosticsDto>.Fail("Paper session was not found.");
        }

        if (!session.FromUtc.HasValue || !session.ToUtc.HasValue)
        {
            return ServiceResult<PipelineDiagnosticsDto>.Fail("Paper session date range is not configured.");
        }

        var riskRules = await _riskRuleRepository.GetByProfileIdAsync(session.RiskProfileId, cancellationToken);
        var effectiveMin = TradingPipelineConfidence.ResolveEffectiveMinimum(session.MinConfidenceScore, riskRules);
        var symbolId = ParsePaperSymbolId(session.ConfigJson);
        var timeframe = ParsePaperTimeframe(session.ConfigJson);

        var candleCount = symbolId.HasValue && timeframe.HasValue
            ? await CountCandlesAsync(session.ExchangeId, symbolId.Value, timeframe.Value, session.FromUtc.Value, session.ToUtc.Value, cancellationToken)
            : 0;
        var indicatorCount = symbolId.HasValue && timeframe.HasValue
            ? await CountIndicatorsAsync(symbolId.Value, timeframe.Value, session.FromUtc.Value, session.ToUtc.Value, cancellationToken)
            : 0;

        BacktestContext? context = null;
        if (_paperStateStore.TryGet(paperSessionId, out var paperState) && paperState is not null)
        {
            context = paperState.Context;
        }

        var persistedCounters = TryReadPersistedCounters(session.ConfigJson);
        return ServiceResult<PipelineDiagnosticsDto>.Ok(await BuildFromSessionAsync(
            session.TradingSessionId,
            context,
            persistedCounters,
            candleCount,
            indicatorCount,
            effectiveMin,
            session.UseAiScoring,
            cancellationToken));
    }

    public async Task<ServiceResult<PipelineDiagnosticsDto>> GetForReplaySessionAsync(long replaySessionId, CancellationToken cancellationToken = default)
    {
        var session = await _replaySessionRepository.GetByIdAsync(replaySessionId, cancellationToken);
        if (session is null)
        {
            return ServiceResult<PipelineDiagnosticsDto>.Fail("Replay session was not found.");
        }

        var riskRules = await _riskRuleRepository.GetByProfileIdAsync(session.RiskProfileId, cancellationToken);
        var sessionMin = ParseSessionMinConfidence(session.ConfigJson);
        var effectiveMin = TradingPipelineConfidence.ResolveEffectiveMinimum(sessionMin, riskRules);

        var candleCount = await CountCandlesAsync(session.ExchangeId, session.SymbolId, session.Timeframe, session.FromUtc, session.ToUtc, cancellationToken);
        var indicatorCount = await CountIndicatorsAsync(session.SymbolId, session.Timeframe, session.FromUtc, session.ToUtc, cancellationToken);

        BacktestContext? context = null;
        if (_replayStateStore.TryGet(replaySessionId, out var state) && state is not null)
        {
            context = state.Context;
        }

        var persistedCounters = TryReadPersistedCounters(session.ConfigJson);
        return ServiceResult<PipelineDiagnosticsDto>.Ok(await BuildFromSessionAsync(
            session.TradingSessionId,
            context,
            persistedCounters,
            candleCount,
            indicatorCount,
            effectiveMin,
            session.UseAiScoring,
            cancellationToken));
    }

    private async Task<PipelineDiagnosticsDto> BuildFromSessionAsync(
        long tradingSessionId,
        BacktestContext? context,
        PipelineDiagnosticsCounters? persistedCounters,
        int candleCount,
        int indicatorSnapshotCount,
        decimal effectiveMinConfidenceScore,
        bool aiEnabled,
        CancellationToken cancellationToken)
    {
        var signals = await _signalRepository.GetByTradingSessionIdAsync(tradingSessionId, cancellationToken);
        var riskDecisions = await _riskDecisionRepository.GetByTradingSessionIdAsync(tradingSessionId, cancellationToken);
        var orders = await _orderRepository.GetByTradingSessionIdAsync(tradingSessionId, cancellationToken);
        var fills = await _orderFillRepository.GetByTradingSessionIdAsync(tradingSessionId, cancellationToken);
        var missed = await _missedOrderRepository.GetByTradingSessionIdAsync(tradingSessionId, cancellationToken);
        var trades = await _tradeRepository.GetByTradingSessionIdAsync(tradingSessionId, cancellationToken);
        var aiDecisions = await _aiDecisionRepository.GetByTradingSessionIdAsync(tradingSessionId, cancellationToken);

        return TradingPipelineDiagnosticsBuilder.ToDto(
            context,
            persistedCounters,
            candleCount,
            indicatorSnapshotCount,
            effectiveMinConfidenceScore,
            aiEnabled,
            riskDecisions,
            signals,
            orders.Count,
            fills.Count,
            missed.Count,
            trades.Count,
            trades.Count(trade => trade.Status == TradeStatus.Closed),
            aiDecisions.Count);
    }

    private async Task<int> CountCandlesAsync(
        long exchangeId,
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var times = await _candleRepository.GetOpenTimesInRangeAsync(exchangeId, symbolId, timeframe, fromUtc, toUtc, cancellationToken);
        return times.Count;
    }

    private async Task<int> CountIndicatorsAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        var candles = await _candleRepository.GetCandlesChronologicalAsync(symbolId, timeframe, fromUtc, toUtc, 0, cancellationToken);
        if (candles.Count == 0)
        {
            return 0;
        }

        var snapshots = await _indicatorSnapshotRepository.GetByCandleIdsAsync(
            symbolId,
            timeframe,
            candles.Select(candle => candle.Id).ToList(),
            cancellationToken);
        return snapshots.Count;
    }

    private static decimal ParseSessionMinConfidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0m;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("minConfidenceScore", out var value))
            {
                return value.GetDecimal();
            }

            if (document.RootElement.TryGetProperty("pipelineDiagnostics", out var diagnostics) &&
                diagnostics.TryGetProperty("effectiveMinConfidenceScore", out var effective))
            {
                return effective.GetDecimal();
            }
        }
        catch (JsonException)
        {
        }

        return 0m;
    }

    private static PipelineDiagnosticsCounters? TryReadPersistedCounters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("pipelineDiagnostics", out var diagnostics))
            {
                return null;
            }

            var counters = new PipelineDiagnosticsCounters();
            if (diagnostics.TryGetProperty("strategyEvaluations", out var strategyEvaluations))
            {
                counters.StrategiesEvaluated = strategyEvaluations.GetInt32();
            }

            if (diagnostics.TryGetProperty("noTradeSignals", out var noTradeSignals))
            {
                counters.NoTradeSignals = noTradeSignals.GetInt32();
            }

            if (diagnostics.TryGetProperty("entrySignals", out var entrySignals))
            {
                counters.EntrySignals = entrySignals.GetInt32();
            }
            if (diagnostics.TryGetProperty("candidateSignals", out var candidateSignals))
            {
                counters.CandidateSignals = candidateSignals.GetInt32();
            }

            if (diagnostics.TryGetProperty("warningSignals", out var warningSignals))
            {
                counters.WarningSignals = warningSignals.GetInt32();
            }

            if (diagnostics.TryGetProperty("invalidSignals", out var invalidSignals))
            {
                counters.InvalidSignals = invalidSignals.GetInt32();
            }

            if (diagnostics.TryGetProperty("riskEvaluations", out var riskEvaluations))
            {
                counters.RiskEvaluations = riskEvaluations.GetInt32();
            }
            if (diagnostics.TryGetProperty("confidenceEvaluations", out var confidenceEvaluations))
            {
                counters.ConfidenceEvaluations = confidenceEvaluations.GetInt32();
            }
            if (diagnostics.TryGetProperty("confidenceApproved", out var confidenceApproved))
            {
                counters.ConfidenceApproved = confidenceApproved.GetInt32();
            }
            if (diagnostics.TryGetProperty("confidenceRejected", out var confidenceRejected))
            {
                counters.ConfidenceRejected = confidenceRejected.GetInt32();
            }

            if (diagnostics.TryGetProperty("riskApproved", out var riskApproved))
            {
                counters.RiskApproved = riskApproved.GetInt32();
            }

            if (diagnostics.TryGetProperty("riskRejected", out var riskRejected))
            {
                counters.RiskRejected = riskRejected.GetInt32();
            }

            return counters;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long? ParsePaperSymbolId(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(configJson);
            if (document.RootElement.TryGetProperty("symbolIds", out var symbolIds) &&
                symbolIds.ValueKind == JsonValueKind.Array &&
                symbolIds.GetArrayLength() > 0)
            {
                return symbolIds[0].GetInt64();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static Timeframe? ParsePaperTimeframe(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(configJson);
            if (document.RootElement.TryGetProperty("timeframes", out var timeframes) &&
                timeframes.ValueKind == JsonValueKind.Array &&
                timeframes.GetArrayLength() > 0)
            {
                var raw = timeframes[0].GetString();
                return raw is not null && TimeframeParser.TryParse(raw, out var timeframe) ? timeframe : null;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
