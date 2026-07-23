namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Controlled capability token authorizing unrestricted candle bootstrap.
/// Only <see cref="ValidationTrainingCandleScopeFactory"/> holds and activates this token.
/// Callers must not attempt a public <c>bypassValidationScope</c> flag.
/// </summary>
public sealed class ValidationScopeFactoryCapability
{
    private static readonly AsyncLocal<ValidationScopeFactoryCapability?> Active = new();

    private ValidationScopeFactoryCapability()
    {
    }

    /// <summary>
    /// Creates a capability instance. Intended only for <see cref="ValidationTrainingCandleScopeFactory"/>.
    /// </summary>
    internal static ValidationScopeFactoryCapability Create() => new();

    public static bool IsActive => Active.Value is not null;

    public IDisposable Activate()
    {
        var previous = Active.Value;
        Active.Value = this;
        return new Pop(previous);
    }

    private sealed class Pop : IDisposable
    {
        private readonly ValidationScopeFactoryCapability? _previous;
        private bool _disposed;

        public Pop(ValidationScopeFactoryCapability? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Active.Value = _previous;
        }
    }
}
