using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Ai.Dtos;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Domain.Ai;
using MomoQuant.Domain.Enums;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Ai;

public sealed class PersistAiEvaluationRequest
{
    public long? TradingSessionId { get; init; }
    public long StrategySignalId { get; init; }
    public long SymbolId { get; init; }
    public required Timeframe Timeframe { get; init; }
    public long? CandleId { get; init; }
    public StrategyCode StrategyCode { get; init; }
    public required DetectRegimeResponseDto Regime { get; init; }
    public required ScoreConfidenceResponseDto Confidence { get; init; }
    public DetectAnomalyResponseDto? Anomaly { get; init; }
    public bool TradeAllowed { get; init; }
    public required DetectRegimeRequestDto RegimeRequest { get; init; }
    public required ScoreConfidenceRequestDto ConfidenceRequest { get; init; }
    public DetectAnomalyRequestDto? AnomalyRequest { get; init; }
}

public interface IAiDecisionService
{
    Task<ServiceResult<AiDecisionDto>> GetDecisionByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResult<AiDecisionDto>>> GetDecisionsAsync(
        PagedRequest request,
        long? symbolId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AiDecisionDto>> PersistEvaluationAsync(
        PersistAiEvaluationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class AiDecisionService : IAiDecisionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IAiDecisionRepository _aiDecisionRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;

    public AiDecisionService(
        IAiDecisionRepository aiDecisionRepository,
        ICurrentUserService currentUserService,
        IAuditService auditService)
    {
        _aiDecisionRepository = aiDecisionRepository;
        _currentUserService = currentUserService;
        _auditService = auditService;
    }

    public async Task<ServiceResult<AiDecisionDto>> GetDecisionByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var decision = await _aiDecisionRepository.GetByIdAsync(id, cancellationToken);
        return decision is null
            ? ServiceResult<AiDecisionDto>.Fail("AI decision was not found.")
            : ServiceResult<AiDecisionDto>.Ok(MapToDto(decision));
    }

    public async Task<ServiceResult<PagedResult<AiDecisionDto>>> GetDecisionsAsync(
        PagedRequest request,
        long? symbolId,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _aiDecisionRepository.GetPagedAsync(request, symbolId, cancellationToken);
        return ServiceResult<PagedResult<AiDecisionDto>>.Ok(new PagedResult<AiDecisionDto>
        {
            Items = items.Select(item => MapToDto(item)).ToList(),
            Page = Math.Max(request.Page, 1),
            PageSize = Math.Clamp(request.PageSize, 1, 100),
            TotalCount = totalCount
        });
    }

    public async Task<ServiceResult<AiDecisionDto>> PersistEvaluationAsync(
        PersistAiEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        var marketRegime = ParseMarketRegime(request.Regime.Regime);
        var reasons = MergeReasons(request.Regime.Reasons, request.Confidence.Reasons, request.Anomaly?.Reasons);
        var warnings = request.Confidence.Warnings.ToList();
        if (request.Anomaly?.IsAnomalous == true)
        {
            warnings.Add($"Anomaly detected ({request.Anomaly.Severity}).");
        }

        var usedFallback = request.Regime.UsedFallback ||
                           request.Confidence.UsedFallback ||
                           request.Anomaly?.UsedFallback == true;

        var decision = new AiDecision
        {
            TradingSessionId = request.TradingSessionId,
            SymbolId = request.SymbolId,
            Timeframe = request.Timeframe,
            CandleId = request.CandleId,
            SignalId = request.StrategySignalId,
            MarketRegime = marketRegime,
            RegimeConfidence = request.Regime.Confidence,
            ConfidenceScore = request.Confidence.ConfidenceScore,
            ConfidenceClassification = request.Confidence.Classification,
            PreferredStrategyCode = request.StrategyCode,
            IsAnomalous = request.Anomaly?.IsAnomalous ?? false,
            AnomalySeverity = request.Anomaly?.Severity,
            TradeAllowed = request.TradeAllowed,
            Summary = BuildSummary(request.Regime, request.Confidence, request.Anomaly),
            Explanation = request.Confidence.Reasons.FirstOrDefault() ?? "AI evaluation completed.",
            ReasonsJson = JsonSerializer.Serialize(reasons, JsonOptions),
            WarningsJson = warnings.Count > 0 ? JsonSerializer.Serialize(warnings, JsonOptions) : null,
            RawRequestJson = JsonSerializer.Serialize(new
            {
                regime = request.RegimeRequest,
                confidence = request.ConfidenceRequest,
                anomaly = request.AnomalyRequest
            }, JsonOptions),
            RawResponseJson = JsonSerializer.Serialize(new
            {
                regime = request.Regime,
                confidence = request.Confidence,
                anomaly = request.Anomaly
            }, JsonOptions),
            CreatedAtUtc = DateTime.UtcNow
        };

        await _aiDecisionRepository.AddAsync(decision, cancellationToken);
        await _aiDecisionRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "AI_DECISION_PERSISTED",
            nameof(AiDecision),
            entityId: decision.Id,
            userId: _currentUserService.UserId,
            newValueJson: JsonSerializer.Serialize(new
            {
                decision.SymbolId,
                timeframe = TimeframeParser.ToApiString(decision.Timeframe),
                marketRegime = decision.MarketRegime.ToString(),
                decision.ConfidenceScore,
                decision.TradeAllowed,
                usedFallback
            }, JsonOptions),
            cancellationToken: cancellationToken);

        return ServiceResult<AiDecisionDto>.Ok(MapToDto(decision, usedFallback));
    }

    private static MarketRegime ParseMarketRegime(string regime)
    {
        return Enum.TryParse<MarketRegime>(regime, ignoreCase: true, out var parsed)
            ? parsed
            : MarketRegime.Unknown;
    }

    private static IReadOnlyList<string> MergeReasons(
        IReadOnlyList<string> regimeReasons,
        IReadOnlyList<string> confidenceReasons,
        IReadOnlyList<string>? anomalyReasons)
    {
        var reasons = new List<string>();
        reasons.AddRange(regimeReasons);
        reasons.AddRange(confidenceReasons);
        if (anomalyReasons is not null)
        {
            reasons.AddRange(anomalyReasons);
        }

        return reasons;
    }

    private static string BuildSummary(
        DetectRegimeResponseDto regime,
        ScoreConfidenceResponseDto confidence,
        DetectAnomalyResponseDto? anomaly)
    {
        var summary = $"Regime {regime.Regime} ({regime.Confidence}%), confidence {confidence.ConfidenceScore} ({confidence.Classification}).";
        if (anomaly?.IsAnomalous == true)
        {
            summary += $" Anomaly severity: {anomaly.Severity}.";
        }

        return summary;
    }

    private static AiDecisionDto MapToDto(AiDecision decision, bool? usedFallback = null) => new()
    {
        Id = decision.Id,
        TradingSessionId = decision.TradingSessionId,
        StrategySignalId = decision.SignalId,
        SymbolId = decision.SymbolId,
        Timeframe = TimeframeParser.ToApiString(decision.Timeframe),
        StrategyCode = decision.PreferredStrategyCode?.ToCode(),
        MarketRegime = decision.MarketRegime.ToString(),
        RegimeConfidence = decision.RegimeConfidence,
        ConfidenceScore = decision.ConfidenceScore,
        ConfidenceClassification = decision.ConfidenceClassification,
        IsAnomalous = decision.IsAnomalous,
        AnomalySeverity = decision.AnomalySeverity,
        TradeAllowed = decision.TradeAllowed,
        Summary = decision.Summary,
        Explanation = decision.Explanation,
        ReasonsJson = decision.ReasonsJson,
        WarningsJson = decision.WarningsJson,
        CreatedAtUtc = decision.CreatedAtUtc,
        UsedFallback = usedFallback ?? decision.MarketRegime == MarketRegime.Unknown && decision.ConfidenceScore == 0
    };
}
