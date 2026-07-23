using MomoQuant.Application.Auth.Dtos;
using MomoQuant.Application.Common;
using MomoQuant.Domain.Identity;
using MomoQuant.Application.Abstractions;

namespace MomoQuant.Application.Auth;

public sealed class AuthService : IAuthService
{
    private const string InvalidCredentialsMessage = "Invalid email or password.";
    private const string InactiveAccountMessage = "Account is inactive.";

    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        ICurrentUserService currentUserService,
        IAuditService auditService)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _currentUserService = currentUserService;
        _auditService = auditService;
    }

    public async Task<ServiceResult<LoginResponse>> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailForLoginAsync(normalizedEmail, cancellationToken);

        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            await _auditService.LogAsync(
                "USER_LOGIN_FAILED",
                nameof(User),
                userId: user?.Id,
                ipAddress: ipAddress,
                userAgent: userAgent,
                cancellationToken: cancellationToken);

            return ServiceResult<LoginResponse>.Fail(InvalidCredentialsMessage);
        }

        if (!user.IsActive)
        {
            await _auditService.LogAsync(
                "USER_LOGIN_FAILED_INACTIVE",
                nameof(User),
                entityId: user.Id,
                userId: user.Id,
                ipAddress: ipAddress,
                userAgent: userAgent,
                cancellationToken: cancellationToken);

            return ServiceResult<LoginResponse>.Fail(InactiveAccountMessage);
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user, cancellationToken);
        await _userRepository.SaveChangesAsync(cancellationToken);

        var token = _jwtTokenService.GenerateToken(user.Id, user.Email, user.Role);

        await _auditService.LogAsync(
            "USER_LOGIN_SUCCESS",
            nameof(User),
            entityId: user.Id,
            userId: user.Id,
            ipAddress: ipAddress,
            userAgent: userAgent,
            cancellationToken: cancellationToken);

        return ServiceResult<LoginResponse>.Ok(new LoginResponse
        {
            AccessToken = token.AccessToken,
            ExpiresAtUtc = token.ExpiresAtUtc,
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role.ToString()
        });
    }

    public async Task<ServiceResult<UserProfileDto>> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId is null)
        {
            return ServiceResult<UserProfileDto>.Fail("User is not authenticated.");
        }

        var user = await _userRepository.GetByIdAsync(_currentUserService.UserId.Value, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return ServiceResult<UserProfileDto>.Fail("User is not authenticated.");
        }

        return ServiceResult<UserProfileDto>.Ok(MapToProfile(user));
    }

    private static UserProfileDto MapToProfile(User user) => new()
    {
        UserId = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        Role = user.Role.ToString(),
        IsActive = user.IsActive
    };
}
