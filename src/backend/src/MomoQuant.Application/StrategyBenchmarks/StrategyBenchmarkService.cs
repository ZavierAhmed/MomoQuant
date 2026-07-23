using System.Text.Json;
using Microsoft.Extensions.Options;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Options;
using MomoQuant.Application.PaperTrading;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Application.StrategyBenchmarks.Dtos;
using MomoQuant.Domain.Benchmarks;
using MomoQuant.Domain.Enums;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.StrategyBenchmarks;

public interface IStrategyBenchmarkService
{
    Task<ServiceResult<StrategyBenchmarkRunDto>> CreateAsync(
        CreateStrategyBenchmarkRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<StrategyBenchmarkRunDto>> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResult<StrategyBenchmarkRunDto>>> GetPagedAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<StrategyBenchmarkProgressDto>> GetProgressAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<StrategyBenchmarkReportDto>> GetReportAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<StrategyBenchmarkRunItemDto>>> GetRunItemsAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<StrategyBenchmarkDiagnosticsDto>> GetDiagnosticsAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<StrategyBenchmarkPreflightDto>> PreflightAsync(
        StrategyBenchmarkPreflightRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<StrategyBenchmarkRunDto>> CancelAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<StrategyBenchmarkRunDto>> ResumeAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<StrategyBenchmarkRunDto>> RestartAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<StrategyBenchmarkRunDto>> RetryFailedAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<StrategyBenchmarkRunDto>> MarkStalledFailedAsync(long id, CancellationToken cancellationToken = default);
}

public sealed class StrategyBenchmarkService : IStrategyBenchmarkService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IStrategyBenchmarkRunRepository _runRepository;
    private readonly IStrategyBenchmarkRunItemRepository _runItemRepository;
    private readonly IStrategyBenchmarkResultRepository _resultRepository;
    private readonly IExchangeRepository _exchangeRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IStrategyRepository _strategyRepository;
    private readonly IRiskProfileRepository _riskProfileRepository;
    private readonly IStrategyBenchmarkQueue _queue;
    private readonly IStrategyBenchmarkReportService _reportService;
    private readonly IStrategyDataRequirementService _requirementService;
    private readonly IMarketDataService _marketDataService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;
    private readonly StrategyBenchmarkSettings _benchmarkSettings;

    public StrategyBenchmarkService(
        IStrategyBenchmarkRunRepository runRepository,
        IStrategyBenchmarkRunItemRepository runItemRepository,
        IStrategyBenchmarkResultRepository resultRepository,
        IExchangeRepository exchangeRepository,
        ISymbolRepository symbolRepository,
        IStrategyRepository strategyRepository,
        IRiskProfileRepository riskProfileRepository,
        IStrategyBenchmarkQueue queue,
        IStrategyBenchmarkReportService reportService,
        IStrategyDataRequirementService requirementService,
        IMarketDataService marketDataService,
        ICurrentUserService currentUserService,
        IAuditService auditService,
        IOptions<StrategyBenchmarkSettings> benchmarkSettings)
    {
        _runRepository = runRepository;
        _runItemRepository = runItemRepository;
        _resultRepository = resultRepository;
        _exchangeRepository = exchangeRepository;
        _symbolRepository = symbolRepository;
        _strategyRepository = strategyRepository;
        _riskProfileRepository = riskProfileRepository;
        _queue = queue;
        _reportService = reportService;
        _requirementService = requirementService;
        _marketDataService = marketDataService;
        _currentUserService = currentUserService;
        _auditService = auditService;
        _benchmarkSettings = benchmarkSettings.Value;
    }

    public async Task<ServiceResult<StrategyBenchmarkRunDto>> CreateAsync(
        CreateStrategyBenchmarkRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateAsync(request, cancellationToken);
        if (!validation.Succeeded || validation.Data is null)
        {
            return ServiceResult<StrategyBenchmarkRunDto>.Fail(validation.ErrorMessage!, validation.ErrorField);
        }

        var resolved = validation.Data;
        var now = DateTime.UtcNow;
        var totalRuns = resolved.EstimatedRuns;

        var config = new StrategyBenchmarkConfigState
        {
            Request = request,
            ResolvedSymbolIds = resolved.SymbolIds.ToList(),
            ResolvedStrategyIds = resolved.StrategyIds.ToList(),
            RequiredDataTimeframes = resolved.Timeframes.ToList(),
            RequiredIndicatorTimeframes = resolved.ExecutionPlan
                .SelectMany(item => item.RequiredIndicatorTimeframes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            ExecutionPlan = resolved.ExecutionPlan.ToList()
        };

        var run = new StrategyBenchmarkRun
        {
            Name = request.Name.Trim(),
            Status = StrategyBenchmarkStatus.Pending,
            ExchangeId = resolved.ExchangeId,
            SymbolsJson = StrategyBenchmarkMapper.SerializeList(resolved.Symbols),
            TimeframesJson = StrategyBenchmarkMapper.SerializeList(resolved.Timeframes),
            StrategyIdsJson = StrategyBenchmarkMapper.SerializeList(resolved.StrategyIds),
            BenchmarkFromUtc = resolved.BenchmarkFromUtc,
            BenchmarkToUtc = resolved.BenchmarkToUtc,
            WarmupFromUtc = resolved.WarmupFromUtc,
            WarmupToUtc = resolved.WarmupToUtc,
            InitialBalance = request.InitialBalance,
            RiskProfileId = request.RiskProfileId,
            ExecutionMode = resolved.ExecutionMode,
            MakerFeeRate = request.MakerFeeRate,
            TakerFeeRate = request.TakerFeeRate,
            OrderExpiryCandles = request.OrderExpiryCandles,
            UseAiScoring = request.UseAiScoring,
            MinConfidenceScore = request.MinConfidenceScore,
            IncludeDisabledStrategies = request.IncludeDisabledStrategies,
            ConfigJson = StrategyBenchmarkMapper.SerializeConfig(config),
            CurrentStage = "Queued",
            PercentComplete = 0m,
            CompletedRuns = 0,
            TotalRuns = totalRuns,
            Message = "Benchmark queued.",
            CreatedByUserId = _currentUserService.UserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await _runRepository.AddAsync(run, cancellationToken);
        await _runRepository.SaveChangesAsync(cancellationToken);
        _queue.Enqueue(run.Id);

        await _auditService.LogAsync(
            "STRATEGY_BENCHMARK_CREATED",
            nameof(StrategyBenchmarkRun),
            run.Id,
            _currentUserService.UserId,
            newValueJson: JsonSerializer.Serialize(new { run.Name, run.TotalRuns }, JsonOptions),
            cancellationToken: cancellationToken);

        return ServiceResult<StrategyBenchmarkRunDto>.Ok(StrategyBenchmarkMapper.MapRun(run));
    }

    public async Task<ServiceResult<StrategyBenchmarkRunDto>> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(id, cancellationToken);
        return run is null
            ? ServiceResult<StrategyBenchmarkRunDto>.Fail("Strategy benchmark run was not found.")
            : ServiceResult<StrategyBenchmarkRunDto>.Ok(StrategyBenchmarkMapper.MapRun(run));
    }

    public async Task<ServiceResult<PagedResult<StrategyBenchmarkRunDto>>> GetPagedAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _runRepository.GetPagedAsync(request, cancellationToken);
        return ServiceResult<PagedResult<StrategyBenchmarkRunDto>>.Ok(new PagedResult<StrategyBenchmarkRunDto>
        {
            Items = items.Select(StrategyBenchmarkMapper.MapRun).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        });
    }

    public async Task<ServiceResult<StrategyBenchmarkProgressDto>> GetProgressAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(id, cancellationToken);
        if (run is null)
        {
            return ServiceResult<StrategyBenchmarkProgressDto>.Fail("Strategy benchmark run was not found.");
        }

        var items = await _runItemRepository.GetByBenchmarkRunIdAsync(id, cancellationToken);
        return ServiceResult<StrategyBenchmarkProgressDto>.Ok(StrategyBenchmarkMapper.MapProgress(run, items));
    }

    public Task<ServiceResult<StrategyBenchmarkReportDto>> GetReportAsync(
        long id,
        CancellationToken cancellationToken = default) =>
        _reportService.GetReportAsync(id, cancellationToken);

    public async Task<ServiceResult<IReadOnlyList<StrategyBenchmarkRunItemDto>>> GetRunItemsAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(id, cancellationToken);
        if (run is null)
        {
            return ServiceResult<IReadOnlyList<StrategyBenchmarkRunItemDto>>.Fail("Strategy benchmark run was not found.");
        }

        var items = await _runItemRepository.GetByBenchmarkRunIdAsync(id, cancellationToken);
        return ServiceResult<IReadOnlyList<StrategyBenchmarkRunItemDto>>.Ok(
            items.Select(StrategyBenchmarkMapper.MapRunItem).ToList());
    }

    public async Task<ServiceResult<StrategyBenchmarkDiagnosticsDto>> GetDiagnosticsAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(id, cancellationToken);
        if (run is null)
        {
            return ServiceResult<StrategyBenchmarkDiagnosticsDto>.Fail("Strategy benchmark run was not found.");
        }

        var items = await _runItemRepository.GetByBenchmarkRunIdAsync(id, cancellationToken);
        var running = items.FirstOrDefault(item => item.Status == StrategyBenchmarkRunItemStatus.Running);
        var warnings = new List<string>();
        if (run.LastHeartbeatAtUtc is DateTime benchmarkHeartbeat)
        {
            var benchmarkIdleSeconds = (int)Math.Max(0, (DateTime.UtcNow - benchmarkHeartbeat).TotalSeconds);
            var staleAfterSeconds = Math.Max(_benchmarkSettings.HeartbeatSeconds * 3, 15);
            if (benchmarkIdleSeconds > staleAfterSeconds)
            {
                warnings.Add($"No benchmark heartbeat for {benchmarkIdleSeconds} seconds.");
            }
        }

        if (running is not null && running.LastHeartbeatAtUtc is DateTime heartbeat)
        {
            var idleSeconds = (int)Math.Max(0, (DateTime.UtcNow - heartbeat).TotalSeconds);
            var staleAfterSeconds = Math.Max(_benchmarkSettings.HeartbeatSeconds * 3, 15);
            if (idleSeconds > staleAfterSeconds)
            {
                warnings.Add($"Running item heartbeat is stale for {idleSeconds} seconds.");
            }
        }

        return ServiceResult<StrategyBenchmarkDiagnosticsDto>.Ok(new StrategyBenchmarkDiagnosticsDto
        {
            BenchmarkRunId = run.Id,
            Status = run.Status.ToString(),
            CurrentStage = run.CurrentStage,
            PercentComplete = run.PercentComplete,
            CompletedRuns = items.Count(item => item.Status == StrategyBenchmarkRunItemStatus.Completed),
            TotalRuns = items.Count,
            FailedRuns = items.Count(item => item.Status == StrategyBenchmarkRunItemStatus.Failed),
            PendingRuns = items.Count(item =>
                item.Status is StrategyBenchmarkRunItemStatus.Pending or StrategyBenchmarkRunItemStatus.Running),
            RunningItem = running is null ? null : StrategyBenchmarkMapper.MapRunItem(running),
            LastError = run.ErrorMessage ?? items.LastOrDefault(item => item.ErrorMessage is not null)?.ErrorMessage,
            RecentRunItems = items.OrderByDescending(item => item.UpdatedAtUtc ?? item.CreatedAtUtc).Take(20)
                .Select(StrategyBenchmarkMapper.MapRunItem)
                .ToList(),
            Warnings = warnings
        });
    }

    public async Task<ServiceResult<StrategyBenchmarkPreflightDto>> PreflightAsync(
        StrategyBenchmarkPreflightRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Symbols.Count == 0)
        {
            return ServiceResult<StrategyBenchmarkPreflightDto>.Fail("At least one symbol is required.", "symbols");
        }

        if (request.StrategyIds.Count == 0)
        {
            return ServiceResult<StrategyBenchmarkPreflightDto>.Fail("At least one strategy is required.", "strategyIds");
        }

        var exchange = await _exchangeRepository.GetByCodeAsync(request.ExchangeCode, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<StrategyBenchmarkPreflightDto>.Fail("Exchange was not found.", "exchangeCode");
        }

        var symbols = new List<(long Id, string Name)>();
        foreach (var symbolName in request.Symbols.Select(item => item.Trim().ToUpperInvariant()).Distinct())
        {
            var symbol = await _symbolRepository.GetByExchangeAndNameAsync(exchange.Id, symbolName, cancellationToken);
            if (symbol is null)
            {
                return ServiceResult<StrategyBenchmarkPreflightDto>.Fail($"Symbol '{symbolName}' was not found.", "symbols");
            }

            symbols.Add((symbol.Id, symbol.SymbolName));
        }

        var strategyMap = (await _strategyRepository.GetAllAsync(cancellationToken))
            .ToDictionary(strategy => strategy.Id, strategy => strategy);

        var strategyNames = new List<string>();
        foreach (var strategyId in request.StrategyIds.Distinct())
        {
            if (!strategyMap.TryGetValue(strategyId, out var strategy))
            {
                return ServiceResult<StrategyBenchmarkPreflightDto>.Fail($"Strategy {strategyId} was not found.", "strategyIds");
            }

            strategyNames.Add($"{strategy.Name} ({strategy.Code.ToCode()})");
        }

        var resolve = await _requirementService.ResolveAsync(new ResolveStrategyRequirementsRequest
        {
            StrategyIds = request.StrategyIds.Distinct().ToList(),
            SymbolIds = symbols.Select(item => item.Id).ToList(),
            BenchmarkFromDate = request.BenchmarkFromDate,
            BenchmarkToDate = request.BenchmarkToDate,
            Mode = "Benchmark",
            ExecutionScope = request.StrategyExecutionScope,
            ManualExecutionTimeframes = request.ManualExecutionTimeframes
        }, cancellationToken);
        if (!resolve.Succeeded || resolve.Data is null)
        {
            return ServiceResult<StrategyBenchmarkPreflightDto>.Fail(resolve.ErrorMessage ?? "Failed to resolve strategy requirements.");
        }

        var resolvedExecutionRuns = resolve.Data.ExecutionPlan
            .Select(item => new StrategyBenchmarkResolvedExecutionRunDto
            {
                StrategyId = item.StrategyId,
                StrategyCode = item.StrategyCode,
                StrategyName = item.StrategyName,
                ExecutionTimeframes = item.ExecutionTimeframes,
                RequiredDataTimeframes = item.RequiredDataTimeframes,
                RequiredIndicatorTimeframes = item.RequiredIndicatorTimeframes
            })
            .ToList();

        var importPlan = resolve.Data.ImportPlan
            .Select(item => new StrategyBenchmarkPreflightTimeframeRequirementDto
            {
                Symbol = item.Symbol ?? "*",
                Timeframe = item.Timeframe,
                Reason = item.Reason,
                IsAnchorData = item.Reason.Contains("anchor", StringComparison.OrdinalIgnoreCase)
            })
            .ToList();

        var indicatorTimeframes = resolve.Data.ExecutionPlan
            .SelectMany(item => item.RequiredIndicatorTimeframes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var requiredIndicatorTimeframes = indicatorTimeframes
            .SelectMany(timeframe => symbols.Select(symbol => new StrategyBenchmarkPreflightTimeframeRequirementDto
            {
                Symbol = symbol.Name,
                Timeframe = timeframe,
                Reason = "Indicator recalculation candidate",
                IsAnchorData = false
            }))
            .ToList();

        var missingData = new List<string>();
        var fromUtc = request.WarmupFromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = request.BenchmarkToDate.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc);
        foreach (var symbol in symbols)
        {
            foreach (var timeframe in resolve.Data.RequiredTimeframes)
            {
                var quality = await _marketDataService.GetDataQualityAsync(
                    exchange.Id,
                    symbol.Id,
                    timeframe,
                    fromUtc,
                    toUtc,
                    cancellationToken);
                if (!quality.Succeeded || quality.Data is null)
                {
                    missingData.Add($"{symbol.Name} {timeframe}: quality check failed.");
                    continue;
                }

                if (quality.Data.CoveragePercent < 95m)
                {
                    missingData.Add($"{symbol.Name} {timeframe}: coverage {quality.Data.CoveragePercent:0.##}%.");
                }
            }
        }

        var estimatedRuns = symbols.Count * resolve.Data.ExecutionPlan.Sum(item => item.ExecutionTimeframes.Count);
        return ServiceResult<StrategyBenchmarkPreflightDto>.Ok(new StrategyBenchmarkPreflightDto
        {
            SelectedSymbols = symbols.Select(item => item.Name).ToList(),
            SelectedStrategies = strategyNames,
            ExecutionTimeframeMode = request.ExecutionTimeframeMode,
            StrategyExecutionScope = request.StrategyExecutionScope,
            ResolvedExecutionRuns = resolvedExecutionRuns,
            RequiredImportTimeframes = importPlan,
            RequiredIndicatorTimeframes = requiredIndicatorTimeframes,
            EstimatedTotalRuns = estimatedRuns,
            EstimatedCandleCount = Math.Max(0, estimatedRuns * 600),
            MissingDataSummary = missingData,
            MissingIndicatorsSummary = [],
            Warnings = resolve.Data.Warnings,
            BlockingIssues = resolve.Data.BlockingIssues
        });
    }

    public async Task<ServiceResult<StrategyBenchmarkRunDto>> CancelAsync(long id, CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(id, cancellationToken);
        if (run is null)
        {
            return ServiceResult<StrategyBenchmarkRunDto>.Fail("Strategy benchmark run was not found.");
        }

        if (run.Status is StrategyBenchmarkStatus.Completed
            or StrategyBenchmarkStatus.CompletedWithWarnings
            or StrategyBenchmarkStatus.Cancelled)
        {
            return ServiceResult<StrategyBenchmarkRunDto>.Fail($"Benchmark cannot be cancelled from status {run.Status}.", "status");
        }

        run.CancellationRequested = true;
        run.Message = "Cancellation requested.";
        run.UpdatedAtUtc = DateTime.UtcNow;
        await _runRepository.UpdateAsync(run, cancellationToken);
        await _runRepository.SaveChangesAsync(cancellationToken);
        return ServiceResult<StrategyBenchmarkRunDto>.Ok(StrategyBenchmarkMapper.MapRun(run));
    }

    public async Task<ServiceResult<StrategyBenchmarkRunDto>> ResumeAsync(long id, CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(id, cancellationToken);
        if (run is null)
        {
            return ServiceResult<StrategyBenchmarkRunDto>.Fail("Strategy benchmark run was not found.");
        }

        if (run.Status is not (StrategyBenchmarkStatus.Failed
            or StrategyBenchmarkStatus.Stalled
            or StrategyBenchmarkStatus.Cancelled
            or StrategyBenchmarkStatus.RunningBacktests))
        {
            return ServiceResult<StrategyBenchmarkRunDto>.Fail($"Benchmark cannot be resumed from status {run.Status}.", "status");
        }

        var items = await _runItemRepository.GetByBenchmarkRunIdAsync(id, cancellationToken);
        foreach (var item in items.Where(item => item.Status == StrategyBenchmarkRunItemStatus.Running))
        {
            item.Status = StrategyBenchmarkRunItemStatus.Pending;
            item.ErrorMessage = "Reset for resume.";
            item.UpdatedAtUtc = DateTime.UtcNow;
            await _runItemRepository.UpdateAsync(item, cancellationToken);
        }

        await _runItemRepository.SaveChangesAsync(cancellationToken);

        run.CancellationRequested = false;
        run.Status = StrategyBenchmarkStatus.Pending;
        run.CurrentStage = "Queued";
        run.Message = "Benchmark resumed.";
        run.ErrorMessage = null;
        run.CompletedAtUtc = null;
        run.UpdatedAtUtc = DateTime.UtcNow;
        await _runRepository.UpdateAsync(run, cancellationToken);
        await _runRepository.SaveChangesAsync(cancellationToken);
        _queue.Enqueue(run.Id);
        return ServiceResult<StrategyBenchmarkRunDto>.Ok(StrategyBenchmarkMapper.MapRun(run));
    }

    public async Task<ServiceResult<StrategyBenchmarkRunDto>> RestartAsync(long id, CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(id, cancellationToken);
        if (run is null)
        {
            return ServiceResult<StrategyBenchmarkRunDto>.Fail("Strategy benchmark run was not found.");
        }

        await _runItemRepository.DeleteByBenchmarkRunIdAsync(id, cancellationToken);
        await _runItemRepository.SaveChangesAsync(cancellationToken);
        await _resultRepository.DeleteByBenchmarkRunIdAsync(id, cancellationToken);
        await _resultRepository.SaveChangesAsync(cancellationToken);

        var config = StrategyBenchmarkMapper.ParseConfig(run.ConfigJson);
        config.Preparation = null;
        config.ImportProgress = null;
        run.ConfigJson = StrategyBenchmarkMapper.SerializeConfig(config);
        run.CancellationRequested = false;
        run.Status = StrategyBenchmarkStatus.Pending;
        run.CurrentStage = "Queued";
        run.Message = "Benchmark restarted.";
        run.ErrorMessage = null;
        run.CompletedRuns = 0;
        run.PercentComplete = 0m;
        run.DataPreparationPercent = 0m;
        run.BacktestPercent = 0m;
        run.StartedAtUtc = null;
        run.CompletedAtUtc = null;
        run.CurrentStrategy = null;
        run.CurrentSymbol = null;
        run.CurrentTimeframe = null;
        run.UpdatedAtUtc = DateTime.UtcNow;
        await _runRepository.UpdateAsync(run, cancellationToken);
        await _runRepository.SaveChangesAsync(cancellationToken);
        _queue.Enqueue(run.Id);
        return ServiceResult<StrategyBenchmarkRunDto>.Ok(StrategyBenchmarkMapper.MapRun(run));
    }

    public async Task<ServiceResult<StrategyBenchmarkRunDto>> RetryFailedAsync(long id, CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(id, cancellationToken);
        if (run is null)
        {
            return ServiceResult<StrategyBenchmarkRunDto>.Fail("Strategy benchmark run was not found.");
        }

        var items = await _runItemRepository.GetByBenchmarkRunIdAsync(id, cancellationToken);
        var failed = items.Where(item => item.Status == StrategyBenchmarkRunItemStatus.Failed).ToList();
        if (failed.Count == 0)
        {
            return ServiceResult<StrategyBenchmarkRunDto>.Fail("No failed run items to retry.", "status");
        }

        foreach (var item in failed)
        {
            item.Status = StrategyBenchmarkRunItemStatus.Pending;
            item.ErrorMessage = null;
            item.StartedAtUtc = null;
            item.CompletedAtUtc = null;
            item.DurationSeconds = null;
            item.UpdatedAtUtc = DateTime.UtcNow;
            await _runItemRepository.UpdateAsync(item, cancellationToken);
        }

        await _runItemRepository.SaveChangesAsync(cancellationToken);

        run.CancellationRequested = false;
        run.Status = StrategyBenchmarkStatus.Pending;
        run.CurrentStage = "Queued";
        run.Message = $"Retrying {failed.Count} failed run item(s).";
        run.ErrorMessage = null;
        run.CompletedAtUtc = null;
        run.UpdatedAtUtc = DateTime.UtcNow;
        await _runRepository.UpdateAsync(run, cancellationToken);
        await _runRepository.SaveChangesAsync(cancellationToken);
        _queue.Enqueue(run.Id);
        return ServiceResult<StrategyBenchmarkRunDto>.Ok(StrategyBenchmarkMapper.MapRun(run));
    }

    public async Task<ServiceResult<StrategyBenchmarkRunDto>> MarkStalledFailedAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        var run = await _runRepository.GetByIdAsync(id, cancellationToken);
        if (run is null)
        {
            return ServiceResult<StrategyBenchmarkRunDto>.Fail("Strategy benchmark run was not found.");
        }

        var items = await _runItemRepository.GetByBenchmarkRunIdAsync(id, cancellationToken);
        foreach (var item in items.Where(item => item.Status == StrategyBenchmarkRunItemStatus.Running))
        {
            item.Status = StrategyBenchmarkRunItemStatus.Failed;
            item.ErrorMessage = "Marked stalled/failed by user.";
            item.CompletedAtUtc = DateTime.UtcNow;
            item.UpdatedAtUtc = DateTime.UtcNow;
            await _runItemRepository.UpdateAsync(item, cancellationToken);
        }

        await _runItemRepository.SaveChangesAsync(cancellationToken);
        run.Status = StrategyBenchmarkStatus.Stalled;
        run.CurrentStage = "Stalled";
        run.Message = "Benchmark marked as stalled/failed.";
        run.ErrorMessage = "Marked stalled/failed by user.";
        run.CancellationRequested = true;
        run.UpdatedAtUtc = DateTime.UtcNow;
        await _runRepository.UpdateAsync(run, cancellationToken);
        await _runRepository.SaveChangesAsync(cancellationToken);
        return ServiceResult<StrategyBenchmarkRunDto>.Ok(StrategyBenchmarkMapper.MapRun(run));
    }

    private async Task<ServiceResult<ResolvedBenchmarkRequest>> ValidateAsync(
        CreateStrategyBenchmarkRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<ResolvedBenchmarkRequest>.Fail("Name is required.", "name");
        }

        if (request.Symbols is null || request.Symbols.Count == 0)
        {
            return ServiceResult<ResolvedBenchmarkRequest>.Fail("At least one symbol is required.", "symbols");
        }

        if (request.StrategyIds is null || request.StrategyIds.Count == 0)
        {
            return ServiceResult<ResolvedBenchmarkRequest>.Fail("At least one strategy is required.", "strategyIds");
        }

        if (request.BenchmarkFromDate > request.BenchmarkToDate)
        {
            return ServiceResult<ResolvedBenchmarkRequest>.Fail("Benchmark from date must be on or before to date.", "benchmarkFromDate");
        }

        if (request.WarmupFromDate > request.BenchmarkFromDate)
        {
            return ServiceResult<ResolvedBenchmarkRequest>.Fail("Warmup from date must be on or before benchmark from date.", "warmupFromDate");
        }

        if (request.InitialBalance <= 0)
        {
            return ServiceResult<ResolvedBenchmarkRequest>.Fail("Initial balance must be greater than zero.", "initialBalance");
        }

        if (!PaperMapper.TryParseExecutionMode(request.ExecutionMode, out var executionMode))
        {
            return ServiceResult<ResolvedBenchmarkRequest>.Fail("Execution mode is invalid.", "executionMode");
        }

        if (!Enum.TryParse<BenchmarkEvaluationMode>(request.EvaluationMode, true, out _))
        {
            return ServiceResult<ResolvedBenchmarkRequest>.Fail("Evaluation mode is invalid.", "evaluationMode");
        }

        var exchange = await _exchangeRepository.GetByCodeAsync(request.ExchangeCode, cancellationToken);
        if (exchange is null)
        {
            return ServiceResult<ResolvedBenchmarkRequest>.Fail("Exchange was not found.", "exchangeCode");
        }

        var riskProfile = await _riskProfileRepository.GetByIdAsync(request.RiskProfileId, cancellationToken);
        if (riskProfile is null)
        {
            return ServiceResult<ResolvedBenchmarkRequest>.Fail("Risk profile was not found.", "riskProfileId");
        }

        var symbolIds = new List<long>();
        var symbols = new List<string>();
        foreach (var symbolName in request.Symbols.Select(symbol => symbol.Trim().ToUpperInvariant()).Distinct())
        {
            var symbol = await _symbolRepository.GetByExchangeAndNameAsync(exchange.Id, symbolName, cancellationToken);
            if (symbol is null)
            {
                return ServiceResult<ResolvedBenchmarkRequest>.Fail($"Symbol '{symbolName}' was not found.", "symbols");
            }

            symbolIds.Add(symbol.Id);
            symbols.Add(symbol.SymbolName);
        }

        var strategies = await _strategyRepository.GetAllAsync(cancellationToken);
        var strategyMap = strategies.ToDictionary(strategy => strategy.Id);
        var selected = new List<Domain.Strategies.Strategy>();
        foreach (var strategyId in request.StrategyIds.Distinct())
        {
            if (!strategyMap.TryGetValue(strategyId, out var strategy))
            {
                return ServiceResult<ResolvedBenchmarkRequest>.Fail($"Strategy {strategyId} was not found.", "strategyIds");
            }

            if (!strategy.IsEnabled && !request.IncludeDisabledStrategies)
            {
                return ServiceResult<ResolvedBenchmarkRequest>.Fail(
                    $"Strategy '{strategy.Name}' is disabled. Enable includeDisabledStrategies to use it.",
                    "strategyIds");
            }

            selected.Add(strategy);
        }

        var strategyIds = selected.Select(strategy => strategy.Id).ToList();
        var executionScope = NormalizeExecutionScope(request.StrategyExecutionScope);
        var timeframeMode = NormalizeExecutionTimeframeMode(request.ExecutionTimeframeMode);
        var manualExecutionTimeframes = request.ManualExecutionTimeframes;
        if (timeframeMode == "AdvancedManualOverride")
        {
            executionScope = "ManualOverride";
        }

        if (executionScope == "ManualOverride" && (manualExecutionTimeframes is null || manualExecutionTimeframes.Count == 0))
        {
            return ServiceResult<ResolvedBenchmarkRequest>.Fail(
                "Manual override requires at least one execution timeframe.",
                "manualExecutionTimeframes");
        }

        if (executionScope != "ManualOverride" && request.Timeframes.Count > 0)
        {
            manualExecutionTimeframes = null;
        }

        if (executionScope == "ManualOverride")
        {
            foreach (var timeframe in manualExecutionTimeframes!)
            {
                if (!TimeframeParser.TryParse(timeframe, out _))
                {
                    return ServiceResult<ResolvedBenchmarkRequest>.Fail($"Timeframe '{timeframe}' is invalid.", "manualExecutionTimeframes");
                }
            }
        }

        var benchmarkFromUtc = request.BenchmarkFromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var benchmarkToUtc = request.BenchmarkToDate.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc);
        var warmupFromUtc = request.WarmupFromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var requirements = await _requirementService.ResolveAsync(new ResolveStrategyRequirementsRequest
        {
            StrategyIds = strategyIds,
            SymbolIds = symbolIds,
            BenchmarkFromDate = request.BenchmarkFromDate,
            BenchmarkToDate = request.BenchmarkToDate,
            Mode = "Benchmark",
            ExecutionScope = executionScope,
            ManualExecutionTimeframes = manualExecutionTimeframes
        }, cancellationToken);
        if (!requirements.Succeeded || requirements.Data is null)
        {
            return ServiceResult<ResolvedBenchmarkRequest>.Fail(
                requirements.ErrorMessage ?? "Failed resolving strategy timeframe requirements.",
                requirements.ErrorField ?? "strategyIds");
        }

        if (requirements.Data.BlockingIssues.Count > 0)
        {
            return ServiceResult<ResolvedBenchmarkRequest>.Fail(
                string.Join(" ", requirements.Data.BlockingIssues),
                "strategyIds");
        }

        var resolvedDataTimeframes = requirements.Data.RequiredTimeframes
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
        var estimatedRuns = symbolIds.Count * requirements.Data.ExecutionPlan.Sum(item => item.ExecutionTimeframes.Count);
        if (estimatedRuns <= 0)
        {
            return ServiceResult<ResolvedBenchmarkRequest>.Fail(
                "No valid execution runs were resolved for selected strategies and scope.",
                "strategyIds");
        }

        return ServiceResult<ResolvedBenchmarkRequest>.Ok(new ResolvedBenchmarkRequest
        {
            ExchangeId = exchange.Id,
            Symbols = symbols,
            SymbolIds = symbolIds,
            Timeframes = resolvedDataTimeframes,
            StrategyIds = strategyIds,
            ExecutionMode = executionMode,
            BenchmarkFromUtc = benchmarkFromUtc,
            BenchmarkToUtc = benchmarkToUtc,
            WarmupFromUtc = warmupFromUtc,
            WarmupToUtc = benchmarkFromUtc.AddTicks(-1),
            ExecutionPlan = requirements.Data.ExecutionPlan
                .Select(item => new StrategyBenchmarkExecutionPlanState
                {
                    StrategyId = item.StrategyId,
                    StrategyCode = item.StrategyCode,
                    StrategyName = item.StrategyName,
                    PreferredExecutionTimeframe = item.PreferredExecutionTimeframe,
                    ExecutionTimeframes = item.ExecutionTimeframes.ToList(),
                    RequiredDataTimeframes = item.RequiredDataTimeframes.ToList(),
                    RequiredIndicatorTimeframes = item.RequiredIndicatorTimeframes.ToList(),
                    AnchorTimeframes = item.AnchorTimeframes.ToList()
                })
                .ToList(),
            EstimatedRuns = estimatedRuns
        });
    }

    private static string NormalizeExecutionTimeframeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "AutoSelectByStrategy";
        }

        return mode.Trim() switch
        {
            "AdvancedManualOverride" => "AdvancedManualOverride",
            _ => "AutoSelectByStrategy"
        };
    }

    private static string NormalizeExecutionScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return "PreferredOnly";
        }

        return scope.Trim() switch
        {
            "AllSupported" => "AllSupported",
            "ManualOverride" => "ManualOverride",
            _ => "PreferredOnly"
        };
    }

    private sealed class ResolvedBenchmarkRequest
    {
        public long ExchangeId { get; init; }
        public required IReadOnlyList<string> Symbols { get; init; }
        public required IReadOnlyList<long> SymbolIds { get; init; }
        public required IReadOnlyList<string> Timeframes { get; init; }
        public required IReadOnlyList<long> StrategyIds { get; init; }
        public required IReadOnlyList<StrategyBenchmarkExecutionPlanState> ExecutionPlan { get; init; }
        public int EstimatedRuns { get; init; }
        public ExecutionMode ExecutionMode { get; init; }
        public DateTime BenchmarkFromUtc { get; init; }
        public DateTime BenchmarkToUtc { get; init; }
        public DateTime WarmupFromUtc { get; init; }
        public DateTime WarmupToUtc { get; init; }
    }
}
