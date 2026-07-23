using System.Text.Json;
using MomoQuant.Application.Validation.Dtos;

namespace MomoQuant.Application.Validation;

public static class ValidationRunLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly string LogDirectory = @"C:\momo_quants_logs";
    private static readonly string LogFileName = "validation-runs.log";

    public static void Log(ValidationRunLogEntry entry)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var line = JsonSerializer.Serialize(entry, JsonOptions);
            File.AppendAllText(Path.Combine(LogDirectory, LogFileName), line + Environment.NewLine);
        }
        catch
        {
            // Best-effort file logging; do not fail validation.
        }
    }
}

public sealed class ValidationRunLogEntry
{
    public Guid ValidationRunId { get; init; } = Guid.NewGuid();
    public required string StrategyCode { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public DateTime TrainingStartUtc { get; init; }
    public DateTime TrainingEndUtc { get; init; }
    public DateTime ValidationStartUtc { get; init; }
    public DateTime ValidationEndUtc { get; init; }
    public int WarmupCandles { get; init; }
    public int CandlesLoadedTraining { get; init; }
    public int CandlesLoadedValidation { get; init; }
    public int EvaluationsTraining { get; init; }
    public int EvaluationsValidation { get; init; }
    public int TradesTraining { get; init; }
    public int TradesValidation { get; init; }
    public string CoverageStatus { get; init; } = "Unknown";
    public bool ImportedCandles { get; init; }
    public bool EngineEvaluationBug { get; init; }
    public string? Error { get; init; }
    public DateTime LoggedAtUtc { get; init; } = DateTime.UtcNow;
}
