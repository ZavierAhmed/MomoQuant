namespace MomoQuant.Application.StrategyLab;

public enum ExecutionPurpose
{
    GeneralResearch = 0,
    ValidationTraining = 1,
    ValidationHoldout = 2,
    Backtest = 3,
    Replay = 4,
    HistoricalPaper = 5
}

/// <summary>
/// Explicit Strategy Laboratory execution contract. Validation training must supply a scoped candle source.
/// </summary>
public sealed class StrategyLabExecutionContext
{
    public ExecutionPurpose ExecutionPurpose { get; init; }

    public long? ValidationExperimentId { get; init; }

    public long? ValidationTrialId { get; init; }

    public int? ValidationTrialNumber { get; init; }

    public DateTime? TrainingBoundaryUtc { get; init; }

    public IStrategyLabCandleDataSource? CandleDataSource { get; init; }

    public bool AllowCoverageImport { get; init; }

    public string CallerComponent { get; init; } = "StrategyLab";

    public string CorrelationId { get; init; } = string.Empty;

    public static StrategyLabExecutionContext ForGeneralResearch(
        string? callerComponent = null,
        string? correlationId = null) =>
        new()
        {
            ExecutionPurpose = ExecutionPurpose.GeneralResearch,
            AllowCoverageImport = true,
            CallerComponent = string.IsNullOrWhiteSpace(callerComponent) ? "StrategyLab" : callerComponent,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId)
                ? Guid.NewGuid().ToString("N")
                : correlationId
        };

    public static StrategyLabExecutionContext ForValidationTraining(
        long validationExperimentId,
        long? validationTrialId,
        int validationTrialNumber,
        DateTime trainingBoundaryUtc,
        IStrategyLabCandleDataSource candleDataSource,
        string callerComponent,
        string? correlationId = null) =>
        new()
        {
            ExecutionPurpose = ExecutionPurpose.ValidationTraining,
            ValidationExperimentId = validationExperimentId,
            ValidationTrialId = validationTrialId,
            ValidationTrialNumber = validationTrialNumber,
            TrainingBoundaryUtc = DateTime.SpecifyKind(trainingBoundaryUtc, DateTimeKind.Utc),
            CandleDataSource = candleDataSource,
            AllowCoverageImport = false,
            CallerComponent = callerComponent,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId)
                ? Guid.NewGuid().ToString("N")
                : correlationId
        };
}
