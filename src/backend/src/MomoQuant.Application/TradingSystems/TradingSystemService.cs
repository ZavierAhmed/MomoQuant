using MomoQuant.Application.TradingSystems.Dtos;

namespace MomoQuant.Application.TradingSystems;

public sealed class TradingSystemService : ITradingSystemService
{
    public IReadOnlyList<TradingSystemInfoDto> GetSystems() =>
    [
        new TradingSystemInfoDto
        {
            Code = SkSystemConstants.SystemCode,
            Name = SkSystemConstants.SystemName,
            Description =
                "Analyze market structure, sequences, Fibonacci zones, and possible scenarios. " +
                "Analysis only — no automated trading.",
            Category = "Structure & Sequence",
            AnalysisOnly = true,
            SupportedPrimaryTimeframes = SkSystemConstants.SupportedPrimaryTimeframes,
            SupportedHigherTimeframes = SkSystemConstants.SupportedHigherTimeframes
        }
    ];
}
