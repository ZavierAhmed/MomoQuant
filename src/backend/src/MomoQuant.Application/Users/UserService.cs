using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Users.Dtos;
using MomoQuant.Domain.Identity;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Users;

public sealed class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;

    public UserService(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPasswordHasher passwordHasher,
        ICurrentUserService currentUserService,
        IAuditService auditService)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _passwordHasher = passwordHasher;
        _currentUserService = currentUserService;
        _auditService = auditService;
    }

    public async Task<ServiceResult<PagedResult<UserDto>>> GetUsersAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _userRepository.GetPagedAsync(request, cancellationToken);

        return ServiceResult<PagedResult<UserDto>>.Ok(new PagedResult<UserDto>
        {
            Items = items.Select(MapToDto).ToList(),
            Page = Math.Max(request.Page, 1),
            PageSize = Math.Max(request.PageSize, 1),
            TotalCount = totalCount
        });
    }

    public async Task<ServiceResult<UserDto>> GetUserByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return ServiceResult<UserDto>.Fail("User was not found.");
        }

        return ServiceResult<UserDto>.Ok(MapToDto(user));
    }

    public async Task<ServiceResult<UserDto>> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        if (await _userRepository.EmailExistsAsync(normalizedEmail, cancellationToken: cancellationToken))
        {
            return ServiceResult<UserDto>.Fail("Email is already in use.", "email");
        }

        var role = await _roleRepository.GetByNameAsync(request.Role, cancellationToken);
        if (role is null)
        {
            return ServiceResult<UserDto>.Fail("Role was not found.", "role");
        }

        var now = DateTime.UtcNow;
        var user = new User
        {
            FullName = request.FullName.Trim(),
            Email = normalizedEmail,
            PasswordHash = _passwordHasher.Hash(request.Password),
            RoleId = role.Id,
            Role = role.Name,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await _userRepository.AddAsync(user, cancellationToken);
        await _userRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "USER_CREATED",
            nameof(User),
            entityId: user.Id,
            userId: _currentUserService.UserId,
            newValueJson: $"{{\"email\":\"{user.Email}\",\"role\":\"{user.Role}\"}}",
            cancellationToken: cancellationToken);

        return ServiceResult<UserDto>.Ok(MapToDto(user));
    }

    public async Task<ServiceResult<UserDto>> UpdateUserAsync(
        long id,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return ServiceResult<UserDto>.Fail("User was not found.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await _userRepository.EmailExistsAsync(normalizedEmail, id, cancellationToken))
        {
            return ServiceResult<UserDto>.Fail("Email is already in use.", "email");
        }

        var role = await _roleRepository.GetByNameAsync(request.Role, cancellationToken);
        if (role is null)
        {
            return ServiceResult<UserDto>.Fail("Role was not found.", "role");
        }

        var oldValue = $"{{\"email\":\"{user.Email}\",\"role\":\"{user.Role}\",\"isActive\":{user.IsActive.ToString().ToLowerInvariant()}}}";

        user.FullName = request.FullName.Trim();
        user.Email = normalizedEmail;
        user.RoleId = role.Id;
        user.Role = role.Name;
        user.IsActive = request.IsActive;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _userRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "USER_UPDATED",
            nameof(User),
            entityId: user.Id,
            userId: _currentUserService.UserId,
            oldValueJson: oldValue,
            newValueJson: $"{{\"email\":\"{user.Email}\",\"role\":\"{user.Role}\",\"isActive\":{user.IsActive.ToString().ToLowerInvariant()}}}",
            cancellationToken: cancellationToken);

        return ServiceResult<UserDto>.Ok(MapToDto(user));
    }

    public async Task<ServiceResult<UserDto>> DisableUserAsync(long id, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return ServiceResult<UserDto>.Fail("User was not found.");
        }

        if (_currentUserService.UserId == user.Id)
        {
            return ServiceResult<UserDto>.Fail("You cannot disable your own account.");
        }

        user.IsActive = false;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _userRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "USER_DISABLED",
            nameof(User),
            entityId: user.Id,
            userId: _currentUserService.UserId,
            cancellationToken: cancellationToken);

        return ServiceResult<UserDto>.Ok(MapToDto(user));
    }

    private static UserDto MapToDto(User user) => new()
    {
        Id = user.Id,
        FullName = user.FullName,
        Email = user.Email,
        Role = user.Role.ToString(),
        IsActive = user.IsActive,
        LastLoginAtUtc = user.LastLoginAtUtc,
        CreatedAtUtc = user.CreatedAtUtc,
        UpdatedAtUtc = user.UpdatedAtUtc
    };
}
