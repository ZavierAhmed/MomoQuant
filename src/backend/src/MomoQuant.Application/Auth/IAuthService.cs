using MomoQuant.Application.Auth.Dtos;
using MomoQuant.Application.Common;

namespace MomoQuant.Application.Auth;

public interface IAuthService
{
    Task<ServiceResult<LoginResponse>> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<UserProfileDto>> GetCurrentUserAsync(CancellationToken cancellationToken = default);
}
