using MomoQuant.Application.Options;
using MomoQuant.Application.TradingSystems.Dtos;

namespace MomoQuant.Application.TradingSystems;

public sealed class SkSequenceAnalysisResult
{
    public IReadOnlyList<SkSequenceCandidateDto> Candidates { get; init; } = [];
    public IReadOnlyList<SkFibonacciZoneDto> FibonacciZones { get; init; } = [];
    public IReadOnlyList<SkKeyLevelDto> KeyLevels { get; init; } = [];
}

public interface ISkSequenceAnalyzer
{
    /// <summary>
    /// Detects possible SK-style sequence structures from swing points.
    /// This is an approximation/configurable analyzer, not an official certified SK implementation.
    /// It only emits scenarios and levels — never trade signals.
    /// </summary>
    SkSequenceAnalysisResult Analyze(
        IReadOnlyList<SwingPointDto> swings,
        decimal currentPrice,
        string directionMode,
        SkSystemSettings settings);
}
