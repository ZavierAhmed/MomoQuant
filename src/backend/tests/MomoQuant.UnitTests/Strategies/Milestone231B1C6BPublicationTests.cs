using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Optimization;
using MomoQuant.Application.Optimization.Dtos;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Audit;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.Strategies;

public sealed class Milestone231B1C6BPublicationTests
{
    private const string StrategyCodeValue = "MOMO_ADAPTIVE_MTF_TREND_BREAKOUT";
    private static readonly DateTimeOffset PublicationTime =
        new(2026, 8, 1, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public async Task Publish_ValidDurableEvidence_CreatesExactQualifiedSetAndAudit()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.PublishAsync(
            fixture.Store.Experiment!.Id,
            new PublishValidationParameterSetRequest { DisplayName = "  independently approved  " });

        Assert.True(result.Succeeded, result.ErrorMessage);
        var dto = Assert.IsType<StrategyParameterSetDto>(result.Data);
        var stored = Assert.Single(fixture.Store.ParameterSets);
        Assert.Equal("independently approved", stored.Name);
        Assert.Equal(fixture.Store.Experiment.FrozenStrategyParameterSnapshotJson, stored.ParametersJson);
        Assert.Equal(StrategyParameterSetSource.ValidationLab, stored.Source);
        Assert.True(stored.IsApproved);
        Assert.Equal(ParameterSetQualificationStatus.DeploymentQualified, stored.QualificationStatus);
        Assert.Equal(fixture.Store.Experiment.Id, stored.QualificationSourceExperimentId);
        Assert.Equal(fixture.Store.Trial!.Id, stored.QualificationSourceTrialId);
        Assert.Equal(fixture.Fingerprints.ComputeFingerprintFromSnapshotJson(stored.ParametersJson),
            stored.QualificationParameterFingerprint);
        Assert.Equal(ValidationParameterSetPublicationService.EvidenceVersion,
            stored.QualificationEvidenceVersion);
        Assert.Equal(PublicationTime.UtcDateTime, stored.QualifiedAtUtc);
        Assert.Equal(PublicationTime.UtcDateTime, stored.ApprovedAtUtc);
        Assert.False(stored.IsDefaultForStrategy);
        Assert.False(stored.IsDefaultForSymbolTimeframe);
        Assert.Null(stored.TrainingMetricsJson);
        Assert.Null(stored.ValidationMetricsJson);
        Assert.Null(stored.RobustnessScore);
        Assert.Equal("DeploymentQualified", dto.QualificationStatus);
        Assert.Equal("Research", dto.ApprovalScope);
        Assert.True(dto.IsDeploymentQualified);
        Assert.Empty(dto.QualificationBlockingReasons);
        Assert.True(fixture.Store.Experiment.IsCanonical);
        Assert.Equal(fixture.Store.Experiment.Id, fixture.Store.Strategy!.CanonicalValidationExperimentId);
        Assert.True(fixture.Store.Strategy.DeploymentQualificationEligible);

        var audit = Assert.Single(fixture.Store.AuditLogs);
        Assert.Equal(ValidationParameterSetPublicationService.AuditAction, audit.Action);
        Assert.DoesNotContain("minimumStrength", audit.NewValueJson, StringComparison.Ordinal);
        using var auditJson = JsonDocument.Parse(audit.NewValueJson!);
        Assert.Equal(stored.Id, auditJson.RootElement.GetProperty("parameterSetId").GetInt64());
        Assert.Equal(stored.QualificationParameterFingerprint,
            auditJson.RootElement.GetProperty("parameterFingerprint").GetString());
        Assert.Equal(
            ["experiment", "trial", "strategy", "publication", "qualified", "canonical"],
            fixture.Store.LockOrder);
    }

    [Theory]
    [InlineData("status", ValidationParameterSetPublicationCodes.NotCompleted)]
    [InlineData("hidden", ValidationParameterSetPublicationCodes.NotPassed)]
    [InlineData("failed", ValidationParameterSetPublicationCodes.NotPassed)]
    [InlineData("conditional", ValidationParameterSetPublicationCodes.NotPassed)]
    [InlineData("incapable", ValidationParameterSetPublicationCodes.NotQualificationCapable)]
    [InlineData("type", ValidationParameterSetPublicationCodes.ExperimentTypeUnsupported)]
    [InlineData("superseded", ValidationParameterSetPublicationCodes.SelectionInvalid)]
    [InlineData("frozen", ValidationParameterSetPublicationCodes.SelectionInvalid)]
    [InlineData("selection", ValidationParameterSetPublicationCodes.SelectionInvalid)]
    [InlineData("reconciliation", ValidationParameterSetPublicationCodes.SelectionInvalid)]
    [InlineData("snapshot", ValidationParameterSetPublicationCodes.SelectionInvalid)]
    public async Task Publish_InvalidExperimentGate_FailsClosed(string mutation, string expectedCode)
    {
        var fixture = CreateFixture();
        var experiment = fixture.Store.Experiment!;
        switch (mutation)
        {
            case "status": experiment.Status = ValidationExperimentStatus.ValidationRunning; break;
            case "hidden": experiment.ValidationRevealStatus = ValidationRevealStatus.Hidden; break;
            case "failed": experiment.StrategyRobustnessDecision = StrategyRobustnessDecision.FailedDataQuality; break;
            case "conditional": experiment.StrategyRobustnessDecision = StrategyRobustnessDecision.ConditionallyPassed; break;
            case "incapable": experiment.IsQualificationCapable = false; break;
            case "type": experiment.ExperimentType = ValidationExperimentType.ValidateExistingFrozenConfiguration; break;
            case "superseded": experiment.SupersessionStatus = ValidationExperimentSupersessionStatus.Superseded; break;
            case "frozen": experiment.FrozenSnapshotValidationStatus = FrozenSnapshotValidationStatus.FingerprintMismatch; break;
            case "selection": experiment.SelectionIntegrityStatus = ValidationSelectionIntegrityStatus.NoEligibleTrial; break;
            case "reconciliation": experiment.TrialSegmentReconciliationStatus = ValidationTrialSegmentReconciliationStatus.Mismatched; break;
            case "snapshot": experiment.FrozenStrategyParameterSnapshotJson = " "; break;
        }

        await AssertBlockedAsync(fixture, expectedCode);
    }

    [Theory]
    [InlineData("missing", ValidationParameterSetPublicationCodes.SelectionInvalid)]
    [InlineData("identity", ValidationParameterSetPublicationCodes.TrialInvalid)]
    [InlineData("running", ValidationParameterSetPublicationCodes.TrialInvalid)]
    [InlineData("rejected", ValidationParameterSetPublicationCodes.TrialInvalid)]
    [InlineData("rank", ValidationParameterSetPublicationCodes.TrialInvalid)]
    [InlineData("audit-id", ValidationParameterSetPublicationCodes.AuditIncomplete)]
    [InlineData("audit-status", ValidationParameterSetPublicationCodes.AuditIncomplete)]
    public async Task Publish_InvalidSelectedTrial_FailsClosed(string mutation, string expectedCode)
    {
        var fixture = CreateFixture();
        var experiment = fixture.Store.Experiment!;
        var trial = fixture.Store.Trial!;
        switch (mutation)
        {
            case "missing": experiment.SelectedTrialId = null; break;
            case "identity": trial.ValidationExperimentId++; break;
            case "running": trial.Status = ValidationTrialStatus.Running; break;
            case "rejected": trial.GuardrailDecision = "Rejected"; break;
            case "rank": trial.TrialRankEligibility = ValidationTrialRankEligibility.Ineligible; break;
            case "audit-id": trial.AuthoritativeAuditExecutionId = null; break;
            case "audit-status": trial.AuditCompletionStatus = ValidationAuditCompletionStatus.Failed; break;
        }

        await AssertBlockedAsync(fixture, expectedCode);
    }

    [Theory]
    [InlineData("trial")]
    [InlineData("selected")]
    [InlineData("frozen")]
    [InlineData("mutated-json")]
    [InlineData("different-content")]
    public async Task Publish_FingerprintOrFrozenContentMismatch_FailsClosed(string mutation)
    {
        var fixture = CreateFixture();
        switch (mutation)
        {
            case "trial": fixture.Store.Trial!.ParameterFingerprint = "BAD"; break;
            case "selected": fixture.Store.Experiment!.SelectedTrialParameterFingerprint = "BAD"; break;
            case "frozen": fixture.Store.Experiment!.FrozenParameterFingerprint = "BAD"; break;
            case "mutated-json": fixture.Store.Experiment!.FrozenStrategyParameterSnapshotJson =
                "{\"minimumStrength\":\"0.9\",\"lookback\":\"20\"}"; break;
            case "different-content": fixture.Store.Trial!.ParameterSnapshotJson =
                "{\"minimumStrength\":\"0.7\",\"lookback\":\"20\"}"; break;
        }

        await AssertBlockedAsync(fixture, ValidationParameterSetPublicationCodes.FingerprintMismatch);
    }

    [Theory]
    [InlineData("not-applicable")]
    [InlineData("ineligible")]
    [InlineData("incomplete")]
    [InlineData("failed")]
    [InlineData("superseded")]
    [InlineData("trial")]
    [InlineData("execution")]
    [InlineData("scope")]
    public async Task Publish_AuthoritativeAuditEvidenceDefect_FailsClosed(string mutation)
    {
        var fixture = CreateFixture(auditMutation: mutation);
        await AssertBlockedAsync(fixture, ValidationParameterSetPublicationCodes.AuditIncomplete);
    }

    [Fact]
    public async Task Publish_StoredPassWhoseStructuredRulesRecalculateToFailure_FailsClosed()
    {
        var fixture = CreateFixture();
        fixture.Store.Experiment!.QualificationRuleResultsJson = ValidationVerdictService.SerializeRules(
        [
            new QualificationRuleResult
            {
                RuleKey = "DataQuality",
                Status = QualificationRuleStatus.Failed,
                Reason = "Persisted evidence fails."
            }
        ]);

        await AssertBlockedAsync(fixture, ValidationParameterSetPublicationCodes.NotPassed);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disabled")]
    [InlineData("archived")]
    [InlineData("code")]
    [InlineData("version")]
    public async Task Publish_IneligibleStrategy_FailsClosed(string mutation)
    {
        var fixture = CreateFixture();
        switch (mutation)
        {
            case "missing": fixture.Store.Strategy = null; break;
            case "disabled": fixture.Store.Strategy!.IsEnabled = false; break;
            case "archived":
                fixture.Store.Experiment!.StrategyCode = "DONCHIAN_BREAKOUT";
                fixture.Store.Strategy!.Code = StrategyCode.DonchianBreakout;
                break;
            case "code": fixture.Store.Strategy!.Code = StrategyCode.MomoVolatilityRangeReversion; break;
            case "version": fixture.Store.Strategy!.Version = "9.9.9"; break;
        }

        await AssertBlockedAsync(fixture, ValidationParameterSetPublicationCodes.StrategyIneligible);
    }

    [Fact]
    public async Task Publish_SameExperiment_IsIdempotentButConflictingProvenanceFailsClosed()
    {
        var fixture = CreateFixture();
        var first = await fixture.Service.PublishAsync(fixture.Store.Experiment!.Id, new());
        Assert.True(first.Succeeded, first.ErrorMessage);

        fixture.Store.ExistingPublication = Assert.Single(fixture.Store.ParameterSets);
        var second = await fixture.Service.PublishAsync(fixture.Store.Experiment.Id, new());

        Assert.True(second.Succeeded, second.ErrorMessage);
        Assert.Equal(first.Data!.Id, second.Data!.Id);
        Assert.Single(fixture.Store.ParameterSets);
        Assert.Single(fixture.Store.AuditLogs);

        fixture.Store.ExistingPublication.QualificationSourceTrialId++;
        var conflict = await fixture.Service.PublishAsync(fixture.Store.Experiment.Id, new());
        Assert.False(conflict.Succeeded);
        Assert.Equal(ValidationParameterSetPublicationCodes.ProvenanceConflict, conflict.ErrorField);
    }

    [Fact]
    public async Task Publish_DifferentCanonicalQualificationOrHistoricalCanonical_FailsClosed()
    {
        var fixture = CreateFixture();
        fixture.Store.QualifiedPublications.Add(new StrategyParameterSet
        {
            Id = 900,
            StrategyCode = StrategyCodeValue,
            QualificationStatus = ParameterSetQualificationStatus.DeploymentQualified,
            QualificationSourceExperimentId = 999
        });

        await AssertBlockedAsync(fixture,
            ValidationParameterSetPublicationCodes.ExistingCanonicalQualification);

        fixture = CreateFixture();
        fixture.Store.CanonicalExperiments.Add(new ValidationExperiment { Id = 999, StrategyCode = StrategyCodeValue });
        await AssertBlockedAsync(fixture,
            ValidationParameterSetPublicationCodes.ExistingCanonicalQualification);
    }

    [Fact]
    public async Task Approve_AlreadyQualifiedSet_DoesNotRewriteTimestampOrProvenance()
    {
        var repository = new InMemoryStrategyParameterSetRepository();
        var qualifiedAt = new DateTime(2026, 7, 31, 1, 2, 3, DateTimeKind.Utc);
        var entity = new StrategyParameterSet
        {
            Name = "Qualified",
            StrategyCode = StrategyCodeValue,
            Timeframe = "15m",
            ParametersJson = "{\"lookback\":\"20\"}",
            Source = StrategyParameterSetSource.ValidationLab,
            IsApproved = true,
            QualificationStatus = ParameterSetQualificationStatus.DeploymentQualified,
            QualificationSourceExperimentId = 41,
            QualificationSourceTrialId = 42,
            QualificationParameterFingerprint = "ABC",
            QualificationEvidenceVersion = ValidationParameterSetPublicationService.EvidenceVersion,
            QualifiedAtUtc = qualifiedAt,
            ApprovedAtUtc = qualifiedAt,
            CreatedAtUtc = qualifiedAt
        };
        await repository.AddAsync(entity);

        var result = await new StrategyParameterSetService(repository).ApproveAsync(entity.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(qualifiedAt, entity.QualifiedAtUtc);
        Assert.Equal(qualifiedAt, entity.ApprovedAtUtc);
        Assert.Equal(41, entity.QualificationSourceExperimentId);
        Assert.Equal(42, entity.QualificationSourceTrialId);
        Assert.Equal("ABC", entity.QualificationParameterFingerprint);
    }

    [Fact]
    public async Task OrdinarySave_CannotBindValidationLabSourceOrPublicationProvenance()
    {
        var repository = new InMemoryStrategyParameterSetRepository();
        var service = new StrategyParameterSetService(repository);

        var result = await service.SaveAsync(new SaveStrategyParameterSetRequest
        {
            Name = "Forged",
            StrategyCode = StrategyCodeValue,
            Timeframe = "15m",
            Parameters = new Dictionary<string, string> { ["lookback"] = "20" },
            Source = "ValidationLab",
            Approve = true
        });

        Assert.True(result.Succeeded, result.ErrorMessage);
        var stored = Assert.IsType<StrategyParameterSet>(await repository.GetByIdAsync(result.Data!.Id));
        Assert.Equal(StrategyParameterSetSource.Manual, stored.Source);
        Assert.Equal(ParameterSetQualificationStatus.ResearchOnly, stored.QualificationStatus);
        Assert.Null(stored.QualificationSourceExperimentId);
        Assert.Null(stored.QualificationSourceTrialId);
        Assert.Null(stored.QualificationParameterFingerprint);
        Assert.Null(stored.QualificationEvidenceVersion);
        Assert.Null(stored.QualifiedAtUtc);
    }

    private static async Task AssertBlockedAsync(PublicationFixture fixture, string expectedCode)
    {
        var result = await fixture.Service.PublishAsync(fixture.Store.Experiment!.Id, new());
        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.ErrorField);
        Assert.Empty(fixture.Store.ParameterSets);
        Assert.Empty(fixture.Store.AuditLogs);
    }

    private static PublicationFixture CreateFixture(string? auditMutation = null)
    {
        var fingerprints = new ValidationParameterFingerprintService();
        const string frozen = "{\"minimumStrength\":\"0.5\",\"lookback\":\"20\"}";
        const string reordered = "{\"lookback\":\"20.0\",\"minimumStrength\":\"0.50\"}";
        var fingerprint = fingerprints.ComputeFingerprintFromSnapshotJson(frozen);
        var executionId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var experiment = new ValidationExperiment
        {
            Id = 101,
            Name = "B1C6B experiment",
            ExperimentType = ValidationExperimentType.TrainingSearchHoldoutValidation,
            Status = ValidationExperimentStatus.Completed,
            StrategyCode = StrategyCodeValue,
            StrategyVersion = "1.0.0",
            SymbolId = 7,
            Symbol = "BTCUSDT",
            Timeframe = "15m",
            ValidationRevealStatus = ValidationRevealStatus.Revealed,
            StrategyRobustnessDecision = StrategyRobustnessDecision.Passed,
            QualificationRuleResultsJson = ValidationVerdictService.SerializeRules(
            [
                new QualificationRuleResult
                {
                    RuleKey = "DataQuality",
                    Status = QualificationRuleStatus.Passed,
                    Reason = "Persisted evidence passes."
                }
            ]),
            IsQualificationCapable = true,
            SupersessionStatus = ValidationExperimentSupersessionStatus.None,
            FrozenSnapshotValidationStatus = FrozenSnapshotValidationStatus.Valid,
            SelectionIntegrityStatus = ValidationSelectionIntegrityStatus.Passed,
            TrialSegmentReconciliationStatus = ValidationTrialSegmentReconciliationStatus.Matched,
            SelectedTrialId = 202,
            SelectedTrialParameterFingerprint = fingerprint,
            FrozenStrategyParameterSnapshotJson = frozen,
            FrozenParameterFingerprint = fingerprint
        };
        var trial = new ValidationParameterTrial
        {
            Id = 202,
            ValidationExperimentId = experiment.Id,
            TrialNumber = 1,
            ParameterSnapshotJson = reordered,
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
            Version = experiment.StrategyVersion,
            IsEnabled = true
        };
        var store = new FakePublicationStore
        {
            Experiment = experiment,
            Trial = trial,
            Strategy = strategy
        };

        var evaluator = new Mock<IValidationAuthoritativeAuditQualificationEvaluator>();
        evaluator.Setup(item => item.EvaluateTrialAsync(
                It.IsAny<ValidationExperiment>(),
                It.IsAny<ValidationParameterTrial>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ValidationExperiment _, ValidationParameterTrial selected, CancellationToken _) =>
                AuditResult(selected, auditMutation));
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(item => item.UserId).Returns(17);
        currentUser.SetupGet(item => item.Email).Returns("admin@example.test");

        var service = new ValidationParameterSetPublicationService(
            store,
            fingerprints,
            evaluator.Object,
            new ValidationVerdictService(),
            currentUser.Object,
            new FixedTimeProvider(PublicationTime),
            NullLogger<ValidationParameterSetPublicationService>.Instance);
        return new PublicationFixture(service, store, fingerprints);
    }

    private static ValidationAuthoritativeAuditQualificationResult AuditResult(
        ValidationParameterTrial trial,
        string? mutation)
    {
        var result = new ValidationAuthoritativeAuditQualificationResult
        {
            IsApplicable = mutation != "not-applicable",
            IsQualificationEligible = mutation != "ineligible",
            TrialId = mutation == "trial" ? trial.Id + 1 : trial.Id,
            AuditExecutionId = mutation == "execution" ? Guid.NewGuid() : trial.AuthoritativeAuditExecutionId,
            ScopeExecutionId = mutation == "scope" ? null : Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"),
            AuthoritativeStatus = mutation switch
            {
                "failed" => ValidationAuditExecutionStatus.Failed,
                "superseded" => ValidationAuditExecutionStatus.Superseded,
                _ => ValidationAuditExecutionStatus.Completed
            },
            TrialAuditCompletionStatus = ValidationAuditCompletionStatus.Complete,
            CompletenessCode = mutation == "incomplete"
                ? ValidationAuditCompletenessCode.SequenceGap
                : ValidationAuditCompletenessCode.Complete,
            Completeness = new ValidationAuditCompletenessResult
            {
                IsAuthoritative = mutation != "incomplete",
                IsComplete = mutation != "incomplete",
                IsTerminal = true,
                CompletionCode = mutation == "incomplete"
                    ? ValidationAuditCompletenessCode.SequenceGap
                    : ValidationAuditCompletenessCode.Complete
            }
        };
        return result;
    }

    private sealed record PublicationFixture(
        ValidationParameterSetPublicationService Service,
        FakePublicationStore Store,
        ValidationParameterFingerprintService Fingerprints);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakePublicationStore : IValidationParameterSetPublicationStore
    {
        public ValidationExperiment? Experiment { get; set; }
        public ValidationParameterTrial? Trial { get; set; }
        public Strategy? Strategy { get; set; }
        public StrategyParameterSet? ExistingPublication { get; set; }
        public List<StrategyParameterSet> QualifiedPublications { get; } = [];
        public List<ValidationExperiment> CanonicalExperiments { get; } = [];
        public List<StrategyParameterSet> ParameterSets { get; } = [];
        public List<AuditLog> AuditLogs { get; } = [];
        public List<string> LockOrder { get; } = [];

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default) => action(cancellationToken);

        public Task<ValidationExperiment?> LockExperimentAsync(long experimentId, CancellationToken cancellationToken = default)
        {
            LockOrder.Add("experiment");
            return Task.FromResult(Experiment?.Id == experimentId ? Experiment : null);
        }

        public Task<ValidationParameterTrial?> LockTrialAsync(long trialId, CancellationToken cancellationToken = default)
        {
            LockOrder.Add("trial");
            return Task.FromResult(Trial?.Id == trialId ? Trial : null);
        }

        public Task<Strategy?> LockStrategyByCodeAsync(string strategyCode, CancellationToken cancellationToken = default)
        {
            LockOrder.Add("strategy");
            return Task.FromResult(Strategy);
        }

        public Task<IReadOnlyList<ValidationExperiment>> ListCanonicalExperimentsAsync(
            string strategyCode,
            CancellationToken cancellationToken = default)
        {
            LockOrder.Add("canonical");
            return Task.FromResult<IReadOnlyList<ValidationExperiment>>(CanonicalExperiments);
        }

        public Task<StrategyParameterSet?> LockPublicationByExperimentAsync(
            long experimentId,
            CancellationToken cancellationToken = default)
        {
            LockOrder.Add("publication");
            return Task.FromResult(ExistingPublication);
        }

        public Task<IReadOnlyList<StrategyParameterSet>> LockQualifiedPublicationsByStrategyAsync(
            string strategyCode,
            CancellationToken cancellationToken = default)
        {
            LockOrder.Add("qualified");
            return Task.FromResult<IReadOnlyList<StrategyParameterSet>>(QualifiedPublications);
        }

        public void AddParameterSet(StrategyParameterSet parameterSet)
        {
            parameterSet.Id = 501;
            ParameterSets.Add(parameterSet);
        }

        public void AddAuditLog(AuditLog auditLog)
        {
            auditLog.Id = 601;
            AuditLogs.Add(auditLog);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
