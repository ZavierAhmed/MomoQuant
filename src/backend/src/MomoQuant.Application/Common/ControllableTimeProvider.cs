namespace MomoQuant.Application.Common;

/// <summary>
/// Controllable <see cref="TimeProvider"/> for stale-timeout tests (no Thread.Sleep).
/// </summary>
public sealed class ControllableTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public ControllableTimeProvider(DateTimeOffset? utcNow = null)
    {
        _utcNow = utcNow ?? DateTimeOffset.UtcNow;
    }

    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);

    public override DateTimeOffset GetUtcNow() => _utcNow;
}
