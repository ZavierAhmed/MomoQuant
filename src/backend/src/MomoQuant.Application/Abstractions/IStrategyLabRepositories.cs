using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Application.Abstractions;

public interface IStrategyLabRunRepository
{
    Task<StrategyLabRun?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StrategyLabRun>> GetByStrategyCodeAsync(string strategyCode, int limit, CancellationToken cancellationToken = default);
    Task AddAsync(StrategyLabRun run, CancellationToken cancellationToken = default);
    Task UpdateAsync(StrategyLabRun run, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StrategyLabRun>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StrategyLabRun>> GetByNamePrefixAsync(string namePrefix, CancellationToken cancellationToken = default);
    Task<(bool RunTableExists, bool CandidateTableExists)> CheckTablesAsync(CancellationToken cancellationToken = default);
}

public interface IStrategyResearchCandidateRepository
{
    Task<IReadOnlyList<StrategyResearchCandidate>> GetByRunIdAsync(long runId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<StrategyResearchCandidate> Items, int TotalItems)> QueryByRunIdAsync(
        long runId,
        StrategyLabCandidateQuerySpec query,
        CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<StrategyResearchCandidate> candidates, CancellationToken cancellationToken = default);
    Task<int> CountByStrategyCodeAsync(string strategyCode, CancellationToken cancellationToken = default);
}

public sealed class StrategyLabCandidateQuerySpec
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string? SortBy { get; init; }
    public string? SortDirection { get; init; }
    public string? Search { get; init; }
    public TradeDirection? Direction { get; init; }
    public RawOutcomeStatus? RawOutcome { get; init; }
    public ResearchConfidenceDecision? ConfidenceDecision { get; init; }
    public decimal? ConfidenceMin { get; init; }
    public decimal? ConfidenceMax { get; init; }
    public ResearchRiskDecision? RiskDecision { get; init; }
    public decimal? RiskMin { get; init; }
    public decimal? RiskMax { get; init; }
    public bool? ProfitableOnly { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public string? QuickFilter { get; init; }
    public ShadowEntryDecision? RiskOnlyEntryDecision { get; init; }
    public ShadowEntryDecision? FullPipelineEntryDecision { get; init; }
    public string? PathDecisionDifference { get; init; }
    public string? RiskOnlyFailedRule { get; init; }
    public string? FullPipelineFailedRule { get; init; }
    public decimal? RiskOnlyDrawdownMin { get; init; }
    public decimal? FullPipelineDrawdownMin { get; init; }
}
