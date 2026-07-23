namespace MomoQuant.Application.Users.Dtos;

public sealed class UserDto
{
    public required long Id { get; init; }
    public required string FullName { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
    public required bool IsActive { get; init; }
    public DateTime? LastLoginAtUtc { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }
}
