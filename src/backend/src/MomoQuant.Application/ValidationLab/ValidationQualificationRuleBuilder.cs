using System.Globalization;
using System.Text.Json;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.ValidationLab;

public static class ValidationQualificationRuleBuilder
{
    public static IReadOnlyList<QualificationRuleResult> Build(
        LayerSegmentMetrics training,
        LayerSegmentMetrics validation,
        ValidationQualificationProfile profile,
        ValidationExperimentType experimentType,
        ParameterStabilityApplicability stabilityApplicability,
        bool parameterStabilityOk,
        bool dataQualityOk,
        bool configurationMatch,
        CandidateReconciliationStatus? reconciliationStatus,
        ValidationLeakageAuditStatus? leakageStatus,
        bool metricConsistencyOk,
        ValidationPrimaryQualificationLayer layer)
    {
        var rules = new List<QualificationRuleResult>();
        var layerName = layer.ToString();
        var metricVersion = training.MetricsVersion ?? ValidationMetricsContract.VersionV11;

        rules.Add(Rule(
            "DataQuality", "Data quality",
            segment: null, layer: layerName, metricKey: null,
            actual: dataQualityOk ? "Ok" : "Fail", limit: "Ok", unit: null, op: "==",
            dataQualityOk ? QualificationRuleStatus.Passed : QualificationRuleStatus.Failed,
            QualificationRuleApplicability.Applicable,
            dataQualityOk ? "Data quality checks passed." : "Data quality checks failed.",
            metricVersion,
            failureDecision: StrategyRobustnessDecision.FailedDataQuality));

        rules.Add(Rule(
            "ConfigurationMatch", "Frozen configuration match",
            null, layerName, null,
            configurationMatch ? "Match" : "Mismatch", "Match", null, "==",
            configurationMatch ? QualificationRuleStatus.Passed : QualificationRuleStatus.Failed,
            QualificationRuleApplicability.Applicable,
            configurationMatch ? "Configuration fingerprints match." : "Configuration mismatch detected.",
            metricVersion,
            failureDecision: StrategyRobustnessDecision.FailedConfigurationMismatch));

        if (reconciliationStatus is CandidateReconciliationStatus.UnexplainedDifference
            or CandidateReconciliationStatus.Invalid)
        {
            rules.Add(Rule(
                "CandidateReconciliation", "Candidate reconciliation",
                null, layerName, "CandidateReconciliationStatus",
                reconciliationStatus.ToString(), "ExactMatch|ExplainedSessionBoundaryDifference", null, "in",
                QualificationRuleStatus.Failed,
                QualificationRuleApplicability.Applicable,
                $"Candidate reconciliation status {reconciliationStatus} blocks robustness approval.",
                metricVersion,
                failureDecision: StrategyRobustnessDecision.FailedDataIntegrity));
        }
        else if (reconciliationStatus.HasValue)
        {
            rules.Add(Rule(
                "CandidateReconciliation", "Candidate reconciliation",
                null, layerName, "CandidateReconciliationStatus",
                reconciliationStatus.ToString(), "ExactMatch|ExplainedSessionBoundaryDifference", null, "in",
                QualificationRuleStatus.Passed,
                QualificationRuleApplicability.Applicable,
                $"Candidate reconciliation status {reconciliationStatus}.",
                metricVersion));
        }

        if (leakageStatus == ValidationLeakageAuditStatus.Failed)
        {
            rules.Add(Rule(
                "LeakageAudit", "Validation leakage audit",
                "Training", layerName, "ValidationLeakageAuditStatus",
                "Failed", "Passed", null, "==",
                QualificationRuleStatus.Failed,
                QualificationRuleApplicability.Applicable,
                "ValidationDataLeakageDetected: optimizer accessed validation-range candles.",
                metricVersion,
                failureDecision: StrategyRobustnessDecision.FailedDataIntegrity));
        }
        else if (leakageStatus == ValidationLeakageAuditStatus.Passed)
        {
            rules.Add(Rule(
                "LeakageAudit", "Validation leakage audit",
                "Training", layerName, "ValidationLeakageAuditStatus",
                "Passed", "Passed", null, "==",
                QualificationRuleStatus.Passed,
                QualificationRuleApplicability.Applicable,
                "MaximumTimestampAccessedByOptimizer < ValidationStartUtc.",
                metricVersion));
        }

        if (!metricConsistencyOk)
        {
            rules.Add(Rule(
                "MetricConsistency", "Metric consistency",
                null, layerName, null,
                "Mismatch", "Consistent", null, "==",
                QualificationRuleStatus.Failed,
                QualificationRuleApplicability.Applicable,
                "Material ValidationMetricMismatch blocks Passed verdict.",
                metricVersion,
                failureDecision: StrategyRobustnessDecision.FailedDataIntegrity));
        }

        rules.Add(SampleRule(
            "TrainingSample", "Training closed-trade sample",
            "Training", layerName, "ClosedTradeCount",
            training.ClosedTradeCount, profile.MinimumTrainingClosedTrades,
            training.ClosedTradeCount >= profile.MinimumTrainingClosedTrades,
            StrategyRobustnessDecision.FailedInsufficientTrainingSample,
            metricVersion));

        rules.Add(SampleRule(
            "ValidationSample", "Validation closed-trade sample",
            "Validation", layerName, "ClosedTradeCount",
            validation.ClosedTradeCount, profile.MinimumValidationClosedTrades,
            validation.ClosedTradeCount >= profile.MinimumValidationClosedTrades,
            StrategyRobustnessDecision.FailedInsufficientValidationSample,
            metricVersion));

        var trainExp = SelectExpectancy(training, profile.ExpectancyMetric);
        var valExp = SelectExpectancy(validation, profile.ExpectancyMetric);
        var expKey = profile.ExpectancyMetric.ToString();

        rules.Add(Rule(
            "TrainingExpectancy", "Training expectancy",
            "Training", layerName, expKey,
            Format(trainExp), Format(profile.MinimumTrainingNetExpectancyR), "R", ">=",
            trainExp >= profile.MinimumTrainingNetExpectancyR
                ? QualificationRuleStatus.Passed
                : QualificationRuleStatus.Failed,
            QualificationRuleApplicability.Applicable,
            $"Training {expKey} was {Format(trainExp)}, below the required minimum of {Format(profile.MinimumTrainingNetExpectancyR)}."
                .Replace("below the required", trainExp >= profile.MinimumTrainingNetExpectancyR
                    ? "at or above the required"
                    : "below the required"),
            metricVersion,
            failureDecision: StrategyRobustnessDecision.FailedNegativeTrainingExpectancy));

        // Fix wording for pass case
        if (trainExp >= profile.MinimumTrainingNetExpectancyR)
        {
            rules[^1] = Rule(
                "TrainingExpectancy", "Training expectancy",
                "Training", layerName, expKey,
                Format(trainExp), Format(profile.MinimumTrainingNetExpectancyR), "R", ">=",
                QualificationRuleStatus.Passed,
                QualificationRuleApplicability.Applicable,
                $"Training {expKey} was {Format(trainExp)}, at or above the required minimum of {Format(profile.MinimumTrainingNetExpectancyR)}.",
                metricVersion);
        }

        if (profile.RequirePositiveValidationNetExpectancy)
        {
            var passed = valExp >= profile.MinimumValidationNetExpectancyR;
            rules.Add(Rule(
                "ValidationExpectancy", "Validation expectancy",
                "Validation", layerName, expKey,
                Format(valExp), Format(profile.MinimumValidationNetExpectancyR), "R", ">=",
                passed ? QualificationRuleStatus.Passed : QualificationRuleStatus.Failed,
                QualificationRuleApplicability.Applicable,
                passed
                    ? $"Validation {expKey} was {Format(valExp)}, at or above the required minimum of {Format(profile.MinimumValidationNetExpectancyR)}."
                    : $"Validation {expKey} was {Format(valExp)}, below the required minimum of {Format(profile.MinimumValidationNetExpectancyR)}.",
                metricVersion,
                failureDecision: StrategyRobustnessDecision.FailedNegativeValidationExpectancy));
        }

        if (profile.RequirePositiveValidationNetPnl)
        {
            var net = validation.NetPnl ?? 0m;
            var passed = net > 0m;
            rules.Add(Rule(
                "ValidationNetPnl", "Validation net PnL",
                "Validation", layerName, "NetPnl",
                Format(net), "0", "currency", ">",
                passed ? QualificationRuleStatus.Passed : QualificationRuleStatus.Failed,
                QualificationRuleApplicability.Applicable,
                passed
                    ? $"Validation NetPnl was {Format(net)}, which is positive."
                    : $"Validation NetPnl was {Format(net)}, which is not greater than 0.",
                metricVersion,
                failureDecision: StrategyRobustnessDecision.FailedNegativeValidationExpectancy));
        }

        var trainPf = SelectProfitFactor(training, profile.ProfitFactorMetric);
        var valPf = SelectProfitFactor(validation, profile.ProfitFactorMetric);
        var pfKey = profile.ProfitFactorMetric.ToString();

        rules.Add(Rule(
            "TrainingProfitFactor", "Training profit factor",
            "Training", layerName, pfKey,
            Format(trainPf), Format(profile.MinimumTrainingProfitFactor), "ratio", ">=",
            trainPf >= profile.MinimumTrainingProfitFactor
                ? QualificationRuleStatus.Passed
                : QualificationRuleStatus.Warning,
            QualificationRuleApplicability.Applicable,
            $"Training {pfKey} was {Format(trainPf)} (minimum {Format(profile.MinimumTrainingProfitFactor)}).",
            metricVersion));

        rules.Add(Rule(
            "ValidationProfitFactor", "Validation profit factor",
            "Validation", layerName, pfKey,
            Format(valPf), Format(profile.MinimumValidationProfitFactor), "ratio", ">=",
            valPf >= profile.MinimumValidationProfitFactor
                ? QualificationRuleStatus.Passed
                : QualificationRuleStatus.Warning,
            QualificationRuleApplicability.Applicable,
            $"Validation {pfKey} was {Format(valPf)} (minimum {Format(profile.MinimumValidationProfitFactor)}).",
            metricVersion));

        var dd = validation.MaximumRealizedDrawdownPercent ?? 0m;
        rules.Add(Rule(
            "ValidationDrawdown", "Validation drawdown",
            "Validation", layerName, "MaximumRealizedDrawdownPercent",
            Format(dd), Format(profile.MaximumValidationDrawdownPercent), "percent", "<=",
            dd <= profile.MaximumValidationDrawdownPercent
                ? QualificationRuleStatus.Passed
                : QualificationRuleStatus.Failed,
            QualificationRuleApplicability.Applicable,
            dd <= profile.MaximumValidationDrawdownPercent
                ? $"Validation drawdown {Format(dd)}% within limit {Format(profile.MaximumValidationDrawdownPercent)}%."
                : $"Validation drawdown {Format(dd)}% exceeds limit {Format(profile.MaximumValidationDrawdownPercent)}%.",
            metricVersion,
            failureDecision: StrategyRobustnessDecision.FailedExcessiveValidationDrawdown));

        if (trainExp > 0m && valExp < 0m)
        {
            rules.Add(Rule(
                "PerformanceCollapse", "Performance collapse",
                null, layerName, expKey,
                $"train={Format(trainExp)},val={Format(valExp)}", "val>=0 when train>0", "R", null,
                QualificationRuleStatus.Failed,
                QualificationRuleApplicability.Applicable,
                $"Performance collapse: training {expKey} positive ({Format(trainExp)}) but validation {expKey} negative ({Format(valExp)}).",
                metricVersion,
                failureDecision: StrategyRobustnessDecision.FailedPerformanceCollapse));
        }

        if (training.OpportunityRatePer1000Candles > 0m)
        {
            var retention = validation.OpportunityRatePer1000Candles / training.OpportunityRatePer1000Candles * 100m;
            var passed = retention >= profile.MinimumOpportunityRetentionPercent;
            rules.Add(Rule(
                "OpportunityRetention", "Opportunity retention",
                null, layerName, "OpportunityRatePer1000Candles",
                Format(retention), Format(profile.MinimumOpportunityRetentionPercent), "percent", ">=",
                passed ? QualificationRuleStatus.Passed : QualificationRuleStatus.Failed,
                QualificationRuleApplicability.Applicable,
                passed
                    ? $"Opportunity retention {Format(retention)}% meets minimum {Format(profile.MinimumOpportunityRetentionPercent)}%."
                    : $"Opportunity retention {Format(retention)}% below minimum {Format(profile.MinimumOpportunityRetentionPercent)}%.",
                metricVersion,
                failureDecision: StrategyRobustnessDecision.FailedOpportunityCollapse));
        }

        // Mode-aware parameter stability
        if (experimentType == ValidationExperimentType.ValidateExistingFrozenConfiguration
            || stabilityApplicability == ParameterStabilityApplicability.NotApplicable)
        {
            rules.Add(Rule(
                "ParameterStability", "Parameter stability",
                "Training", layerName, "ParameterStabilityApplicability",
                "NotApplicable", "NotApplicable", null, "==",
                QualificationRuleStatus.NotApplicable,
                QualificationRuleApplicability.NotApplicableForExperimentType,
                "No parameter search was performed. A single frozen configuration cannot be evaluated for neighborhood stability.",
                metricVersion));
        }
        else if (stabilityApplicability == ParameterStabilityApplicability.InsufficientTrials)
        {
            var status = profile.RequireParameterStability
                ? QualificationRuleStatus.Failed
                : QualificationRuleStatus.Warning;
            rules.Add(Rule(
                "ParameterStability", "Parameter stability",
                "Training", layerName, "ParameterStabilityApplicability",
                "InsufficientTrials", "Evaluated", null, "==",
                status,
                QualificationRuleApplicability.InsufficientSample,
                "Insufficient trials for parameter stability evaluation.",
                metricVersion,
                failureDecision: profile.RequireParameterStability
                    ? StrategyRobustnessDecision.FailedParameterInstability
                    : null));
        }
        else if (profile.RequireParameterStability)
        {
            rules.Add(Rule(
                "ParameterStability", "Parameter stability",
                "Training", layerName, "IsStable",
                parameterStabilityOk ? "true" : "false", "true", null, "==",
                parameterStabilityOk ? QualificationRuleStatus.Passed : QualificationRuleStatus.Failed,
                QualificationRuleApplicability.Applicable,
                parameterStabilityOk
                    ? "Parameter neighborhood stability passed."
                    : "Parameter neighborhood stability failed.",
                metricVersion,
                failureDecision: StrategyRobustnessDecision.FailedParameterInstability));
        }
        else
        {
            rules.Add(Rule(
                "ParameterStability", "Parameter stability",
                "Training", layerName, null,
                "Skipped", null, null, null,
                QualificationRuleStatus.NotEvaluated,
                QualificationRuleApplicability.Applicable,
                "Parameter stability not required by qualification profile.",
                metricVersion));
        }

        return rules;
    }

    public static decimal SelectExpectancy(LayerSegmentMetrics m, ExpectancyMetricType type) =>
        type switch
        {
            ExpectancyMetricType.GrossExpectancyR => m.GrossExpectancyR ?? m.AverageR ?? 0m,
            _ => m.NetExpectancyR ?? 0m
        };

    public static decimal SelectProfitFactor(LayerSegmentMetrics m, ProfitFactorMetricType type) =>
        type switch
        {
            ProfitFactorMetricType.GrossProfitFactor => m.GrossProfitFactor ?? m.ProfitFactor ?? 0m,
            _ => m.NetProfitFactor ?? m.ProfitFactor ?? 0m
        };

    private static QualificationRuleResult SampleRule(
        string key,
        string name,
        string segment,
        string layer,
        string metricKey,
        int actual,
        int limit,
        bool passed,
        StrategyRobustnessDecision failure,
        string metricVersion) =>
        Rule(
            key, name, segment, layer, metricKey,
            actual.ToString(CultureInfo.InvariantCulture),
            limit.ToString(CultureInfo.InvariantCulture),
            "count", ">=",
            passed ? QualificationRuleStatus.Passed : QualificationRuleStatus.Failed,
            QualificationRuleApplicability.Applicable,
            passed
                ? $"{segment} closed trades {actual} meets minimum {limit}."
                : $"{segment} closed trades {actual} below minimum {limit}.",
            metricVersion,
            failureDecision: failure);

    private static QualificationRuleResult Rule(
        string key,
        string name,
        string? segment,
        string? layer,
        string? metricKey,
        string? actual,
        string? limit,
        string? unit,
        string? op,
        QualificationRuleStatus status,
        QualificationRuleApplicability applicability,
        string reason,
        string metricVersion,
        StrategyRobustnessDecision? failureDecision = null) =>
        new()
        {
            RuleKey = key,
            RuleName = name,
            Segment = segment,
            Layer = layer,
            MetricKey = metricKey,
            ActualValue = actual,
            LimitValue = limit,
            Unit = unit,
            ComparisonOperator = op,
            Status = status,
            Applicability = applicability,
            Reason = reason,
            MetricVersion = metricVersion
        };

    private static string Format(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);
}

public interface IValidationVerdictService
{
    RobustnessVerdictResult Evaluate(
        LayerSegmentMetrics training,
        LayerSegmentMetrics validation,
        ValidationQualificationProfile profile,
        ValidationExperimentType experimentType,
        ParameterStabilityApplicability stabilityApplicability,
        bool parameterStabilityOk = true,
        bool dataQualityOk = true,
        bool configurationMatch = true,
        CandidateReconciliationStatus? reconciliationStatus = null,
        ValidationLeakageAuditStatus? leakageStatus = null,
        bool metricConsistencyOk = true);

    RobustnessVerdictResult Recalculate(IReadOnlyList<QualificationRuleResult> persistedRules);
}

public sealed class ValidationVerdictService : IValidationVerdictService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public RobustnessVerdictResult Evaluate(
        LayerSegmentMetrics training,
        LayerSegmentMetrics validation,
        ValidationQualificationProfile profile,
        ValidationExperimentType experimentType,
        ParameterStabilityApplicability stabilityApplicability,
        bool parameterStabilityOk = true,
        bool dataQualityOk = true,
        bool configurationMatch = true,
        CandidateReconciliationStatus? reconciliationStatus = null,
        ValidationLeakageAuditStatus? leakageStatus = null,
        bool metricConsistencyOk = true)
    {
        var rules = ValidationQualificationRuleBuilder.Build(
            training,
            validation,
            profile,
            experimentType,
            stabilityApplicability,
            parameterStabilityOk,
            dataQualityOk,
            configurationMatch,
            reconciliationStatus,
            leakageStatus,
            metricConsistencyOk,
            profile.PrimaryQualificationLayer);

        return FromRules(rules);
    }

    public RobustnessVerdictResult Recalculate(IReadOnlyList<QualificationRuleResult> persistedRules) =>
        FromRules(persistedRules);

    public static RobustnessVerdictResult FromRules(IReadOnlyList<QualificationRuleResult> rules)
    {
        var failures = new List<string>();
        var legacyStrings = new List<string>();
        var collapse = false;

        foreach (var rule in rules)
        {
            legacyStrings.Add($"{rule.RuleKey}:{rule.Status}");
            if (rule.Status == QualificationRuleStatus.NotApplicable) continue;
            if (rule.Status != QualificationRuleStatus.Failed) continue;

            var decision = MapFailure(rule.RuleKey);
            failures.Add(decision.ToString());
            if (rule.RuleKey == "PerformanceCollapse") collapse = true;
        }

        StrategyRobustnessDecision decisionOut;
        string? primary = null;
        if (failures.Count == 0)
        {
            decisionOut = StrategyRobustnessDecision.Passed;
        }
        else
        {
            primary = failures[0];
            decisionOut = Enum.TryParse(primary, out StrategyRobustnessDecision parsed)
                ? parsed
                : StrategyRobustnessDecision.Invalid;
        }

        var explanation = failures.Count == 0
            ? "Primary holdout qualification passed under frozen configuration."
            : $"Primary holdout qualification failed: {string.Join("; ", failures)}. "
              + string.Join(" ", rules.Where(r => r.Status == QualificationRuleStatus.Failed
                  && r.Applicability != QualificationRuleApplicability.NotApplicableForExperimentType)
                  .Select(r => r.Reason));

        return new RobustnessVerdictResult
        {
            Decision = decisionOut,
            PrimaryFailureReason = primary,
            FailureReasons = failures.Distinct().ToList(),
            RuleResults = legacyStrings,
            StructuredRuleResults = rules,
            Explanation = explanation,
            PerformanceCollapseDetected = collapse
        };
    }

    public static string SerializeRules(IReadOnlyList<QualificationRuleResult> rules) =>
        JsonSerializer.Serialize(rules, JsonOptions);

    public static IReadOnlyList<QualificationRuleResult>? DeserializeRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            // Prefer structured; fall back to empty if legacy string array.
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array
                && doc.RootElement.GetArrayLength() > 0
                && doc.RootElement[0].ValueKind == JsonValueKind.Object)
            {
                return JsonSerializer.Deserialize<List<QualificationRuleResult>>(json, JsonOptions);
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static StrategyRobustnessDecision MapFailure(string ruleKey) =>
        ruleKey switch
        {
            "DataQuality" => StrategyRobustnessDecision.FailedDataQuality,
            "ConfigurationMatch" => StrategyRobustnessDecision.FailedConfigurationMismatch,
            "CandidateReconciliation" => StrategyRobustnessDecision.FailedDataIntegrity,
            "LeakageAudit" => StrategyRobustnessDecision.FailedDataIntegrity,
            "MetricConsistency" => StrategyRobustnessDecision.FailedDataIntegrity,
            "TrainingSample" => StrategyRobustnessDecision.FailedInsufficientTrainingSample,
            "ValidationSample" => StrategyRobustnessDecision.FailedInsufficientValidationSample,
            "TrainingExpectancy" => StrategyRobustnessDecision.FailedNegativeTrainingExpectancy,
            "ValidationExpectancy" => StrategyRobustnessDecision.FailedNegativeValidationExpectancy,
            "ValidationNetPnl" => StrategyRobustnessDecision.FailedNegativeValidationExpectancy,
            "ValidationDrawdown" => StrategyRobustnessDecision.FailedExcessiveValidationDrawdown,
            "PerformanceCollapse" => StrategyRobustnessDecision.FailedPerformanceCollapse,
            "OpportunityRetention" => StrategyRobustnessDecision.FailedOpportunityCollapse,
            "ParameterStability" => StrategyRobustnessDecision.FailedParameterInstability,
            _ => StrategyRobustnessDecision.Invalid
        };
}
