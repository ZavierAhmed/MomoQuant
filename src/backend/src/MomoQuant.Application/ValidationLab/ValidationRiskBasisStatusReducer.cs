using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Deterministic aggregate <see cref="ValidationRiskBasisValidationStatus"/> from per-trade statuses.
/// Must not depend on trade order (no last-wins).
/// </summary>
public interface IValidationRiskBasisStatusReducer
{
    /// <summary>
    /// Severity precedence (worst first):
    /// InvalidRiskBasis &gt; PersistedRiskMismatch &gt; InsufficientSample &gt; Valid
    /// (plus all concrete invalid/missing statuses mapped into InvalidRiskBasis).
    /// </summary>
    ValidationRiskBasisValidationStatus Reduce(IEnumerable<ValidationRiskBasisValidationStatus> statuses);
}

public sealed class ValidationRiskBasisStatusReducer : IValidationRiskBasisStatusReducer
{
    /// <summary>
    /// Explicit total order: higher rank is worse.
    /// Aggregate-only statuses <see cref="ValidationRiskBasisValidationStatus.InvalidRiskBasis"/> and
    /// <see cref="ValidationRiskBasisValidationStatus.InsufficientSample"/> sit above Valid.
    /// </summary>
    public static int SeverityRank(ValidationRiskBasisValidationStatus status) => status switch
    {
        ValidationRiskBasisValidationStatus.Valid => 0,
        ValidationRiskBasisValidationStatus.InsufficientSample => 10,
        ValidationRiskBasisValidationStatus.PersistedRiskMismatch => 20,
        ValidationRiskBasisValidationStatus.InvalidRiskBasis => 30,
        // Concrete invalid / missing / incompatible → InvalidRiskBasis band (same severity floor).
        ValidationRiskBasisValidationStatus.MissingEntry => 31,
        ValidationRiskBasisValidationStatus.MissingStop => 32,
        ValidationRiskBasisValidationStatus.MissingQuantity => 33,
        ValidationRiskBasisValidationStatus.NonPositiveRisk => 34,
        ValidationRiskBasisValidationStatus.CurrencyMismatch => 35,
        ValidationRiskBasisValidationStatus.GrossRReconciliationFailed => 36,
        ValidationRiskBasisValidationStatus.NetRReconciliationFailed => 37,
        ValidationRiskBasisValidationStatus.LayerMismatch => 38,
        ValidationRiskBasisValidationStatus.NotAvailable => 39,
        _ => 40
    };

    public static bool IsInvalidRiskBasisBand(ValidationRiskBasisValidationStatus status) =>
        status is not ValidationRiskBasisValidationStatus.Valid
            and not ValidationRiskBasisValidationStatus.PersistedRiskMismatch
            and not ValidationRiskBasisValidationStatus.InsufficientSample;

    public ValidationRiskBasisValidationStatus Reduce(IEnumerable<ValidationRiskBasisValidationStatus> statuses)
    {
        var list = statuses?.ToList() ?? [];
        if (list.Count == 0)
        {
            return ValidationRiskBasisValidationStatus.InsufficientSample;
        }

        var worstRank = int.MinValue;
        var worst = ValidationRiskBasisValidationStatus.Valid;
        foreach (var status in list)
        {
            var rank = SeverityRank(status);
            if (rank > worstRank || (rank == worstRank && (int)status > (int)worst))
            {
                worstRank = rank;
                worst = status;
            }
        }

        if (IsInvalidRiskBasisBand(worst))
        {
            return ValidationRiskBasisValidationStatus.InvalidRiskBasis;
        }

        return worst;
    }
}
