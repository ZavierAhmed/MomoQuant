using System.Globalization;
using System.Text;
using System.Text.Json;
using MomoQuant.Application.Exports.Dtos;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.ValidationLab;

public static class ValidationLabExportBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static object BuildCompleteEnvelope(ValidationExperimentDetailDto detail)
    {
        return new
        {
            experiment = new
            {
                detail.Id,
                detail.Name,
                detail.ExperimentType,
                detail.Status,
                detail.StrategyCode,
                detail.StrategyVersion,
                detail.Symbol,
                detail.Timeframe,
                detail.ValidationMetricsVersion,
                detail.CandidateReconciliationStatus,
                detail.LeakageAuditStatus,
                detail.ParameterStabilityApplicability,
                detail.ExpectancyMetric,
                detail.ProfitFactorMetric,
                detail.StrategyRobustnessDecision,
                detail.PrimaryFailureReason,
                detail.DecisionExplanation,
                detail.ValidationRevealStatus,
                detail.FrozenParameterFingerprint,
                detail.FrozenStrategyFingerprint
            },
            dataSnapshot = SafeParse(detail.CandleDataSnapshotJson),
            split = new
            {
                detail.SplitAlgorithmVersion,
                detail.SplitRatio,
                detail.TrainingStartUtc,
                detail.TrainingEndUtc,
                detail.ValidationStartUtc,
                detail.ValidationEndUtc,
                detail.TrainingCandleCount,
                detail.ValidationCandleCount,
                detail.CandleDataFingerprint,
                detail.SegmentDetectorContinuityMode
            },
            candidateReconciliation = SafeParse(detail.CandidateReconciliationJson),
            holdoutExclusivity = SafeParse(detail.HoldoutExclusivityJson),
            metricConsistency = SafeParse(detail.MetricConsistencyJson),
            trainingSearch = SafeParse(detail.ParameterStabilityJson),
            frozenConfiguration = new
            {
                parameters = SafeParse(detail.FrozenStrategyParameterSnapshotJson),
                confidence = SafeParse(detail.FrozenConfidenceSnapshotJson),
                risk = SafeParse(detail.FrozenRiskSnapshotJson),
                costs = SafeParse(detail.FrozenCostModelSnapshotJson),
                qualification = SafeParse(detail.QualificationProfileSnapshotJson),
                detail.FrozenParameterFingerprint,
                detail.FrozenStrategyFingerprint
            },
            trainingResults = detail.SegmentResults?
                .Where(s => s.SegmentType == ValidationSegmentType.Training)
                .Select(MapSegment)
                .ToList(),
            validationResults = detail.SegmentResults?
                .Where(s => s.SegmentType == ValidationSegmentType.Validation)
                .Select(MapSegment)
                .ToList(),
            comparison = SafeParse(detail.ComparisonJson),
            confidenceAnalysis = (object?)null,
            riskAnalysis = (object?)null,
            regimeComparison = SafeParse(detail.RegimeComparisonJson),
            leakageAudit = SafeParse(detail.LeakageAuditJson),
            diagnostics = SafeParse(detail.DiagnosticsJson),
            qualification = new
            {
                detail.StrategyRobustnessDecision,
                failureReasons = SafeParse(detail.FailureReasonsJson),
                ruleResults = SafeParse(detail.QualificationRuleResultsJson),
                detail.DecisionExplanation
            },
            exportManifest = SafeParse(detail.ExportVerificationJson) ?? new
            {
                verificationStatus = detail.ExportVerificationStatus?.ToString() ?? "NotRun",
                populationCounts = (detail.SegmentResults ?? []).Select(s => new
                {
                    s.SegmentType,
                    s.LayerType,
                    s.PersistedCandidateRowCount,
                    s.MetricIncludedCandidateCount,
                    s.MetricExcludedCandidateCount,
                    s.CrossSegmentOverlapCount
                })
            },
            audit = new
            {
                detail.FrozenAtUtc,
                detail.ValidationRevealedAtUtc,
                detail.ValidationRevealStatus,
                metricsVersion = detail.ValidationMetricsVersion,
                holdoutExclusivityPolicyVersion = detail.HoldoutExclusivityPolicyVersion,
                crossSegmentOverlapCount = detail.CrossSegmentOverlapCount,
                legacyLabel = detail.ValidationMetricsVersion == ValidationMetricsContract.VersionV1Legacy
                    || detail.ValidationMetricsVersion == "ValidationMetrics/v1"
                    ? "Legacy metric definitions"
                    : null
            }
        };
    }

    public static void PopulateExportEnvelope(
        ExportEnvelopeDto envelope,
        ValidationExperimentDetailDto detail,
        bool revealed)
    {
        envelope.SourceMetadata["validationExperimentId"] = detail.Id;
        envelope.SourceMetadata["experimentName"] = detail.Name;
        envelope.SourceMetadata["experimentType"] = detail.ExperimentType.ToString();
        envelope.SourceMetadata["status"] = detail.Status.ToString();
        envelope.SourceMetadata["strategyCode"] = detail.StrategyCode;
        envelope.SourceMetadata["strategyVersion"] = detail.StrategyVersion;
        envelope.SourceMetadata["symbol"] = detail.Symbol;
        envelope.SourceMetadata["timeframe"] = detail.Timeframe;
        envelope.SourceMetadata["candleDataFingerprint"] = detail.CandleDataFingerprint;
        envelope.SourceMetadata["candidateReconciliationStatus"] = detail.CandidateReconciliationStatus?.ToString();
        envelope.SourceMetadata["frozenParameterFingerprint"] = detail.FrozenParameterFingerprint;
        envelope.SourceMetadata["frozenStrategyFingerprint"] = detail.FrozenStrategyFingerprint;
        envelope.SourceMetadata["validationMetricsVersion"] = detail.ValidationMetricsVersion;
        envelope.SourceMetadata["revealStatus"] = detail.ValidationRevealStatus.ToString();
        envelope.SourceMetadata["revealedAtUtc"] = detail.ValidationRevealedAtUtc;
        envelope.SourceMetadata["strategyRobustnessDecision"] = detail.StrategyRobustnessDecision?.ToString();
        envelope.SourceMetadata["filtersApplied"] = false;
        envelope.SourceMetadata["totalRecordCount"] = detail.SegmentResults?.Count ?? 0;
        envelope.SourceMetadata["exportedRecordCount"] = detail.SegmentResults?.Count ?? 0;

        envelope.Summary["expectancyMetric"] = detail.ExpectancyMetric.ToString();
        envelope.Summary["profitFactorMetric"] = detail.ProfitFactorMetric.ToString();
        envelope.Summary["parameterStabilityApplicability"] = detail.ParameterStabilityApplicability?.ToString();
        envelope.Summary["leakageAuditStatus"] = detail.LeakageAuditStatus?.ToString();

        if (!revealed)
        {
            envelope.Warnings.Add("Validation results are hidden until ValidationRevealStatus is Revealed.");
            envelope.Results["blocked"] = true;
            return;
        }

        envelope.Results["complete"] = BuildCompleteEnvelope(detail);
        envelope.Warnings.Add("Validation Laboratory export is observational research only — no orders were placed.");
        if (detail.ValidationMetricsVersion == ValidationMetricsContract.VersionV1Legacy
            || detail.ValidationMetricsVersion == "ValidationMetrics/v1")
        {
            envelope.Warnings.Add("Legacy metric definitions — NetExpectancyR may equal GrossAverageR.");
        }
    }

    public static string BuildCsvBundle(ValidationExperimentDetailDto detail, IReadOnlyList<object>? trials = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# validation-experiment-segment-results.csv");
        sb.AppendLine("# segment-results.csv");
        sb.AppendLine("SegmentType,LayerType,ClosedTradeCount,GrossExpectancyR,NetExpectancyR,GrossProfitFactor,NetProfitFactor,GrossPnl,NetPnl,TransactionCosts,PersistedCandidateRowCount,MetricIncludedCandidateCount,MetricExcludedCandidateCount,CrossSegmentOverlapCount,GrossProfit,GrossLoss,NetProfit,NetLoss,ResultCalculationVersion,MetricWarningBearingIncludedTradeCount,MetricWarningCodes,PopulationContractVersion,CandidatePopulationCount,BoundaryEligibleCandidateCount,PathInputPopulationCount,IncludedPathInputCount,ExcludedPathInputCount,ClosedOutcomePopulationCount,MonetaryPnlPopulationCount,GrossRPopulationCount,NetRPopulationCount,WinnerPopulationCount,LoserPopulationCount,NeutralPopulationCount,RiskBasisValidationStatus,MonetaryPnlApplicability,GrossExpectancyApplicability,NetExpectancyApplicability,ExclusionCountsByReason,WarningCountsByCode");
        foreach (var s in detail.SegmentResults ?? [])
        {
            sb.AppendLine(string.Join(',',
                Csv(s.SegmentType.ToString()),
                Csv(s.LayerType.ToString()),
                s.ClosedTradeCount,
                Fmt(s.GrossExpectancyR),
                Fmt(s.NetExpectancyR),
                Fmt(s.GrossProfitFactor),
                Fmt(s.NetProfitFactor),
                Fmt(s.GrossPnl),
                Fmt(s.NetPnl),
                Fmt(s.TransactionCosts),
                s.PersistedCandidateRowCount,
                s.MetricIncludedCandidateCount,
                s.MetricExcludedCandidateCount,
                s.CrossSegmentOverlapCount,
                Fmt(s.GrossProfit),
                Fmt(s.GrossLoss),
                Fmt(s.NetProfit),
                Fmt(s.NetLoss),
                Csv(s.ResultCalculationVersion),
                s.MetricWarningBearingIncludedTradeCount,
                Csv(s.MetricWarningCodes is { Count: > 0 }
                    ? string.Join('|', s.MetricWarningCodes)
                    : string.Empty),
                Csv(s.PopulationContractVersion),
                s.CandidatePopulationCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                s.BoundaryEligibleCandidateCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                s.PathInputPopulationCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                s.IncludedPathInputCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                s.ExcludedPathInputCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                s.ClosedOutcomePopulationCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                s.MonetaryPnlPopulationCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                s.GrossRPopulationCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                s.NetRPopulationCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                s.WinnerPopulationCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                s.LoserPopulationCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                s.NeutralPopulationCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                Csv(s.RiskBasisValidationStatus?.ToString()),
                Csv(s.MonetaryPnlApplicability?.ToString()),
                Csv(s.GrossExpectancyApplicability?.ToString()),
                Csv(s.NetExpectancyApplicability?.ToString()),
                Csv(FormatCountMap(s.ExclusionCountsByReason)),
                Csv(FormatCountMap(s.WarningCountsByCode))));
        }

        sb.AppendLine();
        sb.AppendLine("# validation-experiment-qualification-rules.csv");
        sb.AppendLine("RuleKey,Status,Applicability,MetricKey,ActualValue,LimitValue,Reason");
        if (!string.IsNullOrWhiteSpace(detail.QualificationRuleResultsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(detail.QualificationRuleResultsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        if (el.ValueKind != JsonValueKind.Object) continue;
                        sb.AppendLine(string.Join(',',
                            Csv(GetStr(el, "ruleKey")),
                            Csv(GetStr(el, "status")),
                            Csv(GetStr(el, "applicability")),
                            Csv(GetStr(el, "metricKey")),
                            Csv(GetStr(el, "actualValue")),
                            Csv(GetStr(el, "limitValue")),
                            Csv(GetStr(el, "reason"))));
                    }
                }
            }
            catch
            {
                // ignore malformed
            }
        }

        sb.AppendLine();
        sb.AppendLine("# validation-experiment-candidate-reconciliation.csv");
        sb.AppendLine("Fingerprint,DifferenceType,Segment,IsExpected,Explanation");
        if (!string.IsNullOrWhiteSpace(detail.CandidateReconciliationJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(detail.CandidateReconciliationJson);
                if (doc.RootElement.TryGetProperty("detailedDifferences", out var diffs)
                    || doc.RootElement.TryGetProperty("DetailedDifferences", out diffs))
                {
                    foreach (var el in diffs.EnumerateArray())
                    {
                        sb.AppendLine(string.Join(',',
                            Csv(GetStr(el, "fingerprint")),
                            Csv(GetStr(el, "differenceType")),
                            Csv(GetStr(el, "segment")),
                            GetStr(el, "isExpected"),
                            Csv(GetStr(el, "explanation"))));
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        if (trials is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("# validation-experiment-training-trials.csv");
            sb.AppendLine("# training-trials.csv");
            sb.AppendLine("TrialNumber,Status,ParameterFingerprint,NetExpectancyR,TrainingScore,GuardrailDecision,Rank,StrategyLabRunId");
            foreach (var t in trials)
            {
                if (t is ValidationParameterTrialDto dto)
                {
                    sb.AppendLine(string.Join(',',
                        dto.TrialNumber,
                        Csv(dto.Status.ToString()),
                        Csv(dto.ParameterFingerprint),
                        Fmt(dto.NetExpectancyR),
                        Fmt(dto.TrainingScore),
                        Csv(dto.GuardrailDecision),
                        dto.Rank?.ToString() ?? "",
                        dto.StrategyLabRunId?.ToString() ?? ""));
                }
                else
                {
                    sb.AppendLine(Csv(JsonSerializer.Serialize(t, JsonOptions)));
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("# training-candidates.csv");
        sb.AppendLine("filtersApplied,experimentId,metricsVersion");
        sb.AppendLine($"true,{detail.Id},{Csv(detail.ValidationMetricsVersion)}");

        sb.AppendLine();
        sb.AppendLine("# validation-candidates.csv");
        sb.AppendLine("filtersApplied,experimentId,metricsVersion,revealStatus");
        sb.AppendLine($"true,{detail.Id},{Csv(detail.ValidationMetricsVersion)},{Csv(detail.ValidationRevealStatus.ToString())}");

        sb.AppendLine();
        sb.AppendLine("# risk-only-ledger.csv");
        sb.AppendLine("Note");
        sb.AppendLine(Csv("Ledger exported via Strategy Lab shadow portfolio when available."));

        sb.AppendLine();
        sb.AppendLine("# full-pipeline-ledger.csv");
        sb.AppendLine("Note");
        sb.AppendLine(Csv("Ledger exported via Strategy Lab shadow portfolio when available."));

        sb.AppendLine();
        sb.AppendLine("# diagnostics.csv");
        sb.AppendLine("DiagnosticJson");
        sb.AppendLine(Csv(detail.DiagnosticsJson ?? "[]"));

        sb.AppendLine();
        sb.AppendLine("# overlap-candidates.csv");
        sb.AppendLine("OverlapFingerprint,CanonicalOccurrenceCandidateId,DuplicateOccurrenceCandidateId,TrainingSetupDetectedAtUtc,ValidationSetupDetectedAtUtc,MetricOwner,ExcludedOccurrence,MetricClassification,MetricExclusionReason");
        if (!string.IsNullOrWhiteSpace(detail.HoldoutExclusivityJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(detail.HoldoutExclusivityJson);
                if (doc.RootElement.TryGetProperty("overlaps", out var overlaps)
                    || doc.RootElement.TryGetProperty("Overlaps", out overlaps))
                {
                    foreach (var el in overlaps.EnumerateArray())
                    {
                        sb.AppendLine(string.Join(',',
                            Csv(GetStr(el, "overlapFingerprint")),
                            Csv(GetStr(el, "canonicalOccurrenceCandidateId")),
                            Csv(GetStr(el, "duplicateOccurrenceCandidateId")),
                            Csv(GetStr(el, "trainingSetupDetectedAtUtc")),
                            Csv(GetStr(el, "validationSetupDetectedAtUtc")),
                            Csv(GetStr(el, "metricOwner")),
                            Csv(GetStr(el, "excludedOccurrence")),
                            Csv(GetStr(el, "metricClassification")),
                            Csv(GetStr(el, "metricExclusionReason"))));
                    }
                }
            }
            catch
            {
                // ignore malformed exclusivity JSON
            }
        }

        return sb.ToString();
    }

    public static string BuildPdfSummaryText(ValidationExperimentDetailDto detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("MOMO Quant — Validation Laboratory Report");
        sb.AppendLine();
        sb.AppendLine("1. Experiment Overview");
        sb.AppendLine($"Experiment: {detail.Name} (Id={detail.Id})");
        sb.AppendLine($"Strategy: {detail.StrategyCode} {detail.StrategyVersion}");
        sb.AppendLine($"Symbol/TF: {detail.Symbol} {detail.Timeframe}");
        sb.AppendLine($"Status: {detail.Status} · Verdict: {detail.StrategyRobustnessDecision}");
        sb.AppendLine();
        sb.AppendLine("2. Data Quality");
        sb.AppendLine($"Eligible candles: {detail.TotalEligibleCandleCount}");
        sb.AppendLine($"Candle fingerprint: {detail.CandleDataFingerprint}");
        sb.AppendLine($"Reconciliation: {detail.CandidateReconciliationStatus}");
        sb.AppendLine();
        sb.AppendLine("3. Chronological Split");
        sb.AppendLine($"Split: {detail.SplitRatio:P0} · Algorithm: {detail.SplitAlgorithmVersion}");
        sb.AppendLine($"Training: {detail.TrainingStartUtc:O} → {detail.TrainingEndUtc:O} ({detail.TrainingCandleCount} candles)");
        sb.AppendLine($"Validation: {detail.ValidationStartUtc:O} → {detail.ValidationEndUtc:O} ({detail.ValidationCandleCount} candles)");
        sb.AppendLine();
        sb.AppendLine("4. Candidate Reconciliation");
        sb.AppendLine($"Status: {detail.CandidateReconciliationStatus}");
        sb.AppendLine();
        sb.AppendLine("5. Training Search");
        sb.AppendLine($"Trials requested: {detail.MaximumTrials} · Seed: {detail.DeterministicSeed}");
        sb.AppendLine($"Parameter stability: {detail.ParameterStabilityApplicability}");
        sb.AppendLine();
        sb.AppendLine("6. Top Trials");
        sb.AppendLine("(See training-trials.csv export for full trial table.)");
        sb.AppendLine();
        sb.AppendLine("7. Frozen Configuration");
        sb.AppendLine($"Frozen parameter fingerprint: {detail.FrozenParameterFingerprint}");
        sb.AppendLine($"Frozen at: {detail.FrozenAtUtc:O}");
        sb.AppendLine();
        sb.AppendLine("8. Training Metrics");
        AppendSegmentMetrics(sb, detail, ValidationSegmentType.Training);
        sb.AppendLine();
        sb.AppendLine("9. Holdout Metrics");
        AppendSegmentMetrics(sb, detail, ValidationSegmentType.Validation);
        sb.AppendLine();
        sb.AppendLine("10. Gross versus Net Results");
        foreach (var s in detail.SegmentResults ?? [])
        {
            if (s.LayerType != ValidationLayerType.RawStrategy) continue;
            sb.AppendLine(
                $"  {s.SegmentType}: GrossE[R]={Fmt(s.GrossExpectancyR)} NetE[R]={Fmt(s.NetExpectancyR)} " +
                $"GrossPF={Fmt(s.GrossProfitFactor)} NetPF={Fmt(s.NetProfitFactor)} " +
                $"GrossPnl={Fmt(s.GrossPnl)} NetPnl={Fmt(s.NetPnl)}");
        }
        sb.AppendLine();
        sb.AppendLine("11. Layer Comparison");
        sb.AppendLine(detail.ComparisonJson ?? "{}");
        sb.AppendLine();
        sb.AppendLine("12. Parameter Stability");
        sb.AppendLine(detail.ParameterStabilityJson ?? "{}");
        sb.AppendLine();
        sb.AppendLine("13. Leakage Audit");
        sb.AppendLine($"Status: {detail.LeakageAuditStatus}");
        sb.AppendLine();
        sb.AppendLine("14. Qualification Rules");
        sb.AppendLine(detail.QualificationRuleResultsJson ?? "[]");
        sb.AppendLine();
        sb.AppendLine("15. Final Verdict");
        sb.AppendLine($"Decision: {detail.StrategyRobustnessDecision}");
        sb.AppendLine($"Primary failure: {detail.PrimaryFailureReason}");
        sb.AppendLine($"Explanation: {detail.DecisionExplanation}");
        sb.AppendLine();
        sb.AppendLine("Holdout Exclusivity");
        sb.AppendLine($"  PolicyVersion: {detail.HoldoutExclusivityPolicyVersion}");
        sb.AppendLine($"  CrossSegmentOverlapCount: {detail.CrossSegmentOverlapCount}");
        sb.AppendLine();
        sb.AppendLine("16. Limitations");
        sb.AppendLine("Observational research only; holdout reuse may contaminate future decisions.");
        sb.AppendLine("Negative strategy performance does not imply Validation Laboratory infrastructure failure.");
        return sb.ToString();
    }

    private static void AppendSegmentMetrics(StringBuilder sb, ValidationExperimentDetailDto detail, ValidationSegmentType segment)
    {
        var raw = detail.SegmentResults?
            .FirstOrDefault(s => s.SegmentType == segment && s.LayerType == ValidationLayerType.RawStrategy);
        if (raw is null)
        {
            sb.AppendLine("  (no RawStrategy segment persisted)");
            return;
        }

        sb.AppendLine(
            $"  n={raw.ClosedTradeCount} NetE[R]={Fmt(raw.NetExpectancyR)} R/trade " +
            $"GrossE[R]={Fmt(raw.GrossExpectancyR)} NetPnl={Fmt(raw.NetPnl)}");
    }

    private static object MapSegment(ValidationSegmentResultDto s) => new
    {
        s.SegmentType,
        s.LayerType,
        s.ClosedTradeCount,
        s.GrossExpectancyR,
        s.NetExpectancyR,
        s.GrossProfitFactor,
        s.NetProfitFactor,
        s.GrossPnl,
        s.NetPnl,
        s.TransactionCosts,
        s.PersistedCandidateRowCount,
        s.MetricIncludedCandidateCount,
        s.MetricExcludedCandidateCount,
        s.CrossSegmentOverlapCount,
        s.GrossProfit,
        s.GrossLoss,
        s.NetProfit,
        s.NetLoss,
        s.ResultCalculationVersion,
        s.MetricWarningBearingIncludedTradeCount,
        s.MetricWarningCodes,
        s.PopulationContractVersion,
        s.CandidatePopulationCount,
        s.BoundaryEligibleCandidateCount,
        s.PathInputPopulationCount,
        s.IncludedPathInputCount,
        s.ExcludedPathInputCount,
        s.ClosedOutcomePopulationCount,
        s.MonetaryPnlPopulationCount,
        s.GrossRPopulationCount,
        s.NetRPopulationCount,
        s.WinnerPopulationCount,
        s.LoserPopulationCount,
        s.NeutralPopulationCount,
        s.ExclusionCountsByReason,
        s.WarningCountsByCode,
        s.RiskBasisValidationStatus,
        s.MonetaryPnlApplicability,
        s.GrossProfitFactorApplicability,
        s.NetProfitFactorApplicability,
        s.GrossExpectancyApplicability,
        s.NetExpectancyApplicability,
        population = s.Population,
        metrics = SafeParse(s.MetricsJson)
    };

    private static object? SafeParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<object>(json, JsonOptions);
        }
        catch
        {
            return json;
        }
    }

    private static string FormatCountMap(IReadOnlyDictionary<string, int>? map)
    {
        if (map is null || map.Count == 0) return string.Empty;
        return string.Join('|', map.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}"));
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private static string Fmt(decimal? v) =>
        v?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string GetStr(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var p)) return p.ToString();
        foreach (var prop in el.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                return prop.Value.ToString();
        }

        return string.Empty;
    }
}
