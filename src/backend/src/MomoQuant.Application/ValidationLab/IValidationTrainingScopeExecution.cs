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
    /// Creates the training candle scope, enters ambient context, runs <paramref name="body"/>,
    /// and flushes access evidence in a finally block.
    /// </summary>
    Task ExecuteWithScopeAsync(
        ValidationExperiment experiment,
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
        Func<IValidationTrainingCandleScope, Task> body,
        CancellationToken cancellationToken = default)
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

        using var execAmbient = _executionContextAccessor.Enter(bootstrapContext);
        await using var scope = await _scopeFactory.CreateForExperimentAsync(experiment, cancellationToken);
        using var ambient = ValidationTrainingCandleScopeAmbient.Enter(scope);
        try
        {
            await body(scope);
        }
        finally
        {
            await _recorder.FlushAsync(scope, CancellationToken.None);
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
