namespace MomoQuant.Domain.Enums;

public enum ExportJobStatus
{
    Pending = 1,
    Running = 2,
    Completed = 3,
    Failed = 4
}

public enum ExportScope
{
    AnalyzerRun = 1,
    AnalyzerHistory = 2,
    SkAnalysisRun = 3,
    SkLivePaperSession = 4,
    StrategyBacktestRun = 5,
    StrategyBenchmarkRun = 6,
    HistoricalPaperSession = 7,
    LivePaperSession = 8,
    StrategyDiagnostics = 9,
    BenchmarkComparison = 10,
    StrategyLabRun = 11,
    ValidationExperiment = 12
}

public enum ExportFormat
{
    Json = 1,
    Pdf = 2,
    Zip = 3,
    Csv = 4
}

public enum ExportDetailLevel
{
    Summary = 1,
    Standard = 2,
    Full = 3
}
