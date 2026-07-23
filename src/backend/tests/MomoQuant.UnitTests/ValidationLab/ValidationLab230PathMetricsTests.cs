using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.ValidationLab;

public class ValidationLab230PathMetricsTests
{
    private readonly ValidationPathMetricInputBuilder _builder = new();
    private readonly ValidationRiskBasisService _risk = new();

    [Fact]
    public void RawStrategy_NormalizedOneUnit_ReconcilesExactly()
    {
        var candidates = new[]
        {
            ClosedCandidate("W", 100m, 99m, 2m, 1.8m, proposedQty: 5m),
            ClosedCandidate("L", 100m, 99m, -1m, -1.2m, proposedQty: 5m)
        };

        var trades = _builder.Build(1, ValidationSegmentType.Validation, ValidationLayerType.RawStrategy, candidates, null, null);
        var metrics = ValidationMetricsContract.FromPathTradesV13(
            trades, 1000, candidates.Length, 0, ValidationLayerType.RawStrategy, _risk);

        Assert.Equal(ValidationMetricsContract.VersionV13, metrics.MetricsVersion);
        Assert.Equal(ValidationRiskBasisType.NormalizedOneUnit, metrics.RiskBasisType);
        Assert.Equal(ValidationMetricApplicability.Evaluated, metrics.NetExpectancyApplicability);
        // Normalized: gross 2/5=0.4 and -1/5=-0.2 → avg R uses risk=1 → gross R 0.4 and -0.2 → avg 0.1
        // Wait: quantity=1, risk=|100-99|*1=1, normalized gross pnl = 2/5=0.4, so GrossR=0.4
        Assert.Equal(0.4m, trades[0].GrossPnl);
        Assert.Equal(0.36m, trades[0].NetPnl);
        var w = _risk.ComputePathTradeBasis(trades[0]);
        Assert.Equal(1m, w.DerivedRiskAmount);
        Assert.Equal(0.4m, w.GrossRMultiple);
        Assert.Equal(0.36m, w.NetRMultiple);
    }

    [Fact]
    public void ConfidenceQualified_ExcludesNonApproved()
    {
        var candidates = new[]
        {
            ClosedCandidate("A", 100m, 99m, 1m, 0.8m, confidence: ResearchConfidenceDecision.Approved),
            ClosedCandidate("B", 100m, 99m, 1m, 0.8m, confidence: ResearchConfidenceDecision.Rejected)
        };

        var trades = _builder.Build(
            1, ValidationSegmentType.Training, ValidationLayerType.ConfidenceQualified, candidates, null, null);
        Assert.Single(trades);
        Assert.Equal("A", trades[0].CandidateFingerprint);
    }

    [Fact]
    public void RiskOnly_And_FullPipeline_UseIndependentPathData()
    {
        var candidate = ClosedCandidate("X", 100m, 99m, 10m, 8m, proposedQty: 10m);
        candidate.Id = 42;
        candidate.RiskOnlyAssessmentJson = AssessmentJson(quantity: 2m, risk: 2m);
        candidate.FullPipelineAssessmentJson = AssessmentJson(quantity: 5m, risk: 5m);

        var riskOnly = ShadowWithTrade(
            path: "RiskOnly",
            candidateId: 42,
            fingerprint: "X",
            quantity: 2m,
            gross: 4m,
            entryFee: 0.1m,
            exitFee: 0.1m,
            net: 3.8m);
        var full = ShadowWithTrade(
            path: "FullPipeline",
            candidateId: 42,
            fingerprint: "X",
            quantity: 5m,
            gross: 10m,
            entryFee: 0.4m,
            exitFee: 0.4m,
            net: 9.2m);

        var roTrades = _builder.Build(
            9, ValidationSegmentType.Validation, ValidationLayerType.RiskOnly, [candidate], riskOnly, full);
        var fpTrades = _builder.Build(
            9, ValidationSegmentType.Validation, ValidationLayerType.FullPipeline, [candidate], riskOnly, full);

        Assert.Single(roTrades);
        Assert.Single(fpTrades);
        Assert.Equal(2m, roTrades[0].Quantity);
        Assert.Equal(5m, fpTrades[0].Quantity);
        Assert.Equal(4m, roTrades[0].GrossPnl);
        Assert.Equal(10m, fpTrades[0].GrossPnl);
        Assert.NotEqual(roTrades[0].NetPnl, fpTrades[0].NetPnl);

        var ro = ValidationMetricsContract.FromPathTradesV13(
            roTrades, 500, 1, 0, ValidationLayerType.RiskOnly, _risk);
        var fp = ValidationMetricsContract.FromPathTradesV13(
            fpTrades, 500, 1, 0, ValidationLayerType.FullPipeline, _risk);

        Assert.Equal(ValidationMetricsContract.VersionV13, ro.MetricsVersion);
        Assert.Equal(ValidationMetricsContract.VersionV13, fp.MetricsVersion);
        Assert.Equal(ValidationRiskBasisType.ShadowPortfolioPosition, ro.RiskBasisType);
        Assert.Equal(2m, ro.GrossExpectancyR); // 4 / (1*2)
        Assert.Equal(1.9m, ro.NetExpectancyR); // 3.8 / 2
        Assert.Equal(2m, fp.GrossExpectancyR); // 10 / (1*5)
        Assert.Equal(1.84m, fp.NetExpectancyR); // 9.2 / 5
        Assert.NotEqual(ro.NetExpectancyR, fp.NetExpectancyR);
        Assert.NotEqual(ro.GrossPnl, fp.GrossPnl);
        Assert.Equal(ValidationMetricApplicability.Evaluated, ro.NetExpectancyApplicability);
        Assert.Equal(ValidationMetricApplicability.Evaluated, fp.NetExpectancyApplicability);
    }

    [Fact]
    public void PathMetrics_MissingShadowLedger_ReturnsInvalidRiskBasisNullNet()
    {
        var candidate = ClosedCandidate("Z", 100m, 99m, 1m, 0.5m);
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

    private static StrategyResearchCandidate ClosedCandidate(
        string fp,
        decimal entry,
        decimal stop,
        decimal gross,
        decimal net,
        decimal? proposedQty = null,
        ResearchConfidenceDecision confidence = ResearchConfidenceDecision.Approved) => new()
    {
        SetupFingerprint = fp,
        CandidateStatus = StrategyResearchCandidateStatus.Closed,
        RawOutcomeStatus = gross >= 0 ? RawOutcomeStatus.Winner : RawOutcomeStatus.Loser,
        ProposedEntryPrice = entry,
        StopLoss = stop,
        ProposedEntryTimeUtc = DateTime.UtcNow,
        SetupDetectedAtUtc = DateTime.UtcNow,
        RawGrossPnl = gross,
        RawNetPnl = net,
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
                PortfolioPath = path,
                CandidateId = candidateId,
                SetupFingerprint = fingerprint,
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
                Direction = TradeDirection.Long,
                ExitOutcome = net >= 0 ? "Winner" : "Loser",
                GrossR = quantity > 0 ? gross / quantity : 0m
            }
        ]
    };
}
