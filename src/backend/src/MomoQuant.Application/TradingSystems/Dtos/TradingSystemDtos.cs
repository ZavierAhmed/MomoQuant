namespace MomoQuant.Application.TradingSystems.Dtos;

/// <summary>
/// Describes an available Trading System. Trading Systems are analytical frameworks only.
/// They are never benchmarkable, backtestable, or bot-compatible.
/// </summary>
public sealed class TradingSystemInfoDto
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public bool AnalysisOnly { get; init; } = true;
    public IReadOnlyList<string> SupportedPrimaryTimeframes { get; init; } = [];
    public IReadOnlyList<string> SupportedHigherTimeframes { get; init; } = [];
}
