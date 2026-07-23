using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Optimization.Dtos;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Optimization;
using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend;
using MomoQuant.Application.Validation;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Optimization;

namespace MomoQuant.Application.Optimization;

public sealed class StrategyValidationService : IStrategyValidationService
{
    private readonly IValidationDateSplitService _splitService;
    private readonly IStrategyValidationEvaluator _validationEvaluator;
    private readonly IStrategyResearchBacktestExecutor _researchExecutor;
    private readonly IStrategyResearchCandleCoverageService _candleCoverageService;
    private readonly IStrategyDataRequirementService _requirementService;
    private readonly IStrategyRepository _strategyRepository;
    private readonly IStrategyParameterSetService _parameterSetService;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IExchangeRepository _exchangeRepository;

    public StrategyValidationService(
        IValidationDateSplitService splitService,
        IStrategyValidationEvaluator validationEvaluator,
        IStrategyResearchBacktestExecutor researchExecutor,
        IStrategyResearchCandleCoverageService candleCoverageService,
        IStrategyDataRequirementService requirementService,
        IStrategyRepository strategyRepository,
        IStrategyParameterSetService parameterSetService,
        ISymbolRepository symbolRepository,
        IExchangeRepository exchangeRepository)
    {
        _splitService = splitService;
        _validationEvaluator = validationEvaluator;
        _researchExecutor = researchExecutor;
        _candleCoverageService = candleCoverageService;
        _requirementService = requirementService;
        _strategyRepository = strategyRepository;
        _parameterSetService = parameterSetService;
        _symbolRepository = symbolRepository;
        _exchangeRepository = exchangeRepository;
    }

    public async Task<ServiceResult<StrategyValidationResultDto>> RunAsync(
        RunStrategyValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        var symbol = await _symbolRepository.GetByIdAsync(request.SymbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<StrategyValidationResultDto>.Fail("Symbol was not found.");
        }

        var exchange = await _exchangeRepository.GetByIdAsync(request.ExchangeId, cancellationToken);
        IReadOnlyDictionary<string, string>? parameters = request.Parameters;
        long? parameterSetId = request.ParameterSetId;
        string? parameterSetName = null;

        if (request.ParameterSetId is long psId)
        {
            parameters = await _parameterSetService.GetFrozenParametersAsync(psId, cancellationToken);
            if (parameters is null)
            {
                return ServiceResult<StrategyValidationResultDto>.Fail("Parameter set was not found.");
            }
        }

        parameters ??= new Dictionary<string, string>();
        var executionOptions = BuildExecutionOptions(request);
        var resolvedParameters = VgResearchProfilePresets.AppliesToStrategy(request.StrategyCode)
            ? VgResearchProfilePresets.Apply(request.VgResearchProfile, parameters)
            : parameters;

        var requiredDataTimeframes = await ResolveRequiredDataTimeframesAsync(request.StrategyCode, cancellationToken);
        var coverageResult = await _candleCoverageService.EnsureCoverageAsync(
            request.ExchangeId,
            request.SymbolId,
            request.StrategyCode,
            request.Timeframe,
            request.FromUtc,
            request.ToUtc,
            request.AutoImportCandles,
            cancellationToken);

        if (!coverageResult.Succeeded)
        {
            return ServiceResult<StrategyValidationResultDto>.Fail(coverageResult.ErrorMessage ?? "Candle coverage check failed.");
        }

        if (request.ValidationMode == ValidationMode.None)
        {
            var singleResult = await _researchExecutor.RunWindowAsync(
                request.ExchangeId, request.SymbolId, request.Timeframe,
                request.FromUtc, request.ToUtc, request.StrategyCode, resolvedParameters,
                request.RiskProfileId, request.InitialBalance, executionOptions, cancellationToken);

            if (singleResult is null)
            {
                return ServiceResult<StrategyValidationResultDto>.Fail("Validation backtest could not be executed.");
            }

            var singleRange = new DateRangeDto { FromUtc = request.FromUtc, ToUtc = request.ToUtc };
            return ServiceResult<StrategyValidationResultDto>.Ok(BuildResult(
                request, symbol.SymbolName, exchange?.Name ?? "Unknown", singleRange, singleRange, singleRange,
                null, resolvedParameters, requiredDataTimeframes, coverageResult.Data ?? [],
                singleResult, singleResult, parameterSetId, parameterSetName,
                ValidationStatus.Passed, [], [], 100m));
        }

        var split = _splitService.Split(request.FromUtc, request.ToUtc);
        var trainingResult = await _researchExecutor.RunWindowAsync(
            request.ExchangeId, request.SymbolId, request.Timeframe,
            split.TrainingRange.FromUtc, split.TrainingRange.ToUtc,
            request.StrategyCode, resolvedParameters, request.RiskProfileId, request.InitialBalance,
            executionOptions, cancellationToken);

        var validationResult = await _researchExecutor.RunWindowAsync(
            request.ExchangeId, request.SymbolId, request.Timeframe,
            split.ValidationRange.FromUtc, split.ValidationRange.ToUtc,
            request.StrategyCode, resolvedParameters, request.RiskProfileId, request.InitialBalance,
            executionOptions, cancellationToken);

        if (trainingResult is null || validationResult is null)
        {
            return ServiceResult<StrategyValidationResultDto>.Fail("Validation backtest could not be executed for one or both windows.");
        }

        var (status, failReasons, warnings, robustness) = _validationEvaluator.Evaluate(
            trainingResult.Metrics, validationResult.Metrics, request.MaxDrawdownPercent);

        warnings = AppendEngineWarnings(warnings, trainingResult, validationResult, coverageResult.Data ?? []);

        if (validationResult.Metrics.TradeCount == 0 && validationResult.ZeroTradeAnalysis is not null)
        {
            warnings = warnings.Concat([validationResult.ZeroTradeAnalysis.Explanation]).Distinct().ToList();
        }

        if (VgResearchProfilePresets.IsExploratory(request.VgResearchProfile))
        {
            warnings = warnings.Concat(["Exploratory profile is research only — not final validation."]).Distinct().ToList();
        }

        var result = BuildResult(
            request, symbol.SymbolName, exchange?.Name ?? "Unknown",
            split.FullDateRange, split.TrainingRange, split.ValidationRange, split,
            resolvedParameters, requiredDataTimeframes, coverageResult.Data ?? [],
            trainingResult, validationResult, parameterSetId, parameterSetName,
            status, failReasons, warnings, robustness);

        ValidationRunLogger.Log(new ValidationRunLogEntry
        {
            StrategyCode = request.StrategyCode,
            Symbol = symbol.SymbolName,
            Timeframe = request.Timeframe,
            TrainingStartUtc = split.TrainingRange.FromUtc,
            TrainingEndUtc = split.TrainingRange.ToUtc,
            ValidationStartUtc = split.ValidationRange.FromUtc,
            ValidationEndUtc = split.ValidationRange.ToUtc,
            WarmupCandles = trainingResult.WarmupCandlesRequired,
            CandlesLoadedTraining = trainingResult.TotalCandlesLoaded,
            CandlesLoadedValidation = validationResult.TotalCandlesLoaded,
            EvaluationsTraining = trainingResult.StrategyEvaluations,
            EvaluationsValidation = validationResult.StrategyEvaluations,
            TradesTraining = trainingResult.Metrics.TradeCount,
            TradesValidation = validationResult.Metrics.TradeCount,
            CoverageStatus = coverageResult.Data?.All(c => c.CoverageStatus == "Complete") == true ? "Complete" : "Partial",
            ImportedCandles = coverageResult.Data?.Any(c => c.ImportedDuringRun) == true,
            EngineEvaluationBug = trainingResult.EngineEvaluationBug || validationResult.EngineEvaluationBug
        });

        return ServiceResult<StrategyValidationResultDto>.Ok(result);
    }

    private static List<string> AppendEngineWarnings(
        IReadOnlyList<string> warnings,
        StrategyResearchBacktestResult training,
        StrategyResearchBacktestResult validation,
        IReadOnlyList<CandleCoverageDto> coverage)
    {
        var merged = warnings.ToList();
        if (training.EngineEvaluationBug || validation.EngineEvaluationBug)
        {
            merged.Add("Candles were loaded but the strategy did not evaluate them. Check strategy execution pipeline.");
        }

        if (coverage.Any(c => c.ImportedDuringRun))
        {
            merged.Add("Missing candles were imported automatically before this run.");
        }

        return merged.Distinct().ToList();
    }

    private async Task<IReadOnlyList<string>> ResolveRequiredDataTimeframesAsync(
        string strategyCode,
        CancellationToken cancellationToken)
    {
        var strategy = await _strategyRepository.GetByCodeAsync(StrategyCodeExtensions.FromCode(strategyCode), cancellationToken);
        if (strategy is null)
        {
            return [];
        }

        var req = await _requirementService.GetByStrategyIdAsync(strategy.Id, cancellationToken);
        return req.Data?.RequiredDataTimeframes ?? [];
    }

    private static StrategyResearchExecutionOptions BuildExecutionOptions(RunStrategyValidationRequest request) => new()
    {
        ExecutionMode = request.ExecutionMode ?? "MarketFill",
        MakerFeeRate = request.MakerFeeRate ?? 0.0002m,
        TakerFeeRate = request.TakerFeeRate ?? 0.0005m,
        OrderExpiryCandles = request.OrderExpiryCandles ?? 3,
        UseAiScoring = request.UseAiScoring ?? false,
        MinConfidenceScore = request.MinConfidenceScore ?? 80m,
        SlippagePercent = request.SlippagePercent ?? 0m,
        AutoImportCandles = request.AutoImportCandles,
        VgResearchProfile = request.VgResearchProfile
    };

    private static StrategyValidationResultDto BuildResult(
        RunStrategyValidationRequest request,
        string symbolName,
        string exchangeName,
        DateRangeDto fullRange,
        DateRangeDto trainingRange,
        DateRangeDto validationRange,
        ValidationSplitDto? splitInfo,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<string> requiredDataTimeframes,
        IReadOnlyList<CandleCoverageDto> coverage,
        StrategyResearchBacktestResult training,
        StrategyResearchBacktestResult validation,
        long? parameterSetId,
        string? parameterSetName,
        ValidationStatus status,
        IReadOnlyList<string> failReasons,
        IReadOnlyList<string> warnings,
        decimal robustness) => new()
    {
        StrategyCode = request.StrategyCode,
        Symbol = symbolName,
        Exchange = exchangeName,
        Timeframe = request.Timeframe,
        FullDateRange = fullRange,
        TrainingRange = trainingRange,
        ValidationRange = validationRange,
        SplitInfo = splitInfo,
        ParameterSetId = parameterSetId,
        ParameterSetName = parameterSetName,
        Parameters = parameters,
        TrainingMetrics = training.Metrics,
        ValidationMetrics = validation.Metrics,
        RobustnessScore = robustness,
        ValidationStatus = status.ToString(),
        FailReasons = failReasons,
        Warnings = warnings,
        CreatedAtUtc = DateTime.UtcNow,
        BacktestEngineUsed = training.BacktestEngineUsed,
        StrategyParametersUsed = parameters,
        ResolvedExecutionTimeframe = request.Timeframe,
        RequiredDataTimeframes = requiredDataTimeframes,
        CandleCoverage = coverage,
        TrainingCandleCount = training.CandleCount,
        ValidationCandleCount = validation.CandleCount,
        TrainingWarmupCandlesLoaded = training.WarmupCandlesLoaded,
        ValidationWarmupCandlesLoaded = validation.WarmupCandlesLoaded,
        TrainingEvaluationCandles = training.CandleCount,
        ValidationEvaluationCandles = validation.CandleCount,
        TrainingEvaluations = training.StrategyEvaluations,
        ValidationEvaluations = validation.StrategyEvaluations,
        SkippedForWarmupCount = training.SkippedForWarmupCount + validation.SkippedForWarmupCount,
        EngineEvaluationBug = training.EngineEvaluationBug || validation.EngineEvaluationBug,
        ImportedDuringRun = coverage.Any(c => c.ImportedDuringRun),
        TrainingFunnel = training.Funnel,
        ValidationFunnel = validation.Funnel,
        DiagnosticsSummary = BuildDiagnosticsSummary(training, validation),
        WhyZeroTrades = validation.ZeroTradeAnalysis ?? training.ZeroTradeAnalysis,
        TrainingWhyZeroTrades = training.ZeroTradeAnalysis,
        ValidationWhyZeroTrades = validation.ZeroTradeAnalysis,
        VgResearchProfile = VgResearchProfilePresets.ProfileLabel(request.VgResearchProfile),
        IsExploratoryProfile = VgResearchProfilePresets.IsExploratory(request.VgResearchProfile)
    };

    private static string? BuildDiagnosticsSummary(
        StrategyResearchBacktestResult training,
        StrategyResearchBacktestResult validation)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(training.DiagnosticsSummary))
        {
            parts.Add($"Training: {training.DiagnosticsSummary}");
        }

        if (!string.IsNullOrWhiteSpace(validation.DiagnosticsSummary))
        {
            parts.Add($"Validation: {validation.DiagnosticsSummary}");
        }

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }
}

public sealed class ParameterOptimizationService : IParameterOptimizationService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
    private static readonly Dictionary<long, CancellationTokenSource> ActiveRuns = new();

    private readonly IParameterOptimizationRunRepository _runRepository;
    private readonly IStrategyParameterDefinitionProvider _definitionProvider;
    private readonly IValidationDateSplitService _splitService;
    private readonly IStrategyValidationEvaluator _validationEvaluator;
    private readonly IParameterOptimizationScorer _scorer;
    private readonly IStrategyResearchBacktestExecutor _researchExecutor;
    private readonly IStrategyParameterSetService _parameterSetService;
    private readonly ISymbolRepository _symbolRepository;

    public ParameterOptimizationService(
        IParameterOptimizationRunRepository runRepository,
        IStrategyParameterDefinitionProvider definitionProvider,
        IValidationDateSplitService splitService,
        IStrategyValidationEvaluator validationEvaluator,
        IParameterOptimizationScorer scorer,
        IStrategyResearchBacktestExecutor researchExecutor,
        IStrategyParameterSetService parameterSetService,
        ISymbolRepository symbolRepository)
    {
        _runRepository = runRepository;
        _definitionProvider = definitionProvider;
        _splitService = splitService;
        _validationEvaluator = validationEvaluator;
        _scorer = scorer;
        _researchExecutor = researchExecutor;
        _parameterSetService = parameterSetService;
        _symbolRepository = symbolRepository;
    }

    public async Task<ServiceResult<ParameterOptimizationResultDto>> RunAsync(
        RunParameterOptimizationRequest request,
        long? userId,
        CancellationToken cancellationToken = default)
    {
        if (request.OptimizationMode == ParameterOptimizationMode.ManualOnly)
        {
            return ServiceResult<ParameterOptimizationResultDto>.Fail("Optimization mode must be GridSearch or RandomSearch.");
        }

        var estimated = _definitionProvider.EstimateGridCombinations(request.StrategyCode, request.ParameterRangeOverrides);
        if (estimated > request.MaxCombinations && request.OptimizationMode == ParameterOptimizationMode.GridSearch)
        {
            return ServiceResult<ParameterOptimizationResultDto>.Fail(
                $"Grid search would produce {estimated} combinations, exceeding max {request.MaxCombinations}. Reduce ranges or use RandomSearch.",
                "maxCombinations");
        }

        var combinations = request.OptimizationMode == ParameterOptimizationMode.RandomSearch
            ? GenerateRandomCombinations(request)
            : _definitionProvider.GenerateGridCombinations(request.StrategyCode, request.MaxCombinations, request.ParameterRangeOverrides);

        if (combinations.Count == 0)
        {
            return ServiceResult<ParameterOptimizationResultDto>.Fail("No parameter combinations generated.");
        }

        var split = _splitService.Split(request.FromUtc, request.ToUtc);
        var executionOptions = BuildExecutionOptions(request);
        var run = new ParameterOptimizationRun
        {
            StrategyCode = request.StrategyCode,
            ExchangeId = request.ExchangeId,
            SymbolId = request.SymbolId,
            Timeframe = request.Timeframe,
            FromUtc = request.FromUtc,
            ToUtc = request.ToUtc,
            ValidationMode = request.ValidationMode,
            OptimizationMode = request.OptimizationMode,
            ObjectivePreset = request.ObjectivePreset,
            MaxCombinations = request.MaxCombinations,
            TotalCombinations = combinations.Count,
            Status = ParameterOptimizationRunStatus.Running,
            RequestedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            HeartbeatAtUtc = DateTime.UtcNow
        };

        await _runRepository.AddAsync(run, cancellationToken);
        await _runRepository.SaveChangesAsync(cancellationToken);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(request.MaxRuntimeMinutes));
        ActiveRuns[run.Id] = cts;

        var symbol = await _symbolRepository.GetByIdAsync(request.SymbolId, cancellationToken);
        var results = new List<ParameterSetResultDto>();
        var warnings = new List<string>();
        var deadline = DateTime.UtcNow.AddMinutes(request.MaxRuntimeMinutes);

        try
        {
            for (var i = 0; i < combinations.Count; i++)
            {
                if (cts.Token.IsCancellationRequested || DateTime.UtcNow >= deadline)
                {
                    warnings.Add("Optimization stopped due to timeout or cancellation.");
                    break;
                }

                var combo = MergeParameters(combinations[i], request.FixedParameters);
                var training = await _researchExecutor.RunWindowAsync(
                    request.ExchangeId, request.SymbolId, request.Timeframe,
                    split.TrainingRange.FromUtc, split.TrainingRange.ToUtc,
                    request.StrategyCode, combo, request.RiskProfileId, request.InitialBalance,
                    executionOptions, cts.Token);

                var validation = await _researchExecutor.RunWindowAsync(
                    request.ExchangeId, request.SymbolId, request.Timeframe,
                    split.ValidationRange.FromUtc, split.ValidationRange.ToUtc,
                    request.StrategyCode, combo, request.RiskProfileId, request.InitialBalance,
                    executionOptions, cts.Token);

                if (training is null || validation is null)
                {
                    continue;
                }

                var (status, failReasons, comboWarnings, robustness) = _validationEvaluator.Evaluate(
                    training.Metrics, validation.Metrics, request.MaxDrawdownPercent);
                var score = validation.Metrics.TradeCount == 0
                    ? 0m
                    : _scorer.Score(training.Metrics, validation.Metrics, request.ObjectivePreset, request.MinTradesTraining, request.MinTradesValidation);

                results.Add(new ParameterSetResultDto
                {
                    Rank = 0,
                    Parameters = combo,
                    TrainingMetrics = training.Metrics,
                    ValidationMetrics = validation.Metrics,
                    RobustnessScore = robustness,
                    OptimizationScore = score,
                    PassStatus = status.ToString(),
                    FailReasons = failReasons,
                    Warnings = comboWarnings
                });

                run.CompletedCombinations = i + 1;
                run.HeartbeatAtUtc = DateTime.UtcNow;
                await _runRepository.UpdateAsync(run, cancellationToken);
                await _runRepository.SaveChangesAsync(cancellationToken);
            }

            var tradeProducing = results.Where(r => (r.ValidationMetrics?.TradeCount ?? 0) > 0).ToList();
            var zeroTrade = results.Where(r => (r.ValidationMetrics?.TradeCount ?? 0) == 0).ToList();

            var ranked = tradeProducing
                .OrderByDescending(r => r.OptimizationScore)
                .ThenByDescending(r => r.ValidationMetrics?.NetPnlPercent ?? 0m)
                .Concat(zeroTrade.OrderByDescending(r => r.TrainingMetrics?.TradeCount ?? 0))
                .Select((r, idx) => new ParameterSetResultDto
                {
                    Rank = idx + 1,
                    Parameters = r.Parameters,
                    TrainingMetrics = r.TrainingMetrics,
                    ValidationMetrics = r.ValidationMetrics,
                    RobustnessScore = r.RobustnessScore,
                    OptimizationScore = r.OptimizationScore,
                    PassStatus = r.PassStatus,
                    FailReasons = r.FailReasons,
                    Warnings = r.Warnings
                })
                .ToList();

            var best = ranked.Where(r => (r.ValidationMetrics?.TradeCount ?? 0) > 0).Take(10).ToList();
            if (best.Count == 0)
            {
                best = ranked.Take(10).ToList();
                warnings.Add("No parameter sets produced validation trades. Review zero-trade diagnostics and try looser ranges.");
            }

            var rejected = ranked.Where(r => r.PassStatus == ValidationStatus.Failed.ToString()).Take(20).ToList();
            var bestNonZero = ranked.FirstOrDefault(r => (r.ValidationMetrics?.TradeCount ?? 0) > 0);

            if (request.SaveBestParameterSet && bestNonZero is not null)
            {
                await _parameterSetService.SaveAsync(new SaveStrategyParameterSetRequest
                {
                    Name = request.ParameterSetName ?? $"{request.StrategyCode} optimized {DateTime.UtcNow:yyyy-MM-dd}",
                    StrategyCode = request.StrategyCode,
                    SymbolId = request.SymbolId,
                    Timeframe = request.Timeframe,
                    Parameters = bestNonZero.Parameters,
                    OptimizationRunId = run.Id,
                    Approve = bestNonZero.PassStatus != ValidationStatus.Failed.ToString(),
                    TrainingMetrics = bestNonZero.TrainingMetrics,
                    ValidationMetrics = bestNonZero.ValidationMetrics,
                    RobustnessScore = bestNonZero.RobustnessScore,
                    TrainingRange = split.TrainingRange,
                    ValidationRange = split.ValidationRange,
                    ValidationStatus = bestNonZero.PassStatus,
                    ValidationTradeCount = bestNonZero.ValidationMetrics?.TradeCount
                }, cancellationToken);
            }

            var resultDto = new ParameterOptimizationResultDto
            {
                OptimizationRunId = run.Id,
                StrategyCode = request.StrategyCode,
                Symbol = symbol?.SymbolName ?? request.SymbolId.ToString(),
                Timeframe = request.Timeframe,
                DateRange = split,
                TotalCombinations = combinations.Count,
                CompletedCombinations = run.CompletedCombinations,
                Status = ParameterOptimizationRunStatus.Completed.ToString(),
                BestParameterSets = best,
                RejectedParameterSets = rejected,
                ZeroTradeParameterSets = zeroTrade.Take(20).ToList(),
                ZeroTradeParameterSetCount = zeroTrade.Count,
                TradeProducingParameterSetCount = tradeProducing.Count,
                BestNonZeroTradeParameterSet = bestNonZero,
                Warnings = warnings,
                CreatedAtUtc = run.CreatedAtUtc,
                CompletedAtUtc = DateTime.UtcNow
            };

            run.Status = ParameterOptimizationRunStatus.Completed;
            run.CompletedAtUtc = DateTime.UtcNow;
            run.ResultJson = JsonSerializer.Serialize(resultDto, JsonOptions);
            run.WarningsJson = JsonSerializer.Serialize(warnings, JsonOptions);
            await _runRepository.UpdateAsync(run, cancellationToken);
            await _runRepository.SaveChangesAsync(cancellationToken);

            return ServiceResult<ParameterOptimizationResultDto>.Ok(resultDto);
        }
        catch (OperationCanceledException)
        {
            run.Status = ParameterOptimizationRunStatus.Cancelled;
            run.CompletedAtUtc = DateTime.UtcNow;
            await _runRepository.UpdateAsync(run, cancellationToken);
            await _runRepository.SaveChangesAsync(cancellationToken);
            return ServiceResult<ParameterOptimizationResultDto>.Fail("Optimization was cancelled or timed out.");
        }
        finally
        {
            ActiveRuns.Remove(run.Id);
            cts.Dispose();
        }
    }

    public async Task<ServiceResult<ParameterOptimizationResultDto>> GetAsync(long runId, CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(runId, cancellationToken);
        if (run is null || string.IsNullOrWhiteSpace(run.ResultJson))
        {
            return ServiceResult<ParameterOptimizationResultDto>.Fail("Optimization run was not found.");
        }

        var dto = JsonSerializer.Deserialize<ParameterOptimizationResultDto>(run.ResultJson, JsonOptions);
        return dto is null
            ? ServiceResult<ParameterOptimizationResultDto>.Fail("Optimization result could not be parsed.")
            : ServiceResult<ParameterOptimizationResultDto>.Ok(dto);
    }

    public Task<ServiceResult<bool>> CancelAsync(long runId, CancellationToken cancellationToken = default)
    {
        if (ActiveRuns.TryGetValue(runId, out var cts))
        {
            cts.Cancel();
            return Task.FromResult(ServiceResult<bool>.Ok(true));
        }

        return Task.FromResult(ServiceResult<bool>.Fail("Optimization run is not active."));
    }

    private static StrategyResearchExecutionOptions BuildExecutionOptions(RunParameterOptimizationRequest request) => new()
    {
        ExecutionMode = request.ExecutionMode ?? "MarketFill",
        MakerFeeRate = request.MakerFeeRate ?? 0.0002m,
        TakerFeeRate = request.TakerFeeRate ?? 0.0005m,
        OrderExpiryCandles = request.OrderExpiryCandles ?? 3,
        UseAiScoring = request.UseAiScoring ?? false,
        MinConfidenceScore = request.MinConfidenceScore ?? 80m,
        SlippagePercent = request.SlippagePercent ?? 0m,
        VgResearchProfile = request.VgResearchProfile
    };

    private static IReadOnlyDictionary<string, string> MergeParameters(
        IReadOnlyDictionary<string, string> combo,
        IReadOnlyDictionary<string, string>? fixedParameters)
    {
        if (fixedParameters is null || fixedParameters.Count == 0) return combo;
        var merged = new Dictionary<string, string>(combo, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in fixedParameters)
        {
            merged[key] = value;
        }

        return merged;
    }

    private IReadOnlyList<IReadOnlyDictionary<string, string>> GenerateRandomCombinations(RunParameterOptimizationRequest request)
    {
        var grid = _definitionProvider.GenerateGridCombinations(request.StrategyCode, int.MaxValue, request.ParameterRangeOverrides);
        if (grid.Count <= request.MaxCombinations) return grid;
        return grid.OrderBy(_ => Random.Shared.Next()).Take(request.MaxCombinations).ToList();
    }
}
