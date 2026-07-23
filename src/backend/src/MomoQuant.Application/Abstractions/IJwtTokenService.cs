using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Abstractions;

public interface IJwtTokenService
{
    JwtTokenResult GenerateToken(long userId, string email, UserRole role);
}

public sealed class JwtTokenResult
{
    public required string AccessToken { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
}
