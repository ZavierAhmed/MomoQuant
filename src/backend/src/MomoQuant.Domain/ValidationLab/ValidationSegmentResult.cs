using MomoQuant.Domain.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Domain.ValidationLab;

public class ValidationSegmentResult : Entity
{
    public long ValidationExperimentId { get; set; }
    public ValidationSegmentType SegmentType { get; set; }
    public ValidationLayerType LayerType { get; set; }
    public long? StrategyLabRunId { get; set; }
    public string MetricsJson { get; set; } = "{}";
    public int CandleCount { get; set; }
    public int CandidateCount { get; set; }
    public int ClosedTradeCount { get; set; }
    public decimal? NetExpectancyR { get; set; }
    public decimal? ProfitFactor { get; set; }
    public decimal? NetPnl { get; set; }
    public decimal? NetReturnPercent { get; set; }
    public decimal? MaximumDrawdownPercent { get; set; }
    public decimal? TransactionCosts { get; set; }
    public int BoundaryCensoredCount { get; set; }
    public string ResultFingerprint { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    // Milestone 22.1 metric contract fields
    public string ResultCalculationVersion { get; set; } = "ValidationMetrics/v1.2";
    public decimal? GrossExpectancyR { get; set; }
    public decimal? GrossProfitFactor { get; set; }
    public decimal? NetProfitFactor { get; set; }
    public decimal? GrossAverageR { get; set; }
    public decimal? NetAverageR { get; set; }
    public decimal? GrossPnl { get; set; }

    // Milestone 22.2 population + gross/net profit columns
    public int PersistedCandidateRowCount { get; set; }
    public int MetricIncludedCandidateCount { get; set; }
    public int MetricExcludedCandidateCount { get; set; }
    public int CrossSegmentOverlapCount { get; set; }
    public decimal? GrossProfit { get; set; }
    public decimal? GrossLoss { get; set; }
    public decimal? NetProfit { get; set; }
    public decimal? NetLoss { get; set; }
}