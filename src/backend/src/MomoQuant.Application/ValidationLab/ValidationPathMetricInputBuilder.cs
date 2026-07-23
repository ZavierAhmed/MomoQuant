using System.Text.Json;
using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.ValidationLab;

public sealed class ValidationPathMetricCostModel
{
    public decimal EntryFeeRate { get; init; } = 0.0004m;
    public decimal ExitFeeRate { get; init; } = 0.0004m;
    public decimal SlippagePercent { get; init; }
    public decimal ContractMultiplier { get; init; } = 1m;
    public string SourceVersion { get; init; } = "ValidationPathMetricCostModel/v1";
}

public interface IValidationPathMetricInputBuilder
{
    IReadOnlyList<ValidationPathTradeMetricInput> Build(
        long experimentId,
        ValidationSegmentType segment,
        ValidationLayerType layer,
        IReadOnlyList<StrategyResearchCandidate> candidates,
        ShadowPortfolioSummaryDto? riskOnlyShadow,
        ShadowPortfolioSummaryDto? fullPipelineShadow,
        ValidationPathMetricCostModel? costModel = null);
}

/// <summary>
/// Builds path-specific metric inputs. RiskOnly/FullPipeline use ledger + assessment data;
/// RawStrategy/ConfidenceQualified independently reconstruct normalized one-unit economics
/// from prices, stop, fees, and slippage (not by dividing existing candidate PnL).
/// </summary>
public sealed class ValidationPathMetricInputBuilder : IValidationPathMetricInputBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Absolute relative tolerance for reconciliation warnings against candidate raw fields.</summary>
    public const decimal CandidateReconciliationTolerance = 0.0001m;

    public IReadOnlyList<ValidationPathTradeMetricInput> Build(
        long experimentId,
        ValidationSegmentType segment,
        ValidationLayerType layer,
        IReadOnlyList<StrategyResearchCandidate> candidates,
        ShadowPortfolioSummaryDto? riskOnlyShadow,
        ShadowPortfolioSummaryDto? fullPipelineShadow,
        ValidationPathMetricCostModel? costModel = null)
    {
        var costs = costModel ?? new ValidationPathMetricCostModel();
        return layer switch
        {
            ValidationLayerType.RawStrategy => BuildNormalizedIndependent(
                experimentId, segment, layer, candidates, confidenceOnly: false, costs),
            ValidationLayerType.ConfidenceQualified => BuildNormalizedIndependent(
                experimentId, segment, layer, candidates, confidenceOnly: true, costs),
            ValidationLayerType.RiskOnly => BuildFromShadowPath(
                experimentId, segment, layer, candidates, riskOnlyShadow, StrategyLabPortfolioPath.RiskOnly),
            ValidationLayerType.FullPipeline => BuildFromShadowPath(
                experimentId, segment, layer, candidates, fullPipelineShadow, StrategyLabPortfolioPath.FullPipeline),
            _ => []
        };
    }

    private static IReadOnlyList<ValidationPathTradeMetricInput> BuildNormalizedIndependent(
        long experimentId,
        ValidationSegmentType segment,
        ValidationLayerType layer,
        IReadOnlyList<StrategyResearchCandidate> candidates,
        bool confidenceOnly,
        ValidationPathMetricCostModel costModel)
    {
        var list = new List<ValidationPathTradeMetricInput>();
        foreach (var c in candidates)
        {
            if (confidenceOnly && c.ConfidenceDecision != ResearchConfidenceDecision.Approved)
            {
                continue;
            }

            if (c.CandidateStatus != StrategyResearchCandidateStatus.Closed
                && c.RawOutcomeStatus is not (RawOutcomeStatus.Winner or RawOutcomeStatus.Loser or RawOutcomeStatus.Expired))
            {
                continue;
            }

            if (c.RawExitPrice is not decimal exitPrice || exitPrice <= 0m)
            {
                list.Add(Excluded(
                    experimentId, segment, layer, c,
                    "MissingExitPrice",
                    costModel));
                continue;
            }

            if (c.ProposedEntryPrice <= 0m || c.StopLoss <= 0m)
            {
                list.Add(Excluded(
                    experimentId, segment, layer, c,
                    c.ProposedEntryPrice <= 0m ? "MissingEntry" : "MissingStop",
                    costModel));
                continue;
            }

            const decimal quantity = 1m;
            var mult = costModel.ContractMultiplier <= 0m ? 1m : costModel.ContractMultiplier;
            var entry = c.ProposedEntryPrice;
            var stop = c.StopLoss;

            var grossPnl = c.Direction == TradeDirection.Long
                ? (exitPrice - entry) * quantity * mult
                : (entry - exitPrice) * quantity * mult;

            var entryNotional = Math.Abs(entry * quantity * mult);
            var exitNotional = Math.Abs(exitPrice * quantity * mult);
            var entryFee = entryNotional * costModel.EntryFeeRate;
            var exitFee = exitNotional * costModel.ExitFeeRate;
            var slippageCost = (entryNotional + exitNotional) * (costModel.SlippagePercent / 100m);
            var totalCosts = entryFee + exitFee + slippageCost;
            var netPnl = grossPnl - totalCosts;
            var derivedRisk = Math.Abs(entry - stop) * quantity * mult;

            var recon = EvaluateReconciliation(c, quantity, grossPnl, netPnl);
            var included = derivedRisk > 0m;
            list.Add(new ValidationPathTradeMetricInput
            {
                ValidationExperimentId = experimentId,
                ValidationSegment = segment,
                ValidationLayer = layer,
                PortfolioPath = layer.ToString(),
                CandidateId = c.Id,
                CandidateFingerprint = c.SetupFingerprint,
                Direction = c.Direction,
                EntryTimeUtc = c.ProposedEntryTimeUtc,
                ExitTimeUtc = c.RawExitTimeUtc,
                EntryPrice = entry,
                ExitPrice = exitPrice,
                StopPriceAtEntry = stop,
                Quantity = quantity,
                ContractMultiplier = mult,
                RiskAmountAtEntry = derivedRisk,
                GrossPnl = grossPnl,
                EntryCosts = entryFee,
                ExitCosts = exitFee,
                OtherTransactionCosts = slippageCost,
                TotalTransactionCosts = totalCosts,
                NetPnl = netPnl,
                Outcome = c.RawOutcomeStatus.ToString(),
                MetricInclusionStatus = included
                    ? ValidationPathMetricInclusionStatus.Included
                    : ValidationPathMetricInclusionStatus.Excluded,
                // Included trades must never carry an exclusion reason.
                MetricExclusionReason = included ? null : "InvalidDerivedRisk",
                MetricWarningCodes = included && recon.WarningCode is not null
                    ? [recon.WarningCode]
                    : Array.Empty<string>(),
                ReconciliationStatus = recon.Status,
                ReconciliationGrossDelta = recon.GrossDelta,
                ReconciliationNetDelta = recon.NetDelta,
                SourceVersion = ValidationPathTradeMetricInput.SourceVersionV11
                    + ":IndependentNormalizedOneUnit"
            });
        }

        return list;
    }

    private readonly record struct ReconciliationEval(
        ValidationMetricReconciliationStatus Status,
        string? WarningCode,
        decimal? GrossDelta,
        decimal? NetDelta);

    private static ReconciliationEval EvaluateReconciliation(
        StrategyResearchCandidate c,
        decimal normalizedQty,
        decimal independentGross,
        decimal independentNet)
    {
        if (c.RawGrossPnl is not decimal rawGross || c.ProposedPositionSize is not decimal actualQty || actualQty <= 0m)
        {
            return new ReconciliationEval(
                ValidationMetricReconciliationStatus.SourceUnavailable, null, null, null);
        }

        var scaledGross = rawGross * (normalizedQty / actualQty);
        var scaledNet = (c.RawNetPnl ?? rawGross) * (normalizedQty / actualQty);
        var grossDelta = Math.Abs(scaledGross - independentGross);
        var netDelta = Math.Abs(scaledNet - independentNet);
        var scale = Math.Max(1m, Math.Abs(independentGross));
        if (grossDelta / scale > CandidateReconciliationTolerance
            || netDelta / Math.Max(1m, Math.Abs(independentNet)) > CandidateReconciliationTolerance)
        {
            return new ReconciliationEval(
                ValidationMetricReconciliationStatus.Mismatched,
                ValidationPathMetricWarningCodes.CandidateRawPnlReconciliationMismatch,
                grossDelta,
                netDelta);
        }

        return new ReconciliationEval(
            ValidationMetricReconciliationStatus.Matched, null, grossDelta, netDelta);
    }

    private static ValidationPathTradeMetricInput Excluded(
        long experimentId,
        ValidationSegmentType segment,
        ValidationLayerType layer,
        StrategyResearchCandidate c,
        string reason,
        ValidationPathMetricCostModel costModel) =>
        new()
        {
            ValidationExperimentId = experimentId,
            ValidationSegment = segment,
            ValidationLayer = layer,
            PortfolioPath = layer.ToString(),
            CandidateId = c.Id,
            CandidateFingerprint = c.SetupFingerprint,
            Direction = c.Direction,
            EntryTimeUtc = c.ProposedEntryTimeUtc,
            ExitTimeUtc = c.RawExitTimeUtc,
            EntryPrice = c.ProposedEntryPrice,
            ExitPrice = c.RawExitPrice,
            StopPriceAtEntry = c.StopLoss,
            Quantity = 1m,
            ContractMultiplier = costModel.ContractMultiplier <= 0m ? 1m : costModel.ContractMultiplier,
            MetricInclusionStatus = ValidationPathMetricInclusionStatus.Excluded,
            MetricExclusionReason = reason,
            MetricWarningCodes = Array.Empty<string>(),
            ReconciliationStatus = ValidationMetricReconciliationStatus.NotApplicable,
            SourceVersion = ValidationPathTradeMetricInput.SourceVersionV11
                + ":IndependentNormalizedOneUnit"
        };

    private static IReadOnlyList<ValidationPathTradeMetricInput> BuildFromShadowPath(
        long experimentId,
        ValidationSegmentType segment,
        ValidationLayerType layer,
        IReadOnlyList<StrategyResearchCandidate> candidates,
        ShadowPortfolioSummaryDto? shadow,
        StrategyLabPortfolioPath path)
    {
        if (shadow?.Ledger is null || shadow.Ledger.Count == 0)
        {
            return [];
        }

        var byId = candidates.ToDictionary(c => c.Id);
        var byFp = candidates
            .GroupBy(c => c.SetupFingerprint, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var includedFingerprints = candidates
            .Select(c => c.SetupFingerprint)
            .ToHashSet(StringComparer.Ordinal);

        var list = new List<ValidationPathTradeMetricInput>();
        var ledgerIndex = 0;
        foreach (var entry in shadow.Ledger)
        {
            ledgerIndex++;
            if (!string.IsNullOrWhiteSpace(entry.SetupFingerprint)
                && includedFingerprints.Count > 0
                && !includedFingerprints.Contains(entry.SetupFingerprint))
            {
                continue;
            }

            StrategyResearchCandidate? candidate = null;
            if (entry.CandidateId > 0 && byId.TryGetValue(entry.CandidateId, out var byIdHit))
            {
                candidate = byIdHit;
            }
            else if (!string.IsNullOrWhiteSpace(entry.SetupFingerprint)
                     && byFp.TryGetValue(entry.SetupFingerprint, out var byFpHit))
            {
                candidate = byFpHit;
            }

            var assessment = TryReadAssessment(candidate, path);
            // Never fall back to the other path or generic ProposedPositionSize.
            var quantity = entry.Quantity > 0m
                ? entry.Quantity
                : assessment?.Quantity ?? 0m;
            var stop = candidate?.StopLoss ?? 0m;
            var entryPrice = entry.EntryPrice > 0m ? entry.EntryPrice : candidate?.ProposedEntryPrice ?? 0m;
            var riskAmount = assessment?.RiskAmount;
            var mult = 1m;
            var derivedRisk = entryPrice > 0m && stop > 0m && quantity > 0m
                ? Math.Abs(entryPrice - stop) * quantity * mult
                : (decimal?)null;

            string? exclusion = null;
            var inclusion = ValidationPathMetricInclusionStatus.Included;
            if (quantity <= 0m)
            {
                exclusion = "MissingPathQuantity";
                inclusion = ValidationPathMetricInclusionStatus.Excluded;
            }
            else if (stop <= 0m)
            {
                exclusion = "MissingStop";
                inclusion = ValidationPathMetricInclusionStatus.Excluded;
            }
            else if (entryPrice <= 0m)
            {
                exclusion = "MissingEntry";
                inclusion = ValidationPathMetricInclusionStatus.Excluded;
            }
            else if (riskAmount is decimal persisted
                     && derivedRisk is decimal derived
                     && derived > 0m
                     && Math.Abs(persisted - derived) / derived > CandidateReconciliationTolerance)
            {
                exclusion = "RiskAmountMismatch";
                inclusion = ValidationPathMetricInclusionStatus.Excluded;
            }

            list.Add(new ValidationPathTradeMetricInput
            {
                ValidationExperimentId = experimentId,
                ValidationSegment = segment,
                ValidationLayer = layer,
                PortfolioPath = path.ToString(),
                CandidateId = entry.CandidateId > 0 ? entry.CandidateId : candidate?.Id ?? 0,
                CandidateFingerprint = entry.SetupFingerprint,
                LedgerEntryId = ledgerIndex,
                Direction = entry.Direction,
                EntryTimeUtc = entry.EntryTimeUtc,
                ExitTimeUtc = entry.ExitTimeUtc,
                EntryPrice = entryPrice,
                ExitPrice = entry.ExitPrice,
                StopPriceAtEntry = stop,
                Quantity = quantity,
                ContractMultiplier = mult,
                RiskAmountAtEntry = riskAmount ?? derivedRisk,
                GrossPnl = entry.GrossPnl,
                EntryCosts = entry.EntryFee,
                ExitCosts = entry.ExitFee,
                OtherTransactionCosts = Math.Max(0m, entry.TotalCost - entry.EntryFee - entry.ExitFee),
                TotalTransactionCosts = entry.TotalCost,
                NetPnl = entry.NetPnl,
                Outcome = entry.ExitOutcome,
                MetricInclusionStatus = inclusion,
                MetricExclusionReason = inclusion == ValidationPathMetricInclusionStatus.Included
                    ? null
                    : exclusion,
                MetricWarningCodes = Array.Empty<string>(),
                ReconciliationStatus = inclusion == ValidationPathMetricInclusionStatus.Included
                    ? ValidationMetricReconciliationStatus.NotApplicable
                    : ValidationMetricReconciliationStatus.NotApplicable,
                SourceVersion = ValidationPathTradeMetricInput.SourceVersionV11 + ":ShadowLedger"
            });
        }

        return list;
    }

    private static PathPortfolioAssessmentDto? TryReadAssessment(
        StrategyResearchCandidate? candidate,
        StrategyLabPortfolioPath path)
    {
        if (candidate is null)
        {
            return null;
        }

        var json = path == StrategyLabPortfolioPath.RiskOnly
            ? candidate.RiskOnlyAssessmentJson
            : candidate.FullPipelineAssessmentJson;
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PathPortfolioAssessmentDto>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
