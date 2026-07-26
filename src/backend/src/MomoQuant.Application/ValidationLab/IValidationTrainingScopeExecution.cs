using MomoQuant.Application.Research;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Production orchestration for training candle scope ambient context and automatic access flush.
/// </summary>
public interface IValidationTrainingScopeExecution
{
    /// <summary>
    /// Creates the training candle scope from a validated request, enters ambient context,
    /// runs <paramref name="body"/>, and flushes access evidence in a finally block.
    /// When <see cref="ValidationTrainingCandleScopeRequest.BoundAuditExecutionId"/> is set,
    /// verifies scope identity and enters <see cref="ValidationAuditExecutionAmbient"/>.
    /// </summary>
    Task ExecuteWithScopeAsync(
        ValidationExperiment experiment,
        ValidationTrainingCandleScopeRequest scopeRequest,
        Func<IValidationTrainingCandleScope, Task> body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the active trial identity, runs the trial body, and flushes access evidence in finally
    /// (including when <see cref="ValidationDataLeakageException"/> is thrown).
    /// </summary>
    Task ExecuteTrialAsync(
        IValidationTrainingCandleScope scope,
        int trialNumber,
        long? trialId,
        Func<Task> trialBody,
        CancellationToken cancellationToken = default);
}

public sealed class ValidationTrainingScopeExecution : IValidationTrainingScopeExecution
{
    private readonly IValidationTrainingCandleScopeFactory _scopeFactory;
    private readonly IValidationCandleAccessRecorder _recorder;
    private readonly IResearchExecutionContextAccessor _executionContextAccessor;

    public ValidationTrainingScopeExecution(
        IValidationTrainingCandleScopeFactory scopeFactory,
        IValidationCandleAccessRecorder recorder,
        IResearchExecutionContextAccessor? executionContextAccessor = null)
    {
        _scopeFactory = scopeFactory;
        _recorder = recorder;
        _executionContextAccessor = executionContextAccessor ?? new ResearchExecutionContextAccessor();
    }

    public async Task ExecuteWithScopeAsync(
        ValidationExperiment experiment,
        ValidationTrainingCandleScopeRequest scopeRequest,
        Func<IValidationTrainingCandleScope, Task> body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentNullException.ThrowIfNull(scopeRequest);
        ArgumentNullException.ThrowIfNull(body);

        var boundary = experiment.ValidationStartUtc is null
            ? (DateTime?)null
            : DateTime.SpecifyKind(experiment.ValidationStartUtc.Value, DateTimeKind.Utc);

        var bootstrapContext = new StrategyLabExecutionContext
        {
            ExecutionPurpose = ExecutionPurpose.ValidationTraining,
            ValidationExperimentId = experiment.Id,
            TrainingBoundaryUtc = boundary,
            AllowCoverageImport = false,
            CallerComponent = "ValidationTrainingScopeExecution",
            CorrelationId = Guid.NewGuid().ToString("N")
        };

        using var execAmbient = _executionContextAccessor.Enter(bootstrapContext);
        await using var scope = await _scopeFactory.CreateAsync(scopeRequest, cancellationToken);
        scope.CorrelationId = bootstrapContext.CorrelationId;

        if (scopeRequest.BoundAuditExecutionId is Guid boundAuditId)
        {
            if (scopeRequest.BoundScopeExecutionId is not Guid boundScope
                || scope.ScopeExecutionId != boundScope)
            {
                throw new ValidationAuditExecutionIdentityMismatchException(
                    "Created training scope ScopeExecutionId does not match the bound durable audit identity.",
                    expectedAuditExecutionId: boundAuditId,
                    actualAuditExecutionId: boundAuditId,
                    expectedScopeExecutionId: scopeRequest.BoundScopeExecutionId,
                    actualScopeExecutionId: scope.ScopeExecutionId,
                    expectedExecutionToken: scopeRequest.BoundExecutionToken,
                    actualExecutionToken: scopeRequest.BoundExecutionToken);
            }
        }

        using var ambient = ValidationTrainingCandleScopeAmbient.Enter(scope);
        IDisposable? auditAmbient = null;
        if (scopeRequest.BoundAuditExecutionId is Guid auditId
            && scopeRequest.BoundScopeExecutionId is Guid scopeId
            && !string.IsNullOrWhiteSpace(scopeRequest.BoundExecutionToken))
        {
            auditAmbient = ValidationAuditExecutionAmbient.Enter(new ValidationAuditExecutionAmbientContext
            {
                AuditExecutionId = auditId,
                ScopeExecutionId = scopeId,
                ExecutionToken = scopeRequest.BoundExecutionToken!,
                AttemptNumber = scopeRequest.BoundAttemptNumber ?? 0,
                ValidationExperimentId = experiment.Id
            });
        }

        try
        {
            await body(scope);
        }
        finally
        {
            // Flush while durable ambient is still available, then release ambient.
            try
            {
                await _recorder.FlushAsync(scope, CancellationToken.None);
            }
            finally
            {
                auditAmbient?.Dispose();
            }
        }
    }

    public async Task ExecuteTrialAsync(
        IValidationTrainingCandleScope scope,
        int trialNumber,
        long? trialId,
        Func<Task> trialBody,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(trialBody);

        scope.ActiveTrialNumber = trialNumber;
        scope.ActiveTrialId = trialId;
        try
        {
            await trialBody();
        }
        finally
        {
            // Flush denied evidence before leakage (or any other) exception propagates.
            await _recorder.FlushAsync(scope, CancellationToken.None);
        }
    }
}
