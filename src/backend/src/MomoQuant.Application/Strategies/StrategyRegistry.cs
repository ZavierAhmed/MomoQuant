using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies;

public interface IStrategyRegistry
{
    IReadOnlyCollection<ITradingStrategy> GetAll();

    ITradingStrategy? GetByCode(StrategyCode code);

    IReadOnlyCollection<ITradingStrategy> GetEnabled(IReadOnlyCollection<StrategyCode> enabledCodes);
}

public sealed class StrategyRegistry : IStrategyRegistry
{
    private readonly IReadOnlyDictionary<StrategyCode, ITradingStrategy> _strategies;

    public StrategyRegistry(IEnumerable<ITradingStrategy> strategies)
    {
        _strategies = strategies.ToDictionary(strategy => strategy.Code);
    }

    public IReadOnlyCollection<ITradingStrategy> GetAll() => _strategies.Values.ToList();

    public ITradingStrategy? GetByCode(StrategyCode code) =>
        _strategies.TryGetValue(code, out var strategy) ? strategy : null;

    public IReadOnlyCollection<ITradingStrategy> GetEnabled(IReadOnlyCollection<StrategyCode> enabledCodes) =>
        enabledCodes
            .Where(code => _strategies.ContainsKey(code))
            .Select(code => _strategies[code])
            .ToList();
}
