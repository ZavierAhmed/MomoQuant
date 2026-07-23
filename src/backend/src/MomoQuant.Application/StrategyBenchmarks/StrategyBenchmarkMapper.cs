using System.Text.Json;
using MomoQuant.Application.StrategyBenchmarks.Dtos;
using MomoQuant.Domain.Benchmarks;
using MomoQuant.Domain.Enums;
using StrategyBenchmarkRunItem = MomoQuant.Domain.Benchmarks.StrategyBenchmarkRunItem;

namespace MomoQuant.Application.StrategyBenchmarks;

public static class StrategyBenchmarkMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static StrategyBenchmarkRunDto MapRun(StrategyBenchmarkRun run)
    {
        var config = ParseConfig(run.ConfigJson);
        return new StrategyBenchmarkRunDto
        {
            Id = run.Id,
            Name = run.Name,
            Status = run.Status.ToString(),
            ExchangeId = run.ExchangeId,
            Symbols = ParseStringList(run.SymbolsJson),
            Timeframes = ParseStringList(run.TimeframesJson),
            StrategyIds = ParseLongList(run.StrategyIdsJson),
            BenchmarkFromUtc = run.BenchmarkFromUtc,
            BenchmarkToUtc = run.BenchmarkToUtc,
            WarmupFromUtc = run.WarmupFromUtc,
            WarmupToUtc = run.WarmupToUtc,
            InitialBalance = run.InitialBalance,
            RiskProfileId = run.RiskProfileId,
            ExecutionMode = run.ExecutionMode.ToString(),
            UseAiScoring = run.UseAiScoring,
            MinConfidenceScore = run.MinConfidenceScore,
            EvaluationMode = config.Request?.EvaluationMode ?? nameof(BenchmarkEvaluationMode.RawStrategyResearch),
            IncludeDisabledStrategies = run.IncludeDisabledStrategies,
            TotalRuns = run.TotalRuns,
            CompletedRuns = run.CompletedRuns,
            PercentComplete = run.PercentComplete,
            CurrentStage = run.CurrentStage,
            CurrentSymbol = run.CurrentSymbol,
            CurrentTimeframe = run.CurrentTimeframe,
            CurrentStrategy = run.CurrentStrategy,
            Message = run.Message,
            StartedAtUtc = run.StartedAtUtc,
            CompletedAtUtc = run.CompletedAtUtc,
            ErrorMessage = run.ErrorMessage,
            CreatedAtUtc = run.CreatedAtUtc
        };
    }

    public static StrategyBenchmarkProgressDto MapProgress(
        StrategyBenchmarkRun run,
        IReadOnlyList<StrategyBenchmarkRunItem>? items = null)
    {
        var config = ParseConfig(run.ConfigJson);
        var importProgress = config.ImportProgress;
        var failedRuns = items?.Count(item => item.Status == StrategyBenchmarkRunItemStatus.Failed) ?? 0;
        var pendingRuns = items?.Count(item =>
            item.Status is StrategyBenchmarkRunItemStatus.Pending or StrategyBenchmarkRunItemStatus.Running) ?? 0;
        return new StrategyBenchmarkProgressDto
        {
            BenchmarkRunId = run.Id,
            Status = run.Status.ToString(),
            CurrentStage = run.CurrentStage,
            PercentComplete = run.PercentComplete,
            DataPreparationPercent = run.DataPreparationPercent,
            BacktestPercent = run.BacktestPercent,
            CurrentSymbol = run.CurrentSymbol,
            CurrentTimeframe = run.CurrentTimeframe,
            CurrentStrategy = run.CurrentStrategy,
            CompletedRuns = run.CompletedRuns,
            TotalRuns = run.TotalRuns,
            FailedRuns = failedRuns,
            PendingRuns = pendingRuns,
            Message = run.Message,
            LastHeartbeatAtUtc = run.LastHeartbeatAtUtc,
            CurrentChunkFromUtc = importProgress?.CurrentChunkFromUtc,
            CurrentChunkToUtc = importProgress?.CurrentChunkToUtc,
            CompletedImportChunks = importProgress?.CompletedChunks ?? 0,
            TotalImportChunks = importProgress?.TotalChunks ?? 0,
            InsertedCandles = importProgress?.InsertedCandles ?? 0,
            SkippedDuplicateCandles = importProgress?.SkippedDuplicateCandles ?? 0
        };
    }

    public static StrategyBenchmarkRunItemDto MapRunItem(StrategyBenchmarkRunItem item) => new()
    {
        Id = item.Id,
        BenchmarkRunId = item.BenchmarkRunId,
        StrategyId = item.StrategyId,
        StrategyCode = item.StrategyCode,
        StrategyName = item.StrategyName,
        SymbolId = item.SymbolId,
        Symbol = item.Symbol,
        Timeframe = item.Timeframe,
        Status = item.Status.ToString(),
        BacktestRunId = item.BacktestRunId,
        StartedAtUtc = item.StartedAtUtc,
        CompletedAtUtc = item.CompletedAtUtc,
        LastHeartbeatAtUtc = item.LastHeartbeatAtUtc,
        DurationSeconds = item.DurationSeconds,
        CandleCount = item.CandleCount,
        LastProcessedCandleTimeUtc = item.LastProcessedCandleTimeUtc,
        LastProcessedCandleIndex = item.LastProcessedCandleIndex,
        TotalCandles = item.TotalCandles,
        ErrorMessage = item.ErrorMessage
    };

    public static StrategyBenchmarkConfigState ParseConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return new StrategyBenchmarkConfigState();
        }

        try
        {
            return JsonSerializer.Deserialize<StrategyBenchmarkConfigState>(configJson, JsonOptions)
                   ?? new StrategyBenchmarkConfigState();
        }
        catch
        {
            return new StrategyBenchmarkConfigState();
        }
    }

    public static string SerializeConfig(StrategyBenchmarkConfigState config) =>
        JsonSerializer.Serialize(config, JsonOptions);

    public static string SerializeList<T>(IEnumerable<T> values) =>
        JsonSerializer.Serialize(values, JsonOptions);

    public static IReadOnlyList<string> ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static IReadOnlyList<long> ParseLongList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<long>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static IReadOnlyList<string> ParseStringListField(string? json) => ParseStringList(json);
}

public sealed class StrategyBenchmarkConfigState
{
    public CreateStrategyBenchmarkRequest? Request { get; set; }
    public StrategyBenchmarkPreparationDto? Preparation { get; set; }
    public StrategyBenchmarkImportProgressState? ImportProgress { get; set; }
    public List<long> ResolvedSymbolIds { get; set; } = [];
    public List<long> ResolvedStrategyIds { get; set; } = [];
    public List<string> RequiredDataTimeframes { get; set; } = [];
    public List<string> RequiredIndicatorTimeframes { get; set; } = [];
    public List<StrategyBenchmarkExecutionPlanState> ExecutionPlan { get; set; } = [];
}

public sealed class StrategyBenchmarkExecutionPlanState
{
    public long StrategyId { get; set; }
    public string StrategyCode { get; set; } = string.Empty;
    public string StrategyName { get; set; } = string.Empty;
    public string PreferredExecutionTimeframe { get; set; } = "5m";
    public List<string> ExecutionTimeframes { get; set; } = [];
    public List<string> RequiredDataTimeframes { get; set; } = [];
    public List<string> RequiredIndicatorTimeframes { get; set; } = [];
    public List<string> AnchorTimeframes { get; set; } = [];
}

public sealed class StrategyBenchmarkImportProgressState
{
    public DateTime? CurrentChunkFromUtc { get; set; }
    public DateTime? CurrentChunkToUtc { get; set; }
    public int CompletedChunks { get; set; }
    public int TotalChunks { get; set; }
    public int InsertedCandles { get; set; }
    public int SkippedDuplicateCandles { get; set; }
}
