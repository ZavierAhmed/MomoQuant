using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Immutable, path-specific trade input for ValidationMetrics/v1.3(.1).
/// PnL, quantity, and risk must come from the same portfolio path / sizing basis.
/// </summary>
public sealed class ValidationPathTradeMetricInput
{
    public const string SourceVersionV11 = "ValidationPathTradeMetricInput/v1.1";

    public long ValidationExperimentId { get; init; }
    public ValidationSegmentType ValidationSegment { get; init; }
    public ValidationLayerType ValidationLayer { get; init; }
    public string PortfolioPath { get; init; } = string.Empty;
    public long CandidateId { get; init; }
    public string CandidateFingerprint { get; init; } = string.Empty;
    public long? PortfolioAssessmentId { get; init; }
    public long? LedgerEntryId { get; init; }
    public TradeDirection Direction { get; init; }
    public DateTime? EntryTimeUtc { get; init; }
    public DateTime? ExitTimeUtc { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal? ExitPrice { get; init; }
    public decimal StopPriceAtEntry { get; init; }
    public decimal Quantity { get; init; }
    public decimal ContractMultiplier { get; init; } = 1m;
    public decimal? RiskAmountAtEntry { get; init; }
    public decimal GrossPnl { get; init; }
    public decimal EntryCosts { get; init; }
    public decimal ExitCosts { get; init; }
    public decimal OtherTransactionCosts { get; init; }
    public decimal TotalTransactionCosts { get; init; }
    public decimal NetPnl { get; init; }
    public string PnlCurrency { get; init; } = "USDT";
    public string RiskCurrency { get; init; } = "USDT";
    public string Outcome { get; init; } = string.Empty;
    public ValidationPathMetricInclusionStatus MetricInclusionStatus { get; init; } =
        ValidationPathMetricInclusionStatus.Included;

    /// <summary>Why the trade was excluded. Must be null when Included.</summary>
    public string? MetricExclusionReason { get; init; }

    /// <summary>Non-blocking warning codes for included trades.</summary>
    public IReadOnlyList<string> MetricWarningCodes { get; init; } = Array.Empty<string>();

    public ValidationMetricReconciliationStatus ReconciliationStatus { get; init; } =
        ValidationMetricReconciliationStatus.NotApplicable;

    public decimal? ReconciliationGrossDelta { get; init; }
    public decimal? ReconciliationNetDelta { get; init; }

    public string SourceVersion { get; init; } = SourceVersionV11;
}

public enum ValidationPathMetricInclusionStatus
{
    Included = 1,
    Excluded = 2,
    NotAvailable = 3
}

public enum ValidationMetricReconciliationStatus
{
    NotApplicable = 0,
    Matched = 1,
    Mismatched = 2,
    SourceUnavailable = 3
}

public static class ValidationPathMetricWarningCodes
{
    public const string CandidateRawPnlReconciliationMismatch = "CandidateRawPnlReconciliationMismatch";
}

public sealed class PathTradeRiskBasisResult
{
    public ValidationRiskBasisType RiskBasisType { get; init; }
    public ValidationRiskBasisValidationStatus Status { get; init; }
    public decimal Quantity { get; init; }
    public decimal ContractMultiplier { get; init; } = 1m;
    public decimal? EntryPrice { get; init; }
    public decimal? StopPrice { get; init; }
    public decimal? DerivedRiskAmount { get; init; }
    public decimal? PersistedRiskAmount { get; init; }
    public decimal GrossPnl { get; init; }
    public decimal NetPnl { get; init; }
    public decimal TotalTransactionCosts { get; init; }
    public decimal? GrossRMultiple { get; init; }
    public decimal? NetRMultiple { get; init; }
    public string? Warning { get; init; }
}
