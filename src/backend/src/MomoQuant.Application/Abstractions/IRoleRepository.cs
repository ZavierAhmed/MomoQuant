using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Identity;

namespace MomoQuant.Application.Abstractions;

public interface IRoleRepository
{
    Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Role?> GetByNameAsync(UserRole name, CancellationToken cancellationToken = default);
    Task<Role?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task AddAsync(Role role, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
