using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.ValidationLab;

public class ValidationLab230PathMetricsTests
{
    private readonly ValidationPathMetricInputBuilder _builder = new();
    private readonly ValidationRiskBasisService _risk = new();
    private static readonly ValidationPathMetricCostModel DefaultCosts = new()
    {
        EntryFeeRate = 0.0004m,
        ExitFeeRate = 0.0004m,
        SlippagePercent = 0m,
        ContractMultiplier = 1m
    };

    [Fact]
    public void RawStrategy_IndependentOneUnit_FromPricesAndFees()
    {
        // Long: entry 100, exit 102, stop 99, qty 1 → gross 2
        // fees: 100*0.0004 + 102*0.0004 = 0.0808 → net 1.9192; risk 1 → GrossR 2, NetR 1.9192
        var candidates = new[]
        {
            ClosedCandidate("W", entry: 100m, stop: 99m, exit: 102m, proposedQty: 5m)
        };

        var trades = _builder.Build(
            1, ValidationSegmentType.Validation, ValidationLayerType.RawStrategy,
            candidates, null, null, DefaultCosts);
        Assert.Single(trades);
        Assert.Equal(1m, trades[0].Quantity);
        Assert.Equal(2m, trades[0].GrossPnl);
        Assert.Equal(0.04m, trades[0].EntryCosts);
        Assert.Equal(0.0408m, trades[0].ExitCosts);
        Assert.Equal(0.0808m, trades[0].TotalTransactionCosts);
        Assert.Equal(1.9192m, trades[0].NetPnl);

        var w = _risk.ComputePathTradeBasis(trades[0]);
        Assert.Equal(1m, w.DerivedRiskAmount);
        Assert.Equal(2m, w.GrossRMultiple);
        Assert.Equal(1.9192m, w.NetRMultiple);

        var metrics = ValidationMetricsContract.FromPathTradesV13(
            trades, 1000, 1, 0, ValidationLayerType.RawStrategy, _risk);
        Assert.Equal(ValidationMetricsContract.VersionV131, metrics.MetricsVersion);
        Assert.Equal(ValidationRiskBasisType.NormalizedOneUnit, metrics.RiskBasisType);
        Assert.Equal(2m, metrics.GrossExpectancyR);
        Assert.Equal(1.9192m, metrics.NetExpectancyR);
    }

    [Fact]
    public void ConfidenceQualified_SameEconomicsAsRaw_FilteredByConfidence()
    {
        var approved = ClosedCandidate("A", 100m, 99m, 102m, confidence: ResearchConfidenceDecision.Approved);
        var rejected = ClosedCandidate("B", 100m, 99m, 102m, confidence: ResearchConfidenceDecision.Rejected);

        var raw = _builder.Build(
            1, ValidationSegmentType.Training, ValidationLayerType.RawStrategy,
            [approved, rejected], null, null, DefaultCosts);
        var conf = _builder.Build(
            1, ValidationSegmentType.Training, ValidationLayerType.ConfidenceQualified,
            [approved, rejected], null, null, DefaultCosts);

        Assert.Equal(2, raw.Count);
        Assert.Single(conf);
        Assert.Equal(raw[0].GrossPnl, conf[0].GrossPnl);
        Assert.Equal(raw[0].NetPnl, conf[0].NetPnl);
        Assert.Equal(raw[0].TotalTransactionCosts, conf[0].TotalTransactionCosts);
        Assert.Equal("A", conf[0].CandidateFingerprint);
    }

    [Fact]
    public void RawStrategy_DoesNotDivideCandidatePnlByProposedSize()
    {
        // Candidate has misleading RawGrossPnl scaled by qty=5; independent calc must ignore it.
        var c = ClosedCandidate("X", 100m, 99m, 101m, proposedQty: 5m);
        c.RawGrossPnl = 999m;
        c.RawNetPnl = 888m;

        var trades = _builder.Build(
            1, ValidationSegmentType.Validation, ValidationLayerType.RawStrategy,
            [c], null, null, DefaultCosts);
        Assert.Equal(1m, trades[0].GrossPnl); // exit 101 - entry 100
        Assert.NotEqual(999m / 5m, trades[0].GrossPnl);
        Assert.Null(trades[0].MetricExclusionReason);
        Assert.Equal(ValidationPathMetricInclusionStatus.Included, trades[0].MetricInclusionStatus);
        Assert.Contains(
            ValidationPathMetricWarningCodes.CandidateRawPnlReconciliationMismatch,
            trades[0].MetricWarningCodes);
        Assert.Equal(ValidationMetricReconciliationStatus.Mismatched, trades[0].ReconciliationStatus);

        var metrics = ValidationMetricsContract.FromPathTradesV13(
            trades, 100, 1, 0, ValidationLayerType.RawStrategy, _risk);
        Assert.Equal(1, metrics.MetricWarningBearingIncludedTradeCount);
        Assert.Equal(1, metrics.NetExpectancyIncludedTradeCount);
        Assert.Equal(0, metrics.NetExpectancyExcludedTradeCount);
    }

    [Fact]
    public void RiskOnly_And_FullPipeline_UseIndependentPathData()
    {
        var candidate = ClosedCandidate("X", 100m, 99m, 102m, proposedQty: 10m);
        candidate.Id = 42;
        candidate.RiskOnlyAssessmentJson = AssessmentJson(quantity: 2m, risk: 2m);
        candidate.FullPipelineAssessmentJson = AssessmentJson(quantity: 5m, risk: 5m);

        var riskOnly = ShadowWithTrade(
            path: "RiskOnly",
            candidateId: 42,
            fingerprint: "X",
            quantity: 2m,
            gross: 4m,
            entryFee: 0.2m,
            exitFee: 0.2m,
            net: 3.6m);
        var full = ShadowWithTrade(
            path: "FullPipeline",
            candidateId: 42,
            fingerprint: "X",
            quantity: 5m,
            gross: -5m,
            entryFee: 0.5m,
            exitFee: 0.5m,
            net: -6m);

        var roTrades = _builder.Build(
            9, ValidationSegmentType.Validation, ValidationLayerType.RiskOnly, [candidate], riskOnly, full);
        var fpTrades = _builder.Build(
            9, ValidationSegmentType.Validation, ValidationLayerType.FullPipeline, [candidate], riskOnly, full);

        Assert.Equal(2m, roTrades[0].Quantity);
        Assert.Equal(5m, fpTrades[0].Quantity);
        Assert.Equal(4m, roTrades[0].GrossPnl);
        Assert.Equal(-5m, fpTrades[0].GrossPnl);
        Assert.Equal(3.6m, roTrades[0].NetPnl);
        Assert.Equal(-6m, fpTrades[0].NetPnl);

        var ro = ValidationMetricsContract.FromPathTradesV13(
            roTrades, 500, 1, 0, ValidationLayerType.RiskOnly, _risk);
        var fp = ValidationMetricsContract.FromPathTradesV13(
            fpTrades, 500, 1, 0, ValidationLayerType.FullPipeline, _risk);

        Assert.Equal(2m, ro.GrossExpectancyR);
        Assert.Equal(1.8m, ro.NetExpectancyR);
        Assert.Equal(-1m, fp.GrossExpectancyR);
        Assert.Equal(-1.2m, fp.NetExpectancyR);
        Assert.NotEqual(ro.NetExpectancyR, fp.NetExpectancyR);
    }

    [Fact]
    public void PathMetrics_MissingShadowLedger_ReturnsInvalidRiskBasisNullNet()
    {
        var candidate = ClosedCandidate("Z", 100m, 99m, 101m);
        var trades = _builder.Build(
            1, ValidationSegmentType.Validation, ValidationLayerType.RiskOnly, [candidate], null, null);
        var metrics = ValidationMetricsContract.FromPathTradesV13(
            trades, 100, 1, 0, ValidationLayerType.RiskOnly, _risk);

        Assert.Empty(trades);
        Assert.Null(metrics.NetExpectancyR);
        Assert.Equal(ValidationMetricApplicability.InsufficientSample, metrics.NetExpectancyApplicability);
    }

    [Fact]
    public void PathMetrics_PersistedRiskMismatch_NullNetExpectancy()
    {
        var trade = new ValidationPathTradeMetricInput
        {
            ValidationLayer = ValidationLayerType.RiskOnly,
            CandidateFingerprint = "BAD",
            EntryPrice = 100m,
            StopPriceAtEntry = 99m,
            Quantity = 1m,
            ContractMultiplier = 1m,
            RiskAmountAtEntry = 0.49m,
            GrossPnl = -1m,
            NetPnl = -1.2m,
            TotalTransactionCosts = 0.2m,
            MetricInclusionStatus = ValidationPathMetricInclusionStatus.Included
        };

        var basis = _risk.ComputePathTradeBasis(trade);
        Assert.Equal(ValidationRiskBasisValidationStatus.PersistedRiskMismatch, basis.Status);
        Assert.Null(basis.NetRMultiple);

        var metrics = ValidationMetricsContract.FromPathTradesV13(
            [trade], 100, 1, 0, ValidationLayerType.RiskOnly, _risk);
        Assert.Null(metrics.NetExpectancyR);
        Assert.Equal(ValidationMetricApplicability.InvalidRiskBasis, metrics.NetExpectancyApplicability);
    }

    [Fact]
    public void RiskOnly_MissingPathQuantity_ExcludedNotFabricated()
    {
        var candidate = ClosedCandidate("Q", 100m, 99m, 102m);
        candidate.Id = 7;
        // Assessment missing quantity; ledger quantity 0 → exclude
        var shadow = ShadowWithTrade("RiskOnly", 7, "Q", quantity: 0m, gross: 1m, entryFee: 0m, exitFee: 0m, net: 1m);
        var trades = _builder.Build(
            1, ValidationSegmentType.Validation, ValidationLayerType.RiskOnly, [candidate], shadow, null);
        Assert.Single(trades);
        Assert.Equal(ValidationPathMetricInclusionStatus.Excluded, trades[0].MetricInclusionStatus);
        Assert.Equal("MissingPathQuantity", trades[0].MetricExclusionReason);

        var metrics = ValidationMetricsContract.FromPathTradesV13(
            trades, 100, 1, 0, ValidationLayerType.RiskOnly, _risk);
        Assert.Null(metrics.NetExpectancyR);
    }

    private static StrategyResearchCandidate ClosedCandidate(
        string fp,
        decimal entry,
        decimal stop,
        decimal exit,
        decimal? proposedQty = null,
        ResearchConfidenceDecision confidence = ResearchConfidenceDecision.Approved) => new()
    {
        SetupFingerprint = fp,
        CandidateStatus = StrategyResearchCandidateStatus.Closed,
        RawOutcomeStatus = exit >= entry ? RawOutcomeStatus.Winner : RawOutcomeStatus.Loser,
        ProposedEntryPrice = entry,
        StopLoss = stop,
        RawExitPrice = exit,
        ProposedEntryTimeUtc = DateTime.UtcNow,
        SetupDetectedAtUtc = DateTime.UtcNow,
        RawExitTimeUtc = DateTime.UtcNow,
        ProposedPositionSize = proposedQty,
        ConfidenceDecision = confidence,
        Direction = TradeDirection.Long
    };

    private static string AssessmentJson(decimal quantity, decimal risk) =>
        System.Text.Json.JsonSerializer.Serialize(new PathPortfolioAssessmentDto
        {
            Quantity = quantity,
            RiskAmount = risk,
            PortfolioPath = "Test"
        });

    private static ShadowPortfolioSummaryDto ShadowWithTrade(
        string path,
        long candidateId,
        string fingerprint,
        decimal quantity,
        decimal gross,
        decimal entryFee,
        decimal exitFee,
        decimal net) => new()
    {
        PathName = path,
        TradesOpened = 1,
        ProfitableTrades = net >= 0 ? 1 : 0,
        LosingTrades = net < 0 ? 1 : 0,
        GrossPnl = gross,
        RealizedNetPnl = net,
        TotalTransactionCosts = entryFee + exitFee,
        Ledger =
        [
            new ShadowTradeLedgerEntry
            {
                CandidateId = candidateId,
                SetupFingerprint = fingerprint,
                Direction = TradeDirection.Long,
                EntryTimeUtc = DateTime.UtcNow.AddHours(-1),
                ExitTimeUtc = DateTime.UtcNow,
                EntryPrice = 100m,
                ExitPrice = 102m,
                Quantity = quantity,
                GrossPnl = gross,
                EntryFee = entryFee,
                ExitFee = exitFee,
                TotalCost = entryFee + exitFee,
                NetPnl = net,
                ExitOutcome = "TargetHit"
            }
        ]
    };
}
