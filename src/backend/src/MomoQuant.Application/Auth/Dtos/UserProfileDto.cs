namespace MomoQuant.Application.Auth.Dtos;

public sealed class UserProfileDto
{
    public required long UserId { get; init; }
    public required string FullName { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
    public required bool IsActive { get; init; }
}
