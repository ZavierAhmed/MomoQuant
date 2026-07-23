using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;

namespace MomoQuant.Persistence.Services;

public sealed class ValidationTrainingDatabaseProbe : IValidationTrainingDatabaseProbe
{
    private readonly MomoQuantDbContext _db;

    public ValidationTrainingDatabaseProbe(MomoQuantDbContext db) => _db = db;

    public async Task<ServiceResult<bool>> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _db.Database.OpenConnectionAsync(cancellationToken);
            await _db.Database.CloseConnectionAsync();
            return ServiceResult<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            return ServiceResult<bool>.Fail($"MySQL connectivity failed: {ex.Message}");
        }
    }

    public Task<IReadOnlyList<string>> GetPendingMigrationNamesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(_db.Database.GetPendingMigrations().ToList());
}
