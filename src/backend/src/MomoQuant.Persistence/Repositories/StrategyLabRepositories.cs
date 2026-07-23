using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.Persistence.Repositories;

public sealed class StrategyLabRunRepository : IStrategyLabRunRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public StrategyLabRunRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<StrategyLabRun?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.StrategyLabRuns.FirstOrDefaultAsync(run => run.Id == id, cancellationToken);

    public async Task<IReadOnlyList<StrategyLabRun>> GetByStrategyCodeAsync(string strategyCode, int limit, CancellationToken cancellationToken = default) =>
        await _dbContext.StrategyLabRuns
            .Where(run => run.StrategyCode == strategyCode)
            .OrderByDescending(run => run.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(StrategyLabRun run, CancellationToken cancellationToken = default)
    {
        await _dbContext.StrategyLabRuns.AddAsync(run, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(StrategyLabRun run, CancellationToken cancellationToken = default)
    {
        _dbContext.StrategyLabRuns.Update(run);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StrategyLabRun>> GetRecentAsync(int limit, CancellationToken cancellationToken = default) =>
        await _dbContext.StrategyLabRuns
            .OrderByDescending(run => run.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StrategyLabRun>> GetByNamePrefixAsync(
        string namePrefix,
        CancellationToken cancellationToken = default) =>
        await _dbContext.StrategyLabRuns
            .Where(run => run.Name.StartsWith(namePrefix))
            .OrderBy(run => run.Name)
            .ToListAsync(cancellationToken);

    public async Task<(bool RunTableExists, bool CandidateTableExists)> CheckTablesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await _dbContext.StrategyLabRuns.CountAsync(cancellationToken);
            _ = await _dbContext.StrategyResearchCandidates.CountAsync(cancellationToken);
            return (true, true);
        }
        catch
        {
            return (false, false);
        }
    }
}

public sealed class StrategyResearchCandidateRepository : IStrategyResearchCandidateRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public StrategyResearchCandidateRepository(MomoQuantDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<StrategyResearchCandidate>> GetByRunIdAsync(long runId, CancellationToken cancellationToken = default) =>
        await _dbContext.StrategyResearchCandidates
            .Where(candidate => candidate.StrategyLabRunId == runId)
            .OrderBy(candidate => candidate.SetupDetectedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<StrategyResearchCandidate> Items, int TotalItems)> QueryByRunIdAsync(
        long runId,
        StrategyLabCandidateQuerySpec query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = query.PageSize is 25 or 50 or 100 or 250 ? query.PageSize : 50;
        var q = _dbContext.StrategyResearchCandidates.AsNoTracking()
            .Where(candidate => candidate.StrategyLabRunId == runId);

        if (query.Direction.HasValue)
        {
            q = q.Where(c => c.Direction == query.Direction.Value);
        }

        if (query.RawOutcome.HasValue)
        {
            q = q.Where(c => c.RawOutcomeStatus == query.RawOutcome.Value);
        }

        if (query.ConfidenceDecision.HasValue)
        {
            q = q.Where(c => c.ConfidenceDecision == query.ConfidenceDecision.Value);
        }

        if (query.ConfidenceMin.HasValue)
        {
            q = q.Where(c => c.ConfidenceScore != null && c.ConfidenceScore >= query.ConfidenceMin.Value);
        }

        if (query.ConfidenceMax.HasValue)
        {
            q = q.Where(c => c.ConfidenceScore != null && c.ConfidenceScore <= query.ConfidenceMax.Value);
        }

        if (query.RiskDecision.HasValue)
        {
            q = q.Where(c => c.RiskDecision == query.RiskDecision.Value);
        }

        if (query.RiskOnlyEntryDecision.HasValue)
        {
            q = q.Where(c => c.RiskOnlyEntryDecision == query.RiskOnlyEntryDecision.Value);
        }

        if (query.FullPipelineEntryDecision.HasValue)
        {
            q = q.Where(c => c.FullPipelineEntryDecision == query.FullPipelineEntryDecision.Value);
        }

        if (query.RiskOnlyDrawdownMin.HasValue)
        {
            q = q.Where(c =>
                c.RiskOnlyCurrentDrawdownPercent != null
                && c.RiskOnlyCurrentDrawdownPercent >= query.RiskOnlyDrawdownMin.Value);
        }

        if (query.FullPipelineDrawdownMin.HasValue)
        {
            q = q.Where(c =>
                c.FullPipelineCurrentDrawdownPercent != null
                && c.FullPipelineCurrentDrawdownPercent >= query.FullPipelineDrawdownMin.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.RiskOnlyFailedRule))
        {
            var rule = query.RiskOnlyFailedRule.Trim();
            q = q.Where(c =>
                c.RiskOnlyAssessmentJson != null && c.RiskOnlyAssessmentJson.Contains(rule));
        }

        if (!string.IsNullOrWhiteSpace(query.FullPipelineFailedRule))
        {
            var rule = query.FullPipelineFailedRule.Trim();
            q = q.Where(c =>
                c.FullPipelineAssessmentJson != null && c.FullPipelineAssessmentJson.Contains(rule));
        }

        ApplyPathDecisionDifference(ref q, query.PathDecisionDifference);

        if (query.RiskMin.HasValue)
        {
            q = q.Where(c => c.RiskScore != null && c.RiskScore >= query.RiskMin.Value);
        }

        if (query.RiskMax.HasValue)
        {
            q = q.Where(c => c.RiskScore != null && c.RiskScore <= query.RiskMax.Value);
        }

        if (query.FromUtc.HasValue)
        {
            q = q.Where(c => c.SetupDetectedAtUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            q = q.Where(c => c.SetupDetectedAtUtc <= query.ToUtc.Value);
        }

        if (query.ProfitableOnly == true)
        {
            q = q.Where(c => c.RawNetPnl != null && c.RawNetPnl > 0);
        }
        else if (query.ProfitableOnly == false)
        {
            q = q.Where(c => c.RawNetPnl != null && c.RawNetPnl < 0);
        }

        ApplyQuickFilter(ref q, query.QuickFilter);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            q = q.Where(c =>
                (c.StrategyReason != null && c.StrategyReason.ToLower().Contains(search))
                || (c.ConfidenceReason != null && c.ConfidenceReason.ToLower().Contains(search))
                || (c.RiskReason != null && c.RiskReason.ToLower().Contains(search))
                || (c.SetupFingerprint != null && c.SetupFingerprint.ToLower().Contains(search))
                || (c.RiskRejectedRuleKey != null && c.RiskRejectedRuleKey.ToLower().Contains(search)));
        }

        var total = await q.CountAsync(cancellationToken);
        q = ApplySort(q, query.SortBy, query.SortDirection);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    private static void ApplyQuickFilter(ref IQueryable<StrategyResearchCandidate> q, string? quickFilter)
    {
        if (string.IsNullOrWhiteSpace(quickFilter))
        {
            return;
        }

        switch (quickFilter.Trim().ToLowerInvariant())
        {
            case "rejectedwinners":
                q = q.Where(c =>
                    c.RawOutcomeStatus == RawOutcomeStatus.Winner
                    && (c.ConfidenceDecision == ResearchConfidenceDecision.Rejected
                        || c.RiskDecision == ResearchRiskDecision.Rejected));
                break;
            case "rejectedlosers":
                q = q.Where(c =>
                    c.RawOutcomeStatus == RawOutcomeStatus.Loser
                    && (c.ConfidenceDecision == ResearchConfidenceDecision.Rejected
                        || c.RiskDecision == ResearchRiskDecision.Rejected));
                break;
            case "confidencerejectedwinners":
                q = q.Where(c =>
                    c.RawOutcomeStatus == RawOutcomeStatus.Winner
                    && c.ConfidenceDecision == ResearchConfidenceDecision.Rejected);
                break;
            case "riskrejectedwinners":
            case "financialriskrejectedwinners":
                q = q.Where(c =>
                    c.RawOutcomeStatus == RawOutcomeStatus.Winner
                    && c.RiskDecision == ResearchRiskDecision.Rejected);
                break;
            case "financialriskrejectedlosers":
                q = q.Where(c =>
                    c.RawOutcomeStatus == RawOutcomeStatus.Loser
                    && c.RiskDecision == ResearchRiskDecision.Rejected);
                break;
            case "financialriskapprovedwinners":
                q = q.Where(c =>
                    c.RawOutcomeStatus == RawOutcomeStatus.Winner
                    && c.RiskDecision == ResearchRiskDecision.Approved);
                break;
            case "confidencerejectedriskapproved":
                q = q.Where(c =>
                    c.ConfidenceDecision == ResearchConfidenceDecision.Rejected
                    && c.RiskDecision == ResearchRiskDecision.Approved);
                break;
            case "confidenceapprovedriskrejected":
                q = q.Where(c =>
                    c.ConfidenceDecision == ResearchConfidenceDecision.Approved
                    && c.RiskDecision == ResearchRiskDecision.Rejected);
                break;
            case "rejectedindependentlybyboth":
                q = q.Where(c =>
                    c.ConfidenceDecision == ResearchConfidenceDecision.Rejected
                    && c.RiskDecision == ResearchRiskDecision.Rejected);
                break;
            case "riskpolicyonlyrejection":
                q = q.Where(c =>
                    c.RiskPolicyEligibilityDecision == ResearchRiskPolicyEligibilityDecision.Ineligible
                    && c.RiskDecision != ResearchRiskDecision.Rejected
                    && c.ConfidenceDecision != ResearchConfidenceDecision.Rejected);
                break;
            case "approvedwinners":
                q = q.Where(c =>
                    c.RawOutcomeStatus == RawOutcomeStatus.Winner
                    && (c.ConfidenceDecision == ResearchConfidenceDecision.Approved
                        || c.RiskDecision == ResearchRiskDecision.Approved)
                    && c.ConfidenceDecision != ResearchConfidenceDecision.Rejected
                    && c.RiskDecision != ResearchRiskDecision.Rejected);
                break;
            case "approvedlosers":
                q = q.Where(c =>
                    c.RawOutcomeStatus == RawOutcomeStatus.Loser
                    && (c.ConfidenceDecision == ResearchConfidenceDecision.Approved
                        || c.RiskDecision == ResearchRiskDecision.Approved)
                    && c.ConfidenceDecision != ResearchConfidenceDecision.Rejected
                    && c.RiskDecision != ResearchRiskDecision.Rejected);
                break;
            case "riskscorepassedbuthardrulesfailed":
                q = q.Where(c =>
                    c.RiskScoreDecision == ResearchRiskScoreDecision.Passed
                    && c.HardRuleComplianceDecision == ResearchHardRuleComplianceDecision.NonCompliant);
                break;
            case "notionalexposurerejected":
                q = q.Where(c =>
                    c.RiskDecision == ResearchRiskDecision.Rejected
                    && c.RiskFailedRuleKeysJson != null
                    && (c.RiskFailedRuleKeysJson.Contains("MaxNotionalExposurePerSymbolPercent")
                        || c.RiskFailedRuleKeysJson.Contains("MaxTotalNotionalExposurePercent")));
                break;
            case "marginusagerejected":
                q = q.Where(c =>
                    c.RiskDecision == ResearchRiskDecision.Rejected
                    && c.RiskFailedRuleKeysJson != null
                    && (c.RiskFailedRuleKeysJson.Contains("MaxMarginUsagePerSymbolPercent")
                        || c.RiskFailedRuleKeysJson.Contains("MaxTotalMarginUsagePercent")));
                break;
            case "leveragerejected":
                q = q.Where(c =>
                    c.RiskDecision == ResearchRiskDecision.Rejected
                    && c.RiskFailedRuleKeysJson != null
                    && c.RiskFailedRuleKeysJson.Contains("MaxLeverage"));
                break;
            case "concurrentriskrejected":
                q = q.Where(c =>
                    c.RiskDecision == ResearchRiskDecision.Rejected
                    && c.RiskFailedRuleKeysJson != null
                    && c.RiskFailedRuleKeysJson.Contains("MaxConcurrentRiskPercent"));
                break;
            case "targethitbutnetloss":
                q = q.Where(c =>
                    c.ExitOutcome == ResearchExitOutcome.TargetHit
                    && c.NetResult == ResearchNetResult.Losing);
                break;
            case "openpositionconflict":
                q = q.Where(c =>
                    c.RiskDecision == ResearchRiskDecision.Rejected
                    && c.RiskFailedRuleKeysJson != null
                    && c.RiskFailedRuleKeysJson.Contains("MaxOpenPositions"));
                break;
            case "dailylossrejection":
                q = q.Where(c =>
                    c.RiskDecision == ResearchRiskDecision.Rejected
                    && c.RiskFailedRuleKeysJson != null
                    && c.RiskFailedRuleKeysJson.Contains("MaxDailyLossPercent"));
                break;
            case "drawdownrejection":
                q = q.Where(c =>
                    c.RiskDecision == ResearchRiskDecision.Rejected
                    && c.RiskFailedRuleKeysJson != null
                    && c.RiskFailedRuleKeysJson.Contains("MaxDrawdownPercent"));
                break;
            case "riskonlyrejectedfullpipelineopened":
                q = q.Where(c =>
                    c.RiskOnlyEntryDecision != ShadowEntryDecision.Opened
                    && c.FullPipelineEntryDecision == ShadowEntryDecision.Opened);
                break;
            case "riskonlyopenedfullpipelinerejected":
                q = q.Where(c =>
                    c.RiskOnlyEntryDecision == ShadowEntryDecision.Opened
                    && c.FullPipelineEntryDecision != ShadowEntryDecision.Opened);
                break;
            case "differentdrawdowndecisions":
                q = q.Where(c =>
                    c.RiskOnlyAssessmentJson != null
                    && c.FullPipelineAssessmentJson != null
                    && ((c.RiskOnlyAssessmentJson.Contains("MaxDrawdownPercent")
                         && !c.FullPipelineAssessmentJson.Contains("MaxDrawdownPercent"))
                        || (!c.RiskOnlyAssessmentJson.Contains("MaxDrawdownPercent")
                            && c.FullPipelineAssessmentJson.Contains("MaxDrawdownPercent"))));
                break;
            case "differentdailylossdecisions":
                q = q.Where(c =>
                    c.RiskOnlyAssessmentJson != null
                    && c.FullPipelineAssessmentJson != null
                    && ((c.RiskOnlyAssessmentJson.Contains("MaxDailyLossPercent")
                         && !c.FullPipelineAssessmentJson.Contains("MaxDailyLossPercent"))
                        || (!c.RiskOnlyAssessmentJson.Contains("MaxDailyLossPercent")
                            && c.FullPipelineAssessmentJson.Contains("MaxDailyLossPercent"))));
                break;
            case "differentexposuredecisions":
                q = q.Where(c =>
                    c.RiskOnlyFinancialRiskDecision != null
                    && c.FullPipelineFinancialRiskDecision != null
                    && c.RiskOnlyFinancialRiskDecision != c.FullPipelineFinancialRiskDecision
                    && ((c.RiskOnlyAssessmentJson != null
                         && (c.RiskOnlyAssessmentJson.Contains("MaxTotalMarginUsagePercent")
                             || c.RiskOnlyAssessmentJson.Contains("MaxTotalNotionalExposurePercent")))
                        || (c.FullPipelineAssessmentJson != null
                            && (c.FullPipelineAssessmentJson.Contains("MaxTotalMarginUsagePercent")
                                || c.FullPipelineAssessmentJson.Contains("MaxTotalNotionalExposurePercent")))));
                break;
            case "openedinboth":
                q = q.Where(c =>
                    c.RiskOnlyEntryDecision == ShadowEntryDecision.Opened
                    && c.FullPipelineEntryDecision == ShadowEntryDecision.Opened);
                break;
            case "openedinneither":
                q = q.Where(c =>
                    c.RiskOnlyEntryDecision != null
                    && c.FullPipelineEntryDecision != null
                    && c.RiskOnlyEntryDecision != ShadowEntryDecision.Opened
                    && c.FullPipelineEntryDecision != ShadowEntryDecision.Opened);
                break;
            case "confidenceonlyrejection":
                q = q.Where(c =>
                    c.FullPipelineEntryDecision == ShadowEntryDecision.RejectedByConfidence
                    && c.RiskOnlyEntryDecision == ShadowEntryDecision.Opened);
                break;
            case "fullpipelineportfolioriskrejection":
                q = q.Where(c =>
                    c.FullPipelineEntryDecision == ShadowEntryDecision.RejectedByPortfolioRisk);
                break;
        }
    }

    private static void ApplyPathDecisionDifference(ref IQueryable<StrategyResearchCandidate> q, string? difference)
    {
        if (string.IsNullOrWhiteSpace(difference))
        {
            return;
        }

        switch (difference.Trim().ToLowerInvariant())
        {
            case "entry":
                q = q.Where(c =>
                    c.RiskOnlyEntryDecision != null
                    && c.FullPipelineEntryDecision != null
                    && c.RiskOnlyEntryDecision != c.FullPipelineEntryDecision);
                break;
            case "financialrisk":
                q = q.Where(c =>
                    c.RiskOnlyFinancialRiskDecision != null
                    && c.FullPipelineFinancialRiskDecision != null
                    && c.RiskOnlyFinancialRiskDecision != c.FullPipelineFinancialRiskDecision);
                break;
            case "any":
                q = q.Where(c =>
                    (c.RiskOnlyEntryDecision != null
                     && c.FullPipelineEntryDecision != null
                     && c.RiskOnlyEntryDecision != c.FullPipelineEntryDecision)
                    || (c.RiskOnlyFinancialRiskDecision != null
                        && c.FullPipelineFinancialRiskDecision != null
                        && c.RiskOnlyFinancialRiskDecision != c.FullPipelineFinancialRiskDecision));
                break;
        }
    }

    private static IQueryable<StrategyResearchCandidate> ApplySort(
        IQueryable<StrategyResearchCandidate> q,
        string? sortBy,
        string? sortDirection)
    {
        var desc = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "direction" => desc ? q.OrderByDescending(c => c.Direction) : q.OrderBy(c => c.Direction),
            "rawoutcome" or "rawoutcomestatus" => desc
                ? q.OrderByDescending(c => c.RawOutcomeStatus)
                : q.OrderBy(c => c.RawOutcomeStatus),
            "rawnetpnl" or "rawpnl" => desc
                ? q.OrderByDescending(c => c.RawNetPnl)
                : q.OrderBy(c => c.RawNetPnl),
            "rawrmultiple" => desc ? q.OrderByDescending(c => c.RawRMultiple) : q.OrderBy(c => c.RawRMultiple),
            "confidencescore" => desc ? q.OrderByDescending(c => c.ConfidenceScore) : q.OrderBy(c => c.ConfidenceScore),
            "confidencedecision" => desc
                ? q.OrderByDescending(c => c.ConfidenceDecision)
                : q.OrderBy(c => c.ConfidenceDecision),
            "riskscore" => desc ? q.OrderByDescending(c => c.RiskScore) : q.OrderBy(c => c.RiskScore),
            "riskdecision" => desc ? q.OrderByDescending(c => c.RiskDecision) : q.OrderBy(c => c.RiskDecision),
            "rewardrisk" => desc ? q.OrderByDescending(c => c.RewardRisk) : q.OrderBy(c => c.RewardRisk),
            "entry" or "proposedentryprice" => desc
                ? q.OrderByDescending(c => c.ProposedEntryPrice)
                : q.OrderBy(c => c.ProposedEntryPrice),
            "mfe" => desc ? q.OrderByDescending(c => c.Mfe) : q.OrderBy(c => c.Mfe),
            "mae" => desc ? q.OrderByDescending(c => c.Mae) : q.OrderBy(c => c.Mae),
            "durationbars" => desc ? q.OrderByDescending(c => c.DurationBars) : q.OrderBy(c => c.DurationBars),
            _ => desc
                ? q.OrderByDescending(c => c.SetupDetectedAtUtc)
                : q.OrderBy(c => c.SetupDetectedAtUtc)
        };
    }

    public async Task AddRangeAsync(IEnumerable<StrategyResearchCandidate> candidates, CancellationToken cancellationToken = default)
    {
        var list = candidates.ToList();
        await _dbContext.StrategyResearchCandidates.AddRangeAsync(list, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var assessments = new List<StrategyResearchCandidatePortfolioAssessment>();
        foreach (var candidate in list)
        {
            TryAddAssessment(assessments, candidate, candidate.RiskOnlyAssessmentJson, StrategyLabPortfolioPath.RiskOnly);
            TryAddAssessment(assessments, candidate, candidate.FullPipelineAssessmentJson, StrategyLabPortfolioPath.FullPipeline);
        }

        if (assessments.Count > 0)
        {
            await _dbContext.StrategyResearchCandidatePortfolioAssessments.AddRangeAsync(assessments, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static void TryAddAssessment(
        List<StrategyResearchCandidatePortfolioAssessment> target,
        StrategyResearchCandidate candidate,
        string? json,
        StrategyLabPortfolioPath path)
    {
        if (string.IsNullOrWhiteSpace(json) || candidate.Id <= 0)
        {
            return;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<PathPortfolioAssessmentDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (dto is null)
            {
                return;
            }

            target.Add(new StrategyResearchCandidatePortfolioAssessment
            {
                StrategyResearchCandidateId = candidate.Id,
                PortfolioPath = path,
                AssessmentBalance = dto.AssessmentBalance,
                RiskAmount = dto.RiskAmount,
                Quantity = dto.Quantity,
                PositionNotional = dto.PositionNotional,
                MinimumRequiredLeverage = dto.MinimumRequiredLeverage,
                AssessmentLeverage = dto.AssessmentLeverage,
                InitialMarginRequired = dto.InitialMarginRequired,
                CandidateMarginUsagePercent = dto.CandidateMarginUsagePercent,
                CurrentNotionalExposurePercent = dto.CurrentNotionalExposurePercent,
                CurrentMarginUsagePercent = dto.CurrentMarginUsagePercent,
                ProjectedTotalNotionalExposurePercent = dto.ProjectedTotalNotionalExposurePercent,
                ProjectedTotalMarginUsagePercent = dto.ProjectedTotalMarginUsagePercent,
                CurrentConcurrentRiskPercent = dto.CurrentConcurrentRiskPercent,
                ProjectedConcurrentRiskPercent = dto.ProjectedConcurrentRiskPercent,
                CurrentDailyLossUsagePercent = dto.CurrentDailyLossUsagePercent,
                CurrentDrawdownPercent = dto.CurrentDrawdownPercent,
                CurrentOpenPositionCount = dto.CurrentOpenPositionCount,
                PortfolioRiskScore = dto.PortfolioRiskScore,
                RiskScoreDecision = dto.RiskScoreDecision,
                HardRuleComplianceDecision = dto.HardRuleComplianceDecision,
                FinancialRiskDecision = dto.FinancialRiskDecision,
                RiskReason = dto.RiskReason ?? string.Empty,
                FailedRuleKeysJson = RiskObservationJson.Serialize(dto.FailedRuleKeys),
                WarningRuleKeysJson = RiskObservationJson.Serialize(dto.WarningRuleKeys),
                RuleResultsJson = RiskObservationJson.Serialize(dto.RuleResults),
                EntryDecision = dto.EntryDecision,
                EntryDecisionReason = dto.EntryDecisionReason ?? string.Empty,
                RejectionSourcesJson = RiskObservationJson.Serialize(dto.RejectionSources),
                AssessmentVersion = string.IsNullOrWhiteSpace(dto.AssessmentVersion)
                    ? IndependentPathsVersions.Current
                    : dto.AssessmentVersion,
                EvaluatedAtUtc = dto.EvaluatedAtUtc == default ? DateTime.UtcNow : dto.EvaluatedAtUtc
            });
        }
        catch
        {
            // Do not fail candidate persistence if assessment deserialization fails.
        }
    }

    public Task<int> CountByStrategyCodeAsync(string strategyCode, CancellationToken cancellationToken = default) =>
        _dbContext.StrategyResearchCandidates.CountAsync(c => c.StrategyCode == strategyCode, cancellationToken);
}
