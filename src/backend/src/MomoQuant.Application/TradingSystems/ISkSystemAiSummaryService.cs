using MomoQuant.Application.TradingSystems.Dtos;

namespace MomoQuant.Application.TradingSystems;

public sealed class SkSummaryInput
{
    public required string Symbol { get; init; }
    public required string PrimaryTimeframe { get; init; }
    public required string HigherTimeframe { get; init; }
    public decimal CurrentPrice { get; init; }
    public required string MarketBias { get; init; }
    public required string ConfidenceLabel { get; init; }
    public IReadOnlyList<SkSequenceCandidateDto> Candidates { get; init; } = [];
    public SkMultiTimeframeContextDto? HigherTimeframeContext { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool UseAiSummary { get; init; } = true;

    /// <summary>Beginner, Intermediate, or Expert.</summary>
    public string ExplanationMode { get; init; } = "Beginner";

    /// <summary>Decimals to use when formatting prices in the narrative.</summary>
    public int PriceDecimals { get; init; } = 2;
}

public interface ISkSystemAiSummaryService
{
    /// <summary>
    /// Produces an explanatory, analysis-only summary. The summary references only calculated
    /// levels, never invents levels, never gives buy/sell advice, and falls back to a
    /// deterministic rule-based summary when an AI service is unavailable.
    /// </summary>
    Task<SkAiSummaryDto> BuildSummaryAsync(SkSummaryInput input, CancellationToken cancellationToken = default);
}
