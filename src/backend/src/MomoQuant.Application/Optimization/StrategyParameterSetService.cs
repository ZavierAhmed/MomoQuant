using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Optimization.Dtos;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Optimization;

public sealed class StrategyParameterSetService : IStrategyParameterSetService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IStrategyParameterSetRepository _repository;

    public StrategyParameterSetService(IStrategyParameterSetRepository repository)
    {
        _repository = repository;
    }

    public async Task<ServiceResult<IReadOnlyList<StrategyParameterSetDto>>> ListAsync(
        string? strategyCode,
        long? symbolId,
        string? timeframe,
        CancellationToken cancellationToken = default)
    {
        var items = await _repository.ListAsync(strategyCode, symbolId, timeframe, cancellationToken);
        return ServiceResult<IReadOnlyList<StrategyParameterSetDto>>.Ok(items.Select(Map).ToList());
    }

    public async Task<ServiceResult<StrategyParameterSetDto>> SaveAsync(
        SaveStrategyParameterSetRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationTradeCount = request.ValidationTradeCount ?? request.ValidationMetrics?.TradeCount;
        var approve = request.Approve && !request.SaveAsFailedResearch;

        if (approve)
        {
            if (string.Equals(request.ValidationStatus, ValidationStatus.Failed.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult<StrategyParameterSetDto>.Fail(
                    "This parameter set failed validation and cannot be approved.");
            }

            if (validationTradeCount is 0)
            {
                return ServiceResult<StrategyParameterSetDto>.Fail(
                    "No trades were produced. Run optimization or adjust strategy settings before approving.");
            }
        }

        var entity = new StrategyParameterSet
        {
            Name = request.Name,
            StrategyCode = request.StrategyCode,
            SymbolId = request.SymbolId,
            Timeframe = request.Timeframe,
            ParametersJson = JsonSerializer.Serialize(request.Parameters, JsonOptions),
            Source = ResolveSource(request),
            OptimizationRunId = request.OptimizationRunId,
            TrainingRangeJson = request.TrainingRange is null ? null : JsonSerializer.Serialize(request.TrainingRange, JsonOptions),
            ValidationRangeJson = request.ValidationRange is null ? null : JsonSerializer.Serialize(request.ValidationRange, JsonOptions),
            TrainingMetricsJson = request.TrainingMetrics is null ? null : JsonSerializer.Serialize(request.TrainingMetrics, JsonOptions),
            ValidationMetricsJson = request.ValidationMetrics is null ? null : JsonSerializer.Serialize(request.ValidationMetrics, JsonOptions),
            RobustnessScore = request.RobustnessScore,
            IsApproved = approve,
            IsDefaultForStrategy = request.SetAsDefault && approve,
            IsDefaultForSymbolTimeframe = request.SetAsDefault && request.SymbolId.HasValue && approve,
            CreatedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = approve ? DateTime.UtcNow : null
        };

        await _repository.AddAsync(entity, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return ServiceResult<StrategyParameterSetDto>.Ok(Map(entity));
    }

    public async Task<ServiceResult<StrategyParameterSetDto>> ApproveAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByIdForUpdateAsync(id, cancellationToken);
        if (entity is null)
        {
            return ServiceResult<StrategyParameterSetDto>.Fail("Parameter set was not found.");
        }

        if (!string.IsNullOrWhiteSpace(entity.ValidationMetricsJson))
        {
            var metrics = JsonSerializer.Deserialize<StrategyPerformanceMetricsDto>(entity.ValidationMetricsJson, JsonOptions);
            if (metrics?.TradeCount == 0)
            {
                return ServiceResult<StrategyParameterSetDto>.Fail(
                    "No trades were produced. This parameter set cannot be approved.");
            }
        }

        entity.IsApproved = true;
        entity.ApprovedAtUtc = DateTime.UtcNow;
        await _repository.UpdateAsync(entity, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return ServiceResult<StrategyParameterSetDto>.Ok(Map(entity));
    }

    public async Task<IReadOnlyDictionary<string, string>?> GetFrozenParametersAsync(long parameterSetId, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByIdAsync(parameterSetId, cancellationToken);
        if (entity is null) return null;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(entity.ParametersJson, JsonOptions);
    }

    private static StrategyParameterSetSource ResolveSource(SaveStrategyParameterSetRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Source) &&
            Enum.TryParse<StrategyParameterSetSource>(request.Source, true, out var parsed))
        {
            return parsed;
        }

        if (request.TargetOptimizationRunId.HasValue)
        {
            return StrategyParameterSetSource.TargetOptimized;
        }

        return request.OptimizationRunId.HasValue
            ? StrategyParameterSetSource.Optimized
            : StrategyParameterSetSource.Manual;
    }

    private static StrategyParameterSetDto Map(StrategyParameterSet entity)
    {
        var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.ParametersJson, JsonOptions)
            ?? new Dictionary<string, string>();
        return new StrategyParameterSetDto
        {
            Id = entity.Id,
            Name = entity.Name,
            StrategyCode = entity.StrategyCode,
            SymbolId = entity.SymbolId,
            Timeframe = entity.Timeframe,
            Parameters = parameters,
            Source = entity.Source.ToString(),
            OptimizationRunId = entity.OptimizationRunId,
            RobustnessScore = entity.RobustnessScore,
            IsApproved = entity.IsApproved,
            IsDefaultForStrategy = entity.IsDefaultForStrategy,
            IsDefaultForSymbolTimeframe = entity.IsDefaultForSymbolTimeframe,
            CreatedAtUtc = entity.CreatedAtUtc,
            ApprovedAtUtc = entity.ApprovedAtUtc
        };
    }
}
