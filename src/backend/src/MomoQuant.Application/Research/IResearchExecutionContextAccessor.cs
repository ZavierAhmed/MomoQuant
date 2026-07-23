using MomoQuant.Application.StrategyLab;

namespace MomoQuant.Application.Research;

/// <summary>
/// Ambient research execution context (AsyncLocal). Secondary fail-closed guard for ValidationTraining.
/// </summary>
public interface IResearchExecutionContextAccessor
{
    StrategyLabExecutionContext? Current { get; }

    bool IsValidationTrainingActive { get; }

    IDisposable Enter(StrategyLabExecutionContext context);
}

public sealed class ResearchExecutionContextAccessor : IResearchExecutionContextAccessor
{
    private static readonly AsyncLocal<StrategyLabExecutionContext?> CurrentContext = new();

    public StrategyLabExecutionContext? Current => CurrentContext.Value;

    public bool IsValidationTrainingActive =>
        Current?.ExecutionPurpose == ExecutionPurpose.ValidationTraining;

    public IDisposable Enter(StrategyLabExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new Pop(previous);
    }

    private sealed class Pop : IDisposable
    {
        private readonly StrategyLabExecutionContext? _previous;
        private bool _disposed;

        public Pop(StrategyLabExecutionContext? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentContext.Value = _previous;
        }
    }
}
