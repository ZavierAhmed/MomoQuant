using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Ai.Dtos;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Options;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Ai;

public interface IAiIntegrationService
{
    Task<ServiceResult<AiHealthDto>> GetHealthAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<DetectRegimeResponseDto>> DetectRegimeAsync(
        DetectRegimeRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ScoreConfidenceResponseDto>> ScoreConfidenceAsync(
        ScoreConfidenceRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<DetectAnomalyResponseDto>> DetectAnomalyAsync(
        DetectAnomalyRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<ExplainTradeResponseDto>> ExplainTradeAsync(
        ExplainTradeRequestDto request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<EvaluateSignalResponseDto>> EvaluateSignalAsync(
        EvaluateSignalRequestDto request,
        CancellationToken cancellationToken = default);
}

public sealed class AiIntegrationService : IAiIntegrationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IAiServiceClient _aiServiceClient;
    private readonly IAiDecisionService _aiDecisionService;
    private readonly IStrategySignalRepository _strategySignalRepository;
    private readonly IStrategyRepository _strategyRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly ICandleRepository _candleRepository;
    private readonly IIndicatorSnapshotRepository _indicatorSnapshotRepository;
    private readonly IRiskProfileRepository _riskProfileRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;
    private readonly AiIntegrationOptions _options;
    private readonly ILogger<AiIntegrationService> _logger;

    public AiIntegrationService(
        IAiServiceClient aiServiceClient,
        IAiDecisionService aiDecisionService,
        IStrategySignalRepository strategySignalRepository,
        IStrategyRepository strategyRepository,
        ISymbolRepository symbolRepository,
        ICandleRepository candleRepository,
        IIndicatorSnapshotRepository indicatorSnapshotRepository,
        IRiskProfileRepository riskProfileRepository,
        ICurrentUserService currentUserService,
        IAuditService auditService,
        IOptions<AiIntegrationOptions> options,
        ILogger<AiIntegrationService> logger)
    {
        _aiServiceClient = aiServiceClient;
        _aiDecisionService = aiDecisionService;
        _strategySignalRepository = strategySignalRepository;
        _strategyRepository = strategyRepository;
        _symbolRepository = symbolRepository;
        _candleRepository = candleRepository;
        _indicatorSnapshotRepository = indicatorSnapshotRepository;
        _riskProfileRepository = riskProfileRepository;
        _currentUserService = currentUserService;
        _auditService = auditService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ServiceResult<AiHealthDto>> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var result = await _aiServiceClient.GetHealthAsync(cancellationToken);
        if (result.Succeeded && result.Data is not null)
        {
            return ServiceResult<AiHealthDto>.Ok(result.Data);
        }

        await LogServiceUnavailableAsync(cancellationToken);
        return ServiceResult<AiHealthDto>.Fail(result.ErrorMessage ?? "AI service is unavailable.");
    }

    public Task<ServiceResult<DetectRegimeResponseDto>> DetectRegimeAsync(
        DetectRegimeRequestDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteWithFallbackAsync(
            () => _aiServiceClient.DetectRegimeAsync(request, cancellationToken),
            AiFallbackFactory.CreateRegimeFallback,
            cancellationToken);

    public Task<ServiceResult<ScoreConfidenceResponseDto>> ScoreConfidenceAsync(
        ScoreConfidenceRequestDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteWithFallbackAsync(
            () => _aiServiceClient.ScoreConfidenceAsync(request, cancellationToken),
            AiFallbackFactory.CreateConfidenceFallback,
            cancellationToken);

    public Task<ServiceResult<DetectAnomalyResponseDto>> DetectAnomalyAsync(
        DetectAnomalyRequestDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteWithFallbackAsync(
            () => _aiServiceClient.DetectAnomalyAsync(request, cancellationToken),
            AiFallbackFactory.CreateAnomalyFallback,
            cancellationToken);

    public Task<ServiceResult<ExplainTradeResponseDto>> ExplainTradeAsync(
        ExplainTradeRequestDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteWithFallbackAsync(
            () => _aiServiceClient.ExplainTradeAsync(request, cancellationToken),
            AiFallbackFactory.CreateExplainFallback,
            cancellationToken);

    public async Task<ServiceResult<EvaluateSignalResponseDto>> EvaluateSignalAsync(
        EvaluateSignalRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!TimeframeParser.TryParse(request.Timeframe, out var timeframe))
        {
            return ServiceResult<EvaluateSignalResponseDto>.Fail("Timeframe is invalid.", "timeframe");
        }

        var signal = await _strategySignalRepository.GetByIdAsync(request.StrategySignalId, cancellationToken);
        if (signal is null)
        {
            return ServiceResult<EvaluateSignalResponseDto>.Fail("Strategy signal was not found.", "strategySignalId");
        }

        if (signal.SymbolId != request.SymbolId)
        {
            return ServiceResult<EvaluateSignalResponseDto>.Fail("Symbol does not match the strategy signal.", "symbolId");
        }

        if (signal.Timeframe != timeframe)
        {
            return ServiceResult<EvaluateSignalResponseDto>.Fail("Timeframe does not match the strategy signal.", "timeframe");
        }

        var symbol = await _symbolRepository.GetByIdAsync(request.SymbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<EvaluateSignalResponseDto>.Fail("Symbol was not found.", "symbolId");
        }

        var strategy = await _strategyRepository.GetByIdAsync(signal.StrategyId, cancellationToken);
        if (strategy is null)
        {
            return ServiceResult<EvaluateSignalResponseDto>.Fail("Strategy was not found.", "strategySignalId");
        }

        var riskProfile = await _riskProfileRepository.GetByIdAsync(request.RiskProfileId, cancellationToken);
        if (riskProfile is null)
        {
            return ServiceResult<EvaluateSignalResponseDto>.Fail("Risk profile was not found.", "riskProfileId");
        }

        Candle? candle = null;
        IndicatorSnapshot? snapshot = null;
        IReadOnlyList<IndicatorSnapshot> recentSnapshots = Array.Empty<IndicatorSnapshot>();

        if (signal.CandleId.HasValue)
        {
            candle = await _candleRepository.GetByIdAsync(signal.CandleId.Value, cancellationToken);
            snapshot = await _indicatorSnapshotRepository.GetByKeyAsync(
                signal.SymbolId,
                signal.Timeframe,
                signal.CandleId.Value,
                cancellationToken);

            if (candle is not null)
            {
                recentSnapshots = await _indicatorSnapshotRepository.GetRecentForSymbolAsync(
                    signal.SymbolId,
                    signal.Timeframe,
                    candle.OpenTimeUtc,
                    2,
                    cancellationToken);
            }
        }

        var regimeRequest = BuildRegimeRequest(symbol.SymbolName, timeframe, candle, snapshot, recentSnapshots);
        var regimeResult = await DetectRegimeAsync(regimeRequest, cancellationToken);
        if (!regimeResult.Succeeded || regimeResult.Data is null)
        {
            return ServiceResult<EvaluateSignalResponseDto>.Fail(regimeResult.ErrorMessage ?? "Regime detection failed.");
        }

        var confidenceRequest = BuildConfidenceRequest(
            symbol.SymbolName,
            timeframe,
            strategy.Code,
            signal,
            regimeResult.Data.Regime,
            snapshot,
            candle);
        var confidenceResult = await ScoreConfidenceAsync(confidenceRequest, cancellationToken);
        if (!confidenceResult.Succeeded || confidenceResult.Data is null)
        {
            return ServiceResult<EvaluateSignalResponseDto>.Fail(confidenceResult.ErrorMessage ?? "Confidence scoring failed.");
        }

        var anomalyRequest = BuildAnomalyRequest(symbol.SymbolName, timeframe, candle, snapshot);
        var anomalyResult = await DetectAnomalyAsync(anomalyRequest, cancellationToken);
        var anomaly = anomalyResult.Succeeded ? anomalyResult.Data : AiFallbackFactory.CreateAnomalyFallback();

        var tradeAllowed = AiFallbackFactory.IsAdvisoryEligible(regimeResult.Data, confidenceResult.Data, anomaly);

        await _auditService.LogAsync(
            "AI_EVALUATION_REQUESTED",
            nameof(Domain.Ai.AiDecision),
            entityId: signal.Id,
            userId: _currentUserService.UserId,
            newValueJson: JsonSerializer.Serialize(new
            {
                request.StrategySignalId,
                request.SymbolId,
                request.Timeframe,
                request.RiskProfileId,
                regime = regimeResult.Data.Regime,
                confidenceScore = confidenceResult.Data.ConfidenceScore,
                advisoryEligible = tradeAllowed,
                usedFallback = regimeResult.Data.UsedFallback || confidenceResult.Data.UsedFallback
            }, JsonOptions),
            cancellationToken: cancellationToken);

        var persistResult = await _aiDecisionService.PersistEvaluationAsync(
            new PersistAiEvaluationRequest
            {
                TradingSessionId = signal.TradingSessionId,
                StrategySignalId = signal.Id,
                SymbolId = signal.SymbolId,
                Timeframe = timeframe,
                CandleId = signal.CandleId,
                StrategyCode = strategy.Code,
                Regime = regimeResult.Data,
                Confidence = confidenceResult.Data,
                Anomaly = anomaly,
                TradeAllowed = tradeAllowed,
                RegimeRequest = regimeRequest,
                ConfidenceRequest = confidenceRequest,
                AnomalyRequest = anomalyRequest
            },
            cancellationToken);

        if (!persistResult.Succeeded || persistResult.Data is null)
        {
            return ServiceResult<EvaluateSignalResponseDto>.Fail(persistResult.ErrorMessage ?? "Failed to persist AI decision.");
        }

        return ServiceResult<EvaluateSignalResponseDto>.Ok(new EvaluateSignalResponseDto
        {
            AiDecisionId = persistResult.Data.Id,
            Regime = regimeResult.Data,
            Confidence = confidenceResult.Data,
            Anomaly = anomaly,
            AdvisoryEligible = tradeAllowed
        });
    }

    private async Task<ServiceResult<T>> ExecuteWithFallbackAsync<T>(
        Func<Task<AiClientResult<T>>> operation,
        Func<T> fallbackFactory,
        CancellationToken cancellationToken)
        where T : class
    {
        var result = await operation();
        if (result.Succeeded && result.Data is not null)
        {
            return ServiceResult<T>.Ok(result.Data);
        }

        if (_options.EnableFallback)
        {
            await LogServiceUnavailableAsync(cancellationToken);
            _logger.LogWarning("AI service call failed. Returning safe fallback response.");
            return ServiceResult<T>.Ok(fallbackFactory());
        }

        await LogServiceUnavailableAsync(cancellationToken);
        return ServiceResult<T>.Fail(result.ErrorMessage ?? "AI service call failed.");
    }

    private async Task LogServiceUnavailableAsync(CancellationToken cancellationToken)
    {
        await _auditService.LogAsync(
            "AI_SERVICE_UNAVAILABLE",
            "AiService",
            userId: _currentUserService.UserId,
            newValueJson: $"{{\"enableFallback\":{_options.EnableFallback.ToString().ToLowerInvariant()}}}",
            cancellationToken: cancellationToken);
    }

    private static DetectRegimeRequestDto BuildRegimeRequest(
        string symbol,
        Timeframe timeframe,
        Candle? candle,
        IndicatorSnapshot? snapshot,
        IReadOnlyList<IndicatorSnapshot> recentSnapshots)
    {
        decimal? atrPercent = null;
        if (snapshot?.Atr14 is not null && candle?.Close > 0)
        {
            atrPercent = snapshot.Atr14.Value / candle.Close * 100m;
        }

        bool? swingHighRising = null;
        bool? swingLowRising = null;
        if (recentSnapshots.Count >= 2)
        {
            var previous = recentSnapshots[^2];
            var current = recentSnapshots[^1];
            if (previous.SwingHigh is not null && current.SwingHigh is not null)
            {
                swingHighRising = current.SwingHigh > previous.SwingHigh;
            }

            if (previous.SwingLow is not null && current.SwingLow is not null)
            {
                swingLowRising = current.SwingLow > previous.SwingLow;
            }
        }

        decimal? recentRangePercent = null;
        if (candle is not null && candle.Close > 0)
        {
            recentRangePercent = (candle.High - candle.Low) / candle.Close * 100m;
        }

        return new DetectRegimeRequestDto
        {
            Symbol = symbol,
            Timeframe = TimeframeParser.ToApiString(timeframe),
            Ema20 = snapshot?.Ema20,
            Ema50 = snapshot?.Ema50,
            Ema200 = snapshot?.Ema200,
            Close = candle?.Close,
            AtrPercent = atrPercent,
            Rsi14 = snapshot?.Rsi14,
            Volume = candle?.Volume,
            VolumeSma20 = snapshot?.VolumeSma20,
            SwingHighRising = swingHighRising,
            SwingLowRising = swingLowRising,
            RecentRangePercent = recentRangePercent
        };
    }

    private static ScoreConfidenceRequestDto BuildConfidenceRequest(
        string symbol,
        Timeframe timeframe,
        StrategyCode strategyCode,
        Domain.Signals.StrategySignal signal,
        string marketRegime,
        IndicatorSnapshot? snapshot,
        Candle? candle)
    {
        decimal? atrPercent = null;
        if (snapshot?.Atr14 is not null && candle?.Close > 0)
        {
            atrPercent = snapshot.Atr14.Value / candle.Close * 100m;
        }

        decimal? rewardRiskRatio = null;
        if (signal.EntryPrice is not null &&
            signal.SuggestedStopLoss is not null &&
            signal.SuggestedTakeProfit is not null)
        {
            var risk = Math.Abs(signal.EntryPrice.Value - signal.SuggestedStopLoss.Value);
            var reward = Math.Abs(signal.SuggestedTakeProfit.Value - signal.EntryPrice.Value);
            if (risk > 0)
            {
                rewardRiskRatio = reward / risk;
            }
        }

        return new ScoreConfidenceRequestDto
        {
            Symbol = symbol,
            Timeframe = TimeframeParser.ToApiString(timeframe),
            StrategyCode = strategyCode.ToCode(),
            SignalDirection = signal.Direction.ToString(),
            MarketRegime = marketRegime,
            StrategyStrength = signal.Strength,
            EmaAlignmentScore = CalculateEmaAlignmentScore(signal.Direction, snapshot),
            VolumeConfirmation = candle?.Volume is not null && snapshot?.VolumeSma20 is not null
                ? candle.Volume >= snapshot.VolumeSma20
                : null,
            Rsi14 = snapshot?.Rsi14,
            AtrPercent = atrPercent,
            RewardRiskRatio = rewardRiskRatio
        };
    }

    private static DetectAnomalyRequestDto BuildAnomalyRequest(
        string symbol,
        Timeframe timeframe,
        Candle? candle,
        IndicatorSnapshot? snapshot)
    {
        decimal? atrPercent = null;
        decimal? candleRangePercent = null;
        if (candle is not null && candle.Close > 0)
        {
            candleRangePercent = (candle.High - candle.Low) / candle.Close * 100m;
            if (snapshot?.Atr14 is not null)
            {
                atrPercent = snapshot.Atr14.Value / candle.Close * 100m;
            }
        }

        return new DetectAnomalyRequestDto
        {
            Symbol = symbol,
            Timeframe = TimeframeParser.ToApiString(timeframe),
            AtrPercent = atrPercent,
            Volume = candle?.Volume,
            VolumeSma20 = snapshot?.VolumeSma20,
            CandleRangePercent = candleRangePercent
        };
    }

    private static decimal? CalculateEmaAlignmentScore(
        TradeDirection direction,
        IndicatorSnapshot? snapshot)
    {
        if (snapshot?.Ema20 is null || snapshot.Ema50 is null || snapshot.Ema200 is null)
        {
            return null;
        }

        return direction switch
        {
            TradeDirection.Long when snapshot.Ema20 > snapshot.Ema50 && snapshot.Ema50 > snapshot.Ema200 => 85m,
            TradeDirection.Short when snapshot.Ema20 < snapshot.Ema50 && snapshot.Ema50 < snapshot.Ema200 => 85m,
            TradeDirection.Long or TradeDirection.Short => 35m,
            _ => null
        };
    }
}
