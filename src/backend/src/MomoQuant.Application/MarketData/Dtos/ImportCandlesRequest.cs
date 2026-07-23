using System.ComponentModel.DataAnnotations;

namespace MomoQuant.Application.MarketData.Dtos;

public sealed class ImportCandlesRequest
{
    [Required]
    public long ExchangeId { get; set; }

    [Required]
    public long SymbolId { get; set; }

    [Required]
    [MaxLength(16)]
    public string Timeframe { get; set; } = string.Empty;

    [Required]
    public DateTime FromUtc { get; set; }

    [Required]
    public DateTime ToUtc { get; set; }

    public string? FromDate { get; set; }

    public string? ToDate { get; set; }
}
