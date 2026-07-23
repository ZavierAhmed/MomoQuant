using System.ComponentModel.DataAnnotations;

namespace MomoQuant.Application.Strategies.Dtos;

public sealed class StrategyEvaluationRequest
{
    [Required]
    public long SymbolId { get; set; }

    [Required]
    [MaxLength(16)]
    public string Timeframe { get; set; } = string.Empty;

    [Required]
    public long CandleId { get; set; }

    [Required]
    [MaxLength(64)]
    public string MarketRegime { get; set; } = string.Empty;

    public List<long>? StrategyIds { get; set; }
}

public sealed class StrategyEvaluateLatestRequest
{
    [Required]
    public long SymbolId { get; set; }

    [Required]
    [MaxLength(16)]
    public string Timeframe { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    public string MarketRegime { get; set; } = string.Empty;

    public List<long>? StrategyIds { get; set; }
}

public sealed class StrategyEvaluationResponse
{
    public required long SymbolId { get; init; }
    public required string Timeframe { get; init; }
    public required long CandleId { get; init; }
    public required string MarketRegime { get; init; }
    public DateTime? CandleOpenTimeUtc { get; init; }
    public DateTime? CandleCloseTimeUtc { get; init; }
    public decimal? CandleClose { get; init; }
    public required IReadOnlyList<Models.StrategyEvaluationResult> Results { get; init; }
}
