using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Persistence.Repositories;

/// <summary>
/// Narrow production seam around audit transaction commit/rollback.
/// The production implementation delegates directly to EF Core. Controlled integration tests may
/// replace it through DI to inject deterministic commit faults; it is never reachable from user input.
/// </summary>
public interface IValidationAccessAuditTransactionBoundary
{
    Task CommitAsync(IDbContextTransaction transaction, CancellationToken cancellationToken);

    Task RollbackAsync(IDbContextTransaction transaction, CancellationToken cancellationToken);
}

public sealed class ValidationAccessAuditTransactionBoundary : IValidationAccessAuditTransactionBoundary
{
    public Task CommitAsync(IDbContextTransaction transaction, CancellationToken cancellationToken) =>
        transaction.CommitAsync(cancellationToken);

    public Task RollbackAsync(IDbContextTransaction transaction, CancellationToken cancellationToken) =>
        transaction.RollbackAsync(cancellationToken);
}

/// <summary>
/// Reads durable audit rows for confirmation using a context and connection that are guaranteed to
/// be distinct from the (possibly failed) write context. Used for all payload confirmation,
/// including recovery after an ambiguous commit.
/// </summary>
public interface IValidationAccessAuditConfirmationReader
{
    /// <summary>True when the implementation creates a fresh DbContext/connection per read.</summary>
    bool UsesFreshContext { get; }

    Task<IReadOnlyList<ValidationCandleAccessAudit>> ReadAsync(
        IReadOnlyCollection<Guid> accessEventIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// Creates a fresh DI scope (and therefore a fresh DbContext + connection) for every confirmation
/// read. Never reuses the failed write context or a disposed service provider.
/// </summary>
public sealed class ValidationAccessAuditConfirmationReader : IValidationAccessAuditConfirmationReader
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ValidationAccessAuditConfirmationReader(IServiceScopeFactory scopeFactory) =>
        _scopeFactory = scopeFactory;

    public bool UsesFreshContext => true;

    /// <summary>Context id of the most recently created confirmation context (test diagnostics).</summary>
    public Guid LastConfirmationContextId { get; private set; }

    public async Task<IReadOnlyList<ValidationCandleAccessAudit>> ReadAsync(
        IReadOnlyCollection<Guid> accessEventIds,
        CancellationToken cancellationToken)
    {
        if (accessEventIds.Count == 0)
        {
            return Array.Empty<ValidationCandleAccessAudit>();
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        LastConfirmationContextId = db.ContextId.InstanceId;

        var ids = accessEventIds.ToList();
        return await db.ValidationCandleAccessAudits
            .AsNoTracking()
            .Where(a => ids.Contains(a.AccessEventId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
