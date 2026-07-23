using System.Text.Json;
using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.ValidationLab;

public interface IValidationPathMetricInputBuilder
{
    IReadOnlyList<ValidationPathTradeMetricInput> Build(
        long experimentId,
        ValidationSegmentType segment,
        ValidationLayerType layer,
        IReadOnlyList<StrategyResearchCandidate> candidates,
        ShadowPortfolioSummaryDto? riskOnlyShadow,
        ShadowPortfolioSummaryDto? fullPipelineShadow);
}

/// <summary>
/// Builds path-specific metric inputs. RiskOnly/FullPipeline use ledger + assessment data;
/// RawStrategy/ConfidenceQualified use normalized one-unit raw candidate economics.
/// </summary>
public sealed class ValidationPathMetricInputBuilder : IValidationPathMetricInputBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IReadOnlyList<ValidationPathTradeMetricInput> Build(
        long experimentId,
        ValidationSegmentType segment,
        ValidationLayerType layer,
        IReadOnlyList<StrategyResearchCandidate> candidates,
        ShadowPortfolioSummaryDto? riskOnlyShadow,
        ShadowPortfolioSummaryDto? fullPipelineShadow)
    {
        return layer switch
        {
            ValidationLayerType.RawStrategy => BuildNormalized(experimentId, segment, layer, candidates, confidenceOnly: false),
            ValidationLayerType.ConfidenceQualified => BuildNormalized(experimentId, segment, layer, candidates, confidenceOnly: true),
            ValidationLayerType.RiskOnly => BuildFromShadowPath(
                experimentId, segment, layer, candidates, riskOnlyShadow, StrategyLabPortfolioPath.RiskOnly),
            ValidationLayerType.FullPipeline => BuildFromShadowPath(
                experimentId, segment, layer, candidates, fullPipelineShadow, StrategyLabPortfolioPath.FullPipeline),
            _ => []
        };
    }

    private static IReadOnlyList<ValidationPathTradeMetricInput> BuildNormalized(
        long experimentId,
        ValidationSegmentType segment,
        ValidationLayerType layer,
        IReadOnlyList<StrategyResearchCandidate> candidates,
        bool confidenceOnly)
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

            var actualQty = c.ProposedPositionSize is > 0m ? c.ProposedPositionSize.Value : 1m;
            var gross = c.RawGrossPnl ?? c.RawNetPnl ?? 0m;
            var net = c.RawNetPnl ?? c.RawGrossPnl ?? 0m;
            var normalizedGross = gross / actualQty;
            var normalizedNet = net / actualQty;
            var costs = Math.Max(0m, normalizedGross - normalizedNet);

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
                EntryPrice = c.ProposedEntryPrice,
                ExitPrice = c.RawExitPrice,
                StopPriceAtEntry = c.StopLoss,
                Quantity = 1m,
                ContractMultiplier = 1m,
                RiskAmountAtEntry = c.RiskAmount.HasValue ? c.RiskAmount.Value / actualQty : null,
                GrossPnl = normalizedGross,
                EntryCosts = 0m,
                ExitCosts = 0m,
                OtherTransactionCosts = costs,
                TotalTransactionCosts = costs,
                NetPnl = normalizedNet,
                Outcome = c.RawOutcomeStatus.ToString(),
                MetricInclusionStatus = ValidationPathMetricInclusionStatus.Included,
                SourceVersion = "ValidationPathTradeMetricInput/v1:NormalizedOneUnit"
            });
        }

        return list;
    }

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
            var quantity = entry.Quantity > 0m
                ? entry.Quantity
                : assessment?.Quantity ?? 0m;
            var stop = candidate?.StopLoss ?? 0m;
            var entryPrice = entry.EntryPrice > 0m ? entry.EntryPrice : candidate?.ProposedEntryPrice ?? 0m;
            var riskAmount = assessment?.RiskAmount;

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
                ContractMultiplier = 1m,
                RiskAmountAtEntry = riskAmount,
                GrossPnl = entry.GrossPnl,
                EntryCosts = entry.EntryFee,
                ExitCosts = entry.ExitFee,
                OtherTransactionCosts = Math.Max(0m, entry.TotalCost - entry.EntryFee - entry.ExitFee),
                TotalTransactionCosts = entry.TotalCost,
                NetPnl = entry.NetPnl,
                Outcome = entry.ExitOutcome,
                MetricInclusionStatus = quantity > 0m && stop > 0m && entryPrice > 0m
                    ? ValidationPathMetricInclusionStatus.Included
                    : ValidationPathMetricInclusionStatus.Excluded,
                MetricExclusionReason = quantity <= 0m
                    ? "MissingQuantity"
                    : stop <= 0m
                        ? "MissingStop"
                        : entryPrice <= 0m
                            ? "MissingEntry"
                            : null,
                SourceVersion = "ValidationPathTradeMetricInput/v1:ShadowLedger"
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
