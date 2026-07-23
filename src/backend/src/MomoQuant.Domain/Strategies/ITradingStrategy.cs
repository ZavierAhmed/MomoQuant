namespace MomoQuant.Domain.Strategies;

using MomoQuant.Domain.Enums;

public interface ITradingStrategy
{
    StrategyCode Code { get; }
    string Name { get; }
    string Description { get; }
    StrategySignalResult Evaluate(StrategyContext context);
    IReadOnlyCollection<MarketRegime> SupportedRegimes { get; }
    IReadOnlyCollection<Timeframe> SupportedTimeframes { get; }
}
