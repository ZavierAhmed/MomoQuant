using System.ComponentModel.DataAnnotations;

namespace MomoQuant.Application.Symbols.Dtos;

public sealed class SyncSymbolsRequest
{
    [Required]
    public long ExchangeId { get; set; }
}
