using System.ComponentModel.DataAnnotations;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Users.Dtos;

public sealed class UpdateUserRequest
{
    [Required]
    [MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public UserRole Role { get; set; }

    public bool IsActive { get; set; } = true;
}
