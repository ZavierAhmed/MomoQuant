using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Optimization.Dtos;
using MomoQuant.Application.Strategies.Optimization;
using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend;
using MomoQuant.Application.Validation;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Optimization;

namespace MomoQuant.Application.Optimization;

public interface ITargetParameterOptimizationService
{
    Task<ServiceResult<TargetOptimizationRunDto>> RunTargetOptimizationAsync(
        TargetOptimizationRequest request,
        long? userId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<TargetOptimizationRunDto>> GetAsync(long runId, CancellationToken cancellationToken = default);

    Task<ServiceResult<bool>> CancelAsync(long runId, CancellationToken cancellationToken = default);

    Task<ServiceResult<StrategyParameterSetDto>> SaveBestAsync(
        long runId,
        SaveTargetOptimizationBestRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<StrategyParameterSetDto>> ApproveBestAsync(
        long runId,
        CancellationToken cancellationToken = default);
}

public sealed class TargetParameterOptimizationService : ITargetParameterOptimizationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly Dictionary<long, CancellationTokenSource> ActiveRuns = new();

    private readonly ITargetOptimizationRunRepository _runRepository;
    private readonly IStrategyParameterDefinitionProvider _definitionProvider;
    private readonly IValidationDateSplitService _splitService;
    private readonly ITargetOptimizationRulesEvaluator _rulesEvaluator;
    private readonly IStrategyResearchBacktestExecutor _researchExecutor;
    private readonly IStrategyResearchCandleCoverageService _candleCoverageService;
    private readonly IStrategyParameterSetService _parameterSetService;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IExchangeRepository _exchangeRepository;

    public TargetParameterOptimizationService(
        ITargetOptimizationRunRepository runRepository,
        IStrategyParameterDefinitionProvider definitionProvider,
        IValidationDateSplitService splitService,
        ITargetOptimizationRulesEvaluator rulesEvaluator,
        IStrategyResearchBacktestExecutor researchExecutor,
        IStrategyResearchCandleCoverageService candleCoverageService,
        IStrategyParameterSetService parameterSetService,
        ISymbolRepository symbolRepository,
        IExchangeRepository exchangeRepository)
    {
        _runRepository = runRepository;
        _definitionProvider = definitionProvider;
        _splitService = splitService;
        _rulesEvaluator = rulesEvaluator;
        _researchExecutor = researchExecutor;
        _candleCoverageService = candleCoverageService;
        _parameterSetService = parameterSetService;
        _symbolRepository = symbolRepository;
        _exchangeRepository = exchangeRepository;
    }

    public async Task<ServiceResult<TargetOptimizationRunDto>> RunTargetOptimizationAsync(
        TargetOptimizationRequest request,
        long? userId,
        CancellationToken cancellationToken = default)
    {
        var rules = request.TargetRules ?? TargetOptimizationRulesDto.DefaultResearch();
        var maxAttempts = Math.Max(1, Math.Min(request.MaxAttempts, request.MaxCombinations));

        var estimated = _definitionProvider.EstimateGridCombinations(request.StrategyCode, request.ParameterRanges);
        if (estimated > request.MaxCombinations && request.ParameterSearchMode == ParameterSearchMode.GridSearch)
        {
            return ServiceResult<TargetOptimizationRunDto>.Fail(
                $"Grid search would produce {estimated} combinations, exceeding max {request.MaxCombinations}. Reduce ranges or use RandomSearch/Hybrid.",
                "maxCombinations");
        }

        var combinations = GenerateCombinations(request);
        if (combinations.Count == 0)
        {
            return ServiceResult<TargetOptimizationRunDto>.Fail("No parameter combinations generated.");
        }

        var symbol = await _symbolRepository.GetByIdAsync(request.SymbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<TargetOptimizationRunDto>.Fail("Symbol was not found.");
        }

        var exchange = await _exchangeRepository.GetByIdAsync(request.ExchangeId, cancellationToken);
        var split = _splitService.Split(request.FromUtc, request.ToUtc);
        var executionOptions = BuildExecutionOptions(request);

        var coverageResult = await _candleCoverageService.EnsureCoverageAsync(
            request.ExchangeId,
            request.SymbolId,
            request.StrategyCode,
            request.Timeframe,
            request.FromUtc,
            request.ToUtc,
            request.AutoImportMissingCandles,
            cancellationToken);

        if (!coverageResult.Succeeded)
        {
            return ServiceResult<TargetOptimizationRunDto>.Fail(coverageResult.ErrorMessage ?? "Candle coverage check failed.");
        }

        var run = new TargetOptimizationRun
        {
            StrategyCode = request.StrategyCode,
            ExchangeId = request.ExchangeId,
            SymbolId = request.SymbolId,
            Timeframe = request.Timeframe,
            FromUtc = request.FromUtc,
            ToUtc = request.ToUtc,
            ValidationSplitMode = request.ValidationSplitMode,
            ParameterSearchMode = request.ParameterSearchMode,
            MaxCombinations = request.MaxCombinations,
            MaxAttempts = maxAttempts,
            TotalCombinations = Math.Min(combinations.Count, maxAttempts),
            Status = TargetOptimizationStatus.Running,
            TargetRulesJson = JsonSerializer.Serialize(rules, JsonOptions),
            RequestedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            HeartbeatAtUtc = DateTime.UtcNow
        };

        await _runRepository.AddAsync(run, cancellationToken);
        await _runRepository.SaveChangesAsync(cancellationToken);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(request.MaxRuntimeMinutes));
        ActiveRuns[run.Id] = cts;

        var results = new List<TargetParameterSetResultDto>();
        var warnings = new List<string>();
        if (coverageResult.Data?.Any(c => c.ImportedDuringRun) == true)
        {
            warnings.Add("Missing candles were imported automatically before this run.");
        }

        var deadline = DateTime.UtcNow.AddMinutes(request.MaxRuntimeMinutes);
        var attemptsLimit = Math.Min(combinations.Count, maxAttempts);

        try
        {
            for (var i = 0; i < attemptsLimit; i++)
            {
                if (cts.Token.IsCancellationRequested || DateTime.UtcNow >= deadline)
                {
                    warnings.Add("Target optimization stopped due to timeout or cancellation.");
                    break;
                }

                var combo = MergeParameters(combinations[i], request.FixedParameters);
                if (VgResearchProfilePresets.AppliesToStrategy(request.StrategyCode))
                {
                    combo = VgResearchProfilePresets.Apply(request.VgResearchProfile, combo);
                }
                run.CurrentParametersJson = JsonSerializer.Serialize(combo, JsonOptions);
                run.HeartbeatAtUtc = DateTime.UtcNow;
                await _runRepository.UpdateAsync(run, cancellationToken);
                await _runRepository.SaveChangesAsync(cancellationToken);

                var training = await _researchExecutor.RunWindowAsync(
                    request.ExchangeId, request.SymbolId, request.Timeframe,
                    split.TrainingRange.FromUtc, split.TrainingRange.ToUtc,
                    request.StrategyCode, combo, request.RiskProfileId, request.InitialBalance,
                    executionOptions, cts.Token);

                if (training is null)
                {
                    results.Add(BuildEngineErrorResult(combo, i + 1));
                    run.FailedCount++;
                    continue;
                }

                var (trainingPassed, trainingFailReasons, trainingSummary) =
                    _rulesEvaluator.EvaluateTraining(training.Metrics, rules);

                if (!trainingPassed)
                {
                    results.Add(new TargetParameterSetResultDto
                    {
                        Rank = 0,
                        Status = DetermineTrainingFailureStatus(training.Metrics, trainingSummary),
                        Parameters = combo,
                        TrainingMetrics = training.Metrics,
                        TargetPassSummary = trainingSummary,
                        FailReasons = trainingFailReasons
                    });
                    run.FailedCount++;
                    run.CompletedCombinations = i + 1;
                    run.HeartbeatAtUtc = DateTime.UtcNow;
                    await _runRepository.UpdateAsync(run, cancellationToken);
                    await _runRepository.SaveChangesAsync(cancellationToken);
                    continue;
                }

                run.TrainingPassedCount++;

                var validation = await _researchExecutor.RunWindowAsync(
                    request.ExchangeId, request.SymbolId, request.Timeframe,
                    split.ValidationRange.FromUtc, split.ValidationRange.ToUtc,
                    request.StrategyCode, combo, request.RiskProfileId, request.InitialBalance,
                    executionOptions, cts.Token);

                if (validation is null)
                {
                    results.Add(BuildEngineErrorResult(combo, i + 1, training.Metrics));
                    run.FailedCount++;
                    continue;
                }

                var (validationPassed, status, failReasons, overfitWarnings, passSummary, robustness) =
                    _rulesEvaluator.EvaluateValidation(training.Metrics, validation.Metrics, rules);

                var score = CalculateRankingScore(status, robustness, validation.Metrics);

                if (status == ParameterSetTestStatus.ValidationPassed)
                {
                    run.ValidationPassedCount++;
                }
                else if (status == ParameterSetTestStatus.Overfit)
                {
                    run.OverfitCount++;
                }
                else
                {
                    run.FailedCount++;
                }

                results.Add(new TargetParameterSetResultDto
                {
                    Rank = 0,
                    Status = status,
                    Parameters = combo,
                    TrainingMetrics = training.Metrics,
                    ValidationMetrics = validation.Metrics,
                    RobustnessScore = robustness,
                    Score = score,
                    TargetPassSummary = passSummary,
                    FailReasons = failReasons,
                    OverfitWarnings = overfitWarnings
                });

                run.CompletedCombinations = i + 1;
                run.HeartbeatAtUtc = DateTime.UtcNow;
                await _runRepository.UpdateAsync(run, cancellationToken);
                await _runRepository.SaveChangesAsync(cancellationToken);
            }

            var ranked = RankResults(results);
            var bestPassed = ranked.FirstOrDefault(r => r.Status == ParameterSetTestStatus.ValidationPassed);
            var bestFailed = ranked.FirstOrDefault(r => r.Status != ParameterSetTestStatus.ValidationPassed);

            var dto = BuildRunDto(
                run, rules, split, symbol.SymbolName, exchange?.Name ?? "Unknown",
                ranked, bestPassed, bestFailed, warnings);

            if (request.SaveBestIfPassed && bestPassed is not null)
            {
                var saved = await SaveParameterSetInternalAsync(
                    run.Id, bestPassed, split, rules, request, approve: request.AutoApproveIfPassed, cancellationToken);
                if (saved.Succeeded && saved.Data is not null)
                {
                    bestPassed = bestPassed with { SavedParameterSetId = saved.Data.Id, IsApproved = saved.Data.IsApproved };
                    dto = dto with
                    {
                        BestPassedParameterSet = bestPassed,
                        Results = ranked.Select(r => r.Status == ParameterSetTestStatus.ValidationPassed && r.Rank == bestPassed.Rank
                            ? bestPassed : r).ToList()
                    };
                }
            }

            run.Status = TargetOptimizationStatus.Completed;
            run.CompletedAtUtc = DateTime.UtcNow;
            run.ResultJson = JsonSerializer.Serialize(dto, JsonOptions);
            run.WarningsJson = JsonSerializer.Serialize(warnings, JsonOptions);
            run.TrainingPassedCount = dto.TrainingPassedCount;
            run.ValidationPassedCount = dto.ValidationPassedCount;
            run.OverfitCount = dto.OverfitCount;
            run.FailedCount = dto.FailedCount;
            await _runRepository.UpdateAsync(run, cancellationToken);
            await _runRepository.SaveChangesAsync(cancellationToken);

            return ServiceResult<TargetOptimizationRunDto>.Ok(dto);
        }
        catch (OperationCanceledException)
        {
            run.Status = TargetOptimizationStatus.Cancelled;
            run.CompletedAtUtc = DateTime.UtcNow;
            await _runRepository.UpdateAsync(run, cancellationToken);
            await _runRepository.SaveChangesAsync(cancellationToken);
            return ServiceResult<TargetOptimizationRunDto>.Fail("Target optimization was cancelled or timed out.");
        }
        catch (Exception ex)
        {
            run.Status = TargetOptimizationStatus.Failed;
            run.ErrorMessage = ex.Message;
            run.CompletedAtUtc = DateTime.UtcNow;
            await _runRepository.UpdateAsync(run, cancellationToken);
            await _runRepository.SaveChangesAsync(cancellationToken);
            return ServiceResult<TargetOptimizationRunDto>.Fail($"Target optimization failed: {ex.Message}");
        }
        finally
        {
            ActiveRuns.Remove(run.Id);
            cts.Dispose();
        }
    }

    public async Task<ServiceResult<TargetOptimizationRunDto>> GetAsync(long runId, CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(runId, cancellationToken);
        if (run is null)
        {
            return ServiceResult<TargetOptimizationRunDto>.Fail("Target optimization run was not found.");
        }

        if (!string.IsNullOrWhiteSpace(run.ResultJson))
        {
            var dto = JsonSerializer.Deserialize<TargetOptimizationRunDto>(run.ResultJson, JsonOptions);
            if (dto is not null)
            {
                return ServiceResult<TargetOptimizationRunDto>.Ok(dto with
                {
                    Status = run.Status,
                    CompletedCombinations = run.CompletedCombinations,
                    HeartbeatAtUtc = run.HeartbeatAtUtc,
                    TrainingPassedCount = run.TrainingPassedCount,
                    ValidationPassedCount = run.ValidationPassedCount,
                    OverfitCount = run.OverfitCount,
                    FailedCount = run.FailedCount,
                    CurrentParameters = string.IsNullOrWhiteSpace(run.CurrentParametersJson)
                        ? null
                        : JsonSerializer.Deserialize<Dictionary<string, string>>(run.CurrentParametersJson, JsonOptions)
                });
            }
        }

        var rules = string.IsNullOrWhiteSpace(run.TargetRulesJson)
            ? TargetOptimizationRulesDto.DefaultResearch()
            : JsonSerializer.Deserialize<TargetOptimizationRulesDto>(run.TargetRulesJson, JsonOptions)
              ?? TargetOptimizationRulesDto.DefaultResearch();

        var symbol = await _symbolRepository.GetByIdAsync(run.SymbolId, cancellationToken);
        var exchange = await _exchangeRepository.GetByIdAsync(run.ExchangeId, cancellationToken);
        var split = _splitService.Split(run.FromUtc, run.ToUtc);

        return ServiceResult<TargetOptimizationRunDto>.Ok(new TargetOptimizationRunDto
        {
            Id = run.Id,
            StrategyCode = run.StrategyCode,
            SymbolId = run.SymbolId,
            Exchange = exchange?.Name ?? "Unknown",
            Symbol = symbol?.SymbolName ?? run.SymbolId.ToString(),
            Timeframe = run.Timeframe,
            DateRange = split.FullDateRange,
            TrainingRange = split.TrainingRange,
            ValidationRange = split.ValidationRange,
            TargetRules = rules,
            ParameterSearchMode = run.ParameterSearchMode,
            MaxCombinations = run.MaxCombinations,
            CompletedCombinations = run.CompletedCombinations,
            Status = run.Status,
            TrainingPassedCount = run.TrainingPassedCount,
            ValidationPassedCount = run.ValidationPassedCount,
            OverfitCount = run.OverfitCount,
            FailedCount = run.FailedCount,
            CreatedAtUtc = run.CreatedAtUtc,
            CompletedAtUtc = run.CompletedAtUtc,
            HeartbeatAtUtc = run.HeartbeatAtUtc
        });
    }

    public Task<ServiceResult<bool>> CancelAsync(long runId, CancellationToken cancellationToken = default)
    {
        if (ActiveRuns.TryGetValue(runId, out var cts))
        {
            cts.Cancel();
            return Task.FromResult(ServiceResult<bool>.Ok(true));
        }

        return Task.FromResult(ServiceResult<bool>.Fail("Target optimization run is not active."));
    }

    public async Task<ServiceResult<StrategyParameterSetDto>> SaveBestAsync(
        long runId,
        SaveTargetOptimizationBestRequest request,
        CancellationToken cancellationToken = default)
    {
        var runResult = await GetAsync(runId, cancellationToken);
        if (!runResult.Succeeded || runResult.Data is null)
        {
            return ServiceResult<StrategyParameterSetDto>.Fail(runResult.ErrorMessage ?? "Run not found.");
        }

        var run = runResult.Data;
        var target = request.SaveAsFailedResearch ? run.BestFailedParameterSet : run.BestPassedParameterSet;
        if (target is null)
        {
            return ServiceResult<StrategyParameterSetDto>.Fail("No suitable parameter set found to save.");
        }

        if (request.Approve && target.Status != ParameterSetTestStatus.ValidationPassed)
        {
            return ServiceResult<StrategyParameterSetDto>.Fail("Only validation-passed parameter sets can be approved.");
        }

        return await SaveParameterSetInternalAsync(
            runId, target, run, request.Approve, request.Name, request.SaveAsFailedResearch, cancellationToken);
    }

    public async Task<ServiceResult<StrategyParameterSetDto>> ApproveBestAsync(
        long runId,
        CancellationToken cancellationToken = default)
    {
        return await SaveBestAsync(runId, new SaveTargetOptimizationBestRequest { Approve = true }, cancellationToken);
    }

    private async Task<ServiceResult<StrategyParameterSetDto>> SaveParameterSetInternalAsync(
        long runId,
        TargetParameterSetResultDto target,
        ValidationSplitDto split,
        TargetOptimizationRulesDto rules,
        TargetOptimizationRequest request,
        bool approve,
        CancellationToken cancellationToken)
    {
        var symbol = await _symbolRepository.GetByIdAsync(request.SymbolId, cancellationToken);
        var name = BuildParameterSetName(request.StrategyCode, symbol?.SymbolName ?? "Symbol", request.Timeframe);

        return await _parameterSetService.SaveAsync(new SaveStrategyParameterSetRequest
        {
            Name = name,
            StrategyCode = request.StrategyCode,
            SymbolId = request.SymbolId,
            Timeframe = request.Timeframe,
            Parameters = target.Parameters,
            TargetOptimizationRunId = runId,
            Source = StrategyParameterSetSource.TargetOptimized.ToString(),
            Approve = approve,
            TrainingMetrics = target.TrainingMetrics,
            ValidationMetrics = target.ValidationMetrics,
            RobustnessScore = target.RobustnessScore,
            TrainingRange = split.TrainingRange,
            ValidationRange = split.ValidationRange,
            ValidationStatus = target.Status.ToString(),
            ValidationTradeCount = target.ValidationMetrics?.TradeCount,
            SaveAsFailedResearch = false
        }, cancellationToken);
    }

    private async Task<ServiceResult<StrategyParameterSetDto>> SaveParameterSetInternalAsync(
        long runId,
        TargetParameterSetResultDto target,
        TargetOptimizationRunDto run,
        bool approve,
        string? nameOverride,
        bool saveAsFailedResearch,
        CancellationToken cancellationToken)
    {
        if (approve && target.Status != ParameterSetTestStatus.ValidationPassed)
        {
            return ServiceResult<StrategyParameterSetDto>.Fail("Only validation-passed parameter sets can be approved.");
        }

        var name = nameOverride ?? BuildParameterSetName(run.StrategyCode, run.Symbol, run.Timeframe);

        return await _parameterSetService.SaveAsync(new SaveStrategyParameterSetRequest
        {
            Name = name,
            StrategyCode = run.StrategyCode,
            SymbolId = run.SymbolId,
            Timeframe = run.Timeframe,
            Parameters = target.Parameters,
            TargetOptimizationRunId = runId,
            Source = StrategyParameterSetSource.TargetOptimized.ToString(),
            Approve = approve && !saveAsFailedResearch,
            TrainingMetrics = target.TrainingMetrics,
            ValidationMetrics = target.ValidationMetrics,
            RobustnessScore = target.RobustnessScore,
            TrainingRange = run.TrainingRange,
            ValidationRange = run.ValidationRange,
            ValidationStatus = target.Status.ToString(),
            ValidationTradeCount = target.ValidationMetrics?.TradeCount,
            SaveAsFailedResearch = saveAsFailedResearch
        }, cancellationToken);
    }

    private static TargetOptimizationRunDto BuildRunDto(
        TargetOptimizationRun run,
        TargetOptimizationRulesDto rules,
        ValidationSplitDto split,
        string symbolName,
        string exchangeName,
        IReadOnlyList<TargetParameterSetResultDto> ranked,
        TargetParameterSetResultDto? bestPassed,
        TargetParameterSetResultDto? bestFailed,
        IReadOnlyList<string> warnings)
    {
        var passed = ranked.Where(r => r.Status == ParameterSetTestStatus.ValidationPassed).ToList();
        var overfit = ranked.Where(r => r.Status == ParameterSetTestStatus.Overfit).ToList();
        var failed = ranked.Where(r => r.Status is ParameterSetTestStatus.TrainingFailed
            or ParameterSetTestStatus.ValidationFailed
            or ParameterSetTestStatus.TooFewTrades
            or ParameterSetTestStatus.TooHighDrawdown
            or ParameterSetTestStatus.NoTrades
            or ParameterSetTestStatus.EngineError).ToList();

        var validationResults = ranked.Where(r => r.ValidationMetrics is not null).ToList();

        return new TargetOptimizationRunDto
        {
            Id = run.Id,
            StrategyCode = run.StrategyCode,
            SymbolId = run.SymbolId,
            Exchange = exchangeName,
            Symbol = symbolName,
            Timeframe = run.Timeframe,
            DateRange = split.FullDateRange,
            TrainingRange = split.TrainingRange,
            ValidationRange = split.ValidationRange,
            TargetRules = rules,
            ParameterSearchMode = run.ParameterSearchMode,
            MaxCombinations = run.MaxCombinations,
            CompletedCombinations = run.CompletedCombinations,
            Status = run.Status,
            BestPassedParameterSet = bestPassed,
            BestFailedParameterSet = bestFailed,
            Results = ranked,
            Summary = new TargetOptimizationSummaryDto
            {
                BestStatus = bestPassed?.Status.ToString() ?? bestFailed?.Status.ToString() ?? "None",
                PassedCount = passed.Count,
                OverfitCount = overfit.Count,
                FailedCount = failed.Count,
                TrainingPassedCount = ranked.Count(r => r.Status != ParameterSetTestStatus.TrainingFailed
                    && r.Status != ParameterSetTestStatus.NoTrades
                    && r.Status != ParameterSetTestStatus.EngineError
                    && r.TrainingMetrics is not null),
                BestRobustnessScore = validationResults.MaxBy(r => r.RobustnessScore)?.RobustnessScore,
                BestValidationNetPnlPercent = validationResults.MaxBy(r => r.ValidationMetrics!.NetPnlPercent)?.ValidationMetrics?.NetPnlPercent,
                BestValidationProfitFactor = validationResults.MaxBy(r => r.ValidationMetrics!.ProfitFactor)?.ValidationMetrics?.ProfitFactor,
                BestValidationDrawdownPercent = validationResults.MinBy(r => r.ValidationMetrics!.MaxDrawdownPercent)?.ValidationMetrics?.MaxDrawdownPercent
            },
            Warnings = warnings,
            TrainingPassedCount = run.TrainingPassedCount,
            ValidationPassedCount = run.ValidationPassedCount,
            OverfitCount = run.OverfitCount,
            FailedCount = run.FailedCount,
            CreatedAtUtc = run.CreatedAtUtc,
            CompletedAtUtc = run.CompletedAtUtc,
            HeartbeatAtUtc = run.HeartbeatAtUtc
        };
    }

    private static List<TargetParameterSetResultDto> RankResults(IReadOnlyList<TargetParameterSetResultDto> results)
    {
        var ranked = results
            .OrderByDescending(r => r.Status == ParameterSetTestStatus.ValidationPassed)
            .ThenByDescending(r => r.RobustnessScore)
            .ThenByDescending(r => r.ValidationMetrics?.NetPnlPercent ?? decimal.MinValue)
            .ThenByDescending(r => r.ValidationMetrics?.ProfitFactor ?? 0m)
            .ThenBy(r => r.ValidationMetrics?.MaxDrawdownPercent ?? decimal.MaxValue)
            .ThenByDescending(r => r.ValidationMetrics?.TradeCount ?? 0)
            .Select((r, idx) => r with { Rank = idx + 1 })
            .ToList();

        return ranked;
    }

    private static decimal CalculateRankingScore(
        ParameterSetTestStatus status,
        decimal robustness,
        StrategyPerformanceMetricsDto? validation)
    {
        if (status != ParameterSetTestStatus.ValidationPassed || validation is null)
        {
            return 0m;
        }

        return Math.Round(robustness * 0.5m + validation.NetPnlPercent * 0.2m +
                          validation.ProfitFactor * 10m + validation.TradeCount * 0.1m, 2);
    }

    private static ParameterSetTestStatus DetermineTrainingFailureStatus(
        StrategyPerformanceMetricsDto metrics,
        TargetPassSummary summary)
    {
        if (metrics.TradeCount == 0) return ParameterSetTestStatus.NoTrades;
        if (!summary.TrainingTradesPassed) return ParameterSetTestStatus.TooFewTrades;
        if (!summary.TrainingDrawdownPassed) return ParameterSetTestStatus.TooHighDrawdown;
        return ParameterSetTestStatus.TrainingFailed;
    }

    private static TargetParameterSetResultDto BuildEngineErrorResult(
        IReadOnlyDictionary<string, string> combo,
        int index,
        StrategyPerformanceMetricsDto? training = null) => new()
    {
        Rank = index,
        Status = ParameterSetTestStatus.EngineError,
        Parameters = combo,
        TrainingMetrics = training,
        FailReasons = ["Backtest engine could not execute for this parameter set."]
    };

    private IReadOnlyList<IReadOnlyDictionary<string, string>> GenerateCombinations(TargetOptimizationRequest request)
    {
        var overrides = request.ParameterRanges;
        return request.ParameterSearchMode switch
        {
            ParameterSearchMode.RandomSearch => GenerateRandomCombinations(request),
            ParameterSearchMode.Hybrid => GenerateHybridCombinations(request),
            _ => _definitionProvider.GenerateGridCombinations(request.StrategyCode, request.MaxCombinations, overrides)
        };
    }

    private IReadOnlyList<IReadOnlyDictionary<string, string>> GenerateRandomCombinations(TargetOptimizationRequest request)
    {
        var grid = _definitionProvider.GenerateGridCombinations(request.StrategyCode, int.MaxValue, request.ParameterRanges);
        if (grid.Count <= request.MaxCombinations) return grid;
        return grid.OrderBy(_ => Random.Shared.Next()).Take(request.MaxCombinations).ToList();
    }

    private IReadOnlyList<IReadOnlyDictionary<string, string>> GenerateHybridCombinations(TargetOptimizationRequest request)
    {
        var grid = _definitionProvider.GenerateGridCombinations(request.StrategyCode, int.MaxValue, request.ParameterRanges);
        if (grid.Count <= request.MaxCombinations) return grid;

        var gridHalf = Math.Max(1, request.MaxCombinations / 2);
        var randomHalf = request.MaxCombinations - gridHalf;
        var ordered = grid.Take(gridHalf).ToList();
        var remainder = grid.Skip(gridHalf).OrderBy(_ => Random.Shared.Next()).Take(randomHalf);
        return ordered.Concat(remainder).ToList();
    }

    private static StrategyResearchExecutionOptions BuildExecutionOptions(TargetOptimizationRequest request) => new()
    {
        ExecutionMode = request.ExecutionMode ?? "MarketFill",
        MakerFeeRate = request.MakerFeeRate ?? 0.0002m,
        TakerFeeRate = request.TakerFeeRate ?? 0.0004m,
        OrderExpiryCandles = request.OrderExpiryCandles ?? 3,
        UseAiScoring = request.UseAiScoring ?? false,
        MinConfidenceScore = request.MinimumConfidenceScore ?? 80m,
        SlippagePercent = request.SlippagePercent ?? 0m,
        AutoImportCandles = request.AutoImportMissingCandles,
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

    public static string BuildParameterSetName(string strategyCode, string symbol, string timeframe)
    {
        var shortName = strategyCode switch
        {
            "VOLATILITY_GATED_SUPERTREND_MOMENTUM" => "VG SuperTrend",
            _ => strategyCode.Length > 24 ? strategyCode[..24] : strategyCode
        };
        return $"{shortName} {symbol} {timeframe} TargetOpt {DateTime.UtcNow:yyyyMMdd}";
    }
}
