using System.ComponentModel.DataAnnotations;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Strategies.Dtos;

public sealed class StrategyParameterDto
{
    public required long Id { get; init; }
    public required string ParameterKey { get; init; }
    public required string ParameterValue { get; init; }
    public required SettingValueType ValueType { get; init; }
    public required string Timeframe { get; init; }
    public long? SymbolId { get; init; }
    public required bool IsActive { get; init; }
}

public sealed class UpdateStrategyParameterItem
{
    [Required]
    [MaxLength(128)]
    public string ParameterKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string ParameterValue { get; set; } = string.Empty;

    [Required]
    [MaxLength(16)]
    public string Timeframe { get; set; } = string.Empty;

    public long? SymbolId { get; set; }

    public SettingValueType ValueType { get; set; } = SettingValueType.String;
}

public sealed class UpdateStrategyParametersRequest
{
    [Required]
    public List<UpdateStrategyParameterItem> Parameters { get; set; } = [];
}
