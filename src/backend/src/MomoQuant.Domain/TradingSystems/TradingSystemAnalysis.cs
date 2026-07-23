using MomoQuant.Domain.Common;

namespace MomoQuant.Domain.TradingSystems;

/// <summary>
/// A saved analysis produced by a Trading System (e.g. SK System Analyzer).
/// Trading Systems are analytical frameworks only — they never execute trades,
/// generate orders, run benchmarks, or participate in strategy execution.
/// </summary>
public class TradingSystemAnalysis : Entity
{
    public string SystemCode { get; set; } = "SK_SYSTEM";
    public string SystemName { get; set; } = "SK System Analyzer";

    public long ExchangeId { get; set; }
    public string ExchangeName { get; set; } = string.Empty;
    public long SymbolId { get; set; }
    public string Symbol { get; set; } = string.Empty;

    public string PrimaryTimeframe { get; set; } = string.Empty;
    public string HigherTimeframe { get; set; } = string.Empty;
    public int LookbackCandles { get; set; }
    public string SwingSensitivity { get; set; } = "Balanced";
    public string DirectionMode { get; set; } = "Auto";

    public string Status { get; set; } = "Completed";
    public DateTime AnalysisTimeUtc { get; set; }
    public DateTime? LatestCandleTimeUtc { get; set; }
    public decimal CurrentPrice { get; set; }

    public string MarketBias { get; set; } = "Unknown";
    public string ConfidenceLabel { get; set; } = "Low";

    public string SummaryText { get; set; } = string.Empty;
    public string BullishScenarioText { get; set; } = string.Empty;
    public string BearishScenarioText { get; set; } = string.Empty;
    public string InvalidationsText { get; set; } = string.Empty;

    public string WarningsJson { get; set; } = "[]";
    public string ChartDataJson { get; set; } = "{}";
    public string SequenceCandidatesJson { get; set; } = "[]";
    public string FibonacciZonesJson { get; set; } = "[]";
    public string KeyLevelsJson { get; set; } = "[]";
    public string AiSummaryJson { get; set; } = "{}";
    public string RawDiagnosticsJson { get; set; } = "{}";

    public long? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
