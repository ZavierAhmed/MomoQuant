using MomoQuant.Application.Strategies.PriceStructure.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.StrategyLab.Confidence;

public sealed record CandidateConfidenceScoreComponent
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public decimal Score { get; init; }
    public decimal Max { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed class CandidateConfidenceResult
{
    public decimal Score { get; init; }
    public string ModelVersion { get; init; } = StrategySetupQualityScorer.ModelVersion;
    public IReadOnlyList<CandidateConfidenceScoreComponent> Components { get; init; } = [];
    public string Explanation { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class CandidateConfidenceContext
{
    public required string StrategyCode { get; init; }
    public required TradeDirection Direction { get; init; }
    public required decimal EntryPrice { get; init; }
    public required decimal StopLoss { get; init; }
    public required decimal Target1 { get; init; }
    public required decimal RewardRisk { get; init; }
    public required PriceStructureSetupDto Structure { get; init; }
    /// <summary>Candles available at candidate creation time (inclusive of confirmation candle). No future candles.</summary>
    public required IReadOnlyList<Candle> CandlesThroughSetup { get; init; }
}

public interface ICandidateConfidenceScorer
{
    CandidateConfidenceResult Score(CandidateConfidenceContext context);
}
