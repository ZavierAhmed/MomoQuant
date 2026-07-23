using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Identity;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public UserRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<User?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        return user is null ? null : await AttachRoleAsync(user, cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        return user is null ? null : await AttachRoleAsync(user, cancellationToken);
    }

    public async Task<User?> GetByEmailForLoginAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
        return user is null ? null : await AttachRoleAsync(user, cancellationToken);
    }

    public async Task<bool> EmailExistsAsync(
        string email,
        long? excludeUserId = null,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users.AnyAsync(
            u => u.Email == email && (!excludeUserId.HasValue || u.Id != excludeUserId.Value),
            cancellationToken);
    }

    public async Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = _dbContext.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(u =>
                u.Email.Contains(search) ||
                u.FullName.ToLower().Contains(search));
        }

        query = ApplySorting(query, request);

        var totalCount = await query.CountAsync(cancellationToken);
        var users = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var roleMap = await _dbContext.Roles.AsNoTracking()
            .ToDictionaryAsync(role => role.Id, role => role.Name, cancellationToken);

        foreach (var user in users)
        {
            if (roleMap.TryGetValue(user.RoleId, out var role))
            {
                user.Role = role;
            }
        }

        return (users, totalCount);
    }

    public Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        _dbContext.Users.Add(user);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        _dbContext.Users.Update(user);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    private async Task<User> AttachRoleAsync(User user, CancellationToken cancellationToken)
    {
        var role = await _dbContext.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == user.RoleId, cancellationToken);

        if (role is not null)
        {
            user.Role = role.Name;
        }

        return user;
    }

    private static IQueryable<User> ApplySorting(IQueryable<User> query, PagedRequest request)
    {
        var descending = request.SortDirection == SortDirection.Desc;
        return request.SortBy?.ToLowerInvariant() switch
        {
            "email" => descending ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
            "fullname" => descending ? query.OrderByDescending(u => u.FullName) : query.OrderBy(u => u.FullName),
            "role" => descending ? query.OrderByDescending(u => u.RoleId) : query.OrderBy(u => u.RoleId),
            _ => descending ? query.OrderByDescending(u => u.CreatedAtUtc) : query.OrderBy(u => u.CreatedAtUtc)
        };
    }
}
