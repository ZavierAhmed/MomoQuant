using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.MarketData.Dtos;
using MomoQuant.Application.Options;
using MomoQuant.Application.TradingSystems.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.TradingSystems;

namespace MomoQuant.Application.TradingSystems;

public sealed class SkSystemAnalysisService : ISkSystemAnalysisService
{
    private readonly ISwingStructureService _swingStructureService;
    private readonly ISkSequenceAnalyzer _sequenceAnalyzer;
    private readonly ISkMultiTimeframeContextService _multiTimeframeContextService;
    private readonly ISkSystemAiSummaryService _aiSummaryService;
    private readonly ICandleRepository _candleRepository;
    private readonly IExchangeRepository _exchangeRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly ITradingSystemAnalysisRepository _analysisRepository;
    private readonly IMarketDataService _marketDataService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;
    private readonly SkSystemSettings _settings;
    private readonly ILogger<SkSystemAnalysisService> _logger;

    public SkSystemAnalysisService(
        ISwingStructureService swingStructureService,
        ISkSequenceAnalyzer sequenceAnalyzer,
        ISkMultiTimeframeContextService multiTimeframeContextService,
        ISkSystemAiSummaryService aiSummaryService,
        ICandleRepository candleRepository,
        IExchangeRepository exchangeRepository,
        ISymbolRepository symbolRepository,
        ITradingSystemAnalysisRepository analysisRepository,
        IMarketDataService marketDataService,
        ICurrentUserService currentUserService,
        IAuditService auditService,
        IOptions<SkSystemSettings> settings,
        ILogger<SkSystemAnalysisService> logger)
    {
        _swingStructureService = swingStructureService;
        _sequenceAnalyzer = sequenceAnalyzer;
        _multiTimeframeContextService = multiTimeframeContextService;
        _aiSummaryService = aiSummaryService;
        _candleRepository = candleRepository;
        _exchangeRepository = exchangeRepository;
        _symbolRepository = symbolRepository;
        _analysisRepository = analysisRepository;
        _marketDataService = marketDataService;
        _currentUserService = currentUserService;
        _auditService = auditService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ServiceResult<SkSystemAnalysisResultDto>> AnalyzeAsync(
        SkSystemAnalyzeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return ServiceResult<SkSystemAnalysisResultDto>.Fail("Request is required.");
        }

        if (!SkSystemConstants.IsSupportedPrimaryTimeframe(request.PrimaryTimeframe) ||
            !TimeframeParser.TryParse(request.PrimaryTimeframe, out var primaryTimeframe))
        {
            return ServiceResult<SkSystemAnalysisResultDto>.Fail(
                $"Primary timeframe '{request.PrimaryTimeframe}' is not supported for analysis.",
                "primaryTimeframe");
        }

        if (!SkSystemConstants.IsSupportedHigherTimeframe(request.HigherTimeframe) ||
            !TimeframeParser.TryParse(request.HigherTimeframe, out var higherTimeframe))
        {
            return ServiceResult<SkSystemAnalysisResultDto>.Fail(
                $"Higher timeframe '{request.HigherTimeframe}' is not supported for analysis.",
                "higherTimeframe");
        }

        if (!TimeframeParser.IsHigherTimeframe(request.HigherTimeframe, request.PrimaryTimeframe))
        {
            return ServiceResult<SkSystemAnalysisResultDto>.Fail(
                $"Higher timeframe '{request.HigherTimeframe}' must be longer than primary timeframe '{request.PrimaryTimeframe}'.",
                "higherTimeframe");
        }

        var exchange = await _exchangeRepository.GetByIdAsync(request.ExchangeId, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<SkSystemAnalysisResultDto>.Fail("Exchange was not found.", "exchangeId");
        }

        var symbol = await _symbolRepository.GetByIdAsync(request.SymbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<SkSystemAnalysisResultDto>.Fail("Symbol was not found.", "symbolId");
        }

        if (symbol.ExchangeId != exchange.Id)
        {
            return ServiceResult<SkSystemAnalysisResultDto>.Fail(
                "Symbol does not belong to the specified exchange.",
                "symbolId");
        }

        var lookback = Math.Clamp(
            request.LookbackCandles <= 0 ? SkSystemConstants.DefaultLookbackCandles : request.LookbackCandles,
            SkSystemConstants.MinLookbackCandles,
            SkSystemConstants.MaxLookbackCandles);

        var sensitivity = SkSystemConstants.NormalizeSensitivity(request.SwingSensitivity);
        var directionMode = SkSystemConstants.NormalizeDirectionMode(request.DirectionMode);
        var explanationMode = SkSystemConstants.NormalizeExplanationMode(request.ExplanationMode);

        var now = DateTime.UtcNow;
        var primaryCandles = await _candleRepository.GetRecentCandlesAsync(
            symbol.Id, primaryTimeframe, now, lookback, cancellationToken);

        var minimumRequired = Math.Min(lookback, SkSystemConstants.MinLookbackCandles);
        if (primaryCandles.Count < minimumRequired && request.AutoImportMissingCandles)
        {
            _logger.LogInformation(
                "SK analysis auto-importing missing candles for {Symbol} {Primary}/{Higher}",
                symbol.SymbolName,
                request.PrimaryTimeframe,
                request.HigherTimeframe);

            var importResult = await ImportRequiredDataAsync(new SkImportRequiredDataRequest
            {
                ExchangeId = request.ExchangeId,
                SymbolId = request.SymbolId,
                PrimaryTimeframe = request.PrimaryTimeframe,
                HigherTimeframe = request.HigherTimeframe,
                LookbackCandles = lookback
            }, cancellationToken);

            if (!importResult.Succeeded)
            {
                return ServiceResult<SkSystemAnalysisResultDto>.Fail(
                    $"Could not load enough candles for {symbol.SymbolName} {request.PrimaryTimeframe}/{request.HigherTimeframe}. Check Binance public data connectivity.",
                    "candles");
            }

            primaryCandles = await _candleRepository.GetRecentCandlesAsync(
                symbol.Id, primaryTimeframe, now, lookback, cancellationToken);
        }

        if (primaryCandles.Count < minimumRequired)
        {
            return ServiceResult<SkSystemAnalysisResultDto>.Fail(
                "Required candles are missing. Import data first.",
                "candles");
        }

        var higherCandles = await _candleRepository.GetRecentCandlesAsync(
            symbol.Id, higherTimeframe, now, Math.Min(lookback, 300), cancellationToken);

        var result = RunAnalysis(
            exchange.Id,
            exchange.Name,
            symbol.Id,
            symbol.SymbolName,
            request.PrimaryTimeframe,
            request.HigherTimeframe,
            lookback,
            sensitivity,
            directionMode,
            explanationMode,
            SkSystemConstants.NormalizeQuickViewMode(request.QuickViewMode),
            request.UseAiSummary,
            primaryCandles,
            higherCandles,
            cancellationToken);

        var analysisResult = await result;

        var entity = MapToEntity(analysisResult, now);
        await _analysisRepository.AddAsync(entity, cancellationToken);
        await _analysisRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "TRADING_SYSTEM_ANALYSIS_CREATED",
            nameof(TradingSystemAnalysis),
            entityId: entity.Id,
            userId: _currentUserService.UserId,
            newValueJson: SkSystemJson.Serialize(new
            {
                entity.SystemCode,
                entity.Symbol,
                entity.PrimaryTimeframe,
                entity.HigherTimeframe,
                entity.MarketBias
            }),
            cancellationToken: cancellationToken);

        return ServiceResult<SkSystemAnalysisResultDto>.Ok(analysisResult with { AnalysisId = entity.Id });
    }

    public async Task<ServiceResult<SkSystemAnalysisResultDto>> AnalyzeSnapshotAsync(
        SkSystemAnalyzeRequest request,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAnalysisInputsAsync(request, cancellationToken);
        if (!prepared.Succeeded || prepared.Data is null)
        {
            return ServiceResult<SkSystemAnalysisResultDto>.Fail(
                prepared.ErrorMessage ?? "Analysis failed.",
                prepared.ErrorField);
        }

        var input = prepared.Data;
        var analysisResult = await RunAnalysis(
            input.ExchangeId,
            input.ExchangeName,
            input.SymbolId,
            input.SymbolName,
            input.PrimaryTimeframe,
            input.HigherTimeframe,
            input.Lookback,
            input.Sensitivity,
            input.DirectionMode,
            input.ExplanationMode,
            input.QuickViewMode,
            input.UseAiSummary,
            input.PrimaryCandles,
            input.HigherCandles,
            cancellationToken);

        return ServiceResult<SkSystemAnalysisResultDto>.Ok(analysisResult);
    }

    private sealed class AnalysisInput
    {
        public required long ExchangeId { get; init; }
        public required string ExchangeName { get; init; }
        public required long SymbolId { get; init; }
        public required string SymbolName { get; init; }
        public required string PrimaryTimeframe { get; init; }
        public required string HigherTimeframe { get; init; }
        public required int Lookback { get; init; }
        public required string Sensitivity { get; init; }
        public required string DirectionMode { get; init; }
        public required string ExplanationMode { get; init; }
        public required string QuickViewMode { get; init; }
        public required bool UseAiSummary { get; init; }
        public required IReadOnlyList<Candle> PrimaryCandles { get; init; }
        public required IReadOnlyList<Candle> HigherCandles { get; init; }
    }

    private async Task<ServiceResult<AnalysisInput>> PrepareAnalysisInputsAsync(
        SkSystemAnalyzeRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return ServiceResult<AnalysisInput>.Fail("Request is required.");
        }

        if (!SkSystemConstants.IsSupportedPrimaryTimeframe(request.PrimaryTimeframe) ||
            !TimeframeParser.TryParse(request.PrimaryTimeframe, out var primaryTimeframe))
        {
            return ServiceResult<AnalysisInput>.Fail(
                $"Primary timeframe '{request.PrimaryTimeframe}' is not supported for analysis.",
                "primaryTimeframe");
        }

        if (!SkSystemConstants.IsSupportedHigherTimeframe(request.HigherTimeframe) ||
            !TimeframeParser.TryParse(request.HigherTimeframe, out var higherTimeframe))
        {
            return ServiceResult<AnalysisInput>.Fail(
                $"Higher timeframe '{request.HigherTimeframe}' is not supported for analysis.",
                "higherTimeframe");
        }

        if (!TimeframeParser.IsHigherTimeframe(request.HigherTimeframe, request.PrimaryTimeframe))
        {
            return ServiceResult<AnalysisInput>.Fail(
                $"Higher timeframe '{request.HigherTimeframe}' must be longer than primary timeframe '{request.PrimaryTimeframe}'.",
                "higherTimeframe");
        }

        var exchange = await _exchangeRepository.GetByIdAsync(request.ExchangeId, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<AnalysisInput>.Fail("Exchange was not found.", "exchangeId");
        }

        var symbol = await _symbolRepository.GetByIdAsync(request.SymbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<AnalysisInput>.Fail("Symbol was not found.", "symbolId");
        }

        if (symbol.ExchangeId != exchange.Id)
        {
            return ServiceResult<AnalysisInput>.Fail(
                "Symbol does not belong to the specified exchange.",
                "symbolId");
        }

        var lookback = Math.Clamp(
            request.LookbackCandles <= 0 ? SkSystemConstants.DefaultLookbackCandles : request.LookbackCandles,
            SkSystemConstants.MinLookbackCandles,
            SkSystemConstants.MaxLookbackCandles);

        var now = DateTime.UtcNow;
        var primaryCandles = await _candleRepository.GetRecentCandlesAsync(
            symbol.Id, primaryTimeframe, now, lookback, cancellationToken);

        var minimumRequired = Math.Min(lookback, SkSystemConstants.MinLookbackCandles);
        if (primaryCandles.Count < minimumRequired && request.AutoImportMissingCandles)
        {
            var importResult = await ImportRequiredDataAsync(new SkImportRequiredDataRequest
            {
                ExchangeId = request.ExchangeId,
                SymbolId = request.SymbolId,
                PrimaryTimeframe = request.PrimaryTimeframe,
                HigherTimeframe = request.HigherTimeframe,
                LookbackCandles = lookback
            }, cancellationToken);

            if (!importResult.Succeeded)
            {
                return ServiceResult<AnalysisInput>.Fail(
                    $"Could not load enough candles for {symbol.SymbolName} {request.PrimaryTimeframe}/{request.HigherTimeframe}.",
                    "candles");
            }

            primaryCandles = await _candleRepository.GetRecentCandlesAsync(
                symbol.Id, primaryTimeframe, now, lookback, cancellationToken);
        }

        if (primaryCandles.Count < minimumRequired)
        {
            return ServiceResult<AnalysisInput>.Fail("Required candles are missing. Import data first.", "candles");
        }

        var higherCandles = await _candleRepository.GetRecentCandlesAsync(
            symbol.Id, higherTimeframe, now, Math.Min(lookback, 300), cancellationToken);

        return ServiceResult<AnalysisInput>.Ok(new AnalysisInput
        {
            ExchangeId = exchange.Id,
            ExchangeName = exchange.Name,
            SymbolId = symbol.Id,
            SymbolName = symbol.SymbolName,
            PrimaryTimeframe = request.PrimaryTimeframe,
            HigherTimeframe = request.HigherTimeframe,
            Lookback = lookback,
            Sensitivity = SkSystemConstants.NormalizeSensitivity(request.SwingSensitivity),
            DirectionMode = SkSystemConstants.NormalizeDirectionMode(request.DirectionMode),
            ExplanationMode = SkSystemConstants.NormalizeExplanationMode(request.ExplanationMode),
            QuickViewMode = SkSystemConstants.NormalizeQuickViewMode(request.QuickViewMode),
            UseAiSummary = request.UseAiSummary,
            PrimaryCandles = primaryCandles,
            HigherCandles = higherCandles
        });
    }

    private async Task<SkSystemAnalysisResultDto> RunAnalysis(
        long exchangeId,
        string exchangeName,
        long symbolId,
        string symbolName,
        string primaryTimeframe,
        string higherTimeframe,
        int lookback,
        string sensitivity,
        string directionMode,
        string explanationMode,
        string quickViewMode,
        bool useAiSummary,
        IReadOnlyList<Candle> primaryCandles,
        IReadOnlyList<Candle> higherCandles,
        CancellationToken cancellationToken)
    {
        var orderedPrimary = primaryCandles.OrderBy(candle => candle.OpenTimeUtc).ToList();
        var latest = orderedPrimary[^1];
        var currentPrice = latest.Close;
        var priceDecimals = SkPriceFormatter.ResolveDecimals(symbolName, currentPrice);

        var swings = _swingStructureService.DetectSwings(orderedPrimary, sensitivity, _settings);
        var sequence = _sequenceAnalyzer.Analyze(swings, currentPrice, directionMode, _settings);

        var warnings = new List<string>();
        if (swings.Count < 4)
        {
            warnings.Add("Few swing points detected; structure may be unclear.");
        }

        if (sequence.Candidates.Count == 0)
        {
            warnings.Add("No SK sequence candidates were detected for the selected settings.");
        }

        var primaryBias = ResolveBias(sequence.Candidates);

        var higherContext = _multiTimeframeContextService.BuildContext(
            higherCandles, higherTimeframe, primaryBias, sensitivity, _settings);

        if (higherContext.ConflictWarning is { Length: > 0 } conflict)
        {
            warnings.Add(conflict);
        }

        var finalBias = ResolveFinalBias(primaryBias, higherContext.HigherTimeframeBias);
        var confidenceLabel = ResolveConfidenceLabel(sequence.Candidates, higherContext.ConflictWarning is not null);

        var eligibleCandidates = sequence.Candidates.Where(c => c.EligibleForBestIdea).ToList();
        foreach (var invalid in sequence.Candidates.Where(c => !c.EligibleForBestIdea && !string.IsNullOrWhiteSpace(c.ValidationMessage)))
        {
            if (!warnings.Contains(invalid.ValidationMessage))
            {
                warnings.Add(invalid.ValidationMessage);
            }
        }

        var bestBull = eligibleCandidates
            .Where(c => c.Direction == "Bullish")
            .OrderByDescending(c => c.ConfidenceScore)
            .FirstOrDefault();
        var bestBear = eligibleCandidates
            .Where(c => c.Direction == "Bearish")
            .OrderByDescending(c => c.ConfidenceScore)
            .FirstOrDefault();

        var aiSummary = await _aiSummaryService.BuildSummaryAsync(new SkSummaryInput
        {
            Symbol = symbolName,
            PrimaryTimeframe = primaryTimeframe,
            HigherTimeframe = higherTimeframe,
            CurrentPrice = currentPrice,
            MarketBias = finalBias,
            ConfidenceLabel = confidenceLabel,
            Candidates = sequence.Candidates,
            HigherTimeframeContext = higherContext,
            Warnings = warnings,
            UseAiSummary = useAiSummary,
            ExplanationMode = explanationMode,
            PriceDecimals = priceDecimals
        }, cancellationToken);

        var bestBullIdea = BuildIdea(bestBull, priceDecimals, higherContext, sequence.Candidates.Count);
        var bestBearIdea = BuildIdea(bestBear, priceDecimals, higherContext, sequence.Candidates.Count);

        var (clarityReasons, clarityWarnings) = BuildClarity(
            eligibleCandidates, higherContext, finalBias, confidenceLabel, primaryTimeframe, higherTimeframe, priceDecimals);

        var keyIdea = SelectKeyIdea(finalBias, bestBullIdea, bestBearIdea);
        var keyAreaToWatch = keyIdea is null
            ? "No clear reaction area — no confirmed setup detected."
            : $"Reaction zone: {keyIdea.ReactionZoneText}";
        var dangerLevelToWatch = BuildDangerLevelToWatch(keyIdea);

        if (eligibleCandidates.Count == 0)
        {
            warnings.Add("No clear SK structure found that passed directional validation.");
        }

        var htfContextDto = BuildHtfContext(
            higherTimeframe, higherContext, finalBias, aiSummary.HigherTimeframeExplanation);
        var ltfContextDto = BuildLtfContext(primaryTimeframe, keyIdea, confidenceLabel, clarityWarnings);

        var htfAgrees = higherContext.ConflictWarning is null &&
                        finalBias == higherContext.HigherTimeframeBias &&
                        finalBias is "Bullish" or "Bearish";
        var competingCount = sequence.Candidates.Count;

        var sequences = sequence.Candidates
            .Select(candidate => SkSequenceAnatomyMapper.Map(
                candidate,
                symbolName,
                primaryTimeframe,
                currentPrice,
                htfAgrees,
                competingCount,
                candidate.Id == bestBull?.Id || candidate.Id == bestBear?.Id,
                candidate.Id == bestBull?.Id
                    ? "Highest clarity valid upward structure."
                    : candidate.Id == bestBear?.Id
                        ? "Highest clarity valid downward structure."
                        : null))
            .ToList();

        var selectedSequence = sequences.FirstOrDefault(s => s.SelectedAsBest)
            ?? sequences.Where(s => s.EligibleForBestIdea)
                .OrderByDescending(s => s.ClarityScore)
                .FirstOrDefault();

        var conceptAudit = SkConceptAuditBuilder.Build(
            sequences, selectedSequence, htfContextDto, ltfContextDto, bestBullIdea, bestBearIdea);

        var overlays = BuildChartOverlays(
            swings, sequence, higherContext, currentPrice, latest.OpenTimeUtc, priceDecimals,
            bestBull?.Id, bestBear?.Id, symbolName);

        var chartCandles = orderedPrimary
            .Select(candle => new SkChartCandleDto
            {
                TimeUtc = candle.OpenTimeUtc,
                Open = candle.Open,
                High = candle.High,
                Low = candle.Low,
                Close = candle.Close,
                Volume = candle.Volume
            })
            .ToList();

        var invalidationLevels = sequence.Candidates
            .Select(candidate =>
            {
                var side = candidate.Direction == "Bullish" ? "below" : "above";
                var moveWord = candidate.Direction == "Bullish" ? "upward" : "downward";
                return $"The {moveWord} idea is no longer valid {side} {SkPriceFormatter.Format(candidate.InvalidationLevel, priceDecimals)}.";
            })
            .ToList();

        var diagnostics = new SkAnalysisDiagnosticsDto
        {
            PrimaryCandleCount = orderedPrimary.Count,
            HigherCandleCount = higherCandles.Count,
            SwingHighCount = swings.Count(swing => swing.Type == "High"),
            SwingLowCount = swings.Count(swing => swing.Type == "Low"),
            SequenceCandidateCount = sequence.Candidates.Count,
            ResolvedSensitivity = sensitivity,
            MinSwingDistancePercent = _settings.MinSwingDistancePercent,
            MinSwingCandles = _settings.MinSwingCandles,
            FibonacciCorrectionLevels = _settings.FibonacciCorrectionLevels,
            FibonacciExtensionLevels = _settings.FibonacciExtensionLevels
        };

        return new SkSystemAnalysisResultDto
        {
            AnalysisId = 0,
            SystemCode = SkSystemConstants.SystemCode,
            SystemName = SkSystemConstants.SystemName,
            ExchangeId = exchangeId,
            ExchangeName = exchangeName,
            SymbolId = symbolId,
            Symbol = symbolName,
            PrimaryTimeframe = primaryTimeframe,
            HigherTimeframe = higherTimeframe,
            LookbackCandles = lookback,
            SwingSensitivity = sensitivity,
            DirectionMode = directionMode,
            Status = "Completed",
            AnalysisTimeUtc = DateTime.UtcNow,
            LatestCandleTimeUtc = latest.OpenTimeUtc,
            CurrentPrice = currentPrice,
            MarketBias = finalBias,
            ConfidenceLabel = confidenceLabel,
            Summary = aiSummary.Summary,
            BullishScenario = aiSummary.BullishScenario,
            BearishScenario = aiSummary.BearishScenario,
            InvalidationLevels = invalidationLevels,
            ExplanationMode = explanationMode,
            PriceDecimals = priceDecimals,
            PlainLanguageSummary = aiSummary.PlainLanguageSummary,
            BottomLine = aiSummary.BottomLine,
            WhatThisMeans = aiSummary.WhatThisMeans,
            KeyAreaToWatch = keyAreaToWatch,
            DangerLevelToWatch = dangerLevelToWatch,
            HigherTimeframeExplanation = aiSummary.HigherTimeframeExplanation,
            ConflictExplanation = aiSummary.ConflictExplanation,
            BestBullishIdea = bestBullIdea,
            BestBearishIdea = bestBearIdea,
            ClarityReasons = clarityReasons,
            ClarityWarnings = clarityWarnings,
            GlossaryTerms = SkGlossary.Terms,
            DisplayLabels = SkDisplayLabels.Map,
            SwingPoints = swings,
            SequenceCandidates = sequence.Candidates,
            FibonacciZones = sequence.FibonacciZones,
            KeyLevels = sequence.KeyLevels,
            ChartOverlays = overlays,
            HigherTimeframeContext = higherContext,
            HtfContext = htfContextDto,
            LtfContext = ltfContextDto,
            AiSummary = aiSummary,
            Sequences = sequences,
            ConceptAudit = conceptAudit,
            QuickViewMode = quickViewMode,
            AnalysisOnlyDisclaimer = SkSystemConstants.AnalysisOnlyDisclaimer,
            Candles = chartCandles,
            Warnings = warnings,
            Diagnostics = diagnostics,
            AnalysisOnly = true
        };
    }

    private static SkIdeaDto? BuildIdea(
        SkSequenceCandidateDto? candidate,
        int priceDecimals,
        SkMultiTimeframeContextDto higherContext,
        int competingStructureCount)
    {
        if (candidate is null)
        {
            return null;
        }

        var upward = candidate.Direction == "Bullish";
        var htfAgrees = higherContext.ConflictWarning is null;
        var (clarityScore, clarityLabel) = SkConceptScoring.ComputeClarity(
            candidate, htfAgrees, competingStructureCount);

        return new SkIdeaDto
        {
            Direction = candidate.Direction,
            DirectionLabel = SkDisplayLabels.Direction(candidate.Direction),
            Status = candidate.Status,
            StatusLabel = candidate.ValidationStatus != SkScenarioValidator.Valid
                ? SkDisplayLabels.Status(candidate.ValidationStatus)
                : SkDisplayLabels.Status(candidate.Status),
            ClarityLabel = clarityLabel,
            ClarityScore = clarityScore,
            ReactionZoneMin = candidate.CorrectionZoneMin,
            ReactionZoneMax = candidate.CorrectionZoneMax,
            ReactionZoneText = SkPriceFormatter.Range(candidate.CorrectionZoneMin, candidate.CorrectionZoneMax, priceDecimals),
            StrongReactionZoneMin = candidate.GoldenPocketMin,
            StrongReactionZoneMax = candidate.GoldenPocketMax,
            StrongReactionZoneText = SkPriceFormatter.Range(candidate.GoldenPocketMin, candidate.GoldenPocketMax, priceDecimals),
            DangerLevel = candidate.InvalidationLevel,
            DangerLevelText = SkPriceFormatter.Format(candidate.InvalidationLevel, priceDecimals),
            Target1 = candidate.Target1,
            Target2 = candidate.Target2,
            TargetsText = $"{SkPriceFormatter.Format(candidate.Target1, priceDecimals)}, then {SkPriceFormatter.Format(candidate.Target2, priceDecimals)}",
            CurrentPricePositionLabel = SkDisplayLabels.Position(candidate.CurrentPricePosition),
            WhyItMatters = upward
                ? "This is the clearest upward structure detected. It matters while price stays above the danger level."
                : "This is the clearest downward structure detected. It matters while price stays below the danger level.",
            PlainExplanation = BuildPlainExplanation(candidate, upward),
            CandidateId = candidate.Id,
            ValidationStatus = candidate.ValidationStatus,
            ValidationMessage = candidate.ValidationMessage
        };
    }

    private static string BuildPlainExplanation(SkSequenceCandidateDto candidate, bool upward)
    {
        if (candidate.ValidationStatus == SkScenarioValidator.AlreadyReached)
        {
            return SkScenarioValidator.AlreadyReachedMessage;
        }

        if (candidate.ValidationStatus == SkScenarioValidator.StructureInvalidated)
        {
            return SkScenarioValidator.StructureInvalidatedMessage;
        }

        if (candidate.ValidationStatus == SkScenarioValidator.DirectionMismatch)
        {
            return SkScenarioValidator.DirectionMismatchMessage;
        }

        return upward
            ? "Price made a strong upward move and may pull back before another move up. This idea is only useful while price stays above the danger level."
            : "Price made a strong downward move and may bounce before another move down. This idea is only useful while price stays below the danger level.";
    }

    private static string ClarityFromScore(decimal score) =>
        score >= 70m ? "High" : score >= 50m ? "Medium" : "Low";

    private static SkTimeframeContextDto BuildHtfContext(
        string timeframe,
        SkMultiTimeframeContextDto context,
        string primaryBias,
        string summary) =>
        new()
        {
            Timeframe = timeframe,
            Role = "HTF",
            Direction = context.HigherTimeframeBias,
            Summary = string.IsNullOrWhiteSpace(summary)
                ? context.HigherTimeframeTrendDescription
                : summary,
            ReactionZoneText = context.ImportantHigherTimeframeLevels.Count > 0
                ? string.Join(", ", context.ImportantHigherTimeframeLevels.Select(level => level.Label))
                : "No major higher-timeframe zones detected.",
            ClarityLabel = context.ConflictWarning is null ? "Medium" : "Low",
            AgreesWithPrimary = primaryBias == context.HigherTimeframeBias &&
                                primaryBias is "Bullish" or "Bearish",
            Warnings = context.ConflictWarning is null ? [] : new[] { context.ConflictWarning }
        };

    private static SkTimeframeContextDto BuildLtfContext(
        string timeframe,
        SkIdeaDto? idea,
        string clarityLabel,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Timeframe = timeframe,
            Role = "LTF",
            Direction = idea?.DirectionLabel ?? "Unclear",
            Summary = idea?.PlainExplanation ?? "No clear SK structure found.",
            ReactionZoneText = idea?.ReactionZoneText ?? "—",
            DangerLevelText = idea?.DangerLevelText ?? "—",
            TargetsText = idea?.TargetsText ?? "—",
            ClarityLabel = clarityLabel,
            AgreesWithPrimary = true,
            Warnings = warnings
        };

    private static SkIdeaDto? SelectKeyIdea(string finalBias, SkIdeaDto? bull, SkIdeaDto? bear)
    {
        if (finalBias == "Bullish" && bull is not null)
        {
            return bull;
        }

        if (finalBias == "Bearish" && bear is not null)
        {
            return bear;
        }

        if (bull is null)
        {
            return bear;
        }

        if (bear is null)
        {
            return bull;
        }

        return bull.ClarityScore >= bear.ClarityScore ? bull : bear;
    }

    private static string BuildDangerLevelToWatch(SkIdeaDto? idea)
    {
        if (idea is null)
        {
            return "No danger level — no confirmed setup detected.";
        }

        return idea.Direction == "Bullish"
            ? $"Below {idea.DangerLevelText}, the upward idea is no longer valid."
            : $"Above {idea.DangerLevelText}, the downward idea is no longer valid.";
    }

    private static (IReadOnlyList<string> reasons, IReadOnlyList<string> warnings) BuildClarity(
        IReadOnlyList<SkSequenceCandidateDto> candidates,
        SkMultiTimeframeContextDto context,
        string finalBias,
        string confidenceLabel,
        string primaryTimeframe,
        string higherTimeframe,
        int priceDecimals)
    {
        var reasons = new List<string>();
        var warnings = new List<string>();

        if (candidates.Count == 0)
        {
            reasons.Add("Low clarity: no clear setup could be built from the current candles.");
            return (reasons, warnings);
        }

        reasons.Add(confidenceLabel switch
        {
            "High" => "High clarity: price is reacting from a defined zone and the danger level is clearly set.",
            "Medium" => "Medium clarity: there is a possible setup, but a strong reaction has not formed yet.",
            _ => "Low clarity: the setup is weak or the two timeframes disagree."
        });

        var top = candidates.OrderByDescending(c => c.ConfidenceScore).First();

        reasons.Add(top.CurrentPricePosition switch
        {
            "InsideCorrectionZone" => "Price is inside the reaction zone, which is where this setup becomes interesting.",
            "BeforeCorrectionZone" => "Price has not reached the reaction zone yet, so the setup still needs time.",
            "NearTarget" => "Price is already close to the target area, so most of the move may be done.",
            "LeftCorrectionZone" => "Price has moved away from the reaction zone, which weakens the setup.",
            _ => "Price is near the calculated levels for this setup."
        });

        if (context.ConflictWarning is not null)
        {
            warnings.Add($"The {primaryTimeframe} chart and the {higherTimeframe} chart do not agree.");
        }
        else if (finalBias == context.HigherTimeframeBias && finalBias is "Bullish" or "Bearish")
        {
            reasons.Add($"The {higherTimeframe} chart agrees with the {primaryTimeframe} direction.");
        }

        reasons.Add($"The danger level is clearly defined at {SkPriceFormatter.Format(top.InvalidationLevel, priceDecimals)}.");

        foreach (var warning in top.Warnings)
        {
            if (!warnings.Contains(warning))
            {
                warnings.Add(warning);
            }
        }

        return (reasons, warnings);
    }

    public async Task<ServiceResult<IReadOnlyList<TradingSystemAnalysisSummaryDto>>> GetRecentAnalysesAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var analyses = await _analysisRepository.GetRecentAsync(
            SkSystemConstants.SystemCode, limit <= 0 ? 50 : limit, cancellationToken);

        var items = analyses
            .Select(analysis =>
            {
                var stored = SkSystemJson.Deserialize<StoredDiagnostics>(analysis.RawDiagnosticsJson);
                var audit = stored?.ConceptAudit;
                var exportStatus = stored?.LastExportStatus;
                return new TradingSystemAnalysisSummaryDto
                {
                    Id = analysis.Id,
                    SystemCode = analysis.SystemCode,
                    Symbol = analysis.Symbol,
                    PrimaryTimeframe = analysis.PrimaryTimeframe,
                    HigherTimeframe = analysis.HigherTimeframe,
                    MarketBias = analysis.MarketBias,
                    ConfidenceLabel = audit?.ClarityLabel ?? analysis.ConfidenceLabel,
                    Status = analysis.Status,
                    Conclusion = ExtractConclusion(analysis.RawDiagnosticsJson),
                    ClarityLabel = audit?.ClarityLabel ?? analysis.ConfidenceLabel,
                    UsefulnessStatus = audit?.UsefulnessStatus ?? "Fresh",
                    SequenceStatus = audit?.SequenceStatus ?? "Building",
                    ValidityStatus = audit?.ValidityStatus ?? "Valid",
                    ChartExportStatus = exportStatus?.ChartIncluded == true
                        ? "Chart included"
                        : exportStatus?.ChartUnavailableReason ?? "Not exported",
                    AnalysisTimeUtc = analysis.AnalysisTimeUtc,
                    LatestCandleTimeUtc = analysis.LatestCandleTimeUtc,
                    CreatedAtUtc = analysis.CreatedAtUtc
                };
            })
            .ToList();

        return ServiceResult<IReadOnlyList<TradingSystemAnalysisSummaryDto>>.Ok(items);
    }

    public async Task<ServiceResult<SkSystemAnalysisResultDto>> GetAnalysisAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _analysisRepository.GetByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return ServiceResult<SkSystemAnalysisResultDto>.Fail("Analysis was not found.", "id");
        }

        return ServiceResult<SkSystemAnalysisResultDto>.Ok(MapToResult(entity));
    }

    public async Task<ServiceResult<bool>> DeleteAnalysisAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var deleted = await _analysisRepository.DeleteAsync(id, cancellationToken);
        if (!deleted)
        {
            return ServiceResult<bool>.Fail("Analysis was not found.", "id");
        }

        await _analysisRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "TRADING_SYSTEM_ANALYSIS_DELETED",
            nameof(TradingSystemAnalysis),
            entityId: id,
            userId: _currentUserService.UserId,
            cancellationToken: cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<IReadOnlyList<MarketDataImportDto>>> ImportRequiredDataAsync(
        SkImportRequiredDataRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return ServiceResult<IReadOnlyList<MarketDataImportDto>>.Fail("Request is required.");
        }

        if (!SkSystemConstants.IsSupportedPrimaryTimeframe(request.PrimaryTimeframe) ||
            !TimeframeParser.TryParse(request.PrimaryTimeframe, out var primaryTimeframe))
        {
            return ServiceResult<IReadOnlyList<MarketDataImportDto>>.Fail(
                $"Primary timeframe '{request.PrimaryTimeframe}' is not supported for analysis.",
                "primaryTimeframe");
        }

        if (!SkSystemConstants.IsSupportedHigherTimeframe(request.HigherTimeframe) ||
            !TimeframeParser.TryParse(request.HigherTimeframe, out var higherTimeframe))
        {
            return ServiceResult<IReadOnlyList<MarketDataImportDto>>.Fail(
                $"Higher timeframe '{request.HigherTimeframe}' is not supported for analysis.",
                "higherTimeframe");
        }

        var lookback = Math.Clamp(
            request.LookbackCandles <= 0 ? SkSystemConstants.DefaultLookbackCandles : request.LookbackCandles,
            SkSystemConstants.MinLookbackCandles,
            SkSystemConstants.MaxLookbackCandles);

        var imports = new List<MarketDataImportDto>();

        var timeframes = new[]
        {
            (Value: request.PrimaryTimeframe, Timeframe: primaryTimeframe),
            (Value: request.HigherTimeframe, Timeframe: higherTimeframe)
        };

        foreach (var (value, timeframe) in timeframes.DistinctBy(item => item.Timeframe))
        {
            var toUtc = DateTime.UtcNow;
            var minutesBack = (long)(int)timeframe * (lookback + 20);
            // Stay under the Binance historical import window (typically 30 days per import).
            var maxMinutes = 25L * 24 * 60;
            var fromUtc = toUtc.AddMinutes(-Math.Min(minutesBack, maxMinutes));

            var import = await _marketDataService.ImportCandlesAsync(new ImportCandlesRequest
            {
                ExchangeId = request.ExchangeId,
                SymbolId = request.SymbolId,
                Timeframe = value,
                FromUtc = fromUtc,
                ToUtc = toUtc
            }, cancellationToken);

            if (!import.Succeeded || import.Data is null)
            {
                return ServiceResult<IReadOnlyList<MarketDataImportDto>>.Fail(
                    import.ErrorMessage ?? "Failed to start required data import.",
                    import.ErrorField);
            }

            imports.Add(import.Data);
        }

        return ServiceResult<IReadOnlyList<MarketDataImportDto>>.Ok(imports);
    }

    private static string ResolveBias(IReadOnlyList<SkSequenceCandidateDto> candidates)
    {
        if (candidates.Count == 0)
        {
            return "Unknown";
        }

        var bull = candidates.Where(c => c.Direction == "Bullish").Select(c => c.ConfidenceScore).DefaultIfEmpty(0m).Max();
        var bear = candidates.Where(c => c.Direction == "Bearish").Select(c => c.ConfidenceScore).DefaultIfEmpty(0m).Max();

        if (bull > 0 && bear <= 0)
        {
            return "Bullish";
        }

        if (bear > 0 && bull <= 0)
        {
            return "Bearish";
        }

        if (Math.Abs(bull - bear) < 10m)
        {
            return "Mixed";
        }

        return bull > bear ? "Bullish" : "Bearish";
    }

    private static string ResolveFinalBias(string primaryBias, string higherBias)
    {
        if (primaryBias == "Unknown")
        {
            return "Unknown";
        }

        if ((primaryBias == "Bullish" && higherBias == "Bearish") ||
            (primaryBias == "Bearish" && higherBias == "Bullish"))
        {
            return "Mixed";
        }

        return primaryBias;
    }

    private static string ResolveConfidenceLabel(IReadOnlyList<SkSequenceCandidateDto> candidates, bool hasConflict)
    {
        if (candidates.Count == 0)
        {
            return "Low";
        }

        var top = candidates.Max(candidate => candidate.ConfidenceScore);
        var label = top >= 70m ? "High" : top >= 50m ? "Medium" : "Low";

        if (hasConflict)
        {
            label = label switch
            {
                "High" => "Medium",
                "Medium" => "Low",
                _ => "Low"
            };
        }

        return label;
    }

    private static IReadOnlyList<ChartOverlayDto> BuildChartOverlays(
        IReadOnlyList<SwingPointDto> swings,
        SkSequenceAnalysisResult sequence,
        SkMultiTimeframeContextDto higherContext,
        decimal currentPrice,
        DateTime latestTimeUtc,
        int priceDecimals,
        string? bestBullId,
        string? bestBearId,
        string symbol)
    {
        var overlays = new List<ChartOverlayDto>();
        string Fmt(decimal value) => SkPriceFormatter.Format(value, priceDecimals);

        // Rank the "other" structures (best setups are rank 0).
        var rankMap = new Dictionary<string, int>(StringComparer.Ordinal);
        var otherRank = 0;
        foreach (var candidate in sequence.Candidates)
        {
            if (candidate.Id == bestBullId || candidate.Id == bestBearId)
            {
                rankMap[candidate.Id] = 0;
            }
            else
            {
                rankMap[candidate.Id] = ++otherRank;
            }
        }

        // Swing points (advanced, hidden by default).
        foreach (var swing in swings)
        {
            var high = swing.Type == "High";
            overlays.Add(new ChartOverlayDto
            {
                Type = "Marker",
                Category = "SwingPoint",
                Label = high ? "Swing high" : "Swing low",
                ShortLabel = high ? "High" : "Low",
                Color = high ? "#ef4444" : "#22c55e",
                Price = swing.Price,
                TimeUtc = swing.TimeUtc,
                Direction = high ? "Bearish" : "Bullish",
                GroupName = "Swing points",
                LevelType = high ? "SwingHigh" : "SwingLow",
                DisplayName = high ? "Swing high" : "Swing low",
                PlainLanguageMeaning = high
                    ? "A recent swing high — a local peak in price."
                    : "A recent swing low — a local dip in price.",
                TooltipTitle = high ? "Swing high" : "Swing low",
                TooltipBody = high
                    ? "A recent local peak the analyzer used to build structure."
                    : "A recent local dip the analyzer used to build structure.",
                Importance = "Low",
                IsAdvanced = true,
                IsPrimary = false,
                VisibleByDefault = false
            });
        }

        foreach (var candidate in sequence.Candidates)
        {
            var isBull = candidate.Direction == "Bullish";
            var isBestBull = candidate.Id == bestBullId;
            var isBestBear = candidate.Id == bestBearId;
            var isBest = isBestBull || isBestBear;
            var isInvalid = SkConceptScoring.IsHiddenFromBeginner(candidate);
            var dir = candidate.Direction;
            var word = isBull ? "Upward" : "Downward";
            var rank = rankMap.TryGetValue(candidate.Id, out var value) ? value : 0;
            var group = isInvalid
                ? "Invalid structures (diagnostics)"
                : isBestBull
                    ? "Best upward idea"
                    : isBestBear
                        ? "Best downward idea"
                        : "Other possible structures";
            var visibilityTier = isInvalid ? "Advanced" : isBest ? "Beginner" : "Intermediate";
            var showByDefault = isBest && !isInvalid;

            string DangerMeaning() => isBull
                ? $"If {symbol} falls below this price, the upward idea is no longer valid."
                : $"If {symbol} rises above this price, the downward idea is no longer valid.";
            string ReactionMeaning() => isBull
                ? "Area where price may pull back and possibly react upward."
                : "Area where price may bounce and possibly react downward.";
            string TargetMeaning() => isBull
                ? "An area price may move toward above if the upward idea works."
                : "An area price may move toward below if the downward idea works.";

            // Only label the major points of the best setups to reduce clutter.
            if (isBest)
            {
                foreach (var point in new[] { candidate.PointZ, candidate.PointA, candidate.PointB, candidate.PointC })
                {
                    if (point is null)
                    {
                        continue;
                    }

                    var levelType = point.Label switch
                    {
                        "Z" => "SequenceStart",
                        "A" => "FirstStrongMove",
                        "B" => "PullbackPoint",
                        "C" => "TargetArea",
                        _ => "SetupPoint"
                    };

                    overlays.Add(new ChartOverlayDto
                    {
                        Type = "Label",
                        Category = "SetupPoint",
                        Label = point.Label switch
                        {
                            "Z" => "Starting point",
                            "A" => "First strong move",
                            "B" => "Pullback point",
                            "C" => "Target area",
                            _ => point.Label
                        },
                        ShortLabel = point.Label switch
                        {
                            "Z" => "Start",
                            "A" => "Move",
                            "B" => "Pullback",
                            "C" => "Target",
                            _ => point.Label
                        },
                        Color = isBull ? "#38bdf8" : "#f472b6",
                        Price = point.Price,
                        TimeUtc = point.TimeUtc,
                        Direction = dir,
                        SequenceId = candidate.Id,
                        IsBestBullish = isBestBull,
                        IsBestBearish = isBestBear,
                        GroupName = group,
                        SetupId = candidate.Id,
                        SetupRank = rank,
                        SetupDirection = dir,
                        LevelType = levelType,
                        DisplayName = point.Label switch
                        {
                            "Z" => "Sequence start",
                            "A" => "First strong move",
                            "B" => "Pullback point",
                            "C" => "Target area",
                            _ => point.Label
                        },
                        PlainLanguageMeaning = point.Label switch
                        {
                            "Z" => "Where the analyzed move started.",
                            "A" => "Where the first strong move ended.",
                            "B" => "Where price pulled back during the move.",
                            "C" => "The projected target area of the move.",
                            _ => "A point used to build the setup."
                        },
                        TooltipTitle = point.Label switch
                        {
                            "Z" => "Sequence start",
                            "B" => "Pullback point",
                            _ => "Setup point"
                        },
                        TooltipBody = "A structure point the analyzer used to build this idea.",
                        Importance = "Medium",
                        IsAdvanced = visibilityTier == "Advanced",
                        IsPrimary = isBest && !isInvalid,
                        VisibleByDefault = showByDefault,
                        LayerKey = $"setup-{candidate.Id}-{levelType}",
                        VisibilityTier = visibilityTier,
                        BeginnerLabel = point.Label switch
                        {
                            "Z" => "Sequence start",
                            "A" => "Impulse end",
                            "B" => "Correction point",
                            _ => "Structure point"
                        },
                        AdvancedLabel = $"Point {point.Label}",
                        Explanation = "Structure point selected from detected swings."
                    });
                }
            }

            overlays.Add(new ChartOverlayDto
            {
                Type = "Zone",
                Category = "ReactionZone",
                Label = $"{word} reaction zone",
                ShortLabel = isBull ? "Upward zone" : "Downward zone",
                Color = "rgba(245,158,11,0.18)",
                PriceLow = candidate.CorrectionZoneMin,
                PriceHigh = candidate.CorrectionZoneMax,
                TimeUtc = candidate.ImpulseStartTimeUtc,
                EndTimeUtc = latestTimeUtc,
                Direction = dir,
                SequenceId = candidate.Id,
                IsBestBullish = isBestBull,
                IsBestBearish = isBestBear,
                GroupName = group,
                SetupId = candidate.Id,
                SetupRank = rank,
                SetupDirection = dir,
                LevelType = "ReactionZone",
                DisplayName = $"{word} reaction zone",
                PlainLanguageMeaning = ReactionMeaning(),
                TooltipTitle = $"{word} reaction zone",
                TooltipBody = ReactionMeaning(),
                Importance = isBest && !isInvalid ? "High" : "Low",
                IsAdvanced = !showByDefault,
                IsPrimary = isBest && !isInvalid,
                VisibleByDefault = showByDefault,
                LayerKey = $"{candidate.Id}-reaction-zone",
                VisibilityTier = visibilityTier,
                BeginnerLabel = $"{word} reaction zone",
                AdvancedLabel = $"{word} correction zone",
                Explanation = ReactionMeaning()
            });

            overlays.Add(new ChartOverlayDto
            {
                Type = "Zone",
                Category = "StrongReactionZone",
                Label = $"{word} strong reaction zone",
                ShortLabel = isBull ? "Upward strong zone" : "Downward strong zone",
                Color = "rgba(168,85,247,0.25)",
                PriceLow = candidate.GoldenPocketMin,
                PriceHigh = candidate.GoldenPocketMax,
                TimeUtc = candidate.ImpulseStartTimeUtc,
                EndTimeUtc = latestTimeUtc,
                Direction = dir,
                SequenceId = candidate.Id,
                IsBestBullish = isBestBull,
                IsBestBearish = isBestBear,
                GroupName = group,
                SetupId = candidate.Id,
                SetupRank = rank,
                SetupDirection = dir,
                LevelType = "StrongReactionZone",
                DisplayName = $"{word} strong reaction zone",
                PlainLanguageMeaning = "A tighter area within the reaction zone where a turn is more likely.",
                TooltipTitle = $"{word} strong reaction zone",
                TooltipBody = "A tighter, higher-probability area inside the reaction zone.",
                Importance = isBest && !isInvalid ? "Medium" : "Low",
                IsAdvanced = !showByDefault,
                IsPrimary = isBest && !isInvalid,
                VisibleByDefault = showByDefault,
                LayerKey = $"{candidate.Id}-strong-zone",
                VisibilityTier = visibilityTier,
                BeginnerLabel = $"{word} strong reaction zone",
                AdvancedLabel = $"{word} golden pocket",
                Explanation = "A tighter area within the reaction zone where a turn is more likely."
            });

            overlays.Add(new ChartOverlayDto
            {
                Type = "HorizontalLine",
                Category = "Danger",
                Label = $"{word} danger level",
                ShortLabel = isBull ? "Upward danger" : "Downward danger",
                Color = "#ef4444",
                Price = candidate.InvalidationLevel,
                Direction = dir,
                SequenceId = candidate.Id,
                IsBestBullish = isBestBull,
                IsBestBearish = isBestBear,
                GroupName = group,
                SetupId = candidate.Id,
                SetupRank = rank,
                SetupDirection = dir,
                LevelType = "DangerLevel",
                DisplayName = $"{word} danger level",
                PlainLanguageMeaning = DangerMeaning(),
                TooltipTitle = $"{word} danger level",
                TooltipBody = DangerMeaning(),
                Importance = isBest && !isInvalid ? "High" : "Low",
                IsAdvanced = !showByDefault,
                IsPrimary = isBest && !isInvalid,
                VisibleByDefault = showByDefault,
                LayerKey = $"{candidate.Id}-danger",
                VisibilityTier = visibilityTier,
                BeginnerLabel = $"{word} danger level",
                AdvancedLabel = $"{word} invalidation",
                Explanation = DangerMeaning()
            });

            overlays.Add(new ChartOverlayDto
            {
                Type = "HorizontalLine",
                Category = "Target",
                Label = $"{word} target 1",
                ShortLabel = isBull ? "Upward target 1" : "Downward target 1",
                Color = "#22c55e",
                Price = candidate.Target1,
                Direction = dir,
                SequenceId = candidate.Id,
                IsBestBullish = isBestBull,
                IsBestBearish = isBestBear,
                GroupName = group,
                SetupId = candidate.Id,
                SetupRank = rank,
                SetupDirection = dir,
                LevelType = "Target1",
                DisplayName = $"{word} target 1",
                PlainLanguageMeaning = TargetMeaning(),
                TooltipTitle = $"{word} target 1",
                TooltipBody = TargetMeaning(),
                Importance = isBest && !isInvalid ? "High" : "Low",
                IsAdvanced = !showByDefault,
                IsPrimary = isBest && !isInvalid,
                VisibleByDefault = showByDefault,
                LayerKey = $"{candidate.Id}-target1",
                VisibilityTier = visibilityTier,
                BeginnerLabel = $"{word} target 1",
                AdvancedLabel = $"{word} extension target 1",
                Explanation = TargetMeaning()
            });

            overlays.Add(new ChartOverlayDto
            {
                Type = "HorizontalLine",
                Category = "Target",
                Label = $"{word} target 2",
                ShortLabel = isBull ? "Upward target 2" : "Downward target 2",
                Color = "#16a34a",
                Price = candidate.Target2,
                Direction = dir,
                SequenceId = candidate.Id,
                IsBestBullish = isBestBull,
                IsBestBearish = isBestBear,
                GroupName = group,
                SetupId = candidate.Id,
                SetupRank = rank,
                SetupDirection = dir,
                LevelType = "Target2",
                DisplayName = $"{word} target 2",
                PlainLanguageMeaning = TargetMeaning(),
                TooltipTitle = $"{word} target 2",
                TooltipBody = TargetMeaning(),
                Importance = isBest && !isInvalid ? "High" : "Low",
                IsAdvanced = !showByDefault,
                IsPrimary = isBest && !isInvalid,
                VisibleByDefault = showByDefault,
                LayerKey = $"{candidate.Id}-target2",
                VisibilityTier = visibilityTier,
                BeginnerLabel = $"{word} target 2",
                AdvancedLabel = $"{word} extension target 2",
                Explanation = TargetMeaning()
            });

            if (!isInvalid)
            {
                overlays.Add(new ChartOverlayDto
                {
                    Type = "ScenarioArrow",
                    Category = "Scenario",
                    Label = isBull ? "Possible upward move" : "Possible downward move",
                    ShortLabel = isBull ? "Up scenario" : "Down scenario",
                    Color = isBull ? "#22c55e" : "#ef4444",
                    Price = candidate.Target1,
                    TimeUtc = latestTimeUtc,
                    Direction = dir,
                    SequenceId = candidate.Id,
                    IsBestBullish = isBestBull,
                    IsBestBearish = isBestBear,
                    GroupName = group,
                    SetupId = candidate.Id,
                    SetupRank = rank,
                    SetupDirection = dir,
                    LevelType = "ScenarioArrow",
                    DisplayName = isBull ? "Possible upward move" : "Possible downward move",
                    PlainLanguageMeaning = isBull
                        ? "The direction price may move if the upward idea works."
                        : "The direction price may move if the downward idea works.",
                    TooltipTitle = isBull ? "Upward scenario" : "Downward scenario",
                    TooltipBody = "The possible direction of the idea. Not a trade signal.",
                    Importance = isBest ? "Medium" : "Low",
                    IsAdvanced = !showByDefault,
                    IsPrimary = isBest,
                    VisibleByDefault = showByDefault,
                    LayerKey = $"{candidate.Id}-scenario",
                    VisibilityTier = visibilityTier,
                    BeginnerLabel = isBull ? "Possible upward idea" : "Possible downward idea",
                    AdvancedLabel = "Scenario direction",
                    Explanation = "Possible scenario direction. Not a trade signal."
                });
            }

            foreach (var zone in sequence.FibonacciZones.Where(z => z.SequenceId == candidate.Id))
            {
                var extension = zone.Kind == "Extension";
                overlays.Add(new ChartOverlayDto
                {
                    Type = extension ? "FibonacciExtension" : "FibonacciRetracement",
                    Category = "Fibonacci",
                    Label = zone.Label,
                    ShortLabel = (extension ? "Ext " : "Fib ") + FormatRatio(zone.Ratio),
                    Color = zone.IsGoldenPocket ? "#a855f7" : "#64748b",
                    Price = zone.Price,
                    Direction = dir,
                    SequenceId = candidate.Id,
                    Ratio = zone.Ratio,
                    IsBestBullish = isBestBull,
                    IsBestBearish = isBestBear,
                    GroupName = "Fibonacci detail levels",
                    SetupId = candidate.Id,
                    SetupRank = rank,
                    SetupDirection = dir,
                    LevelType = extension ? "FibonacciExtension" : "FibonacciRetracement",
                    DisplayName = (extension ? "Fibonacci extension " : "Fibonacci retracement ") + FormatRatio(zone.Ratio),
                    PlainLanguageMeaning = "A calculated Fibonacci level used by the analyzer. It is not a trade signal.",
                    TooltipTitle = (extension ? "Ext " : "Fib ") + FormatRatio(zone.Ratio),
                    TooltipBody = "Fibonacci levels are calculated levels used by the analyzer. They are not trade signals.",
                    Importance = "Low",
                    IsAdvanced = true,
                    IsPrimary = false,
                    VisibleByDefault = false
                });
            }
        }

        // Higher timeframe levels (advanced, hidden by default).
        foreach (var level in higherContext.ImportantHigherTimeframeLevels)
        {
            overlays.Add(new ChartOverlayDto
            {
                Type = "HorizontalLine",
                Category = "HigherTimeframe",
                Label = level.Label,
                ShortLabel = "HTF level",
                Color = "#0ea5e9",
                Price = level.Price,
                GroupName = "Higher timeframe context",
                LevelType = "HigherTimeframeLevel",
                DisplayName = string.IsNullOrWhiteSpace(level.Label) ? "Higher timeframe level" : level.Label,
                PlainLanguageMeaning = "An important level taken from the higher timeframe chart.",
                TooltipTitle = "Higher timeframe level",
                TooltipBody = "An important level from the higher timeframe used for context.",
                Importance = "Low",
                IsAdvanced = true,
                IsPrimary = false,
                VisibleByDefault = false
            });
        }

        overlays.Add(new ChartOverlayDto
        {
            Type = "HorizontalLine",
            Category = "Current",
            Label = "Current price",
            ShortLabel = "Current price",
            Color = "#facc15",
            Price = currentPrice,
            TimeUtc = latestTimeUtc,
            GroupName = "Current price",
            LevelType = "CurrentPrice",
            DisplayName = "Current price",
            PlainLanguageMeaning = "The latest market price.",
            TooltipTitle = "Current price",
            TooltipBody = "The latest market price.",
            Importance = "High",
            IsAdvanced = false,
            IsPrimary = true,
            VisibleByDefault = true
        });

        return overlays;
    }

    private static string FormatRatio(decimal ratio)
    {
        var text = ratio.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        // Pad single-decimal ratios (e.g. "0.5" -> "0.50", "1" -> "1.0") for readable labels.
        if (!text.Contains('.'))
        {
            return text + ".0";
        }

        var decimals = text.Length - text.IndexOf('.') - 1;
        return decimals == 1 && text.StartsWith("0.", StringComparison.Ordinal) ? text + "0" : text;
    }

    private TradingSystemAnalysis MapToEntity(SkSystemAnalysisResultDto result, DateTime createdAtUtc) => new()
    {
        SystemCode = result.SystemCode,
        SystemName = result.SystemName,
        ExchangeId = result.ExchangeId,
        ExchangeName = result.ExchangeName,
        SymbolId = result.SymbolId,
        Symbol = result.Symbol,
        PrimaryTimeframe = result.PrimaryTimeframe,
        HigherTimeframe = result.HigherTimeframe,
        LookbackCandles = result.LookbackCandles,
        SwingSensitivity = result.SwingSensitivity,
        DirectionMode = result.DirectionMode,
        Status = result.Status,
        AnalysisTimeUtc = result.AnalysisTimeUtc,
        LatestCandleTimeUtc = result.LatestCandleTimeUtc,
        CurrentPrice = result.CurrentPrice,
        MarketBias = result.MarketBias,
        ConfidenceLabel = result.ConfidenceLabel,
        SummaryText = result.Summary,
        BullishScenarioText = result.BullishScenario,
        BearishScenarioText = result.BearishScenario,
        InvalidationsText = string.Join(Environment.NewLine, result.InvalidationLevels),
        WarningsJson = SkSystemJson.Serialize(result.Warnings),
        ChartDataJson = SkSystemJson.Serialize(new StoredChartData
        {
            Candles = result.Candles,
            Overlays = result.ChartOverlays
        }),
        SequenceCandidatesJson = SkSystemJson.Serialize(result.SequenceCandidates),
        FibonacciZonesJson = SkSystemJson.Serialize(result.FibonacciZones),
        KeyLevelsJson = SkSystemJson.Serialize(result.KeyLevels),
        AiSummaryJson = SkSystemJson.Serialize(result.AiSummary),
        RawDiagnosticsJson = SkSystemJson.Serialize(new StoredDiagnostics
        {
            SwingPoints = result.SwingPoints,
            HigherTimeframeContext = result.HigherTimeframeContext,
            HtfContext = result.HtfContext,
            LtfContext = result.LtfContext,
            QuickViewMode = result.QuickViewMode,
            Diagnostics = result.Diagnostics,
            Sequences = result.Sequences,
            ConceptAudit = result.ConceptAudit,
            LastExportStatus = result.LastExportStatus,
            Narrative = new SkNarrativeDto
            {
                ExplanationMode = result.ExplanationMode,
                PriceDecimals = result.PriceDecimals,
                BottomLine = result.BottomLine,
                KeyAreaToWatch = result.KeyAreaToWatch,
                DangerLevelToWatch = result.DangerLevelToWatch,
                BestBullishIdea = result.BestBullishIdea,
                BestBearishIdea = result.BestBearishIdea,
                ClarityReasons = result.ClarityReasons,
                ClarityWarnings = result.ClarityWarnings
            }
        }),
        CreatedByUserId = _currentUserService.UserId,
        CreatedAtUtc = createdAtUtc
    };

    private static string ExtractConclusion(string rawDiagnosticsJson)
    {
        if (string.IsNullOrWhiteSpace(rawDiagnosticsJson))
        {
            return string.Empty;
        }

        var diagnostics = SkSystemJson.Deserialize<StoredDiagnostics>(rawDiagnosticsJson);
        return diagnostics?.Narrative?.BottomLine ?? string.Empty;
    }

    private static SkSystemAnalysisResultDto MapToResult(TradingSystemAnalysis entity)
    {
        var chartData = SkSystemJson.Deserialize<StoredChartData>(entity.ChartDataJson) ?? new StoredChartData();
        var diagnostics = SkSystemJson.Deserialize<StoredDiagnostics>(entity.RawDiagnosticsJson) ?? new StoredDiagnostics();
        var aiSummary = SkSystemJson.Deserialize<SkAiSummaryDto>(entity.AiSummaryJson);
        var narrative = diagnostics.Narrative ?? new SkNarrativeDto();

        return new SkSystemAnalysisResultDto
        {
            AnalysisId = entity.Id,
            SystemCode = entity.SystemCode,
            SystemName = entity.SystemName,
            ExchangeId = entity.ExchangeId,
            ExchangeName = entity.ExchangeName,
            SymbolId = entity.SymbolId,
            Symbol = entity.Symbol,
            PrimaryTimeframe = entity.PrimaryTimeframe,
            HigherTimeframe = entity.HigherTimeframe,
            LookbackCandles = entity.LookbackCandles,
            SwingSensitivity = entity.SwingSensitivity,
            DirectionMode = entity.DirectionMode,
            Status = entity.Status,
            AnalysisTimeUtc = entity.AnalysisTimeUtc,
            LatestCandleTimeUtc = entity.LatestCandleTimeUtc,
            CurrentPrice = entity.CurrentPrice,
            MarketBias = entity.MarketBias,
            ConfidenceLabel = entity.ConfidenceLabel,
            Summary = entity.SummaryText,
            BullishScenario = entity.BullishScenarioText,
            BearishScenario = entity.BearishScenarioText,
            InvalidationLevels = string.IsNullOrWhiteSpace(entity.InvalidationsText)
                ? []
                : entity.InvalidationsText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
            ExplanationMode = narrative.ExplanationMode,
            PriceDecimals = narrative.PriceDecimals,
            PlainLanguageSummary = aiSummary?.PlainLanguageSummary ?? string.Empty,
            BottomLine = narrative.BottomLine,
            WhatThisMeans = aiSummary?.WhatThisMeans ?? string.Empty,
            KeyAreaToWatch = narrative.KeyAreaToWatch,
            DangerLevelToWatch = narrative.DangerLevelToWatch,
            HigherTimeframeExplanation = aiSummary?.HigherTimeframeExplanation ?? string.Empty,
            ConflictExplanation = aiSummary?.ConflictExplanation ?? string.Empty,
            BestBullishIdea = narrative.BestBullishIdea,
            BestBearishIdea = narrative.BestBearishIdea,
            ClarityReasons = narrative.ClarityReasons,
            ClarityWarnings = narrative.ClarityWarnings,
            GlossaryTerms = SkGlossary.Terms,
            DisplayLabels = SkDisplayLabels.Map,
            SwingPoints = diagnostics.SwingPoints ?? [],
            SequenceCandidates = SkSystemJson.Deserialize<List<SkSequenceCandidateDto>>(entity.SequenceCandidatesJson) ?? [],
            FibonacciZones = SkSystemJson.Deserialize<List<SkFibonacciZoneDto>>(entity.FibonacciZonesJson) ?? [],
            KeyLevels = SkSystemJson.Deserialize<List<SkKeyLevelDto>>(entity.KeyLevelsJson) ?? [],
            ChartOverlays = chartData.Overlays ?? [],
            HigherTimeframeContext = diagnostics.HigherTimeframeContext,
            HtfContext = diagnostics.HtfContext,
            LtfContext = diagnostics.LtfContext,
            QuickViewMode = diagnostics.QuickViewMode ?? SkSystemConstants.DefaultQuickViewMode,
            AnalysisOnlyDisclaimer = SkSystemConstants.AnalysisOnlyDisclaimer,
            AiSummary = aiSummary,
            Sequences = diagnostics.Sequences ?? [],
            ConceptAudit = diagnostics.ConceptAudit,
            LastExportStatus = diagnostics.LastExportStatus,
            Candles = chartData.Candles ?? [],
            Warnings = SkSystemJson.Deserialize<List<string>>(entity.WarningsJson) ?? [],
            Diagnostics = diagnostics.Diagnostics ?? new SkAnalysisDiagnosticsDto(),
            AnalysisOnly = true
        };
    }

    private sealed class StoredChartData
    {
        public IReadOnlyList<SkChartCandleDto>? Candles { get; init; }
        public IReadOnlyList<ChartOverlayDto>? Overlays { get; init; }
    }

    private sealed class StoredDiagnostics
    {
        public IReadOnlyList<SwingPointDto>? SwingPoints { get; init; }
        public SkMultiTimeframeContextDto? HigherTimeframeContext { get; init; }
        public SkTimeframeContextDto? HtfContext { get; init; }
        public SkTimeframeContextDto? LtfContext { get; init; }
        public string? QuickViewMode { get; init; }
        public SkAnalysisDiagnosticsDto? Diagnostics { get; init; }
        public SkNarrativeDto? Narrative { get; init; }
        public IReadOnlyList<SkSequenceDto>? Sequences { get; init; }
        public SkConceptAuditDto? ConceptAudit { get; init; }
        public SkExportStatusDto? LastExportStatus { get; init; }
    }
}
