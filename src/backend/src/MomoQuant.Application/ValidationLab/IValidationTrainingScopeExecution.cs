using System.Runtime.ExceptionServices;
using MomoQuant.Application.Research;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Production orchestration for training candle scope ambient context and automatic access flush.
/// </summary>
public interface IValidationTrainingScopeExecution
{
    Task<ValidationTrainingScopeExecutionResult> ExecuteWithLtfWarmupBootstrapAsync(
        ValidationExperiment experiment,
        ValidationLtfWarmupBootstrapRequest scopeRequest,
        Func<IValidationTrainingCandleScope, Task> body,
        CancellationToken cancellationToken = default);

    Task<ValidationTrainingScopeExecutionResult> ExecuteWithCanonicalScopeAsync(
        ValidationExperiment experiment,
        ValidationCanonicalTrainingCandleScopeRequest scopeRequest,
        Func<IValidationTrainingCandleScope, Task> body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Quarantined — use <see cref="ExecuteWithLtfWarmupBootstrapAsync"/> or
    /// <see cref="ExecuteWithCanonicalScopeAsync"/>.
    /// </summary>
    [Obsolete("Use ExecuteWithLtfWarmupBootstrapAsync or ExecuteWithCanonicalScopeAsync.")]
    Task<ValidationTrainingScopeExecutionResult> ExecuteWithScopeAsync(
        ValidationExperiment experiment,
        ValidationTrainingCandleScopeRequest scopeRequest,
        Func<IValidationTrainingCandleScope, Task> body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the active trial identity, runs the trial body, and performs one authoritative
    /// trial-scope flush. Body and flush outcomes are captured without masking.
    /// </summary>
    Task<ValidationTrainingScopeExecutionResult> ExecuteTrialAsync(
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

    public Task<ValidationTrainingScopeExecutionResult> ExecuteWithScopeAsync(
        ValidationExperiment experiment,
        ValidationTrainingCandleScopeRequest scopeRequest,
        Func<IValidationTrainingCandleScope, Task> body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentNullException.ThrowIfNull(scopeRequest);
        ArgumentNullException.ThrowIfNull(body);
        throw new InvalidOperationException(
            "ExecuteWithScopeAsync is quarantined. Use ExecuteWithLtfWarmupBootstrapAsync or ExecuteWithCanonicalScopeAsync.");
    }

    public Task<ValidationTrainingScopeExecutionResult> ExecuteWithLtfWarmupBootstrapAsync(
        ValidationExperiment experiment,
        ValidationLtfWarmupBootstrapRequest scopeRequest,
        Func<IValidationTrainingCandleScope, Task> body,
        CancellationToken cancellationToken = default) =>
        ExecuteWithScopeCoreAsync(
            experiment,
            () => _scopeFactory.CreateLtfWarmupBootstrapAsync(scopeRequest, cancellationToken),
            boundAuditExecutionId: null,
            boundScopeExecutionId: null,
            boundExecutionToken: null,
            boundAttemptNumber: null,
            body,
            cancellationToken);

    public Task<ValidationTrainingScopeExecutionResult> ExecuteWithCanonicalScopeAsync(
        ValidationExperiment experiment,
        ValidationCanonicalTrainingCandleScopeRequest scopeRequest,
        Func<IValidationTrainingCandleScope, Task> body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopeRequest);
        return ExecuteWithScopeCoreAsync(
            experiment,
            () => _scopeFactory.CreateCanonicalAsync(scopeRequest, cancellationToken),
            boundAuditExecutionId: scopeRequest.AuditExecution.AuditExecutionId,
            boundScopeExecutionId: scopeRequest.AuditExecution.ScopeExecutionId,
            boundExecutionToken: scopeRequest.AuditExecution.ExecutionToken,
            boundAttemptNumber: scopeRequest.AuditExecution.AttemptNumber,
            body,
            cancellationToken);
    }

    private async Task<ValidationTrainingScopeExecutionResult> ExecuteWithScopeCoreAsync(
        ValidationExperiment experiment,
        Func<Task<IValidationTrainingCandleScope>> createScope,
        Guid? boundAuditExecutionId,
        Guid? boundScopeExecutionId,
        string? boundExecutionToken,
        int? boundAttemptNumber,
        Func<IValidationTrainingCandleScope, Task> body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(experiment);
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

        IDisposable? execAmbient = null;
        IValidationTrainingCandleScope? scope = null;
        IDisposable? ambient = null;
        IDisposable? auditAmbient = null;
        ExceptionDispatchInfo? bodyException = null;
        ExceptionDispatchInfo? flushException = null;
        ExceptionDispatchInfo? disposalException = null;
        var flushAttempted = false;

        try
        {
            execAmbient = _executionContextAccessor.Enter(bootstrapContext);
            scope = await createScope();
            scope.CorrelationId = bootstrapContext.CorrelationId;

            if (boundAuditExecutionId is Guid boundAuditId)
            {
                if (boundScopeExecutionId is not Guid boundScope
                    || scope.ScopeExecutionId != boundScope)
                {
                    throw new ValidationAuditExecutionIdentityMismatchException(
                        "Created training scope ScopeExecutionId does not match the bound durable audit identity.",
                        expectedAuditExecutionId: boundAuditId,
                        actualAuditExecutionId: boundAuditId,
                        expectedScopeExecutionId: boundScopeExecutionId,
                        actualScopeExecutionId: scope.ScopeExecutionId,
                        expectedExecutionToken: boundExecutionToken,
                        actualExecutionToken: boundExecutionToken);
                }
            }

            ambient = ValidationTrainingCandleScopeAmbient.Enter(scope);
            if (boundAuditExecutionId is Guid auditId
                && boundScopeExecutionId is Guid scopeId
                && !string.IsNullOrWhiteSpace(boundExecutionToken))
            {
                auditAmbient = ValidationAuditExecutionAmbient.Enter(new ValidationAuditExecutionAmbientContext
                {
                    AuditExecutionId = auditId,
                    ScopeExecutionId = scopeId,
                    ExecutionToken = boundExecutionToken,
                    AttemptNumber = boundAttemptNumber ?? 0,
                    ValidationExperimentId = experiment.Id
                });
            }

            try
            {
                await body(scope);
            }
            catch (Exception ex)
            {
                bodyException = ExceptionDispatchInfo.Capture(ex);
            }

            try
            {
                flushAttempted = true;
                await _recorder.FlushAsync(scope, CancellationToken.None);
            }
            catch (Exception ex)
            {
                flushException = ExceptionDispatchInfo.Capture(ex);
            }
        }
        finally
        {
            CaptureDispose(ref disposalException, () => auditAmbient?.Dispose());
            CaptureDispose(ref disposalException, () => ambient?.Dispose());
            if (scope is not null)
            {
                try
                {
                    await scope.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    disposalException ??= ExceptionDispatchInfo.Capture(ex);
                }
            }

            CaptureDispose(ref disposalException, () => execAmbient?.Dispose());
        }

        return new ValidationTrainingScopeExecutionResult
        {
            BodyException = bodyException,
            FlushException = flushException,
            DisposalException = disposalException,
            BodyPhase = ValidationTrainingFailurePhase.TrialBody,
            FlushPhase = ValidationTrainingFailurePhase.OuterScopeFlush,
            FlushAttempted = flushAttempted
        };
    }

    public async Task<ValidationTrainingScopeExecutionResult> ExecuteTrialAsync(
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

        ExceptionDispatchInfo? bodyException = null;
        ExceptionDispatchInfo? flushException = null;
        var flushAttempted = false;
        try
        {
            await trialBody();
        }
        catch (Exception ex)
        {
            bodyException = ExceptionDispatchInfo.Capture(ex);
        }

        try
        {
            flushAttempted = true;
            await _recorder.FlushAsync(scope, CancellationToken.None);
        }
        catch (Exception ex)
        {
            flushException = ExceptionDispatchInfo.Capture(ex);
        }

        return new ValidationTrainingScopeExecutionResult
        {
            BodyException = bodyException,
            FlushException = flushException,
            BodyPhase = ValidationTrainingFailurePhase.TrialBody,
            FlushPhase = ValidationTrainingFailurePhase.TrialScopeFlush,
            FlushAttempted = flushAttempted
        };
    }

    private static void CaptureDispose(ref ExceptionDispatchInfo? disposalException, Action dispose)
    {
        try
        {
            dispose();
        }
        catch (Exception ex)
        {
            disposalException ??= ExceptionDispatchInfo.Capture(ex);
        }
    }
}
