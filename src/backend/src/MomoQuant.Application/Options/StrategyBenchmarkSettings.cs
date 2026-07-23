namespace MomoQuant.Application.Options;

public sealed class StrategyBenchmarkSettings
{
    public const string SectionName = "StrategyBenchmark";

    /// <summary>
    /// Preferred Binance public import chunk size in days for benchmark warmup/benchmark ranges.
    /// Must be &gt; 0 and &lt;= MarketData:Binance:MaxDaysPerImport.
    /// </summary>
    public int BinanceImportChunkDays { get; set; } = 7;

    public int MaxBacktestRunMinutes { get; set; } = 10;

    public bool ContinueOnRunFailure { get; set; } = true;

    public int HeartbeatSeconds { get; set; } = 5;

    public int StalledAfterMinutes { get; set; } = 15;

    public bool SkipImportIfCoverageAlreadyGood { get; set; } = true;

    public decimal SkipImportCoveragePercent { get; set; } = 99m;

    public List<string> AnchorTimeframes { get; set; } = ["4h"];
}
