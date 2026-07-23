using System.ComponentModel.DataAnnotations;

namespace MomoQuant.Application.Symbols.Dtos;

public sealed class UpdateSymbolStatusRequest
{
    [Required]
    public bool IsActive { get; set; }
}
