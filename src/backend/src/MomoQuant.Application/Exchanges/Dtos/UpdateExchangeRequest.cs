using System.ComponentModel.DataAnnotations;

namespace MomoQuant.Application.Exchanges.Dtos;

public sealed class UpdateExchangeRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(32)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    [Url]
    public string BaseUrl { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string WebSocketUrl { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
