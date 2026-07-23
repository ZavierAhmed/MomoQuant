using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>
/// Milestone 23.0C Parts 10–15 — ValidationMetrics/v1.3.2 populations, reducer, fixtures.
/// </summary>
public class ValidationLab230CPopulationMetricsTests
{
    private readonly ValidationRiskBasisService _risk = new();
    private readonly ValidationRiskBasisStatusReducer _reducer = new();

    [Fact]
    public void AggregateStatus_IsPermutationIndependent()
    {
        var statuses = new[]
        {
            ValidationRiskBasisValidationStatus.Valid,
            ValidationRiskBasisValidationStatus.PersistedRiskMismatch,
            ValidationRiskBasisValidationStatus.MissingQuantity,
            ValidationRiskBasisValidationStatus.Valid
        };

        var permutations = Permute(statuses).ToList();
        Assert.True(permutations.Count >= 12);
        var results = permutations.Select(p => _reducer.Reduce(p)).Distinct().ToList();
        Assert.Single(results);
        Assert.Equal(ValidationRiskBasisValidationStatus.InvalidRiskBasis, results[0]);
    }

    [Fact]
    public void AggregateStatus_Precedence_InvalidOverMismatchOverInsufficientOverValid()
    {
        Assert.Equal(
            ValidationRiskBasisValidationStatus.Valid,
            _reducer.Reduce([ValidationRiskBasisValidationStatus.Valid]));
        Assert.Equal(
            ValidationRiskBasisValidationStatus.InsufficientSample,
            _reducer.Reduce(Array.Empty<ValidationRiskBasisValidationStatus>()));
        Assert.Equal(
            ValidationRiskBasisValidationStatus.PersistedRiskMismatch,
            _reducer.Reduce(
            [
                ValidationRiskBasisValidationStatus.Valid,
                ValidationRiskBasisValidationStatus.PersistedRiskMismatch
            ]));
        Assert.Equal(
            ValidationRiskBasisValidationStatus.InvalidRiskBasis,
            _reducer.Reduce(
            [
                ValidationRiskBasisValidationStatus.PersistedRiskMismatch,
                ValidationRiskBasisValidationStatus.NonPositiveRisk
            ]));
    }

    [Fact]
    public void SevenTradeFixture_PopulationClassification_ExactCounts()
    {
        var trades = BuildSevenTradeFixture();
        var metrics = ValidationMetricsContract.FromPathTradesV132(
            trades, candleCount: 1000, candidatePopulationCount: 7, boundaryEligibleCandidateCount: 7,
            boundaryCensored: 0, ValidationLayerType.RawStrategy, _risk, _reducer);

        Assert.Equal(ValidationMetricsContract.VersionV132, metrics.MetricsVersion);
        Assert.Equal(ValidationMetricPopulationSummary.Version, metrics.PopulationContractVersion);

        Assert.Equal(7, metrics.CandidatePopulationCount);
        Assert.Equal(7, metrics.BoundaryEligibleCandidateCount);
        Assert.Equal(7, metrics.PathInputPopulationCount);
        Assert.Equal(6, metrics.IncludedPathInputCount);
        Assert.Equal(1, metrics.ExcludedPathInputCount);
        Assert.Equal(1, metrics.MetricWarningBearingIncludedTradeCount);
        Assert.Equal(5, metrics.ClosedOutcomePopulationCount);
        Assert.Equal(5, metrics.MonetaryPnlPopulationCount);
        Assert.Equal(3, metrics.GrossRPopulationCount);
        Assert.Equal(3, metrics.NetRPopulationCount);
        Assert.Equal(3, metrics.WinnerPopulationCount);
        Assert.Equal(2, metrics.LoserPopulationCount);
        Assert.Equal(0, metrics.NeutralPopulationCount);

        Assert.Equal(6, metrics.MetricIncludedCandidateCount);
        Assert.Equal(1, metrics.MetricExcludedCandidateCount);
        Assert.Equal(1, metrics.ExclusionCountsByReason!["MissingPathQuantity"]);
        Assert.Equal(1, metrics.WarningCountsByCode![ValidationPathMetricWarningCodes.CandidateRawPnlReconciliationMismatch]);
        Assert.Equal(ValidationRiskBasisValidationStatus.InvalidRiskBasis, metrics.RiskBasisValidationStatus);
    }

    [Fact]
    public void SevenTradeFixture_ExactMetricValues()
    {
        var trades = BuildSevenTradeFixture();
        var metrics = ValidationMetricsContract.FromPathTradesV132(
            trades, 1000, 7, 7, 0, ValidationLayerType.RawStrategy, _risk, _reducer);

        // A:2/1.8  B:-1/-1.2  C:2/1.8  D:-1/-1.1  E:5/4.5
        Assert.Equal(7m, metrics.GrossPnl);
        Assert.Equal(5.8m, metrics.NetPnl);
        Assert.Equal(1.2m, metrics.TransactionCosts);
        Assert.Equal(9m, metrics.GrossProfit);
        Assert.Equal(2m, metrics.GrossLoss);
        Assert.Equal(8.1m, metrics.NetProfit);
        Assert.Equal(2.3m, metrics.NetLoss);
        Assert.Equal(4.5m, metrics.GrossProfitFactor);
        Assert.Equal(Math.Round(8.1m / 2.3m, 8), metrics.NetProfitFactor);
        Assert.Equal(1m, metrics.GrossExpectancyR);
        Assert.Equal(0.8m, metrics.NetExpectancyR);

        Assert.Equal(ValidationMetricApplicability.Evaluated, metrics.MonetaryPnlApplicability);
        Assert.Equal(ValidationMetricApplicability.Evaluated, metrics.GrossProfitFactorApplicability);
        Assert.Equal(ValidationMetricApplicability.Evaluated, metrics.NetProfitFactorApplicability);
        Assert.Equal(ValidationMetricApplicability.Evaluated, metrics.GrossExpectancyApplicability);
        Assert.Equal(ValidationMetricApplicability.Evaluated, metrics.NetExpectancyApplicability);
    }

    [Fact]
    public void WinnerLoser_UsesNetPnlSign_NotFreeTextOutcome()
    {
        // Trade C has Outcome="Loser" but NetPnl > 0 → counted as Winner.
        var trades = BuildSevenTradeFixture();
        Assert.Equal("Loser", trades.Single(t => t.CandidateFingerprint == "C").Outcome);

        var metrics = ValidationMetricsContract.FromPathTradesV132(
            trades, 1000, 7, 7, 0, ValidationLayerType.RawStrategy, _risk, _reducer);
        Assert.Equal(3, metrics.WinnerCount);
        Assert.Equal(2, metrics.LoserCount);
        Assert.Equal(3, metrics.WinnerPopulationCount);
        Assert.Equal(2, metrics.LoserPopulationCount);
    }

    [Fact]
    public void PnLPopulation_IncludesMismatchAndInvalidRisk_RPopulationDoesNot()
    {
        var trades = BuildSevenTradeFixture();
        var metrics = ValidationMetricsContract.FromPathTradesV132(
            trades, 1000, 7, 7, 0, ValidationLayerType.RawStrategy, _risk, _reducer);

        Assert.Equal(5, metrics.MonetaryPnlPopulationCount);
        Assert.Equal(3, metrics.GrossRPopulationCount);
        Assert.Equal(3, metrics.NetRPopulationCount);
        // D (mismatch) + E (invalid) contribute to PnL but not R
        Assert.True(metrics.MonetaryPnlPopulationCount > metrics.GrossRPopulationCount);
    }

    [Fact]
    public void PathExclusionAndWarningCounts_PersistedOnMetrics()
    {
        var trades = BuildSevenTradeFixture();
        var metrics = ValidationMetricsContract.FromPathTradesV132(
            trades, 1000, 7, 7, 0, ValidationLayerType.RawStrategy, _risk, _reducer);

        Assert.Equal(1, metrics.ExcludedPathInputCount);
        Assert.Equal(1, metrics.ExclusionCountsByReason!["MissingPathQuantity"]);
        Assert.Equal(1, metrics.MetricWarningBearingIncludedTradeCount);
        Assert.Contains(
            ValidationPathMetricWarningCodes.CandidateRawPnlReconciliationMismatch,
            metrics.MetricWarningCodes!);
    }

    [Fact]
    public void SegmentWriter_PreservesCalculatedCounts_DoesNotOverwriteWithCandidateCount()
    {
        // Simulate writer rule: for v1.3.2, MetricIncluded = path included (6), not candidatePopulation (7).
        var trades = BuildSevenTradeFixture();
        var metrics = ValidationMetricsContract.FromPathTradesV132(
            trades, 1000, candidatePopulationCount: 7, boundaryEligibleCandidateCount: 7,
            0, ValidationLayerType.RawStrategy, _risk, _reducer);

        const int callerCandidateCount = 7;
        Assert.NotEqual(callerCandidateCount, metrics.MetricIncludedCandidateCount);
        Assert.Equal(6, metrics.MetricIncludedCandidateCount);
        Assert.Equal(1, metrics.MetricExcludedCandidateCount);

        // Fingerprint uses path counts, not caller candidate count as included.
        var fpFields = ValidationMetricsContract.BuildPathResultFingerprintFields(
            ValidationSegmentType.Validation, ValidationLayerType.RawStrategy, metrics);
        Assert.Equal("6", fpFields["includedPath"]);
        Assert.Equal("1", fpFields["excludedPath"]);
        Assert.Equal("7", fpFields["candidatePopulation"]);
    }

    [Fact]
    public void ResultFingerprint_IsSensitiveToPopulationFields()
    {
        var trades = BuildSevenTradeFixture();
        var metrics = ValidationMetricsContract.FromPathTradesV132(
            trades, 1000, 7, 7, 0, ValidationLayerType.RawStrategy, _risk, _reducer);

        var baseFields = ValidationMetricsContract.BuildPathResultFingerprintFields(
            ValidationSegmentType.Validation, ValidationLayerType.RawStrategy, metrics);
        var baseFp = ValidationLabService.ParameterFingerprint(baseFields);

        var altered = new Dictionary<string, string>(baseFields)
        {
            ["pnlPopulation"] = "4"
        };
        var alteredFp = ValidationLabService.ParameterFingerprint(altered);
        Assert.NotEqual(baseFp, alteredFp);

        var alteredR = new Dictionary<string, string>(baseFields)
        {
            ["grossRPopulation"] = "2"
        };
        Assert.NotEqual(baseFp, ValidationLabService.ParameterFingerprint(alteredR));
    }

    [Fact]
    public void SevenTradeFixture_AllPermutations_ProduceIdenticalResults()
    {
        var trades = BuildSevenTradeFixture();
        var orderings = Permute(trades).ToList();
        Assert.Equal(5040, orderings.Count);

        string? canonicalHash = null;
        LayerSegmentMetrics? first = null;
        foreach (var ordering in orderings)
        {
            var metrics = ValidationMetricsContract.FromPathTradesV132(
                ordering, 1000, 7, 7, 0, ValidationLayerType.RawStrategy, _risk, _reducer);
            var json = ValidationMetricsContract.SerializeMetrics(metrics);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

            if (canonicalHash is null)
            {
                canonicalHash = hash;
                first = metrics;
            }
            else
            {
                Assert.Equal(canonicalHash, hash);
            }
        }

        Assert.NotNull(first);
        Assert.Equal(ValidationRiskBasisValidationStatus.InvalidRiskBasis, first!.RiskBasisValidationStatus);
        Assert.Equal(5.8m, first.NetPnl);
        Assert.Equal(1m, first.GrossExpectancyR);
        Assert.Equal(0.8m, first.NetExpectancyR);
    }

    [Fact]
    public void HistoricalVersionRouting_V131_StillDeserializes_AndFromPathTradesV13Unchanged()
    {
        Assert.True(ValidationMetricsContract.IsPathMetricsVersion(ValidationMetricsContract.VersionV13));
        Assert.True(ValidationMetricsContract.IsPathMetricsVersion(ValidationMetricsContract.VersionV131));
        Assert.True(ValidationMetricsContract.IsPathMetricsVersion(ValidationMetricsContract.VersionV132));
        Assert.False(ValidationMetricsContract.IsPopulationPathMetricsVersion(ValidationMetricsContract.VersionV131));
        Assert.True(ValidationMetricsContract.IsPopulationPathMetricsVersion(ValidationMetricsContract.VersionV132));
        Assert.Equal(ValidationMetricsContract.VersionV132, ValidationMetricsContract.Current);

        var historicalJson = """
            {
              "candleCount": 100,
              "candidateCount": 1,
              "closedTradeCount": 1,
              "winnerCount": 1,
              "loserCount": 0,
              "grossExpectancyR": 2,
              "netExpectancyR": 1.9,
              "metricsVersion": "ValidationMetrics/v1.3.1",
              "metricWarningBearingIncludedTradeCount": 1,
              "metricWarningCodes": ["CandidateRawPnlReconciliationMismatch"]
            }
            """;
        var deserialized = ValidationMetricsContract.DeserializeMetrics(historicalJson);
        Assert.NotNull(deserialized);
        Assert.Equal(ValidationMetricsContract.VersionV131, deserialized!.MetricsVersion);
        Assert.Equal(1, deserialized.MetricWarningBearingIncludedTradeCount);
        Assert.Null(deserialized.Population);

        var trade = BuildTrade("H", 100m, 99m, 1m, 2m, 1.9m, 0.1m, "Winner", riskAtEntry: 1m);
        var v131 = ValidationMetricsContract.FromPathTradesV13(
            [trade], 100, 1, 0, ValidationLayerType.RawStrategy, _risk);
        Assert.Equal(ValidationMetricsContract.VersionV131, v131.MetricsVersion);
        Assert.Null(v131.Population);
    }

    [Fact]
    public void Export_IncludesPopulationFields_InJsonAndCsv()
    {
        var trades = BuildSevenTradeFixture();
        var metrics = ValidationMetricsContract.FromPathTradesV132(
            trades, 1000, 7, 7, 0, ValidationLayerType.RawStrategy, _risk, _reducer);
        var dto = new ValidationSegmentResultDto
        {
            SegmentType = ValidationSegmentType.Validation,
            LayerType = ValidationLayerType.RawStrategy,
            MetricsJson = ValidationMetricsContract.SerializeMetrics(metrics),
            ClosedTradeCount = metrics.ClosedTradeCount,
            GrossExpectancyR = metrics.GrossExpectancyR,
            NetExpectancyR = metrics.NetExpectancyR,
            GrossProfitFactor = metrics.GrossProfitFactor,
            NetProfitFactor = metrics.NetProfitFactor,
            GrossPnl = metrics.GrossPnl,
            NetPnl = metrics.NetPnl,
            TransactionCosts = metrics.TransactionCosts,
            ResultCalculationVersion = metrics.MetricsVersion,
            MetricWarningBearingIncludedTradeCount = metrics.MetricWarningBearingIncludedTradeCount,
            MetricWarningCodes = metrics.MetricWarningCodes,
            Population = metrics.Population,
            CandidatePopulationCount = metrics.CandidatePopulationCount,
            BoundaryEligibleCandidateCount = metrics.BoundaryEligibleCandidateCount,
            PathInputPopulationCount = metrics.PathInputPopulationCount,
            IncludedPathInputCount = metrics.IncludedPathInputCount,
            ExcludedPathInputCount = metrics.ExcludedPathInputCount,
            ClosedOutcomePopulationCount = metrics.ClosedOutcomePopulationCount,
            MonetaryPnlPopulationCount = metrics.MonetaryPnlPopulationCount,
            GrossRPopulationCount = metrics.GrossRPopulationCount,
            NetRPopulationCount = metrics.NetRPopulationCount,
            WinnerPopulationCount = metrics.WinnerPopulationCount,
            LoserPopulationCount = metrics.LoserPopulationCount,
            NeutralPopulationCount = metrics.NeutralPopulationCount,
            PopulationContractVersion = metrics.PopulationContractVersion,
            ExclusionCountsByReason = metrics.ExclusionCountsByReason,
            WarningCountsByCode = metrics.WarningCountsByCode,
            RiskBasisValidationStatus = metrics.RiskBasisValidationStatus,
            MonetaryPnlApplicability = metrics.MonetaryPnlApplicability,
            GrossExpectancyApplicability = metrics.GrossExpectancyApplicability,
            NetExpectancyApplicability = metrics.NetExpectancyApplicability
        };

        var detail = new ValidationExperimentDetailDto
        {
            Id = 1,
            Name = "fixture",
            ValidationMetricsVersion = ValidationMetricsContract.VersionV132,
            SegmentResults = [dto],
            CandleDataSnapshotJson = "{}",
            WarmupSnapshotJson = "{}",
            ParameterSearchSpaceSnapshotJson = "{}",
            OptimizationObjectiveSnapshotJson = "{}",
            QualificationProfileSnapshotJson = "{}",
            DiagnosticsJson = "[]",
            DraftConfigurationJson = "{}"
        };

        var envelope = ValidationLabExportBuilder.BuildCompleteEnvelope(detail);
        var json = JsonSerializer.Serialize(envelope);
        Assert.Contains("ValidationMetricPopulation/v1", json, StringComparison.Ordinal);
        Assert.Contains("monetaryPnlPopulationCount", json, StringComparison.OrdinalIgnoreCase);

        var csv = ValidationLabExportBuilder.BuildCsvBundle(detail);
        Assert.Contains("PopulationContractVersion", csv, StringComparison.Ordinal);
        Assert.Contains("MonetaryPnlPopulationCount", csv, StringComparison.Ordinal);
        Assert.Contains("GrossRPopulationCount", csv, StringComparison.Ordinal);
        Assert.Contains("ValidationMetricPopulation/v1", csv, StringComparison.Ordinal);
        Assert.Contains("MissingPathQuantity=1", csv, StringComparison.Ordinal);
    }

    /// <summary>
    /// Controlled A–G fixture (Milestone 23.0C Part 15).
    /// </summary>
    public static IReadOnlyList<ValidationPathTradeMetricInput> BuildSevenTradeFixture() =>
    [
        // A: valid included winner — all populations
        BuildTrade("A", 100m, 99m, 1m, 2m, 1.8m, 0.2m, "Winner", riskAtEntry: 1m),
        // B: valid included loser — all populations
        BuildTrade("B", 100m, 99m, 1m, -1m, -1.2m, 0.2m, "Loser", riskAtEntry: 1m),
        // C: included with warning; Outcome free-text says Loser but NetPnl > 0 → Winner
        BuildTrade("C", 100m, 99m, 1m, 2m, 1.8m, 0.2m, "Loser", riskAtEntry: 1m,
            warnings: [ValidationPathMetricWarningCodes.CandidateRawPnlReconciliationMismatch]),
        // D: PersistedRiskMismatch — monetary yes, R no
        BuildTrade("D", 100m, 99m, 1m, -1m, -1.1m, 0.1m, "Loser", riskAtEntry: 0.49m),
        // E: invalid risk (non-positive stop distance) — monetary yes, R no
        BuildTrade("E", 100m, 100m, 1m, 5m, 4.5m, 0.5m, "Winner", riskAtEntry: null),
        // F: excluded missing quantity
        new ValidationPathTradeMetricInput
        {
            CandidateFingerprint = "F",
            ValidationLayer = ValidationLayerType.RawStrategy,
            EntryPrice = 100m,
            StopPriceAtEntry = 99m,
            Quantity = 0m,
            GrossPnl = 1m,
            NetPnl = 1m,
            TotalTransactionCosts = 0m,
            Outcome = "Winner",
            ExitPrice = 101m,
            MetricInclusionStatus = ValidationPathMetricInclusionStatus.Excluded,
            MetricExclusionReason = "MissingPathQuantity"
        },
        // G: open / unresolved — no closed PnL
        new ValidationPathTradeMetricInput
        {
            CandidateFingerprint = "G",
            ValidationLayer = ValidationLayerType.RawStrategy,
            EntryPrice = 100m,
            StopPriceAtEntry = 99m,
            Quantity = 1m,
            ContractMultiplier = 1m,
            RiskAmountAtEntry = 1m,
            GrossPnl = 0m,
            NetPnl = 0m,
            TotalTransactionCosts = 0m,
            Outcome = "Open",
            ExitPrice = null,
            MetricInclusionStatus = ValidationPathMetricInclusionStatus.Included
        }
    ];

    private static ValidationPathTradeMetricInput BuildTrade(
        string fingerprint,
        decimal entry,
        decimal stop,
        decimal qty,
        decimal gross,
        decimal net,
        decimal costs,
        string outcome,
        decimal? riskAtEntry,
        IReadOnlyList<string>? warnings = null) => new()
    {
        CandidateFingerprint = fingerprint,
        ValidationLayer = ValidationLayerType.RawStrategy,
        EntryPrice = entry,
        StopPriceAtEntry = stop,
        Quantity = qty,
        ContractMultiplier = 1m,
        RiskAmountAtEntry = riskAtEntry,
        GrossPnl = gross,
        NetPnl = net,
        TotalTransactionCosts = costs,
        Outcome = outcome,
        ExitPrice = entry + (gross >= 0 ? 1m : -1m),
        MetricInclusionStatus = ValidationPathMetricInclusionStatus.Included,
        MetricWarningCodes = warnings ?? Array.Empty<string>(),
        PnlCurrency = "USDT",
        RiskCurrency = "USDT"
    };

    private static IEnumerable<IReadOnlyList<T>> Permute<T>(IReadOnlyList<T> items)
    {
        var arr = items.ToArray();
        foreach (var p in PermuteArray(arr, 0))
        {
            yield return p;
        }
    }

    private static IEnumerable<T[]> PermuteArray<T>(T[] items, int start)
    {
        if (start >= items.Length)
        {
            yield return (T[])items.Clone();
            yield break;
        }

        for (var i = start; i < items.Length; i++)
        {
            (items[start], items[i]) = (items[i], items[start]);
            foreach (var p in PermuteArray(items, start + 1))
            {
                yield return p;
            }

            (items[start], items[i]) = (items[i], items[start]);
        }
    }
}
