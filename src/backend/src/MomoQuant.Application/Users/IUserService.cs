using MomoQuant.Application.Common;
using MomoQuant.Application.Users.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Users;

public interface IUserService
{
    Task<ServiceResult<PagedResult<UserDto>>> GetUsersAsync(PagedRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<UserDto>> GetUserByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<ServiceResult<UserDto>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<UserDto>> UpdateUserAsync(long id, UpdateUserRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<UserDto>> DisableUserAsync(long id, CancellationToken cancellationToken = default);
}
