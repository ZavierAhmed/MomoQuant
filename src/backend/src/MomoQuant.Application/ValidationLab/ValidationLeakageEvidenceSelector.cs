using MomoQuant.Domain.Enums;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

/// <summary>
/// Milestone 23.0E2C3 — separates positive completion proof (authoritative scopes only)
/// from negative violation proof (never discarded by supersession).
/// </summary>
public static class ValidationLeakageEvidenceSelector
{
    public static bool IsNegativeBlockingRow(ValidationCandleAccessAudit a) =>
        a.WasDenied
        || string.Equals(a.DenialCode, "ValidationDataLeakageDetected", StringComparison.OrdinalIgnoreCase)
        || (a.DenialCode is not null
            && a.DenialCode.Contains("Leakage", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<ValidationCandleAccessAudit> CollectNegativeBlockingEvidence(
        IEnumerable<ValidationCandleAccessAudit> allAudits) =>
        allAudits.Where(IsNegativeBlockingRow).ToList();

    public sealed class PositiveEvidenceSelection
    {
        public bool AuthoritativeEvidenceIncomplete { get; init; }
        public IReadOnlyList<ValidationCandleAccessAudit> PositiveRows { get; init; } = [];
        public IReadOnlyList<Guid> AuthoritativeScopeExecutionIds { get; init; } = [];
    }

    /// <summary>
    /// Positive rows may come only from verifier-complete authoritative scopes.
    /// Foreign / superseded scopes never fill sequence gaps or supply passing evidence.
    /// </summary>
    public static PositiveEvidenceSelection SelectPositiveEvidence(
        IReadOnlyList<ValidationCandleAccessAudit> allAudits,
        IEnumerable<(ValidationParameterTrial Trial, ValidationAuthoritativeAuditQualificationResult Evaluation)> evaluations)
    {
        ArgumentNullException.ThrowIfNull(allAudits);
        ArgumentNullException.ThrowIfNull(evaluations);

        var positive = new List<ValidationCandleAccessAudit>();
        var scopes = new List<Guid>();
        var incomplete = false;

        foreach (var (trial, evaluation) in evaluations)
        {
            if (!ValidationAuthoritativeAuditQualificationEvaluator.IsGuardrailPassedCompleted(trial))
            {
                continue;
            }

            if (!evaluation.IsApplicable)
            {
                continue;
            }

            if (!evaluation.IsQualificationEligible || evaluation.ScopeExecutionId is null)
            {
                incomplete = true;
                continue;
            }

            var scopeId = evaluation.ScopeExecutionId.Value;
            if (!scopes.Contains(scopeId))
            {
                scopes.Add(scopeId);
            }

            positive.AddRange(allAudits.Where(a => a.ScopeExecutionId == scopeId));
        }

        return new PositiveEvidenceSelection
        {
            AuthoritativeEvidenceIncomplete = incomplete,
            PositiveRows = positive,
            AuthoritativeScopeExecutionIds = scopes
        };
    }
}
