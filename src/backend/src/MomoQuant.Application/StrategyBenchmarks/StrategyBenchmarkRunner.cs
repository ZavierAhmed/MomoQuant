using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Backtesting.Dtos;
using MomoQuant.Application.Indicators;
using MomoQuant.Application.Indicators.Dtos;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.MarketData.Dtos;
using MomoQuant.Application.Options;
using MomoQuant.Application.StrategyBenchmarks.Dtos;
using MomoQuant.Domain.Benchmarks;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.StrategyBenchmarks;

public interface IStrategyBenchmarkRunner
{
    Task ExecuteAsync(long benchmarkRunId, CancellationToken cancellationToken = default);
}

public sealed class StrategyBenchmarkRunner : IStrategyBenchmarkRunner
{
    private readonly IStrategyBenchmarkRunRepository _runRepository;
    private readonly IStrategyBenchmarkResultRepository _resultRepository;
    private readonly IStrategyBenchmarkRunItemRepository _runItemRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IStrategyRepository _strategyRepository;
    private readonly IMarketDataService _marketDataService;
    private readonly IIndicatorCalculationService _indicatorCalculationService;
    private readonly IBacktestProgressStore _progressStore;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IStrategyGradeService _gradeService;
    private readonly IBenchmarkImportRangeChunker _importRangeChunker;
    private readonly MarketDataSettings _marketDataSettings;
    private readonly StrategyBenchmarkSettings _benchmarkSettings;
    private readonly ILogger<StrategyBenchmarkRunner> _logger;

    public StrategyBenchmarkRunner(
        IStrategyBenchmarkRunRepository runRepository,
        IStrategyBenchmarkResultRepository resultRepository,
        IStrategyBenchmarkRunItemRepository runItemRepository,
        ISymbolRepository symbolRepository,
        IStrategyRepository strategyRepository,
        IMarketDataService marketDataService,
        IIndicatorCalculationService indicatorCalculationService,
        IBacktestProgressStore progressStore,
        IServiceScopeFactory serviceScopeFactory,
        IStrategyGradeService gradeService,
        IBenchmarkImportRangeChunker importRangeChunker,
        IOptions<MarketDataSettings> marketDataSettings,
        IOptions<StrategyBenchmarkSettings> benchmarkSettings,
        ILogger<StrategyBenchmarkRunner> logger)
    {
        _runRepository = runRepository;
        _resultRepository = resultRepository;
        _runItemRepository = runItemRepository;
        _symbolRepository = symbolRepository;
        _strategyRepository = strategyRepository;
        _marketDataService = marketDataService;
        _indicatorCalculationService = indicatorCalculationService;
        _progressStore = progressStore;
        _serviceScopeFactory = serviceScopeFactory;
        _gradeService = gradeService;
        _importRangeChunker = importRangeChunker;
        _marketDataSettings = marketDataSettings.Value;
        _benchmarkSettings = benchmarkSettings.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(long benchmarkRunId, CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(benchmarkRunId, cancellationToken);
        if (run is null)
        {
            return;
        }

        if (run.Status is StrategyBenchmarkStatus.Completed or StrategyBenchmarkStatus.Cancelled)
        {
            return;
        }

        var config = StrategyBenchmarkMapper.ParseConfig(run.ConfigJson);
        var request = config.Request ?? new CreateStrategyBenchmarkRequest();
        var symbols = StrategyBenchmarkMapper.ParseStringList(run.SymbolsJson);
        var timeframes = StrategyBenchmarkMapper.ParseStringList(run.TimeframesJson);
        var strategyIds = StrategyBenchmarkMapper.ParseLongList(run.StrategyIdsJson);

        try
        {
            _logger.LogInformation(
                "Starting strategy benchmark execution. BenchmarkRunId={BenchmarkRunId}, Symbols={Symbols}, Timeframes={Timeframes}, Strategies={Strategies}, BenchmarkFromUtc={BenchmarkFromUtc}, BenchmarkToUtc={BenchmarkToUtc}, WarmupFromUtc={WarmupFromUtc}, ImportMissingData={ImportMissingData}, RecalculateIndicators={RecalculateIndicators}, InitialBalance={InitialBalance}, ExecutionMode={ExecutionMode}, UseAiScoring={UseAiScoring}",
                run.Id,
                string.Join(",", symbols),
                string.Join(",", timeframes),
                string.Join(",", strategyIds),
                run.BenchmarkFromUtc,
                run.BenchmarkToUtc,
                run.WarmupFromUtc,
                request.ImportMissingData,
                request.RecalculateIndicators,
                run.InitialBalance,
                run.ExecutionMode,
                run.UseAiScoring);

            run.CancellationRequested = false;
            run.StartedAtUtc ??= DateTime.UtcNow;
            run.UpdatedAtUtc = DateTime.UtcNow;
            await SaveRunAsync(run, cancellationToken);

            var imports = config.Preparation?.Imports.ToList() ?? [];
            var quality = config.Preparation?.DataQuality.ToList() ?? [];
            var indicators = config.Preparation?.Indicators.ToList() ?? [];
            var skipPreparation = config.Preparation is not null
                && (await _runItemRepository.GetByBenchmarkRunIdAsync(run.Id, cancellationToken)).Count > 0;

            var symbolMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var symbolName in symbols)
            {
                var symbol = await _symbolRepository.GetByExchangeAndNameAsync(run.ExchangeId, symbolName, cancellationToken);
                if (symbol is null)
                {
                    throw new InvalidOperationException($"Symbol '{symbolName}' was not found.");
                }

                symbolMap[symbolName] = symbol.Id;
            }

            var strategies = await _strategyRepository.GetAllAsync(cancellationToken);
            var strategyMap = strategies
                .Where(strategy => strategyIds.Contains(strategy.Id))
                .ToDictionary(strategy => strategy.Id);

            if (!skipPreparation && request.ImportMissingData)
            {
                run.Status = StrategyBenchmarkStatus.ImportingData;
                run.CurrentStage = "ImportingData";
                run.Message = "Importing public historical candles.";
                run.PercentComplete = 5m;
                run.UpdatedAtUtc = DateTime.UtcNow;
                await SaveRunAsync(run, cancellationToken);

                var chunkDays = BenchmarkImportRangeChunker.ResolveChunkDays(
                    _benchmarkSettings.BinanceImportChunkDays,
                    _marketDataSettings.Binance.MaxDaysPerImport);
                var chunks = _importRangeChunker.CreateChunks(run.WarmupFromUtc, run.BenchmarkToUtc, chunkDays);
                if (chunks.Count == 0)
                {
                    throw new InvalidOperationException("Benchmark import range produced no valid chunks.");
                }

                var pairCount = Math.Max(symbols.Count * timeframes.Count, 1);
                var totalChunks = pairCount * chunks.Count;
                var completedChunks = 0;
                var totalInserted = 0;
                var totalSkipped = 0;

                config.ImportProgress = new StrategyBenchmarkImportProgressState
                {
                    TotalChunks = totalChunks,
                    CompletedChunks = 0,
                    InsertedCandles = 0,
                    SkippedDuplicateCandles = 0
                };
                run.ConfigJson = StrategyBenchmarkMapper.SerializeConfig(config);
                run.Message = "Benchmark import range was split into valid Binance chunks.";
                run.UpdatedAtUtc = DateTime.UtcNow;
                await SaveRunAsync(run, cancellationToken);

                var pairIndex = 0;
                foreach (var symbolName in symbols)
                {
                    var symbolId = symbolMap[symbolName];
                    foreach (var timeframe in timeframes)
                    {
                        run.CurrentSymbol = symbolName;
                        run.CurrentTimeframe = timeframe;

                        var chunkSummaries = new List<StrategyBenchmarkImportChunkSummaryDto>();
                        var received = 0;
                        var inserted = 0;
                        var skipped = 0;
                        var failedChunks = 0;

                        foreach (var chunk in chunks)
                        {
                            config.ImportProgress = new StrategyBenchmarkImportProgressState
                            {
                                CurrentChunkFromUtc = chunk.FromUtc,
                                CurrentChunkToUtc = chunk.ToUtc,
                                CompletedChunks = completedChunks,
                                TotalChunks = totalChunks,
                                InsertedCandles = totalInserted,
                                SkippedDuplicateCandles = totalSkipped
                            };
                            run.ConfigJson = StrategyBenchmarkMapper.SerializeConfig(config);
                            run.Message =
                                $"Importing {symbolName} {timeframe} chunk {completedChunks + 1}/{totalChunks} " +
                                $"({chunk.FromUtc:yyyy-MM-dd} to {chunk.ToUtc:yyyy-MM-dd}). Benchmark import range was split into valid Binance chunks.";
                            run.PercentComplete = 5m + (completedChunks * 20m / Math.Max(totalChunks, 1));
                            run.UpdatedAtUtc = DateTime.UtcNow;
                            await SaveRunAsync(run, cancellationToken);

                            if (_benchmarkSettings.SkipImportIfCoverageAlreadyGood)
                            {
                                var existingQuality = await _marketDataService.GetDataQualityAsync(
                                    run.ExchangeId,
                                    symbolId,
                                    timeframe,
                                    chunk.FromUtc,
                                    chunk.ToUtc,
                                    cancellationToken);
                                if (existingQuality.Succeeded
                                    && existingQuality.Data is not null
                                    && existingQuality.Data.CoveragePercent >= _benchmarkSettings.SkipImportCoveragePercent)
                                {
                                    completedChunks++;
                                    chunkSummaries.Add(new StrategyBenchmarkImportChunkSummaryDto
                                    {
                                        FromUtc = chunk.FromUtc,
                                        ToUtc = chunk.ToUtc,
                                        ReceivedCandles = existingQuality.Data.TotalCandles,
                                        InsertedCandles = 0,
                                        SkippedDuplicateCandles = existingQuality.Data.TotalCandles,
                                        Status = "SkippedExistingCoverage",
                                        ErrorMessage = null
                                    });
                                    totalSkipped += existingQuality.Data.TotalCandles;
                                    continue;
                                }
                            }

                            var importResult = await _marketDataService.ImportCandlesAsync(new ImportCandlesRequest
                            {
                                ExchangeId = run.ExchangeId,
                                SymbolId = symbolId,
                                Timeframe = timeframe,
                                FromUtc = chunk.FromUtc,
                                ToUtc = chunk.ToUtc
                            }, cancellationToken);

                            if (!importResult.Succeeded || importResult.Data is null)
                            {
                                failedChunks++;
                                var error = importResult.ErrorMessage ?? "Unknown import error.";
                                chunkSummaries.Add(new StrategyBenchmarkImportChunkSummaryDto
                                {
                                    FromUtc = chunk.FromUtc,
                                    ToUtc = chunk.ToUtc,
                                    ReceivedCandles = 0,
                                    InsertedCandles = 0,
                                    SkippedDuplicateCandles = 0,
                                    Status = "Failed",
                                    ErrorMessage = error
                                });

                                throw new InvalidOperationException(
                                    $"Failed importing {symbolName} {timeframe} from {chunk.FromUtc:yyyy-MM-dd} to {chunk.ToUtc:yyyy-MM-dd}: {error}");
                            }

                            received += importResult.Data.TotalReceived;
                            inserted += importResult.Data.InsertedCount;
                            skipped += importResult.Data.SkippedDuplicateCount;
                            totalInserted += importResult.Data.InsertedCount;
                            totalSkipped += importResult.Data.SkippedDuplicateCount;
                            completedChunks++;

                            chunkSummaries.Add(new StrategyBenchmarkImportChunkSummaryDto
                            {
                                FromUtc = chunk.FromUtc,
                                ToUtc = chunk.ToUtc,
                                ReceivedCandles = importResult.Data.TotalReceived,
                                InsertedCandles = importResult.Data.InsertedCount,
                                SkippedDuplicateCandles = importResult.Data.SkippedDuplicateCount,
                                Status = "Completed",
                                ErrorMessage = null
                            });

                            config.ImportProgress = new StrategyBenchmarkImportProgressState
                            {
                                CurrentChunkFromUtc = chunk.FromUtc,
                                CurrentChunkToUtc = chunk.ToUtc,
                                CompletedChunks = completedChunks,
                                TotalChunks = totalChunks,
                                InsertedCandles = totalInserted,
                                SkippedDuplicateCandles = totalSkipped
                            };
                            run.ConfigJson = StrategyBenchmarkMapper.SerializeConfig(config);
                            run.UpdatedAtUtc = DateTime.UtcNow;
                            await SaveRunAsync(run, cancellationToken);
                        }

                        imports.Add(new StrategyBenchmarkImportSummaryDto
                        {
                            Symbol = symbolName,
                            Timeframe = timeframe,
                            RequestedFromUtc = run.WarmupFromUtc,
                            RequestedToUtc = run.BenchmarkToUtc,
                            ReceivedCandles = received,
                            InsertedCandles = inserted,
                            SkippedDuplicateCandles = skipped,
                            MissingCandles = 0,
                            CoveragePercent = 0m,
                            TotalChunks = chunks.Count,
                            FailedChunks = failedChunks,
                            Chunks = chunkSummaries
                        });

                        pairIndex++;
                    }
                }
            }

            if (!skipPreparation)
            {
            run.Status = StrategyBenchmarkStatus.CheckingDataQuality;
            run.CurrentStage = "CheckingDataQuality";
            run.Message = "Checking candle coverage.";
            run.PercentComplete = 30m;
            run.DataPreparationPercent = 40m;
            run.UpdatedAtUtc = DateTime.UtcNow;
            await SaveRunAsync(run, cancellationToken);

            foreach (var symbolName in symbols)
            {
                var symbolId = symbolMap[symbolName];
                foreach (var timeframe in timeframes)
                {
                    var qualityResult = await _marketDataService.GetDataQualityAsync(
                        run.ExchangeId,
                        symbolId,
                        timeframe,
                        run.WarmupFromUtc,
                        run.BenchmarkToUtc,
                        cancellationToken);

                    if (!qualityResult.Succeeded || qualityResult.Data is null)
                    {
                        throw new InvalidOperationException(
                            qualityResult.ErrorMessage
                            ?? $"Failed to check data quality for {symbolName} {timeframe}.");
                    }

                    var warnings = new List<string>();
                    if (qualityResult.Data.CoveragePercent < 90m)
                    {
                        warnings.Add($"Coverage is {qualityResult.Data.CoveragePercent:0.##}%.");
                        if (!request.AllowLowCoverage)
                        {
                            throw new InvalidOperationException(
                                $"Insufficient candle coverage for {symbolName} {timeframe} ({qualityResult.Data.CoveragePercent:0.##}%).");
                        }
                    }

                    quality.Add(new StrategyBenchmarkDataQualityDto
                    {
                        Symbol = symbolName,
                        Timeframe = timeframe,
                        TotalCandles = qualityResult.Data.TotalCandles,
                        ExpectedCandles = qualityResult.Data.ExpectedCandles,
                        MissingCandles = qualityResult.Data.MissingCandles,
                        DuplicateCandles = qualityResult.Data.DuplicateCandles,
                        FirstOpenTimeUtc = qualityResult.Data.FirstOpenTimeUtc,
                        LastOpenTimeUtc = qualityResult.Data.LastOpenTimeUtc,
                        CoveragePercent = qualityResult.Data.CoveragePercent,
                        Warnings = warnings
                    });

                    var import = imports.FirstOrDefault(item =>
                        item.Symbol == symbolName && item.Timeframe == timeframe);
                    if (import is not null)
                    {
                        var index = imports.IndexOf(import);
                        imports[index] = new StrategyBenchmarkImportSummaryDto
                        {
                            Symbol = import.Symbol,
                            Timeframe = import.Timeframe,
                            RequestedFromUtc = import.RequestedFromUtc,
                            RequestedToUtc = import.RequestedToUtc,
                            ReceivedCandles = import.ReceivedCandles,
                            InsertedCandles = import.InsertedCandles,
                            SkippedDuplicateCandles = import.SkippedDuplicateCandles,
                            MissingCandles = qualityResult.Data.MissingCandles,
                            CoveragePercent = qualityResult.Data.CoveragePercent,
                            TotalChunks = import.TotalChunks,
                            FailedChunks = import.FailedChunks,
                            Chunks = import.Chunks
                        };
                    }
                }
            }

            if (request.RecalculateIndicators)
            {
                var indicatorTimeframes = config.ExecutionPlan
                    .SelectMany(item => item.RequiredIndicatorTimeframes)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (indicatorTimeframes.Count == 0)
                {
                    run.Message = "No indicator recalculation required for selected strategy setup.";
                    run.UpdatedAtUtc = DateTime.UtcNow;
                    await SaveRunAsync(run, cancellationToken);
                }
                else
                {
                    run.Status = StrategyBenchmarkStatus.RecalculatingIndicators;
                    run.CurrentStage = "RecalculatingIndicators";
                    run.Message = "Recalculating indicators.";
                    run.PercentComplete = 40m;
                    run.UpdatedAtUtc = DateTime.UtcNow;
                    await SaveRunAsync(run, cancellationToken);

                    foreach (var symbolName in symbols)
                    {
                        var symbolId = symbolMap[symbolName];
                        foreach (var timeframe in indicatorTimeframes)
                        {
                            run.CurrentSymbol = symbolName;
                            run.CurrentTimeframe = timeframe;
                            run.Message = $"Recalculating indicators for {symbolName} {timeframe}.";
                            run.UpdatedAtUtc = DateTime.UtcNow;
                            await SaveRunAsync(run, cancellationToken);

                            var recalc = await _indicatorCalculationService.RecalculateAsync(
                                new RecalculateIndicatorsRequest
                                {
                                    SymbolId = symbolId,
                                    Timeframe = timeframe,
                                    FromUtc = run.WarmupFromUtc,
                                    ToUtc = run.BenchmarkToUtc
                                },
                                cancellationToken);

                            if (!recalc.Succeeded || recalc.Data is null)
                            {
                                throw new InvalidOperationException(
                                    recalc.ErrorMessage
                                    ?? $"Failed to recalculate indicators for {symbolName} {timeframe}.");
                            }

                            indicators.Add(new StrategyBenchmarkIndicatorSummaryDto
                            {
                                Symbol = symbolName,
                                Timeframe = timeframe,
                                CandlesProcessed = recalc.Data.CandlesProcessed,
                                SnapshotsInserted = recalc.Data.SnapshotsInserted,
                                SnapshotsUpdated = recalc.Data.SnapshotsUpdated,
                                MissingSnapshots = Math.Max(0, recalc.Data.CandlesProcessed - recalc.Data.SnapshotsInserted - recalc.Data.SnapshotsUpdated)
                            });
                        }
                    }
                }
            }

            config.Preparation = new StrategyBenchmarkPreparationDto
            {
                Imports = imports,
                DataQuality = quality,
                Indicators = indicators
            };
            run.ConfigJson = StrategyBenchmarkMapper.SerializeConfig(config);
            }

            run.DataPreparationPercent = 100m;
            run.Status = StrategyBenchmarkStatus.RunningBacktests;
            run.CurrentStage = "RunningBacktests";
            run.Message = "Running strategy backtests.";
            run.PercentComplete = 50m;
            run.LastHeartbeatAtUtc = DateTime.UtcNow;
            run.UpdatedAtUtc = DateTime.UtcNow;
            await SaveRunAsync(run, cancellationToken);

            await EnsureRunItemsAsync(
                run,
                config.ExecutionPlan,
                strategyMap,
                symbolMap,
                symbols,
                timeframes,
                strategyIds,
                cancellationToken);
            var runItems = (await _runItemRepository.GetByBenchmarkRunIdAsync(run.Id, cancellationToken)).ToList();
            run.TotalRuns = runItems.Count;
            await RefreshRunCountsAsync(run, runItems, cancellationToken);

            var continueOnFailure = _benchmarkSettings.ContinueOnRunFailure && !request.StopOnFirstFailure;
            var timeoutMinutes = Math.Max(_benchmarkSettings.MaxBacktestRunMinutes, 1);
            var existingResults = (await _resultRepository.GetByBenchmarkRunIdAsync(run.Id, cancellationToken)).ToList();

            foreach (var item in runItems.Where(item =>
                         item.Status is StrategyBenchmarkRunItemStatus.Pending
                             or StrategyBenchmarkRunItemStatus.Failed
                             or StrategyBenchmarkRunItemStatus.Running))
            {
                // Reload cancellation flag.
                run = await _runRepository.GetByIdAsync(run.Id, cancellationToken) ?? run;
                if (run.CancellationRequested)
                {
                    foreach (var pending in runItems.Where(candidate =>
                                 candidate.Status is StrategyBenchmarkRunItemStatus.Pending
                                     or StrategyBenchmarkRunItemStatus.Running))
                    {
                        pending.Status = StrategyBenchmarkRunItemStatus.Cancelled;
                        pending.UpdatedAtUtc = DateTime.UtcNow;
                        pending.CompletedAtUtc = DateTime.UtcNow;
                        pending.ErrorMessage = "Cancelled by user.";
                        await _runItemRepository.UpdateAsync(pending, cancellationToken);
                    }

                    await _runItemRepository.SaveChangesAsync(cancellationToken);
                    run.Status = StrategyBenchmarkStatus.Cancelled;
                    run.CurrentStage = "Cancelled";
                    run.Message = "Benchmark cancelled.";
                    run.CompletedAtUtc = DateTime.UtcNow;
                    run.UpdatedAtUtc = DateTime.UtcNow;
                    await RefreshRunCountsAsync(run, await _runItemRepository.GetByBenchmarkRunIdAsync(run.Id, cancellationToken), cancellationToken);
                    await SaveRunAsync(run, cancellationToken);
                    return;
                }

                var startedAtUtc = DateTime.UtcNow;
                item.Status = StrategyBenchmarkRunItemStatus.Running;
                item.StartedAtUtc = startedAtUtc;
                item.LastHeartbeatAtUtc = startedAtUtc;
                item.ErrorMessage = null;
                item.UpdatedAtUtc = startedAtUtc;
                await _runItemRepository.UpdateAsync(item, cancellationToken);
                await _runItemRepository.SaveChangesAsync(cancellationToken);

                run.CurrentStrategy = item.StrategyName;
                run.CurrentSymbol = item.Symbol;
                run.CurrentTimeframe = item.Timeframe;
                run.Message = $"Backtesting {item.StrategyCode} on {item.Symbol} {item.Timeframe}.";
                run.LastHeartbeatAtUtc = startedAtUtc;
                run.UpdatedAtUtc = startedAtUtc;
                await RefreshRunCountsAsync(run, await _runItemRepository.GetByBenchmarkRunIdAsync(run.Id, cancellationToken), cancellationToken);
                await SaveRunAsync(run, cancellationToken);

                _logger.LogInformation(
                    "Starting benchmark run item. BenchmarkRunId={BenchmarkRunId}, RunItemId={RunItemId}, StrategyCode={StrategyCode}, StrategyName={StrategyName}, Symbol={Symbol}, Timeframe={Timeframe}, CandleCount={CandleCount}, IndicatorSnapshotCount={IndicatorSnapshotCount}, FromUtc={FromUtc}, ToUtc={ToUtc}",
                    run.Id,
                    item.Id,
                    item.StrategyCode,
                    item.StrategyName,
                    item.Symbol,
                    item.Timeframe,
                    item.CandleCount,
                    0,
                    run.BenchmarkFromUtc,
                    run.BenchmarkToUtc);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes));

                try
                {
                    _progressStore.Clear(item.Id);
                    await using var backtestScope = _serviceScopeFactory.CreateAsyncScope();
                    var scopedBacktestRunner = backtestScope.ServiceProvider.GetRequiredService<IBacktestRunner>();
                    var backtestTask = scopedBacktestRunner.RunAsync(new RunBacktestRequest
                    {
                        Name = $"Benchmark {run.Id}: {item.StrategyCode} {item.Symbol} {item.Timeframe}",
                        ExchangeId = run.ExchangeId,
                        SymbolIds = [item.SymbolId],
                        Timeframes = [item.Timeframe],
                        FromUtc = run.BenchmarkFromUtc,
                        ToUtc = run.BenchmarkToUtc,
                        InitialBalance = run.InitialBalance,
                        RiskProfileId = run.RiskProfileId,
                        StrategyIds = [item.StrategyId],
                        ExecutionMode = run.ExecutionMode.ToString(),
                        MakerFeeRate = run.MakerFeeRate,
                        TakerFeeRate = run.TakerFeeRate,
                        OrderExpiryCandles = run.OrderExpiryCandles,
                        UseAiScoring = run.UseAiScoring,
                        MinConfidenceScore = run.MinConfidenceScore,
                        EvaluationMode = request.EvaluationMode,
                        EnableShadowTradeAnalysis = request.EnableShadowTradeAnalysis,
                        SameCandleExitPolicy = request.SameCandleExitPolicy,
                        RunAnyway = true,
                        BenchmarkRunId = run.Id,
                        BenchmarkRunItemId = item.Id,
                        BenchmarkStrategyCode = item.StrategyCode,
                        BenchmarkSymbol = item.Symbol,
                        BenchmarkTimeframe = item.Timeframe,
                        RequestedByUserId = run.CreatedByUserId
                    }, timeoutCts.Token);

                    var heartbeatInterval = TimeSpan.FromSeconds(Math.Max(_benchmarkSettings.HeartbeatSeconds, 1));
                    while (!backtestTask.IsCompleted)
                    {
                        var delayTask = Task.Delay(heartbeatInterval, cancellationToken);
                        await Task.WhenAny(backtestTask, delayTask);
                        if (backtestTask.IsCompleted)
                        {
                            break;
                        }

                        var now = DateTime.UtcNow;
                        var elapsedSeconds = (int)Math.Max(0, (now - startedAtUtc).TotalSeconds);
                        var progressSnapshot = _progressStore.Get(item.Id);
                        item.LastHeartbeatAtUtc = now;
                        item.DurationSeconds = elapsedSeconds;
                        item.LastProcessedCandleTimeUtc = progressSnapshot?.CurrentCandleTimeUtc;
                        item.LastProcessedCandleIndex = progressSnapshot?.CandleIndex;
                        item.TotalCandles = progressSnapshot?.TotalCandles;
                        item.CandleCount = progressSnapshot?.TotalCandles ?? item.CandleCount;
                        item.UpdatedAtUtc = now;
                        await _runItemRepository.UpdateAsync(item, cancellationToken);
                        await _runItemRepository.SaveChangesAsync(cancellationToken);

                        run.LastHeartbeatAtUtc = now;
                        run.UpdatedAtUtc = now;
                        await SaveRunAsync(run, cancellationToken);

                        _logger.LogInformation(
                            "Benchmark run item heartbeat. BenchmarkRunId={BenchmarkRunId}, RunItemId={RunItemId}, StrategyCode={StrategyCode}, Symbol={Symbol}, Timeframe={Timeframe}, CurrentCandleTimeUtc={CurrentCandleTimeUtc}, CandleIndex={CandleIndex}, TotalCandles={TotalCandles}, ElapsedSeconds={ElapsedSeconds}, SignalsGenerated={SignalsGenerated}, TradesGenerated={TradesGenerated}",
                            run.Id,
                            item.Id,
                            item.StrategyCode,
                            item.Symbol,
                            item.Timeframe,
                            progressSnapshot?.CurrentCandleTimeUtc,
                            progressSnapshot?.CandleIndex,
                            progressSnapshot?.TotalCandles,
                            elapsedSeconds,
                            progressSnapshot?.SignalsGenerated ?? 0,
                            progressSnapshot?.TradesGenerated ?? 0);
                    }

                    var backtestResult = await backtestTask;

                    var completedAtUtc = DateTime.UtcNow;
                    var durationSeconds = (int)Math.Max(0, (completedAtUtc - startedAtUtc).TotalSeconds);
                    var completedSnapshot = _progressStore.Get(item.Id);

                    if (!backtestResult.Succeeded || backtestResult.Data is null)
                    {
                        var error = backtestResult.ErrorMessage ?? "Backtest failed.";
                        await MarkItemFailedAsync(item, error, durationSeconds, completedSnapshot, cancellationToken);
                        _logger.LogWarning(
                            "Failed benchmark run item. BenchmarkRunId={BenchmarkRunId}, RunItemId={RunItemId}, StrategyCode={StrategyCode}, Symbol={Symbol}, Timeframe={Timeframe}, DurationSeconds={DurationSeconds}, Error={Error}",
                            run.Id,
                            item.Id,
                            item.StrategyCode,
                            item.Symbol,
                            item.Timeframe,
                            durationSeconds,
                            error);

                        if (!continueOnFailure)
                        {
                            throw new InvalidOperationException(error);
                        }
                    }
                    else
                    {
                        var summary = backtestResult.Data.Summary;
                        var metrics = new StrategyBenchmarkMetrics
                        {
                            NetPnlPercent = summary.NetPnlPercent,
                            MaxDrawdownPercent = summary.MaxDrawdownPercent,
                            ProfitFactor = summary.ProfitFactor,
                            WinRatePercent = summary.WinRatePercent,
                            TotalTrades = summary.TotalTrades,
                            RejectedSignals = summary.RejectedSignals,
                            MissedOrders = summary.MissedOrders
                        };
                        var grade = _gradeService.Grade(metrics);
                        var result = BuildResult(run, item, backtestResult.Data.BacktestRunId, summary, grade);
                        await _resultRepository.AddAsync(result, cancellationToken);
                        await _resultRepository.SaveChangesAsync(cancellationToken);

                        item.Status = StrategyBenchmarkRunItemStatus.Completed;
                        item.BacktestRunId = backtestResult.Data.BacktestRunId;
                        item.CompletedAtUtc = completedAtUtc;
                        item.LastHeartbeatAtUtc = completedAtUtc;
                        item.DurationSeconds = durationSeconds;
                        item.LastProcessedCandleTimeUtc = completedSnapshot?.CurrentCandleTimeUtc;
                        item.LastProcessedCandleIndex = completedSnapshot?.CandleIndex;
                        item.TotalCandles = completedSnapshot?.TotalCandles;
                        item.CandleCount = completedSnapshot?.TotalCandles ?? item.CandleCount;
                        item.UpdatedAtUtc = completedAtUtc;
                        item.ResultJson = StrategyBenchmarkMapper.SerializeList(new[] { grade.Grade, grade.Score.ToString() });
                        await _runItemRepository.UpdateAsync(item, cancellationToken);
                        await _runItemRepository.SaveChangesAsync(cancellationToken);

                        _logger.LogInformation(
                            "Completed benchmark run item. BenchmarkRunId={BenchmarkRunId}, RunItemId={RunItemId}, StrategyCode={StrategyCode}, Symbol={Symbol}, Timeframe={Timeframe}, DurationSeconds={DurationSeconds}, TotalSignals={TotalSignals}, TotalTrades={TotalTrades}, NetPnl={NetPnl}, FinalBalance={FinalBalance}, ResultStatus={ResultStatus}, BacktestRunId={BacktestRunId}",
                            run.Id,
                            item.Id,
                            item.StrategyCode,
                            item.Symbol,
                            item.Timeframe,
                            durationSeconds,
                            summary.TotalTrades + summary.RejectedSignals,
                            summary.TotalTrades,
                            summary.NetPnl,
                            summary.FinalBalance,
                            item.Status.ToString(),
                            backtestResult.Data.BacktestRunId);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    var durationSeconds = (int)Math.Max(0, (DateTime.UtcNow - startedAtUtc).TotalSeconds);
                    var error = $"Backtest run timed out after {timeoutMinutes} minutes.";
                    await MarkItemFailedAsync(item, error, durationSeconds, _progressStore.Get(item.Id), cancellationToken);
                    _logger.LogWarning(
                        "Failed benchmark run item. BenchmarkRunId={BenchmarkRunId}, RunItemId={RunItemId}, StrategyCode={StrategyCode}, Symbol={Symbol}, Timeframe={Timeframe}, DurationSeconds={DurationSeconds}, Error={Error}",
                        run.Id,
                        item.Id,
                        item.StrategyCode,
                        item.Symbol,
                        item.Timeframe,
                        durationSeconds,
                        error);

                    if (!continueOnFailure)
                    {
                        throw new InvalidOperationException(error);
                    }
                }
                catch (Exception ex) when (continueOnFailure)
                {
                    var durationSeconds = (int)Math.Max(0, (DateTime.UtcNow - startedAtUtc).TotalSeconds);
                    await MarkItemFailedAsync(item, ex.Message, durationSeconds, _progressStore.Get(item.Id), cancellationToken);
                    _logger.LogWarning(
                        ex,
                        "Failed benchmark run item. BenchmarkRunId={BenchmarkRunId}, RunItemId={RunItemId}, StrategyCode={StrategyCode}, Symbol={Symbol}, Timeframe={Timeframe}, DurationSeconds={DurationSeconds}",
                        run.Id,
                        item.Id,
                        item.StrategyCode,
                        item.Symbol,
                        item.Timeframe,
                        durationSeconds);
                }
                finally
                {
                    _progressStore.Clear(item.Id);
                }

                runItems = (await _runItemRepository.GetByBenchmarkRunIdAsync(run.Id, cancellationToken)).ToList();
                await RefreshRunCountsAsync(run, runItems, cancellationToken);
                run.LastHeartbeatAtUtc = DateTime.UtcNow;
                run.UpdatedAtUtc = DateTime.UtcNow;
                await SaveRunAsync(run, cancellationToken);
            }

            runItems = (await _runItemRepository.GetByBenchmarkRunIdAsync(run.Id, cancellationToken)).ToList();
            existingResults = (await _resultRepository.GetByBenchmarkRunIdAsync(run.Id, cancellationToken)).ToList();
            await RegradeResultsAsync(existingResults, cancellationToken);

            run.Status = StrategyBenchmarkStatus.GeneratingReport;
            run.CurrentStage = "GeneratingReport";
            run.Message = "Generating benchmark report.";
            run.PercentComplete = 95m;
            run.BacktestPercent = 100m;
            run.UpdatedAtUtc = DateTime.UtcNow;
            await SaveRunAsync(run, cancellationToken);

            var failedCount = runItems.Count(item => item.Status == StrategyBenchmarkRunItemStatus.Failed);
            var completedCount = runItems.Count(item => item.Status == StrategyBenchmarkRunItemStatus.Completed);
            run.Status = failedCount > 0 && completedCount > 0
                ? StrategyBenchmarkStatus.CompletedWithWarnings
                : failedCount > 0 && completedCount == 0
                    ? StrategyBenchmarkStatus.Failed
                    : StrategyBenchmarkStatus.Completed;
            run.CurrentStage = run.Status.ToString();
            run.Message = failedCount > 0
                ? $"Benchmark finished with {failedCount} failed run(s)."
                : "Benchmark completed.";
            run.PercentComplete = 100m;
            run.CompletedAtUtc = DateTime.UtcNow;
            run.CurrentStrategy = null;
            run.CurrentSymbol = null;
            run.CurrentTimeframe = null;
            run.LastHeartbeatAtUtc = DateTime.UtcNow;
            run.UpdatedAtUtc = DateTime.UtcNow;
            await RefreshRunCountsAsync(run, runItems, cancellationToken);
            await SaveRunAsync(run, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Strategy benchmark run {BenchmarkRunId} failed.", benchmarkRunId);
            run.Status = StrategyBenchmarkStatus.Failed;
            run.CurrentStage = "Failed";
            run.ErrorMessage = ex.Message;
            run.Message = "Benchmark failed.";
            run.CompletedAtUtc = DateTime.UtcNow;
            run.UpdatedAtUtc = DateTime.UtcNow;
            await SaveRunAsync(run, cancellationToken);
        }
    }

    private async Task SaveRunAsync(StrategyBenchmarkRun run, CancellationToken cancellationToken)
    {
        await _runRepository.UpdateAsync(run, cancellationToken);
        await _runRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureRunItemsAsync(
        StrategyBenchmarkRun run,
        IReadOnlyList<StrategyBenchmarkExecutionPlanState> executionPlan,
        IReadOnlyDictionary<long, Strategy> strategyMap,
        IReadOnlyDictionary<string, long> symbolMap,
        IReadOnlyList<string> symbols,
        IReadOnlyList<string> timeframes,
        IReadOnlyList<long> strategyIds,
        CancellationToken cancellationToken)
    {
        var existing = await _runItemRepository.GetByBenchmarkRunIdAsync(run.Id, cancellationToken);
        if (existing.Count > 0)
        {
            // Reset interrupted Running items so resume can continue.
            foreach (var item in existing.Where(item => item.Status == StrategyBenchmarkRunItemStatus.Running))
            {
                item.Status = StrategyBenchmarkRunItemStatus.Pending;
                item.ErrorMessage = "Reset after interrupted run.";
                item.UpdatedAtUtc = DateTime.UtcNow;
                await _runItemRepository.UpdateAsync(item, cancellationToken);
            }

            await _runItemRepository.SaveChangesAsync(cancellationToken);
            return;
        }

        var now = DateTime.UtcNow;
        var items = new List<StrategyBenchmarkRunItem>();
        var executionByStrategy = executionPlan
            .Where(item => item.ExecutionTimeframes.Count > 0)
            .ToDictionary(item => item.StrategyId, item => item.ExecutionTimeframes, comparer: EqualityComparer<long>.Default);

        foreach (var strategyId in strategyIds)
        {
            if (!strategyMap.TryGetValue(strategyId, out var strategy))
            {
                continue;
            }

            var strategyExecutionTimeframes = executionByStrategy.TryGetValue(strategyId, out var configuredTimeframes)
                ? configuredTimeframes
                : timeframes;

            foreach (var symbolName in symbols)
            {
                if (!symbolMap.TryGetValue(symbolName, out var symbolId))
                {
                    continue;
                }

                foreach (var timeframe in strategyExecutionTimeframes)
                {
                    items.Add(new StrategyBenchmarkRunItem
                    {
                        BenchmarkRunId = run.Id,
                        StrategyId = strategy.Id,
                        StrategyCode = strategy.Code.ToCode(),
                        StrategyName = strategy.Name,
                        SymbolId = symbolId,
                        Symbol = symbolName,
                        Timeframe = timeframe,
                        Status = StrategyBenchmarkRunItemStatus.Pending,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    });
                }
            }
        }

        if (items.Count == 0)
        {
            throw new InvalidOperationException("No benchmark run items were generated from the selected strategies/symbols/timeframes.");
        }

        await _runItemRepository.AddRangeAsync(items, cancellationToken);
        await _runItemRepository.SaveChangesAsync(cancellationToken);
        run.TotalRuns = items.Count;
    }

    private async Task RefreshRunCountsAsync(
        StrategyBenchmarkRun run,
        IReadOnlyList<StrategyBenchmarkRunItem> items,
        CancellationToken cancellationToken)
    {
        var completed = items.Count(item => item.Status == StrategyBenchmarkRunItemStatus.Completed);
        var total = Math.Max(items.Count, 1);
        run.CompletedRuns = completed;
        run.TotalRuns = items.Count;
        run.BacktestPercent = Math.Round(completed * 100m / total, 2);
        run.PercentComplete = Math.Round(
            (run.DataPreparationPercent * 0.5m) + (run.BacktestPercent * 0.5m),
            2);
        await Task.CompletedTask;
    }

    private async Task MarkItemFailedAsync(
        StrategyBenchmarkRunItem item,
        string error,
        int durationSeconds,
        BacktestProgressSnapshot? progressSnapshot,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        item.Status = StrategyBenchmarkRunItemStatus.Failed;
        item.ErrorMessage = error;
        item.CompletedAtUtc = now;
        item.LastHeartbeatAtUtc = now;
        item.DurationSeconds = durationSeconds;
        item.LastProcessedCandleTimeUtc = progressSnapshot?.CurrentCandleTimeUtc;
        item.LastProcessedCandleIndex = progressSnapshot?.CandleIndex;
        item.TotalCandles = progressSnapshot?.TotalCandles;
        item.CandleCount = progressSnapshot?.TotalCandles ?? item.CandleCount;
        item.UpdatedAtUtc = now;
        await _runItemRepository.UpdateAsync(item, cancellationToken);
        await _runItemRepository.SaveChangesAsync(cancellationToken);
    }

    private static StrategyBenchmarkResult BuildResult(
        StrategyBenchmarkRun run,
        StrategyBenchmarkRunItem item,
        long backtestRunId,
        BacktestSummaryDto summary,
        StrategyBenchmarkGradeDto grade) => new()
    {
        BenchmarkRunId = run.Id,
        StrategyId = item.StrategyId,
        StrategyCode = item.StrategyCode,
        StrategyName = item.StrategyName,
        SymbolId = item.SymbolId,
        Symbol = item.Symbol,
        Timeframe = item.Timeframe,
        BacktestRunId = backtestRunId,
        InitialBalance = summary.InitialBalance,
        FinalBalance = summary.FinalBalance,
        NetPnl = summary.NetPnl,
        NetPnlPercent = summary.NetPnlPercent,
        ProfitFactor = summary.ProfitFactor,
        MaxDrawdownPercent = summary.MaxDrawdownPercent,
        TotalTrades = summary.TotalTrades,
        WinningTrades = summary.WinningTrades,
        LosingTrades = summary.LosingTrades,
        WinRatePercent = summary.WinRatePercent,
        AverageWin = summary.AverageWin,
        AverageLoss = summary.AverageLoss,
        LargestLoss = summary.LargestLoss,
        AverageRewardRisk = summary.AverageRewardRisk,
        TotalFees = summary.TotalFees,
        TotalSignals = summary.TotalSignals,
        EntrySignals = summary.TotalSignals,
        NoTradeSignals = Math.Max(0, summary.TotalSignals - summary.RejectedSignals - summary.ApprovedSignals),
        ApprovedSignals = summary.ApprovedSignals,
        RejectedSignals = summary.RejectedSignals,
        MissedOrders = summary.MissedOrders,
        FilledOrders = summary.FilledOrders,
        Grade = grade.Grade,
        Score = grade.Score,
        StrengthsJson = StrategyBenchmarkMapper.SerializeList(grade.Strengths),
        WeaknessesJson = StrategyBenchmarkMapper.SerializeList(grade.Weaknesses),
        WarningsJson = StrategyBenchmarkMapper.SerializeList(grade.Warnings),
        CreatedAtUtc = DateTime.UtcNow
    };

    private async Task RegradeResultsAsync(
        IReadOnlyList<StrategyBenchmarkResult> results,
        CancellationToken cancellationToken)
    {
        // Results are graded at write-time; sibling re-grade can be added later if results become mutable.
        await Task.CompletedTask;
    }
}
