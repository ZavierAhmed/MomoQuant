using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

public class ValidationLab223Tests
{
    [Fact]
    public void NetExpectancyR_IsAverageNotSum()
    {
        var candidates = Enumerable.Range(1, 100).Select(i => new StrategyResearchCandidate
        {
            CandidateStatus = StrategyResearchCandidateStatus.Closed,
            RawOutcomeStatus = RawOutcomeStatus.Loser,
            RawRMultiple = -0.15m,
            RawGrossPnl = -15m,
            RawNetPnl = -15.5m,
            RiskAmount = 100m,
            SetupFingerprint = $"FP{i:D3}"
        }).ToList();

        var metrics = ValidationMetricsContract.FromCandidates(candidates, 1000, 0, ValidationLayerType.RawStrategy);
        Assert.Equal(-0.155m, metrics.NetExpectancyR);
        Assert.NotEqual(-15m, metrics.NetExpectancyR);
    }

    [Fact]
    public void MetricDisplayFormatter_UsesRPerTradeUnit()
    {
        var label = ValidationMetricDisplayFormatter.FormatExpectancyR(-0.1522m);
        Assert.Contains("R/trade", label);
        Assert.Contains("-0.1522", label);
    }

    [Fact]
    public void TrialSelection_AllGuardrailRejected_IsInvalid()
    {
        var auditor = new ValidationTrialSelectionAuditor();
        var experiment = new ValidationExperiment
        {
            Id = 23,
            ExperimentType = ValidationExperimentType.TrainingSearchHoldoutValidation,
            FrozenParameterFingerprint = "ABC123",
            TrainingStrategyLabRunId = 100
        };
        var trials = Enumerable.Range(1, 5).Select(i => new ValidationParameterTrial
        {
            Id = i,
            TrialNumber = i,
            ParameterFingerprint = i == 1 ? "ABC123" : $"FP{i}",
            Status = ValidationTrialStatus.GuardrailRejected,
            GuardrailDecision = "Failed",
            StrategyLabRunId = i == 1 ? 100 : i + 200,
            GuardrailFailureReasonsJson = "[\"NetExpectancyR<0\"]"
        }).ToList();

        var audit = auditor.AuditSelection(experiment, trials);
        Assert.Equal(ValidationSelectionIntegrityStatus.InvalidSelectedTrial, audit.IntegrityStatus);
        Assert.False(audit.IsEligibleForSelection);
        Assert.Equal(StrategyRobustnessDecision.FailedNoTrainingTrialPassedGuardrails, audit.DerivedIntegrityVerdict);
    }

    [Fact]
    public void TrialSelection_GuardrailPassedWinner_IsValid()
    {
        var auditor = new ValidationTrialSelectionAuditor();
        var experiment = new ValidationExperiment
        {
            Id = 1,
            ExperimentType = ValidationExperimentType.TrainingSearchHoldoutValidation,
            FrozenParameterFingerprint = "WINNER",
            TrainingStrategyLabRunId = 10
        };
        var trials = new List<ValidationParameterTrial>
        {
            new()
            {
                Id = 1,
                TrialNumber = 1,
                ParameterFingerprint = "WINNER",
                Status = ValidationTrialStatus.Completed,
                GuardrailDecision = "Passed",
                TrainingScore = 10m,
                Rank = 1,
                StrategyLabRunId = 10
            },
            new()
            {
                Id = 2,
                TrialNumber = 2,
                ParameterFingerprint = "LOSER",
                Status = ValidationTrialStatus.GuardrailRejected,
                GuardrailDecision = "Failed",
                StrategyLabRunId = 11
            }
        };

        var audit = auditor.AuditSelection(experiment, trials);
        Assert.Equal(ValidationSelectionIntegrityStatus.Valid, audit.IntegrityStatus);
        Assert.True(audit.IsEligibleForSelection);
    }

    [Fact]
    public void ExportVerifier_RejectsPlaceholderPdf()
    {
        var verifier = new ValidationExportContentVerifier();
        var detail = new ValidationExperimentDetailDto
        {
            Id = 23,
            Name = "Test",
            ExperimentType = ValidationExperimentType.TrainingSearchHoldoutValidation,
            Status = ValidationExperimentStatus.Completed,
            StrategyCode = "PRICE_STRUCTURE_BREAKOUT_RETEST",
            StrategyVersion = "1.0.0",
            ExchangeId = 1,
            Exchange = "Binance Futures",
            SymbolId = 1,
            Symbol = "BTCUSDT",
            Timeframe = "15m",
            RequestedStartUtc = DateTime.UtcNow.AddMonths(-3),
            RequestedEndUtc = DateTime.UtcNow,
            SplitRatio = 0.7m,
            SplitAlgorithmVersion = "ChronologicalHoldout/v1",
            ValidationMetricsVersion = ValidationMetricsContract.Current,
            ValidationRevealStatus = ValidationRevealStatus.Revealed,
            SegmentResults = []
        };

        var result = verifier.Verify(detail, "{}", "training-trials.csv", "short");
        Assert.Equal(ValidationExportVerificationStatus.Failed, result.Status);
    }

    [Fact]
    public void FromShadow_DoesNotUseMonetaryAverageAsNetExpectancyR()
    {
        var shadow = new ShadowPortfolioSummaryDto
        {
            TradesOpened = 10,
            RealizedNetPnl = -1520m,
            GrossPnl = -1500m,
            TotalTransactionCosts = 20m,
            Ledger = []
        };

        var metrics = ValidationMetricsContract.FromShadow(shadow, 100, 10, 0);
        Assert.Null(metrics.NetExpectancyR);
    }

    [Fact]
    public void MetricAudit_FlagsMisScaledRiskAmount()
    {
        var candidates = Enumerable.Range(1, 10).Select(i => new StrategyResearchCandidate
        {
            CandidateStatus = StrategyResearchCandidateStatus.Closed,
            RawOutcomeStatus = RawOutcomeStatus.Loser,
            RawRMultiple = -0.10m,
            RawGrossPnl = -10m,
            RawNetPnl = -10.5m,
            RiskAmount = 0.5m,
            SetupFingerprint = $"FP{i:D3}"
        }).ToList();

        var audit = new ValidationMetricAuditService().AuditSegment(
            ValidationSegmentType.Training,
            ValidationLayerType.RawStrategy,
            candidates,
            new ValidationSegmentResult
            {
                ClosedTradeCount = 10,
                GrossExpectancyR = -0.10m,
                NetExpectancyR = -21m,
                NetPnl = -105m
            });

        Assert.True(audit.HasRiskAmountUnitWarning);
        Assert.True(audit.MatchesPersisted);
        Assert.NotNull(audit.CorrectedNetExpectancyR);
        Assert.True(Math.Abs(audit.CorrectedNetExpectancyR!.Value - (-0.105m)) < 0.001m);
    }
}
