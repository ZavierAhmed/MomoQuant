using System.ComponentModel.DataAnnotations;

namespace MomoQuant.Application.ValidationLab.Dtos;

public sealed class PublishValidationParameterSetRequest
{
    [MaxLength(200)]
    public string? DisplayName { get; init; }
}
