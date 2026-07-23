using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.ValidationLab;

public sealed class TradeRiskBasisResult
{
    public ValidationRiskBasisType RiskBasisType { get; init; }
    public ValidationRiskBasisValidationStatus Status { get; init; }
    public decimal Quantity { get; init; }
    public decimal ContractMultiplier { get; init; } = 1m;
    public decimal? EntryPrice { get; init; }
    public decimal? StopPrice { get; init; }
    public decimal? DerivedRiskAmount { get; init; }
    public decimal? PersistedRiskAmount { get; init; }
    public decimal? NormalizedGrossPnl { get; init; }
    public decimal? NormalizedNetPnl { get; init; }
    public decimal? GrossRMultiple { get; init; }
    public decimal? NetRMultiple { get; init; }
    public string? Warning { get; init; }
}

public sealed class SegmentRiskBasisAudit
{
    public ValidationLayerType LayerType { get; init; }
    public int IncludedTradeCount { get; init; }
    public int ExcludedTradeCount { get; init; }
    public int ValidTradeCount { get; init; }
    public ValidationMetricApplicability NetExpectancyApplicability { get; init; }
    public IReadOnlyList<string> ExclusionReasons { get; init; } = [];
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}

public interface IValidationRiskBasisService
{
    TradeRiskBasisResult ComputeTradeBasis(StrategyResearchCandidate candidate, ValidationLayerType layer);

    PathTradeRiskBasisResult ComputePathTradeBasis(ValidationPathTradeMetricInput trade);

    SegmentRiskBasisAudit AuditSegment(
        IReadOnlyList<StrategyResearchCandidate> candidates,
        ValidationLayerType layer,
        ExpiredTradeMetricPolicy expiredPolicy = ExpiredTradeMetricPolicy.IncludeAsClosedAtExpiry);

    SegmentRiskBasisAudit AuditPathTrades(IReadOnlyList<ValidationPathTradeMetricInput> trades);
}

public sealed class ValidationRiskBasisService : IValidationRiskBasisService
{
    public const string Version = "ValidationRiskBasis/v1";
    private const decimal ReconciliationTolerance = 0.0001m;
    private const decimal PersistedRiskToleranceRatio = 0.05m;

    public TradeRiskBasisResult ComputeTradeBasis(StrategyResearchCandidate candidate, ValidationLayerType layer)
    {
        var entry = candidate.ProposedEntryPrice;
        var stop = candidate.StopLoss;
        if (entry <= 0m)
        {
            return Unavailable(layer, ValidationRiskBasisValidationStatus.MissingEntry);
        }

        if (stop <= 0m)
        {
            return Unavailable(layer, ValidationRiskBasisValidationStatus.MissingStop);
        }

        var stopDistance = Math.Abs(entry - stop);
        if (stopDistance <= 0m)
        {
            return Unavailable(layer, ValidationRiskBasisValidationStatus.NonPositiveRisk);
        }

        decimal quantity;
        ValidationRiskBasisType basisType;
        switch (layer)
        {
            case ValidationLayerType.RawStrategy:
            case ValidationLayerType.ConfidenceQualified:
                quantity = 1m;
                basisType = ValidationRiskBasisType.NormalizedOneUnit;
                break;
            default:
                quantity = candidate.ProposedPositionSize ?? 0m;
                basisType = ValidationRiskBasisType.PositionSized;
                if (quantity <= 0m)
                {
                    return Unavailable(layer, ValidationRiskBasisValidationStatus.MissingQuantity);
                }

                break;
        }

        var derivedRisk = stopDistance * quantity * 1m;
        if (derivedRisk <= 0m)
        {
            return Unavailable(layer, ValidationRiskBasisValidationStatus.NonPositiveRisk);
        }

        var grossPnl = candidate.RawGrossPnl ?? candidate.RawNetPnl ?? 0m;
        var netPnl = candidate.RawNetPnl ?? candidate.RawGrossPnl ?? 0m;
        var costs = grossPnl - netPnl;

        decimal normalizedGrossPnl;
        decimal normalizedNetPnl;
        if (layer is ValidationLayerType.RawStrategy or ValidationLayerType.ConfidenceQualified)
        {
            var actualQty = candidate.ProposedPositionSize is > 0m ? candidate.ProposedPositionSize.Value : 1m;
            normalizedGrossPnl = grossPnl / actualQty;
            normalizedNetPnl = netPnl / actualQty;
        }
        else
        {
            normalizedGrossPnl = grossPnl;
            normalizedNetPnl = netPnl;
        }

        decimal? grossR = normalizedGrossPnl / derivedRisk;
        decimal? netR = normalizedNetPnl / derivedRisk;

        if (candidate.RawRMultiple is decimal rawR && layer == ValidationLayerType.RawStrategy)
        {
            grossR = rawR;
            if (grossPnl != 0m)
            {
                netR = rawR * (netPnl / grossPnl);
            }
        }

        var status = ValidationRiskBasisValidationStatus.Valid;
        string? warning = null;
        if (candidate.RiskAmount is decimal persisted && persisted > 0m)
        {
            var ratio = Math.Abs(persisted - derivedRisk) / derivedRisk;
            if (ratio > PersistedRiskToleranceRatio)
            {
                status = ValidationRiskBasisValidationStatus.PersistedRiskMismatch;
                warning = $"RiskAmountUnitMismatch: persisted {persisted:F8} vs derived {derivedRisk:F8}.";
            }
        }

        if (grossR is decimal gR && Math.Abs(gR * derivedRisk - normalizedGrossPnl) > ReconciliationTolerance)
        {
            status = ValidationRiskBasisValidationStatus.GrossRReconciliationFailed;
        }

        if (netR is decimal nR && Math.Abs(nR * derivedRisk - normalizedNetPnl) > ReconciliationTolerance)
        {
            status = ValidationRiskBasisValidationStatus.NetRReconciliationFailed;
        }

        return new TradeRiskBasisResult
        {
            RiskBasisType = basisType,
            Status = status,
            Quantity = quantity,
            EntryPrice = entry,
            StopPrice = stop,
            DerivedRiskAmount = derivedRisk,
            PersistedRiskAmount = candidate.RiskAmount,
            NormalizedGrossPnl = normalizedGrossPnl,
            NormalizedNetPnl = normalizedNetPnl,
            GrossRMultiple = grossR,
            NetRMultiple = netR,
            Warning = warning
        };
    }

    public SegmentRiskBasisAudit AuditSegment(
        IReadOnlyList<StrategyResearchCandidate> candidates,
        ValidationLayerType layer,
        ExpiredTradeMetricPolicy expiredPolicy = ExpiredTradeMetricPolicy.IncludeAsClosedAtExpiry)
    {
        var closed = ValidationMetricsContract.SelectClosedTradesPublic(candidates, expiredPolicy);
        var exclusionReasons = new List<string>();
        var diagnostics = new List<string>();
        var valid = 0;

        foreach (var c in closed)
        {
            var basis = ComputeTradeBasis(c, layer);
            if (basis.Status is ValidationRiskBasisValidationStatus.Valid
                && basis.NetRMultiple is decimal
                && basis.GrossRMultiple is decimal)
            {
                valid++;
            }
            else
            {
                exclusionReasons.Add($"{c.SetupFingerprint}:{basis.Status}");
                if (basis.Warning is not null)
                {
                    diagnostics.Add(basis.Warning);
                }
            }
        }

        var applicability = closed.Count == 0
            ? ValidationMetricApplicability.InsufficientSample
            : valid == closed.Count
                ? ValidationMetricApplicability.Evaluated
                : valid == 0
                    ? ValidationMetricApplicability.InvalidRiskBasis
                    : ValidationMetricApplicability.NotEvaluated;

        return new SegmentRiskBasisAudit
        {
            LayerType = layer,
            IncludedTradeCount = valid,
            ExcludedTradeCount = closed.Count - valid,
            ValidTradeCount = valid,
            NetExpectancyApplicability = applicability,
            ExclusionReasons = exclusionReasons,
            Diagnostics = diagnostics
        };
    }

    public PathTradeRiskBasisResult ComputePathTradeBasis(ValidationPathTradeMetricInput trade)
    {
        if (trade.MetricInclusionStatus != ValidationPathMetricInclusionStatus.Included)
        {
            return PathUnavailable(trade, ValidationRiskBasisValidationStatus.NotAvailable, trade.MetricExclusionReason);
        }

        if (trade.EntryPrice <= 0m)
        {
            return PathUnavailable(trade, ValidationRiskBasisValidationStatus.MissingEntry);
        }

        if (trade.StopPriceAtEntry <= 0m)
        {
            return PathUnavailable(trade, ValidationRiskBasisValidationStatus.MissingStop);
        }

        if (trade.Quantity <= 0m)
        {
            return PathUnavailable(trade, ValidationRiskBasisValidationStatus.MissingQuantity);
        }

        if (trade.ContractMultiplier <= 0m)
        {
            return PathUnavailable(trade, ValidationRiskBasisValidationStatus.NonPositiveRisk);
        }

        if (!string.Equals(trade.PnlCurrency, trade.RiskCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return PathUnavailable(trade, ValidationRiskBasisValidationStatus.CurrencyMismatch);
        }

        var stopDistance = Math.Abs(trade.EntryPrice - trade.StopPriceAtEntry);
        var derivedRisk = stopDistance * trade.Quantity * trade.ContractMultiplier;
        if (derivedRisk <= 0m)
        {
            return PathUnavailable(trade, ValidationRiskBasisValidationStatus.NonPositiveRisk);
        }

        var basisType = trade.ValidationLayer is ValidationLayerType.RawStrategy or ValidationLayerType.ConfidenceQualified
            ? ValidationRiskBasisType.NormalizedOneUnit
            : ValidationRiskBasisType.ShadowPortfolioPosition;

        var grossR = trade.GrossPnl / derivedRisk;
        var netR = trade.NetPnl / derivedRisk;
        var status = ValidationRiskBasisValidationStatus.Valid;
        string? warning = null;

        if (trade.RiskAmountAtEntry is decimal persisted && persisted > 0m)
        {
            var ratio = Math.Abs(persisted - derivedRisk) / derivedRisk;
            if (ratio > PersistedRiskToleranceRatio)
            {
                status = ValidationRiskBasisValidationStatus.PersistedRiskMismatch;
                warning = $"RiskAmountUnitMismatch: persisted {persisted:F8} vs derived {derivedRisk:F8}.";
            }
        }

        if (Math.Abs(grossR * derivedRisk - trade.GrossPnl) > ReconciliationTolerance)
        {
            status = ValidationRiskBasisValidationStatus.GrossRReconciliationFailed;
        }

        if (Math.Abs(netR * derivedRisk - trade.NetPnl) > ReconciliationTolerance)
        {
            status = ValidationRiskBasisValidationStatus.NetRReconciliationFailed;
        }

        // Material risk mismatch blocks canonical Net R.
        decimal? netROut = status is ValidationRiskBasisValidationStatus.Valid
            or ValidationRiskBasisValidationStatus.PersistedRiskMismatch
            ? netR
            : null;
        decimal? grossROut = status is ValidationRiskBasisValidationStatus.Valid
            or ValidationRiskBasisValidationStatus.PersistedRiskMismatch
            or ValidationRiskBasisValidationStatus.NetRReconciliationFailed
            ? grossR
            : null;

        if (status == ValidationRiskBasisValidationStatus.PersistedRiskMismatch)
        {
            netROut = null;
        }

        return new PathTradeRiskBasisResult
        {
            RiskBasisType = basisType,
            Status = status == ValidationRiskBasisValidationStatus.PersistedRiskMismatch
                ? ValidationRiskBasisValidationStatus.PersistedRiskMismatch
                : status,
            Quantity = trade.Quantity,
            ContractMultiplier = trade.ContractMultiplier,
            EntryPrice = trade.EntryPrice,
            StopPrice = trade.StopPriceAtEntry,
            DerivedRiskAmount = derivedRisk,
            PersistedRiskAmount = trade.RiskAmountAtEntry,
            GrossPnl = trade.GrossPnl,
            NetPnl = trade.NetPnl,
            TotalTransactionCosts = trade.TotalTransactionCosts,
            GrossRMultiple = grossROut,
            NetRMultiple = netROut,
            Warning = warning
        };
    }

    public SegmentRiskBasisAudit AuditPathTrades(IReadOnlyList<ValidationPathTradeMetricInput> trades)
    {
        var exclusionReasons = new List<string>();
        var diagnostics = new List<string>();
        var valid = 0;
        var considered = 0;

        foreach (var trade in trades)
        {
            considered++;
            var basis = ComputePathTradeBasis(trade);
            if (basis.Status == ValidationRiskBasisValidationStatus.Valid
                && basis.NetRMultiple is decimal
                && basis.GrossRMultiple is decimal)
            {
                valid++;
            }
            else
            {
                exclusionReasons.Add($"{trade.CandidateFingerprint}:{basis.Status}");
                if (basis.Warning is not null)
                {
                    diagnostics.Add(basis.Warning);
                }
            }
        }

        var applicability = considered == 0
            ? ValidationMetricApplicability.InsufficientSample
            : valid == considered
                ? ValidationMetricApplicability.Evaluated
                : valid == 0
                    ? ValidationMetricApplicability.InvalidRiskBasis
                    : ValidationMetricApplicability.NotEvaluated;

        return new SegmentRiskBasisAudit
        {
            LayerType = trades.FirstOrDefault()?.ValidationLayer ?? ValidationLayerType.RawStrategy,
            IncludedTradeCount = valid,
            ExcludedTradeCount = considered - valid,
            ValidTradeCount = valid,
            NetExpectancyApplicability = applicability,
            ExclusionReasons = exclusionReasons,
            Diagnostics = diagnostics
        };
    }

    private static PathTradeRiskBasisResult PathUnavailable(
        ValidationPathTradeMetricInput trade,
        ValidationRiskBasisValidationStatus status,
        string? warning = null) =>
        new()
        {
            RiskBasisType = trade.ValidationLayer is ValidationLayerType.RawStrategy or ValidationLayerType.ConfidenceQualified
                ? ValidationRiskBasisType.NormalizedOneUnit
                : ValidationRiskBasisType.NotAvailable,
            Status = status,
            Quantity = trade.Quantity,
            GrossPnl = trade.GrossPnl,
            NetPnl = trade.NetPnl,
            TotalTransactionCosts = trade.TotalTransactionCosts,
            Warning = warning
        };

    private static TradeRiskBasisResult Unavailable(
        ValidationLayerType layer,
        ValidationRiskBasisValidationStatus status) =>
        new()
        {
            RiskBasisType = layer == ValidationLayerType.RawStrategy
                ? ValidationRiskBasisType.NormalizedOneUnit
                : ValidationRiskBasisType.NotAvailable,
            Status = status
        };
}
