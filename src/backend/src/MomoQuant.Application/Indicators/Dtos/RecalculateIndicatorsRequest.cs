using System.ComponentModel.DataAnnotations;
using MomoQuant.Application.Indicators.Dtos;

namespace MomoQuant.Application.Indicators.Dtos;

public sealed class RecalculateIndicatorsRequest
{
    [Required]
    public long SymbolId { get; set; }

    [Required]
    [MaxLength(16)]
    public string Timeframe { get; set; } = string.Empty;

    public DateTime FromUtc { get; set; }

    public DateTime ToUtc { get; set; }

    public string? FromDate { get; set; }

    public string? ToDate { get; set; }

    public bool AutoImportMissingCandles { get; set; } = true;
}
