using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Research;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

public sealed class ValidationTrainingFailureHandlerTests
{
    [Fact]
    public async Task HandleBoundaryFailure_FlushesMarksLeakageFailed_AndSafeUserError()
    {
        var audits = new FakeCandleAccessAuditRepository();
        var recorder = new ValidationCandleAccessRecorder(audits);
        var trials = new FakeTrialRepository();
        var experiments = new FakeExperimentRepository();
        var ops = new FakeOperationStatusService();
        var handler = new ValidationTrainingFailureHandler(
            recorder,
            audits,
            trials,
            experiments,
            new ValidationLeakageAuditor(),
            ops);

        var experiment = new ValidationExperiment
        {
            Id = 9,
            Status = ValidationExperimentStatus.TrainingRunning,
            SelectedTrialId = 55,
            SelectedTrialNumber = 1,
            SelectedTrialParameterFingerprint = "fp",
            TrainingStartUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            TrainingEndUtc = new DateTime(2024, 1, 7, 23, 0, 0, DateTimeKind.Utc),
            ValidationStartUtc = new DateTime(2024, 1, 8, 0, 0, 0, DateTimeKind.Utc),
            MaximumTrials = 3,
            DiagnosticsJson = "[]"
        };
        experiments.Items.Add(experiment);

        var trial = new ValidationParameterTrial
        {
            Id = 55,
            ValidationExperimentId = 9,
            TrialNumber = 1,
            Status = ValidationTrialStatus.Running,
            ParameterFingerprint = "fp"
        };
        trials.Items.Add(trial);

        var scope = CreateScope(experiment.Id, experiment.TrainingStartUtc.Value, experiment.ValidationStartUtc.Value);
        var leakage = Assert.Throws<ValidationDataLeakageException>(() =>
            scope.GetByOpenTimeUtc(experiment.ValidationStartUtc.Value, "Adversarial"));

        var result = await handler.HandleBoundaryFailureAsync(
            experiment,
            trial,
            scope,
            leakage,
            optimizerInputFingerprint: "fp",
            leaseOwner: "test-owner");

        Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, result.ErrorCode);
        Assert.Equal(ValidationTrainingFailureHandler.UserSafeLeakageMessage, result.UserSafeErrorMessage);
        Assert.DoesNotContain("Stack", result.UserSafeErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Open=", result.UserSafeErrorMessage, StringComparison.OrdinalIgnoreCase);

        Assert.Single(audits.Items);
        Assert.True(audits.Items[0].WasDenied);
        Assert.NotEqual(Guid.Empty, audits.Items[0].AccessEventId);

        Assert.Equal(ValidationTrialStatus.LeakageFailed, trial.Status);
        Assert.Equal(ValidationTrainingFailureHandler.UserSafeLeakageMessage, trial.ErrorMessage);

        Assert.Equal(ValidationExperimentStatus.Failed, experiment.Status);
        Assert.Equal(ValidationLeakageAuditStatus.Failed, experiment.LeakageAuditStatus);
        Assert.Equal("LeakageDetected", experiment.CurrentStage);
        Assert.Null(experiment.SelectedTrialId);
        Assert.Null(experiment.SelectedTrialNumber);
        Assert.Equal(ValidationSelectionIntegrityStatus.NoEligibleTrial, experiment.SelectionIntegrityStatus);
        Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, experiment.PrimaryFailureReason);
        Assert.Equal(ValidationTrainingFailureHandler.UserSafeLeakageMessage, experiment.ErrorMessage);

        Assert.NotNull(ops.Last);
        Assert.Equal(ValidationTrainingFailureCodes.ValidationDataLeakage, ops.Last!.ErrorCode);
        Assert.Equal(ValidationTrainingFailureHandler.UserSafeLeakageMessage, ops.Last.UserSafeErrorMessage);
        Assert.Equal("LeakageDetected", ops.Last.Stage);
    }

    private static ValidationTrainingCandleScope CreateScope(long experimentId, DateTime start, DateTime boundary)
    {
        var candles = new List<Candle>
        {
            new()
            {
                OpenTimeUtc = start,
                CloseTimeUtc = start.AddHours(1),
                Open = 1, High = 1, Low = 1, Close = 1, Volume = 1
            }
        };
        return new ValidationTrainingCandleScope(experimentId, start, boundary, candles);
    }

    private sealed class FakeCandleAccessAuditRepository : IValidationCandleAccessAuditRepository
    {
        public List<ValidationCandleAccessAudit> Items { get; } = [];

        public async Task AddRangeAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default) =>
            await AddRangeIdempotentByAccessEventIdAsync(audits, cancellationToken);

        public Task<int> AddRangeIdempotentByAccessEventIdAsync(
            IReadOnlyList<ValidationCandleAccessAudit> audits,
            CancellationToken cancellationToken = default)
        {
            var existing = Items.Select(i => i.AccessEventId).ToHashSet();
            var fresh = audits.Where(a => !existing.Contains(a.AccessEventId)).ToList();
            Items.AddRange(fresh);
            return Task.FromResult(fresh.Count);
        }

        public Task<IReadOnlyList<ValidationCandleAccessAudit>> GetByExperimentIdAsync(
            long experimentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationCandleAccessAudit>>(
                Items.Where(a => a.ValidationExperimentId == experimentId).ToList());
    }

    private sealed class FakeTrialRepository : IValidationParameterTrialRepository
    {
        public List<ValidationParameterTrial> Items { get; } = [];

        public Task<ValidationParameterTrial?> GetByExperimentAndFingerprintAsync(
            long experimentId,
            string fingerprint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(t =>
                t.ValidationExperimentId == experimentId && t.ParameterFingerprint == fingerprint));

        public Task<IReadOnlyList<ValidationParameterTrial>> GetByExperimentIdAsync(
            long experimentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationParameterTrial>>(
                Items.Where(t => t.ValidationExperimentId == experimentId).ToList());

        public Task AddAsync(ValidationParameterTrial trial, CancellationToken cancellationToken = default)
        {
            Items.Add(trial);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ValidationParameterTrial trial, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AddRangeAsync(
            IEnumerable<ValidationParameterTrial> trials,
            CancellationToken cancellationToken = default)
        {
            Items.AddRange(trials);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeExperimentRepository : IValidationExperimentRepository
    {
        public List<ValidationExperiment> Items { get; } = [];

        public Task<ValidationExperiment?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(e => e.Id == id));

        public Task<IReadOnlyList<ValidationExperiment>> GetRecentAsync(
            int limit = 50,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationExperiment>>(Items.Take(limit).ToList());

        public Task<IReadOnlyList<ValidationExperiment>> GetByStrategyFingerprintOverlapAsync(
            string strategyCode,
            string strategyVersion,
            string symbol,
            string timeframe,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationExperiment>>([]);

        public Task AddAsync(ValidationExperiment experiment, CancellationToken cancellationToken = default)
        {
            Items.Add(experiment);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ValidationExperiment experiment, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeOperationStatusService : IResearchOperationStatusService
    {
        public ResearchOperationStatus? Last { get; private set; }

        public Task<ResearchOperationStatus?> GetByOperationIdAsync(
            string operationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Last);

        public Task<ResearchOperationStatus?> GetForValidationExperimentAsync(
            long experimentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Last);

        public Task<ResearchOperationStatus> UpsertValidationTrainingAsync(
            ResearchOperationStatus status,
            CancellationToken cancellationToken = default)
        {
            Last = status;
            return Task.FromResult(status);
        }

        public Task<ResearchOperationStatus> SyncFromValidationTrainingAsync(
            long experimentId,
            string status,
            string stage,
            ValidationTrainingProgressDto progress,
            string? leaseOwner = null,
            string? correlationId = null,
            string? errorCode = null,
            string? userSafeError = null,
            CancellationToken cancellationToken = default)
        {
            Last = ResearchOperationStatusMapper.FromValidationTraining(
                experimentId, status, stage, progress, leaseOwner, correlationId, errorCode, userSafeError);
            return Task.FromResult(Last);
        }

        public Task<ServiceResult<ResearchOperationStatus>> AdvanceProgressAsync(
            string operationId,
            decimal percentComplete,
            int completedWorkCount,
            int failedWorkCount,
            string? stage = null,
            string? status = null,
            string? activeWorkItem = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<ResearchOperationStatus>.Fail("n/a"));

        public Task<ServiceResult<ResearchOperationStatus>> HeartbeatAsync(
            string operationId,
            string leaseOwner,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<ResearchOperationStatus>.Fail("n/a"));

        public Task<ResearchOperationStatus?> DetectAndMarkStaleAsync(
            string operationId,
            TimeSpan staleAfter,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ResearchOperationStatus?>(null);

        public Task<ServiceResult<ResearchOperationStatus>> CancelAsync(
            string operationId,
            string callerIdentity,
            bool callerIsAdmin,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<ResearchOperationStatus>.Fail("n/a"));
    }
}
