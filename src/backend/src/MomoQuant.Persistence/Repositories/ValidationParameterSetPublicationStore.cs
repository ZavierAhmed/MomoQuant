using System.Data;
using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Audit;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Audit;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.ValidationLab;
using MySqlConnector;

namespace MomoQuant.Persistence.Repositories;

public sealed class ValidationParameterSetPublicationStore : IValidationParameterSetPublicationStore
{
    private readonly MomoQuantDbContext _db;

    public ValidationParameterSetPublicationStore(MomoQuantDbContext db) => _db = db;

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        _db.ChangeTracker.Clear();

        await using var transaction = await _db.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var result = await action(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (DbUpdateException ex) when (HasPendingAuditEvidence())
        {
            await TryRollbackAsync(transaction).ConfigureAwait(false);
            _db.ChangeTracker.Clear();
            throw AuditEvidenceException.Unavailable(ex);
        }
        catch (DbUpdateException ex) when (IsPublicationConstraintViolation(ex))
        {
            await TryRollbackAsync(transaction).ConfigureAwait(false);
            _db.ChangeTracker.Clear();
            throw new ValidationPublicationPersistenceException(
                ValidationParameterSetPublicationCodes.ProvenanceConflict,
                "A durable publication uniqueness or provenance constraint was violated.",
                ex);
        }
        catch
        {
            await TryRollbackAsync(transaction).ConfigureAwait(false);
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<ValidationExperiment?> LockExperimentAsync(
        long experimentId,
        CancellationToken cancellationToken = default)
    {
        var ids = await _db.Database.SqlQuery<long>($"""
            SELECT `Id` AS `Value`
            FROM `ValidationExperiments`
            WHERE `Id` = {experimentId}
            FOR UPDATE
            """).ToListAsync(cancellationToken).ConfigureAwait(false);
        return ids.Count == 1
            ? await _db.ValidationExperiments.SingleAsync(item => item.Id == experimentId, cancellationToken)
                .ConfigureAwait(false)
            : null;
    }

    public async Task<ValidationParameterTrial?> LockTrialAsync(
        long trialId,
        CancellationToken cancellationToken = default)
    {
        var ids = await _db.Database.SqlQuery<long>($"""
            SELECT `Id` AS `Value`
            FROM `ValidationParameterTrials`
            WHERE `Id` = {trialId}
            FOR UPDATE
            """).ToListAsync(cancellationToken).ConfigureAwait(false);
        return ids.Count == 1
            ? await _db.ValidationParameterTrials.SingleAsync(item => item.Id == trialId, cancellationToken)
                .ConfigureAwait(false)
            : null;
    }

    public async Task<Strategy?> LockStrategyByCodeAsync(
        string strategyCode,
        CancellationToken cancellationToken = default)
    {
        var ids = await _db.Database.SqlQuery<long>($"""
            SELECT `Id` AS `Value`
            FROM `Strategies`
            WHERE `Code` = {strategyCode}
            FOR UPDATE
            """).ToListAsync(cancellationToken).ConfigureAwait(false);
        return ids.Count == 1
            ? await _db.Strategies.SingleAsync(item => item.Id == ids[0], cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async Task<IReadOnlyList<ValidationExperiment>> ListCanonicalExperimentsAsync(
        string strategyCode,
        CancellationToken cancellationToken = default)
    {
        // The matching Strategy row is already locked and is the serialization point for all
        // publication writers. A second FOR UPDATE range scan here can deadlock when concurrent
        // transactions already hold different experiment rows, so historical compatibility is
        // inspected with a current read after the strategy lock has been acquired.
        return await _db.ValidationExperiments
            .Where(item => item.StrategyCode == strategyCode && item.IsCanonical)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<StrategyParameterSet?> LockPublicationByExperimentAsync(
        long experimentId,
        CancellationToken cancellationToken = default)
    {
        var ids = await _db.Database.SqlQuery<long>($"""
            SELECT `Id` AS `Value`
            FROM `StrategyParameterSets`
            WHERE `QualificationSourceExperimentId` = {experimentId}
            FOR UPDATE
            """).ToListAsync(cancellationToken).ConfigureAwait(false);
        return ids.Count == 1
            ? await _db.StrategyParameterSets.SingleAsync(item => item.Id == ids[0], cancellationToken)
                .ConfigureAwait(false)
            : null;
    }

    public async Task<IReadOnlyList<StrategyParameterSet>> LockQualifiedPublicationsByStrategyAsync(
        string strategyCode,
        CancellationToken cancellationToken = default)
    {
        var qualificationStatus = ParameterSetQualificationStatus.DeploymentQualified.ToString();
        var ids = await _db.Database.SqlQuery<long>($"""
            SELECT `Id` AS `Value`
            FROM `StrategyParameterSets`
            WHERE `StrategyCode` = {strategyCode} AND `QualificationStatus` = {qualificationStatus}
            ORDER BY `Id`
            FOR UPDATE
            """).ToListAsync(cancellationToken).ConfigureAwait(false);
        return ids.Count == 0
            ? []
            : await _db.StrategyParameterSets.Where(item => ids.Contains(item.Id)).OrderBy(item => item.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public void AddParameterSet(StrategyParameterSet parameterSet) => _db.StrategyParameterSets.Add(parameterSet);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);

    private bool HasPendingAuditEvidence() =>
        _db.ChangeTracker.Entries<AuditLog>().Any(entry => entry.State == EntityState.Added);

    private static bool IsPublicationConstraintViolation(DbUpdateException exception) =>
        FindMySqlException(exception) is { Number: 1062 or 1452 or 3819 };

    private static MySqlException? FindMySqlException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is MySqlException mysql)
            {
                return mysql;
            }
        }

        return null;
    }

    private static async Task TryRollbackAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Preserve the primary exception; the transaction is disposed by the caller.
        }
    }
}
