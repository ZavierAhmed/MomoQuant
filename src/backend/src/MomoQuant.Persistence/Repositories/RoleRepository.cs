using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Identity;

namespace MomoQuant.Persistence.Repositories;

public sealed class RoleRepository : IRoleRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public RoleRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Roles.AsNoTracking()
            .OrderBy(role => role.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<Role?> GetByNameAsync(UserRole name, CancellationToken cancellationToken = default) =>
        _dbContext.Roles.AsNoTracking().FirstOrDefaultAsync(role => role.Name == name, cancellationToken);

    public Task<Role?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.Roles.AsNoTracking().FirstOrDefaultAsync(role => role.Id == id, cancellationToken);

    public Task AddAsync(Role role, CancellationToken cancellationToken = default)
    {
        _dbContext.Roles.Add(role);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
