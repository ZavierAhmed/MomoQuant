namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// AsyncLocal ambient context for the current durable audit execution
/// (Milestone 23.0E2C1 WP3 / WP5).
/// </summary>
public sealed class ValidationAuditExecutionAmbientContext
{
    public required Guid AuditExecutionId { get; init; }
    public required Guid ScopeExecutionId { get; init; }
    public required string ExecutionToken { get; init; }
    public required int AttemptNumber { get; init; }
    public long ValidationExperimentId { get; init; }
    public long ValidationTrialId { get; init; }
}

public static class ValidationAuditExecutionAmbient
{
    private static readonly AsyncLocal<ValidationAuditExecutionAmbientContext?> Current = new();

    public static ValidationAuditExecutionAmbientContext? CurrentValue => Current.Value;

    public static IDisposable Enter(ValidationAuditExecutionAmbientContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var previous = Current.Value;
        Current.Value = context;
        return new PopAmbient(previous);
    }

    private sealed class PopAmbient : IDisposable
    {
        private readonly ValidationAuditExecutionAmbientContext? _previous;
        private bool _disposed;

        public PopAmbient(ValidationAuditExecutionAmbientContext? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Current.Value = _previous;
        }
    }
}
