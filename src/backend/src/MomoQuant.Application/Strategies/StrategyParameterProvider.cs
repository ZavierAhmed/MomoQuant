using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Strategies;

public interface IStrategyParameterProvider
{
    Task<IReadOnlyDictionary<string, string>> GetParametersAsync(
        long strategyId,
        Timeframe timeframe,
        long? symbolId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> GetParametersFromSetAsync(
        long parameterSetId,
        CancellationToken cancellationToken = default);
}

public sealed class StrategyParameterProvider : IStrategyParameterProvider
{
    private readonly IStrategyParameterRepository _parameterRepository;
    private readonly IStrategyParameterSetRepository _parameterSetRepository;

    public StrategyParameterProvider(
        IStrategyParameterRepository parameterRepository,
        IStrategyParameterSetRepository parameterSetRepository)
    {
        _parameterRepository = parameterRepository;
        _parameterSetRepository = parameterSetRepository;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetParametersAsync(
        long strategyId,
        Timeframe timeframe,
        long? symbolId = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = await _parameterRepository.GetActiveParametersAsync(
            strategyId,
            timeframe,
            symbolId,
            cancellationToken);

        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in parameters.Where(p => p.SymbolId is null))
        {
            resolved[parameter.ParameterKey] = parameter.ParameterValue;
        }

        foreach (var parameter in parameters.Where(p => p.SymbolId == symbolId))
        {
            resolved[parameter.ParameterKey] = parameter.ParameterValue;
        }

        return resolved;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetParametersFromSetAsync(
        long parameterSetId,
        CancellationToken cancellationToken = default)
    {
        var frozenSet = await _parameterSetRepository.GetByIdAsync(parameterSetId, cancellationToken);
        if (frozenSet is null || !frozenSet.IsApproved)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(frozenSet.ParametersJson)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
