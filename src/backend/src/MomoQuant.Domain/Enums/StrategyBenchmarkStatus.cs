namespace MomoQuant.Domain.Enums;

public enum StrategyBenchmarkStatus
{
    Pending = 1,
    ImportingData = 2,
    CheckingDataQuality = 3,
    RecalculatingIndicators = 4,
    RunningBacktests = 5,
    GeneratingReport = 6,
    Completed = 7,
    CompletedWithWarnings = 8,
    Failed = 9,
    Stalled = 10,
    Cancelled = 11
}
