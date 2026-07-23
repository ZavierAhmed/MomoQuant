using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Risk.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Risk;

namespace MomoQuant.Application.Risk;

public interface IRiskProfileService
{
    Task<ServiceResult<IReadOnlyList<RiskProfileDto>>> GetProfilesAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<RiskProfileDto>> GetProfileByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<RiskProfileDto>> CreateProfileAsync(
        CreateRiskProfileRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<RiskProfileDto>> UpdateProfileAsync(
        long id,
        UpdateRiskProfileRequest request,
        CancellationToken cancellationToken = default);
}

public interface IRiskRuleService
{
    Task<ServiceResult<IReadOnlyList<RiskRuleDto>>> GetRulesAsync(long profileId, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<RiskRuleDto>>> UpdateRulesAsync(
        long profileId,
        UpdateRiskRulesRequest request,
        CancellationToken cancellationToken = default);
}

public interface IRiskDecisionService
{
    Task<ServiceResult<RiskDecisionDto>> GetDecisionByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<ServiceResult<Shared.Contracts.PagedResult<RiskDecisionDto>>> GetDecisionsAsync(
        Shared.Contracts.PagedRequest request,
        long? symbolId,
        CancellationToken cancellationToken = default);

    Task<RiskDecision> PersistDecisionAsync(
        RiskContext context,
        RiskEvaluationResult evaluation,
        CancellationToken cancellationToken = default);
}

public interface IRiskEvaluationService
{
    Task<ServiceResult<RiskEvaluationResponse>> EvaluateAsync(
        RiskEvaluationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class RiskProfileService : IRiskProfileService
{
    private readonly IRiskProfileRepository _profileRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;

    public RiskProfileService(
        IRiskProfileRepository profileRepository,
        ICurrentUserService currentUserService,
        IAuditService auditService)
    {
        _profileRepository = profileRepository;
        _currentUserService = currentUserService;
        _auditService = auditService;
    }

    public async Task<ServiceResult<IReadOnlyList<RiskProfileDto>>> GetProfilesAsync(CancellationToken cancellationToken = default)
    {
        var profiles = await _profileRepository.GetAllAsync(cancellationToken);
        return ServiceResult<IReadOnlyList<RiskProfileDto>>.Ok(profiles.Select(MapToDto).ToList());
    }

    public async Task<ServiceResult<RiskProfileDto>> GetProfileByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var profile = await _profileRepository.GetByIdAsync(id, cancellationToken);
        return profile is null
            ? ServiceResult<RiskProfileDto>.Fail("Risk profile was not found.")
            : ServiceResult<RiskProfileDto>.Ok(MapToDto(profile));
    }

    public async Task<ServiceResult<RiskProfileDto>> CreateProfileAsync(
        CreateRiskProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();
        if (await _profileRepository.GetByNameAsync(name, cancellationToken) is not null)
        {
            return ServiceResult<RiskProfileDto>.Fail("Risk profile name is already in use.", "name");
        }

        var now = DateTime.UtcNow;
        var profile = new RiskProfile
        {
            Name = name,
            Description = request.Description.Trim(),
            IsDefault = request.IsDefault,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await _profileRepository.AddAsync(profile, cancellationToken);
        await _profileRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "RISK_PROFILE_CREATED",
            nameof(RiskProfile),
            entityId: profile.Id,
            userId: _currentUserService.UserId,
            newValueJson: $"{{\"name\":\"{EscapeJson(profile.Name)}\"}}",
            cancellationToken: cancellationToken);

        return ServiceResult<RiskProfileDto>.Ok(MapToDto(profile));
    }

    public async Task<ServiceResult<RiskProfileDto>> UpdateProfileAsync(
        long id,
        UpdateRiskProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var profile = await _profileRepository.GetByIdForUpdateAsync(id, cancellationToken);
        if (profile is null)
        {
            return ServiceResult<RiskProfileDto>.Fail("Risk profile was not found.");
        }

        var name = request.Name.Trim();
        var existing = await _profileRepository.GetByNameAsync(name, cancellationToken);
        if (existing is not null && existing.Id != id)
        {
            return ServiceResult<RiskProfileDto>.Fail("Risk profile name is already in use.", "name");
        }

        profile.Name = name;
        profile.Description = request.Description.Trim();
        profile.IsDefault = request.IsDefault;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        await _profileRepository.UpdateAsync(profile, cancellationToken);
        await _profileRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "RISK_PROFILE_UPDATED",
            nameof(RiskProfile),
            entityId: profile.Id,
            userId: _currentUserService.UserId,
            newValueJson: $"{{\"name\":\"{EscapeJson(profile.Name)}\"}}",
            cancellationToken: cancellationToken);

        return ServiceResult<RiskProfileDto>.Ok(MapToDto(profile));
    }

    private static RiskProfileDto MapToDto(RiskProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Description = profile.Description,
        IsDefault = profile.IsDefault
    };

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}

public sealed class RiskRuleService : IRiskRuleService
{
    private readonly IRiskProfileRepository _profileRepository;
    private readonly IRiskRuleRepository _ruleRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuditService _auditService;

    public RiskRuleService(
        IRiskProfileRepository profileRepository,
        IRiskRuleRepository ruleRepository,
        ICurrentUserService currentUserService,
        IAuditService auditService)
    {
        _profileRepository = profileRepository;
        _ruleRepository = ruleRepository;
        _currentUserService = currentUserService;
        _auditService = auditService;
    }

    public async Task<ServiceResult<IReadOnlyList<RiskRuleDto>>> GetRulesAsync(
        long profileId,
        CancellationToken cancellationToken = default)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId, cancellationToken);
        if (profile is null)
        {
            return ServiceResult<IReadOnlyList<RiskRuleDto>>.Fail("Risk profile was not found.");
        }

        var rules = await _ruleRepository.GetByProfileIdAsync(profileId, cancellationToken);
        return ServiceResult<IReadOnlyList<RiskRuleDto>>.Ok(rules.Select(MapToDto).ToList());
    }

    public async Task<ServiceResult<IReadOnlyList<RiskRuleDto>>> UpdateRulesAsync(
        long profileId,
        UpdateRiskRulesRequest request,
        CancellationToken cancellationToken = default)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId, cancellationToken);
        if (profile is null)
        {
            return ServiceResult<IReadOnlyList<RiskRuleDto>>.Fail("Risk profile was not found.");
        }

        var emergencyStopChanged = false;
        foreach (var item in request.Rules)
        {
            var ruleKey = item.RuleKey.Trim();
            var existing = await _ruleRepository.GetByKeyAsync(profileId, ruleKey, cancellationToken);
            var now = DateTime.UtcNow;

            if (existing is null)
            {
                await _ruleRepository.AddAsync(new RiskRule
                {
                    RiskProfileId = profileId,
                    RuleKey = ruleKey,
                    RuleValue = item.RuleValue.Trim(),
                    ValueType = item.ValueType,
                    IsEnabled = item.IsEnabled,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                }, cancellationToken);
            }
            else
            {
                if (ruleKey == RiskRuleKeys.EmergencyStopEnabled &&
                    !string.Equals(existing.RuleValue, item.RuleValue.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    emergencyStopChanged = true;
                }

                existing.RuleValue = item.RuleValue.Trim();
                existing.ValueType = item.ValueType;
                existing.IsEnabled = item.IsEnabled;
                existing.UpdatedAtUtc = now;
                await _ruleRepository.UpdateAsync(existing, cancellationToken);
            }
        }

        await _ruleRepository.SaveChangesAsync(cancellationToken);

        await _auditService.LogAsync(
            "RISK_RULES_UPDATED",
            nameof(RiskProfile),
            entityId: profileId,
            userId: _currentUserService.UserId,
            newValueJson: $"{{\"ruleCount\":{request.Rules.Count}}}",
            cancellationToken: cancellationToken);

        if (emergencyStopChanged)
        {
            await _auditService.LogAsync(
                "RISK_EMERGENCY_STOP_RULE_CHANGED",
                nameof(RiskProfile),
                entityId: profileId,
                userId: _currentUserService.UserId,
                newValueJson: $"{{\"profileName\":\"{EscapeJson(profile.Name)}\"}}",
                cancellationToken: cancellationToken);
        }

        var updated = await _ruleRepository.GetByProfileIdAsync(profileId, cancellationToken);
        return ServiceResult<IReadOnlyList<RiskRuleDto>>.Ok(updated.Select(MapToDto).ToList());
    }

    private static RiskRuleDto MapToDto(RiskRule rule) => new()
    {
        Id = rule.Id,
        RuleKey = rule.RuleKey,
        RuleValue = rule.RuleValue,
        ValueType = rule.ValueType,
        IsEnabled = rule.IsEnabled
    };

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}

public sealed class RiskDecisionService : IRiskDecisionService
{
    private readonly IRiskDecisionRepository _decisionRepository;

    public RiskDecisionService(IRiskDecisionRepository decisionRepository)
    {
        _decisionRepository = decisionRepository;
    }

    public async Task<ServiceResult<RiskDecisionDto>> GetDecisionByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var decision = await _decisionRepository.GetByIdAsync(id, cancellationToken);
        return decision is null
            ? ServiceResult<RiskDecisionDto>.Fail("Risk decision was not found.")
            : ServiceResult<RiskDecisionDto>.Ok(MapToDto(decision));
    }

    public async Task<ServiceResult<Shared.Contracts.PagedResult<RiskDecisionDto>>> GetDecisionsAsync(
        Shared.Contracts.PagedRequest request,
        long? symbolId,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _decisionRepository.GetPagedAsync(request, symbolId, cancellationToken);

        return ServiceResult<Shared.Contracts.PagedResult<RiskDecisionDto>>.Ok(new Shared.Contracts.PagedResult<RiskDecisionDto>
        {
            Items = items.Select(MapToDto).ToList(),
            Page = Math.Max(request.Page, 1),
            PageSize = Math.Max(request.PageSize, 1),
            TotalCount = totalCount
        });
    }

    public async Task<RiskDecision> PersistDecisionAsync(
        RiskContext context,
        RiskEvaluationResult evaluation,
        CancellationToken cancellationToken = default)
    {
        var decision = new RiskDecision
        {
            TradingSessionId = context.TradingSessionId,
            SignalId = context.SignalId,
            AiDecisionId = context.AiDecisionId,
            SymbolId = context.SymbolId,
            Decision = evaluation.Decision,
            Reason = evaluation.Reason,
            ApprovedRiskPercent = evaluation.ApprovedRiskPercent,
            PositionSize = evaluation.PositionSize,
            StopLoss = evaluation.StopLoss,
            TakeProfit = evaluation.TakeProfit,
            RejectedRuleKey = evaluation.RejectedRuleKey,
            CreatedAtUtc = context.EvaluationTimeUtc
        };

        await _decisionRepository.AddAsync(decision, cancellationToken);
        await _decisionRepository.SaveChangesAsync(cancellationToken);
        return decision;
    }

    internal static RiskDecisionDto MapToDto(RiskDecision decision) => new()
    {
        Id = decision.Id,
        TradingSessionId = decision.TradingSessionId,
        SignalId = decision.SignalId,
        AiDecisionId = decision.AiDecisionId,
        SymbolId = decision.SymbolId,
        Decision = decision.Decision.ToString(),
        Reason = decision.Reason,
        ApprovedRiskPercent = decision.ApprovedRiskPercent,
        PositionSize = decision.PositionSize,
        StopLoss = decision.StopLoss,
        TakeProfit = decision.TakeProfit,
        RejectedRuleKey = decision.RejectedRuleKey,
        CreatedAtUtc = decision.CreatedAtUtc
    };
}

public sealed class RiskEvaluationService : IRiskEvaluationService
{
    private readonly IRiskProfileRepository _profileRepository;
    private readonly IRiskRuleRepository _ruleRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IRiskEngine _riskEngine;
    private readonly IRiskDecisionService _decisionService;

    public RiskEvaluationService(
        IRiskProfileRepository profileRepository,
        IRiskRuleRepository ruleRepository,
        ISymbolRepository symbolRepository,
        IRiskEngine riskEngine,
        IRiskDecisionService decisionService)
    {
        _profileRepository = profileRepository;
        _ruleRepository = ruleRepository;
        _symbolRepository = symbolRepository;
        _riskEngine = riskEngine;
        _decisionService = decisionService;
    }

    public async Task<ServiceResult<RiskEvaluationResponse>> EvaluateAsync(
        RiskEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        var profile = await _profileRepository.GetByIdAsync(request.RiskProfileId, cancellationToken);
        if (profile is null)
        {
            return ServiceResult<RiskEvaluationResponse>.Fail("Risk profile was not found.", "riskProfileId");
        }

        var symbol = await _symbolRepository.GetByIdAsync(request.SymbolId, cancellationToken);
        if (symbol is null)
        {
            return ServiceResult<RiskEvaluationResponse>.Fail("Symbol was not found.", "symbolId");
        }

        if (!TryParseDirection(request.Direction, out var direction))
        {
            return ServiceResult<RiskEvaluationResponse>.Fail("Direction is invalid.", "direction");
        }

        MarketRegime? marketRegime = null;
        if (!string.IsNullOrWhiteSpace(request.MarketRegime))
        {
            if (!Enum.TryParse(request.MarketRegime, ignoreCase: true, out MarketRegime parsedRegime))
            {
                return ServiceResult<RiskEvaluationResponse>.Fail("Market regime is invalid.", "marketRegime");
            }

            marketRegime = parsedRegime;
        }

        var rules = await _ruleRepository.GetByProfileIdAsync(profile.Id, cancellationToken);
        var context = new RiskContext
        {
            TradingSessionId = request.TradingSessionId,
            SymbolId = symbol.Id,
            Symbol = symbol.SymbolName,
            Direction = direction,
            EntryPrice = request.EntryPrice,
            SuggestedStopLoss = request.SuggestedStopLoss,
            SuggestedTakeProfit = request.SuggestedTakeProfit,
            ConfidenceScore = request.ConfidenceScore,
            StrategyCode = request.StrategyCode,
            SignalId = request.SignalId,
            AiDecisionId = request.AiDecisionId,
            AccountBalance = request.AccountBalance,
            DailyPnl = request.DailyPnl,
            WeeklyPnl = request.WeeklyPnl,
            OpenPositionCount = request.OpenPositionCount,
            OpenSymbolExposure = request.OpenSymbolExposure,
            TotalExposure = request.TotalExposure,
            ConsecutiveLosses = request.ConsecutiveLosses,
            SpreadPercent = request.SpreadPercent,
            AtrPercent = request.AtrPercent,
            MarketRegime = marketRegime,
            EmergencyStopEnabled = request.EmergencyStopEnabled,
            Rules = rules,
            EvaluationTimeUtc = DateTime.UtcNow
        };

        var evaluation = _riskEngine.Evaluate(context);

        long? decisionId = null;
        if (request.PersistDecision)
        {
            var persisted = await _decisionService.PersistDecisionAsync(context, evaluation, cancellationToken);
            decisionId = persisted.Id;
        }

        return ServiceResult<RiskEvaluationResponse>.Ok(new RiskEvaluationResponse
        {
            Approved = evaluation.Approved,
            Decision = evaluation.Decision.ToString(),
            Reason = evaluation.Reason,
            RejectedRuleKey = evaluation.RejectedRuleKey,
            ApprovedRiskPercent = evaluation.ApprovedRiskPercent,
            PositionSize = evaluation.PositionSize,
            StopLoss = evaluation.StopLoss,
            TakeProfit = evaluation.TakeProfit,
            RiskAmount = evaluation.RiskAmount,
            RiskDecisionId = decisionId
        });
    }

    private static bool TryParseDirection(string value, out TradeDirection direction) =>
        Enum.TryParse(value, ignoreCase: true, out direction) &&
        direction is TradeDirection.Long or TradeDirection.Short;
}
