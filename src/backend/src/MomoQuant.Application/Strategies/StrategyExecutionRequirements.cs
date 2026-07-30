using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Strategies;

/// <summary>
/// Authoritative strategy execution requirements shared by Strategy Laboratory and Validation training.
/// </summary>
public sealed class StrategyExecutionRequirements
{
    public const string Version = "StrategyExecutionRequirements/v1";

    public required long StrategyId { get; init; }
    public required string StrategyCode { get; init; }
    public string? StrategyName { get; init; }
    public string? StrategyVersion { get; init; }

    /// <summary>
    /// Warm-up candle count — identical source used by <c>StrategyLabRunner.ResolveWarmupAsync</c>.
    /// </summary>
    public required int RequiredWarmupCandleCount { get; init; }

    public string RequirementsVersion { get; init; } = Version;
    public IReadOnlyList<string> RequiredIndicators { get; init; } = [];
    public IReadOnlyList<string> PreferredTimeframes { get; init; } = [];
    public string? PreferredExecutionTimeframe { get; init; }

    /// <summary>When true, canonical validation training must bind an HTF partition at scope construction.</summary>
    public bool RequiresHigherTimeframePartition { get; init; }

    /// <summary>API timeframe string (e.g. "1h") for the required HTF partition when Adaptive needs one.</summary>
    public string? RequiredHigherTimeframeApi { get; init; }

    public IReadOnlyList<string> RequiredDataTimeframes { get; init; } = [];
    public IReadOnlyList<string> HigherTimeframeFilters { get; init; } = [];

    /// <summary>Adaptive HTF mapping contract version when <see cref="RequiresHigherTimeframePartition"/> is true for Adaptive.</summary>
    public string? HigherTimeframeMappingContractVersion { get; init; }
}

public sealed class ResolveStrategyExecutionRequirementsRequest
{
    public long? StrategyId { get; init; }
    public string? StrategyCode { get; init; }
    public string? StrategyVersion { get; init; }
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }
}

public interface IStrategyExecutionRequirementsResolver
{
    Task<ServiceResult<StrategyExecutionRequirements>> ResolveAsync(
        ResolveStrategyExecutionRequirementsRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<StrategyExecutionRequirements>> ResolveByStrategyIdAsync(
        long strategyId,
        string? strategyVersion = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Wraps <see cref="IStrategyDataRequirementService"/> so Validation training and Strategy Lab
/// resolve the same <see cref="StrategyExecutionRequirements.RequiredWarmupCandleCount"/>.
/// </summary>
public sealed class StrategyExecutionRequirementsResolver : IStrategyExecutionRequirementsResolver
{
    private readonly IStrategyDataRequirementService _requirementService;
    private readonly IStrategyRepository _strategyRepository;

    public StrategyExecutionRequirementsResolver(
        IStrategyDataRequirementService requirementService,
        IStrategyRepository strategyRepository)
    {
        _requirementService = requirementService;
        _strategyRepository = strategyRepository;
    }

    public Task<ServiceResult<StrategyExecutionRequirements>> ResolveByStrategyIdAsync(
        long strategyId,
        string? strategyVersion = null,
        CancellationToken cancellationToken = default) =>
        ResolveAsync(
            new ResolveStrategyExecutionRequirementsRequest
            {
                StrategyId = strategyId,
                StrategyVersion = strategyVersion
            },
            cancellationToken);

    public async Task<ServiceResult<StrategyExecutionRequirements>> ResolveAsync(
        ResolveStrategyExecutionRequirementsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        long strategyId;
        Domain.Strategies.Strategy? strategyEntity = null;

        if (request.StrategyId is > 0)
        {
            strategyId = request.StrategyId.Value;
            strategyEntity = await _strategyRepository.GetByIdAsync(strategyId, cancellationToken);
            if (strategyEntity is null)
            {
                return ServiceResult<StrategyExecutionRequirements>.Fail(
                    $"Strategy {strategyId} was not found.",
                    "strategyId");
            }
        }
        else if (!string.IsNullOrWhiteSpace(request.StrategyCode))
        {
            var code = StrategyCodeExtensions.FromCode(request.StrategyCode);
            strategyEntity = await _strategyRepository.GetByCodeAsync(code, cancellationToken);
            if (strategyEntity is null)
            {
                return ServiceResult<StrategyExecutionRequirements>.Fail(
                    $"Strategy '{request.StrategyCode}' was not found.",
                    "strategyCode");
            }

            strategyId = strategyEntity.Id;
        }
        else
        {
            return ServiceResult<StrategyExecutionRequirements>.Fail(
                "StrategyId or StrategyCode is required.",
                "strategyId");
        }

        var requirementResult = await _requirementService.GetByStrategyIdAsync(strategyId, cancellationToken);
        if (!requirementResult.Succeeded || requirementResult.Data is null)
        {
            return ServiceResult<StrategyExecutionRequirements>.Fail(
                requirementResult.ErrorMessage ?? "Failed to resolve strategy data requirements.",
                requirementResult.ErrorField);
        }

        return ServiceResult<StrategyExecutionRequirements>.Ok(
            FromDto(requirementResult.Data, strategyEntity, request.StrategyVersion));
    }

    public static StrategyExecutionRequirements FromDto(
        StrategyDataRequirementDto dto,
        Domain.Strategies.Strategy? strategyEntity = null,
        string? strategyVersion = null)
    {
        var requiresHigherTimeframePartition = false;
        string? requiredHigherTimeframeApi = null;
        string? higherTimeframeMappingContractVersion = null;

        StrategyCode strategyEnum;
        try
        {
            strategyEnum = StrategyCodeExtensions.FromCode(dto.StrategyCode);
        }
        catch (ArgumentOutOfRangeException)
        {
            strategyEnum = default;
        }

        if (strategyEnum == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout)
        {
            requiresHigherTimeframePartition = true;
            var execTfApi = !string.IsNullOrWhiteSpace(dto.PreferredExecutionTimeframe)
                ? dto.PreferredExecutionTimeframe
                : dto.PreferredTimeframes.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(execTfApi)
                && TimeframeParser.TryParse(execTfApi, out var execTf)
                && execTf is Timeframe.M5 or Timeframe.M15 or Timeframe.H1 or Timeframe.H4)
            {
                var mappedHtf = MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(execTf);
                requiredHigherTimeframeApi = TimeframeParser.ToApiString(mappedHtf);
                higherTimeframeMappingContractVersion =
                    StrategyHigherTimeframeSupport.AdaptiveHtfMappingContractVersion;
            }
        }

        return new()
        {
            StrategyId = dto.StrategyId,
            StrategyCode = dto.StrategyCode,
            StrategyName = dto.StrategyName ?? strategyEntity?.Name,
            StrategyVersion = strategyVersion ?? strategyEntity?.Version,
            RequiredWarmupCandleCount = dto.WarmupCandles,
            RequirementsVersion = StrategyExecutionRequirements.Version,
            RequiredIndicators = dto.RequiredIndicators,
            PreferredTimeframes = dto.PreferredTimeframes,
            PreferredExecutionTimeframe = dto.PreferredExecutionTimeframe,
            RequiresHigherTimeframePartition = requiresHigherTimeframePartition,
            RequiredHigherTimeframeApi = requiredHigherTimeframeApi,
            RequiredDataTimeframes = dto.RequiredDataTimeframes,
            HigherTimeframeFilters = dto.HigherTimeframeFilters,
            HigherTimeframeMappingContractVersion = higherTimeframeMappingContractVersion
        };
    }
}
