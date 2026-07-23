using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Abstractions;

public interface ICurrentUserService
{
    long? UserId { get; }
    string? Email { get; }
    UserRole? Role { get; }
    bool IsAuthenticated { get; }
}
