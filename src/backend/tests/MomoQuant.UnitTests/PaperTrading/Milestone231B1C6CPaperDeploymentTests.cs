using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Common;
using MomoQuant.Application.LiveMarket;
using MomoQuant.Application.MarketSituation;
using MomoQuant.Application.PaperTrading;
using MomoQuant.Application.PaperTrading.Dtos;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Domain.Risk;
using MomoQuant.Domain.Sessions;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.PaperTrading;

public sealed class Milestone231B1C6CPaperDeploymentQualificationTests
{
    [Fact]
    public async Task VerifyAsync_ExactPublication_ReturnsFrozenQualifiedBinding()
    {
        var fixture = CreateFixture();

        var result = await fixture.Verifier.VerifyAsync(101, 303, 404, "15M");

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(101, result.ParameterSetId);
        Assert.Equal(303, result.StrategyId);
        Assert.Equal(404, result.SymbolId);
        Assert.Equal("15m", result.Timeframe);
        Assert.Equal(201, result.SourceExperimentId);
        Assert.Equal(202, result.SourceTrialId);
        Assert.Equal(fixture.Fingerprint, result.ParameterFingerprint);
        Assert.Equal(ValidationParameterSetPublicationService.EvidenceVersion, result.EvidenceVersion);
        Assert.Equal("20", result.FrozenParameters["lookback"]);
        fixture.AuditEvaluator.Verify(item => item.EvaluateTrialAsync(
            fixture.Experiment,
            fixture.Trial,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("research-only", PaperDeploymentQualificationCodes.NotQualified)]
    [InlineData("historical", PaperDeploymentQualificationCodes.NotQualified)]
    [InlineData("wrong-source", PaperDeploymentQualificationCodes.NotQualified)]
    [InlineData("default", PaperDeploymentQualificationCodes.NotQualified)]
    [InlineData("missing-provenance", PaperDeploymentQualificationCodes.ProvenanceIncomplete)]
    [InlineData("evidence-version", PaperDeploymentQualificationCodes.EvidenceVersionUnsupported)]
    [InlineData("parameter-json", PaperDeploymentQualificationCodes.FingerprintMismatch)]
    [InlineData("parameter-raw-json", PaperDeploymentQualificationCodes.FingerprintMismatch)]
    [InlineData("parameter-fingerprint", PaperDeploymentQualificationCodes.FingerprintMismatch)]
    [InlineData("frozen-fingerprint", PaperDeploymentQualificationCodes.FingerprintMismatch)]
    [InlineData("trial-fingerprint", PaperDeploymentQualificationCodes.FingerprintMismatch)]
    [InlineData("symbol", PaperDeploymentQualificationCodes.ScopeMismatch)]
    [InlineData("timeframe", PaperDeploymentQualificationCodes.ScopeMismatch)]
    [InlineData("experiment-type", PaperDeploymentQualificationCodes.ExperimentInvalid)]
    [InlineData("experiment-status", PaperDeploymentQualificationCodes.ExperimentInvalid)]
    [InlineData("experiment-superseded", PaperDeploymentQualificationCodes.ExperimentInvalid)]
    [InlineData("selection", PaperDeploymentQualificationCodes.ExperimentInvalid)]
    [InlineData("trial-status", PaperDeploymentQualificationCodes.TrialInvalid)]
    [InlineData("trial-audit-cache", PaperDeploymentQualificationCodes.AuditIncomplete)]
    [InlineData("stored-verdict", PaperDeploymentQualificationCodes.VerdictNotPassed)]
    [InlineData("conditional-verdict", PaperDeploymentQualificationCodes.VerdictNotPassed)]
    [InlineData("recalculated-verdict", PaperDeploymentQualificationCodes.VerdictNotPassed)]
    [InlineData("disabled-strategy", PaperDeploymentQualificationCodes.StrategyIneligible)]
    [InlineData("ineligible-strategy", PaperDeploymentQualificationCodes.StrategyIneligible)]
    [InlineData("strategy-code", PaperDeploymentQualificationCodes.ScopeMismatch)]
    [InlineData("strategy-version", PaperDeploymentQualificationCodes.ScopeMismatch)]
    [InlineData("non-canonical", PaperDeploymentQualificationCodes.CanonicalMismatch)]
    [InlineData("canonical-id", PaperDeploymentQualificationCodes.CanonicalMismatch)]
    [InlineData("audit-evaluator", PaperDeploymentQualificationCodes.AuditIncomplete)]
    public async Task VerifyAsync_DurableEvidenceMutation_FailsClosed(string mutation, string expectedCode)
    {
        var fixture = CreateFixture();
        ApplyMutation(fixture, mutation);

        var result = await fixture.Verifier.VerifyAsync(101, 303, 404, "15m");

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.ErrorCode);
    }

    [Theory]
    [InlineData("parameter", PaperDeploymentQualificationCodes.BindingConflict)]
    [InlineData("strategy", PaperDeploymentQualificationCodes.BindingConflict)]
    [InlineData("symbol", PaperDeploymentQualificationCodes.BindingConflict)]
    [InlineData("timeframe", PaperDeploymentQualificationCodes.BindingConflict)]
    [InlineData("experiment", PaperDeploymentQualificationCodes.BindingConflict)]
    [InlineData("trial", PaperDeploymentQualificationCodes.BindingConflict)]
    [InlineData("evidence", PaperDeploymentQualificationCodes.EvidenceVersionUnsupported)]
    [InlineData("fingerprint", PaperDeploymentQualificationCodes.FingerprintMismatch)]
    public async Task VerifyAsync_DurableSessionBindingMutation_FailsClosed(string mutation, string expectedCode)
    {
        var fixture = CreateFixture();
        var binding = new PaperDeploymentStoredBinding(
            mutation == "parameter" ? 999 : 101,
            mutation == "strategy" ? 999 : 303,
            mutation == "symbol" ? 999 : 404,
            mutation == "timeframe" ? "1h" : "15m",
            mutation == "experiment" ? 999 : 201,
            mutation == "trial" ? 999 : 202,
            mutation == "fingerprint" ? "BAD" : fixture.Fingerprint,
            mutation == "evidence" ? "unsupported/v9" : ValidationParameterSetPublicationService.EvidenceVersion);

        var result = await fixture.Verifier.VerifyAsync(101, 303, 404, "15m", binding);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.ErrorCode);
    }

    private static QualificationFixture CreateFixture()
    {
        const string frozenJson = "{\"lookback\":\"20\",\"minimumStrength\":\"0.5\"}";
        const string selectedJson = "{\"minimumStrength\":\"0.50\",\"lookback\":\"20.0\"}";
        var fingerprints = new ValidationParameterFingerprintService();
        var fingerprint = fingerprints.ComputeFingerprintFromSnapshotJson(frozenJson);
        var executionId = Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");

        var parameterSet = new StrategyParameterSet
        {
            Id = 101,
            Name = "Qualified",
            StrategyCode = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout.ToCode(),
            SymbolId = 404,
            Timeframe = "15m",
            ParametersJson = frozenJson,
            Source = StrategyParameterSetSource.ValidationLab,
            IsApproved = true,
            QualificationStatus = ParameterSetQualificationStatus.DeploymentQualified,
            QualificationSourceExperimentId = 201,
            QualificationSourceTrialId = 202,
            QualificationParameterFingerprint = fingerprint,
            QualificationEvidenceVersion = ValidationParameterSetPublicationService.EvidenceVersion,
            QualifiedAtUtc = new DateTime(2026, 8, 2, 1, 2, 3, DateTimeKind.Utc),
            IsDefaultForStrategy = false,
            IsDefaultForSymbolTimeframe = false
        };
        var experiment = new ValidationExperiment
        {
            Id = 201,
            ExperimentType = ValidationExperimentType.TrainingSearchHoldoutValidation,
            Status = ValidationExperimentStatus.Completed,
            StrategyCode = parameterSet.StrategyCode,
            StrategyVersion = "1.0.0",
            SymbolId = 404,
            Timeframe = "15m",
            ValidationRevealStatus = ValidationRevealStatus.Revealed,
            StrategyRobustnessDecision = StrategyRobustnessDecision.Passed,
            QualificationRuleResultsJson = ValidationVerdictService.SerializeRules([
                new QualificationRuleResult
                {
                    RuleKey = "DataQuality",
                    Status = QualificationRuleStatus.Passed,
                    Reason = "Passed."
                }
            ]),
            IsQualificationCapable = true,
            SupersessionStatus = ValidationExperimentSupersessionStatus.None,
            FrozenSnapshotValidationStatus = FrozenSnapshotValidationStatus.Valid,
            SelectionIntegrityStatus = ValidationSelectionIntegrityStatus.Passed,
            TrialSegmentReconciliationStatus = ValidationTrialSegmentReconciliationStatus.Matched,
            SelectedTrialId = 202,
            SelectedTrialParameterSnapshotJson = selectedJson,
            SelectedTrialParameterFingerprint = fingerprint,
            FrozenStrategyParameterSnapshotJson = frozenJson,
            FrozenParameterFingerprint = fingerprint,
            IsCanonical = true
        };
        var trial = new ValidationParameterTrial
        {
            Id = 202,
            ValidationExperimentId = 201,
            TrialNumber = 1,
            ParameterSnapshotJson = selectedJson,
            ParameterFingerprint = fingerprint,
            Status = ValidationTrialStatus.Completed,
            GuardrailDecision = "Passed",
            TrialRankEligibility = ValidationTrialRankEligibility.Eligible,
            AuditCompletionStatus = ValidationAuditCompletionStatus.Complete,
            AuthoritativeAuditExecutionId = executionId
        };
        var strategy = new Strategy
        {
            Id = 303,
            Code = StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout,
            Name = "Adaptive",
            Version = "1.0.0",
            IsEnabled = true,
            DeploymentQualificationEligible = true,
            CanonicalValidationExperimentId = 201
        };

        var parameterSets = new Mock<IStrategyParameterSetRepository>();
        parameterSets.Setup(item => item.GetByIdAsync(101, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parameterSet);
        var experiments = new Mock<IValidationExperimentRepository>();
        experiments.Setup(item => item.GetByIdAsync(201, It.IsAny<CancellationToken>()))
            .ReturnsAsync(experiment);
        var trials = new Mock<IValidationParameterTrialRepository>();
        trials.Setup(item => item.GetByExperimentIdAsync(201, It.IsAny<CancellationToken>()))
            .ReturnsAsync([trial]);
        var strategies = new Mock<IStrategyRepository>();
        strategies.Setup(item => item.GetByIdAsync(303, It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategy);
        var audit = new Mock<IValidationAuthoritativeAuditQualificationEvaluator>();
        audit.Setup(item => item.EvaluateTrialAsync(
                It.IsAny<ValidationExperiment>(),
                It.IsAny<ValidationParameterTrial>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationAuthoritativeAuditQualificationResult
            {
                IsApplicable = true,
                IsQualificationEligible = true,
                TrialId = 202,
                AuditExecutionId = executionId,
                ScopeExecutionId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                AuthoritativeStatus = ValidationAuditExecutionStatus.Completed,
                TrialAuditCompletionStatus = ValidationAuditCompletionStatus.Complete,
                CompletenessCode = ValidationAuditCompletenessCode.Complete,
                Completeness = new ValidationAuditCompletenessResult
                {
                    IsAuthoritative = true,
                    IsComplete = true,
                    IsTerminal = true,
                    CompletionCode = ValidationAuditCompletenessCode.Complete
                }
            });

        var verifier = new PaperDeploymentQualificationVerifier(
            parameterSets.Object,
            experiments.Object,
            trials.Object,
            strategies.Object,
            fingerprints,
            audit.Object,
            new ValidationVerdictService(),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 2, 2, 3, 4, TimeSpan.Zero)));
        return new QualificationFixture(
            verifier,
            parameterSet,
            experiment,
            trial,
            strategy,
            audit,
            fingerprint);
    }

    private static void ApplyMutation(QualificationFixture fixture, string mutation)
    {
        switch (mutation)
        {
            case "research-only": fixture.ParameterSet.QualificationStatus = ParameterSetQualificationStatus.ResearchOnly; break;
            case "historical": fixture.ParameterSet.QualificationStatus = ParameterSetQualificationStatus.HistoricalNotEvaluated; break;
            case "wrong-source": fixture.ParameterSet.Source = StrategyParameterSetSource.Manual; break;
            case "default": fixture.ParameterSet.IsDefaultForStrategy = true; break;
            case "missing-provenance": fixture.ParameterSet.QualifiedAtUtc = null; break;
            case "evidence-version": fixture.ParameterSet.QualificationEvidenceVersion = "unsupported/v9"; break;
            case "parameter-json": fixture.ParameterSet.ParametersJson = "{\"lookback\":\"21\",\"minimumStrength\":\"0.5\"}"; break;
            case "parameter-raw-json": fixture.ParameterSet.ParametersJson = "{ \"lookback\": \"20\", \"minimumStrength\": \"0.5\" }"; break;
            case "parameter-fingerprint": fixture.ParameterSet.QualificationParameterFingerprint = "BAD"; break;
            case "frozen-fingerprint": fixture.Experiment.FrozenParameterFingerprint = "BAD"; break;
            case "trial-fingerprint": fixture.Trial.ParameterFingerprint = "BAD"; break;
            case "symbol": fixture.ParameterSet.SymbolId = 999; break;
            case "timeframe": fixture.ParameterSet.Timeframe = "1h"; break;
            case "experiment-type": fixture.Experiment.ExperimentType = ValidationExperimentType.ValidateExistingFrozenConfiguration; break;
            case "experiment-status": fixture.Experiment.Status = ValidationExperimentStatus.Failed; break;
            case "experiment-superseded": fixture.Experiment.SupersessionStatus = ValidationExperimentSupersessionStatus.Superseded; break;
            case "selection": fixture.Experiment.SelectionIntegrityStatus = ValidationSelectionIntegrityStatus.FailedParameterFingerprintMismatch; break;
            case "trial-status": fixture.Trial.Status = ValidationTrialStatus.Failed; break;
            case "trial-audit-cache": fixture.Trial.AuditCompletionStatus = ValidationAuditCompletionStatus.RecoveryRequired; break;
            case "stored-verdict": fixture.Experiment.StrategyRobustnessDecision = StrategyRobustnessDecision.FailedDataQuality; break;
            case "conditional-verdict": fixture.Experiment.StrategyRobustnessDecision = StrategyRobustnessDecision.ConditionallyPassed; break;
            case "recalculated-verdict": fixture.Experiment.QualificationRuleResultsJson = ValidationVerdictService.SerializeRules([
                new QualificationRuleResult { RuleKey = "DataQuality", Status = QualificationRuleStatus.Failed, Reason = "Failed." }
            ]); break;
            case "disabled-strategy": fixture.Strategy.IsEnabled = false; break;
            case "ineligible-strategy": fixture.Strategy.DeploymentQualificationEligible = false; break;
            case "strategy-code": fixture.ParameterSet.StrategyCode = StrategyCode.PriceStructureBreakoutRetest.ToCode(); break;
            case "strategy-version": fixture.Strategy.Version = "9.9.9"; break;
            case "non-canonical": fixture.Experiment.IsCanonical = false; break;
            case "canonical-id": fixture.Strategy.CanonicalValidationExperimentId = 999; break;
            case "audit-evaluator": fixture.AuditEvaluator.Setup(item => item.EvaluateTrialAsync(
                    It.IsAny<ValidationExperiment>(),
                    It.IsAny<ValidationParameterTrial>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ValidationAuthoritativeAuditQualificationResult.Blocked(
                    fixture.Trial.Id,
                    ValidationAuditCompletenessCode.SequenceGap,
                    "Incomplete.")); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }

    private sealed record QualificationFixture(
        PaperDeploymentQualificationVerifier Verifier,
        StrategyParameterSet ParameterSet,
        ValidationExperiment Experiment,
        ValidationParameterTrial Trial,
        Strategy Strategy,
        Mock<IValidationAuthoritativeAuditQualificationEvaluator> AuditEvaluator,
        string Fingerprint);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

public sealed class Milestone231B1C6CPaperUseClassGateTests
{
    [Fact]
    public void TryParseUseClass_OmittedValue_DefaultsToResearch()
    {
        Assert.True(PaperMapper.TryParseUseClass(null, out var useClass));
        Assert.Equal(PaperSessionUseClass.Research, useClass);
    }

    [Fact]
    public void TryParseUseClass_NumericEnumValue_IsRejected()
    {
        Assert.False(PaperMapper.TryParseUseClass("1", out _));
    }

    [Fact]
    public async Task CreateAsync_InvalidUseClass_ReturnsStableCodeWithoutCreatingRows()
    {
        var service = CreateEarlyGateService();

        var result = await service.CreateAsync(Request(useClass: "Forged", mode: "LivePaper"));

        Assert.False(result.Succeeded);
        Assert.Equal(PaperDeploymentQualificationCodes.UseClassInvalid, result.ErrorField);
    }

    [Fact]
    public async Task CreateAsync_DeploymentHistorical_ReturnsLiveModeCodeWithoutCreatingRows()
    {
        var service = CreateEarlyGateService();

        var result = await service.CreateAsync(Request(useClass: "DeploymentSimulation", mode: "HistoricalPaper"));

        Assert.False(result.Succeeded);
        Assert.Equal(PaperDeploymentQualificationCodes.LiveModeRequired, result.ErrorField);
    }

    [Theory]
    [InlineData("strategy")]
    [InlineData("symbol")]
    [InlineData("timeframe")]
    public async Task CreateAsync_DeploymentMultipleScope_ReturnsSingleScopeCode(string scope)
    {
        var service = CreateEarlyGateService();
        var request = Request(useClass: "DeploymentSimulation", mode: "LivePaper", parameterSetId: 9);
        request = new CreatePaperSessionRequest
        {
            Name = request.Name,
            PaperAccountId = request.PaperAccountId,
            ExchangeId = request.ExchangeId,
            SymbolIds = scope == "symbol" ? [3, 4] : [3],
            Timeframes = scope == "timeframe" ? ["15m", "1h"] : ["15m"],
            Mode = request.Mode,
            UseClass = request.UseClass,
            RiskProfileId = request.RiskProfileId,
            StrategyIds = scope == "strategy" ? [10, 11] : [10],
            ParameterSetId = request.ParameterSetId
        };

        var result = await service.CreateAsync(request);

        Assert.False(result.Succeeded);
        Assert.Equal(PaperDeploymentQualificationCodes.SingleScopeRequired, result.ErrorField);
    }

    [Fact]
    public async Task CreateAsync_DeploymentWithoutParameterSet_ReturnsRequiredCode()
    {
        var service = CreateEarlyGateService();

        var result = await service.CreateAsync(Request(useClass: "DeploymentSimulation", mode: "LivePaper"));

        Assert.False(result.Succeeded);
        Assert.Equal(PaperDeploymentQualificationCodes.ParameterSetRequired, result.ErrorField);
    }

    private static CreatePaperSessionRequest Request(
        string useClass,
        string mode,
        long? parameterSetId = null) => new()
    {
        Name = "B1C6C",
        PaperAccountId = 1,
        ExchangeId = 2,
        SymbolIds = [3],
        Timeframes = ["15m"],
        Mode = mode,
        UseClass = useClass,
        RiskProfileId = 4,
        StrategyIds = [10],
        ParameterSetId = parameterSetId
    };

    private static PaperSessionService CreateEarlyGateService() => new(
        Mock.Of<IPaperTradingSessionRepository>(),
        Mock.Of<IPaperAccountRepository>(),
        Mock.Of<ITradingSessionRepository>(),
        Mock.Of<IExchangeRepository>(),
        Mock.Of<ISymbolRepository>(),
        Mock.Of<IRiskProfileRepository>(),
        Mock.Of<IStrategyRepository>(),
        Mock.Of<IStrategyRegistry>(),
        Mock.Of<IStrategyParameterSetRepository>(),
        Mock.Of<IStrategyParameterProvider>(),
        Mock.Of<IRiskRuleRepository>(),
        Mock.Of<IBacktestDataLoader>(),
        Mock.Of<IPaperStateStore>(),
        Mock.Of<ILiveMarketConnectionManager>(),
        Mock.Of<IMarketSituationService>(),
        Mock.Of<ICurrentUserService>(),
        Mock.Of<IAuditService>(),
        Mock.Of<IHigherTimeframeDatasetEnricher>());
}

public sealed class Milestone231B1C6CDeploymentRuntimeGateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartOrResume_EvidenceFailure_PreservesNonRunningStateAndCreatesNoSubscription(bool resume)
    {
        var originalStatus = resume ? PaperSessionStatus.Paused : PaperSessionStatus.Created;
        var session = DeploymentSession(originalStatus);
        var repository = new Mock<IPaperTradingSessionRepository>();
        repository.Setup(item => item.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(session);
        var state = MinimalState(session);
        var stateStore = new Mock<IPaperStateStore>();
        stateStore.Setup(item => item.TryGet(1, out state)).Returns(true);
        var verifier = new Mock<IPaperDeploymentQualificationVerifier>();
        verifier.Setup(item => item.VerifyAsync(
                101, 303, 404, "15m", It.IsAny<PaperDeploymentStoredBinding>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PaperDeploymentQualificationResult.Fail(
                PaperDeploymentQualificationCodes.FingerprintMismatch,
                "Fingerprint mismatch."));
        var coordinator = new DirectCoordinator(session);
        var live = new Mock<ILiveMarketConnectionManager>();
        var tradingSessions = new Mock<ITradingSessionRepository>();
        var audit = new Mock<IAuditService>();
        var service = new PaperSessionControlService(
            repository.Object,
            tradingSessions.Object,
            stateStore.Object,
            Mock.Of<IPaperTradingEngine>(),
            Mock.Of<IPaperPersistenceService>(),
            live.Object,
            Mock.Of<ILiveMarketBootstrapService>(),
            Mock.Of<ICurrentUserService>(),
            audit.Object,
            verifier.Object,
            coordinator);

        var result = resume ? await service.ResumeAsync(1) : await service.StartAsync(1);

        Assert.False(result.Succeeded);
        Assert.Equal(PaperDeploymentQualificationCodes.FingerprintMismatch, result.ErrorField);
        Assert.Equal(originalStatus, session.Status);
        live.Verify(item => item.SubscribeAsync(It.IsAny<Application.LiveMarket.Dtos.LiveMarketSubscribeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        tradingSessions.Verify(item => item.UpdateAsync(It.IsAny<TradingSession>(), It.IsAny<CancellationToken>()), Times.Never);
        audit.Verify(item => item.LogAsync(
            "PAPER_DEPLOYMENT_QUALIFICATION_VERIFIED",
            It.IsAny<string>(),
            It.IsAny<long?>(),
            It.IsAny<long?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static PaperTradingSession DeploymentSession(PaperSessionStatus status) => new()
    {
        Id = 1,
        Name = "Deployment simulation",
        PaperAccountId = 1,
        TradingSessionId = 2,
        Status = status,
        Mode = PaperTradingMode.LivePaper,
        UseClass = PaperSessionUseClass.DeploymentSimulation,
        ParameterSetId = 101,
        BoundStrategyId = 303,
        BoundSymbolId = 404,
        BoundTimeframe = "15m",
        QualificationSourceExperimentId = 201,
        QualificationSourceTrialId = 202,
        QualificationParameterFingerprint = "ABCDEF0123456789",
        QualificationEvidenceVersion = ValidationParameterSetPublicationService.EvidenceVersion,
        QualificationVerifiedAtUtc = DateTime.UtcNow,
        ExchangeId = 2,
        RiskProfileId = 4,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static PaperSessionState MinimalState(PaperTradingSession session) => new()
    {
        Session = session,
        Account = new PaperAccount { Id = 1, Name = "Paper", CurrentBalance = 10_000m, CurrentEquity = 10_000m },
        Settings = new PaperSessionSettings
        {
            MakerFeeRate = 0,
            TakerFeeRate = 0,
            OrderExpiryCandles = 3,
            UseAiScoring = false,
            MinConfidenceScore = 80,
            SlippagePercent = 0,
            ExecutionMode = ExecutionMode.MarketFill,
            StrategyIds = [303],
            SymbolIds = [404],
            Timeframes = [Timeframe.M15],
            ParameterSetId = 101
        },
        Context = null!,
        Dataset = null!,
        Strategies = [],
        RiskRules = Array.Empty<RiskRule>()
    };

    private sealed class DirectCoordinator(PaperTradingSession session) : IPaperSessionRelationalCoordinator
    {
        public async Task<T> ExecuteCreationAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default) =>
            await action(cancellationToken);

        public async Task<T> ExecuteSerializedAsync<T>(long paperSessionId, Func<PaperTradingSession?, CancellationToken, Task<T>> action, CancellationToken cancellationToken = default) =>
            await action(session, cancellationToken);
    }
}
