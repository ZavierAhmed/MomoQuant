using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Ai.Dtos;
using MomoQuant.Application.Common;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Dtos;

namespace MomoQuant.Application.Ai;

public interface IAiSetupAdvisorService
{
    Task<ServiceResult<AiSetupAdvisorResponseDto>> GetSetupAdviceAsync(
        AiSetupAdvisorRequestDto request,
        CancellationToken cancellationToken = default);
}

public sealed class AiSetupAdvisorService : IAiSetupAdvisorService
{
    private readonly IAiIntegrationService _aiIntegrationService;
    private readonly IStrategyDataRequirementService _requirementService;
    private readonly IStrategyRepository _strategyRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IRiskProfileRepository _riskProfileRepository;

    public AiSetupAdvisorService(
        IAiIntegrationService aiIntegrationService,
        IStrategyDataRequirementService requirementService,
        IStrategyRepository strategyRepository,
        ISymbolRepository symbolRepository,
        IRiskProfileRepository riskProfileRepository)
    {
        _aiIntegrationService = aiIntegrationService;
        _requirementService = requirementService;
        _strategyRepository = strategyRepository;
        _symbolRepository = symbolRepository;
        _riskProfileRepository = riskProfileRepository;
    }

    public async Task<ServiceResult<AiSetupAdvisorResponseDto>> GetSetupAdviceAsync(
        AiSetupAdvisorRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.SymbolIds.Count == 0)
        {
            return ServiceResult<AiSetupAdvisorResponseDto>.Fail("At least one symbol is required.", "symbolIds");
        }

        if (request.StrategyIds.Count == 0)
        {
            return ServiceResult<AiSetupAdvisorResponseDto>.Fail("At least one strategy is required.", "strategyIds");
        }

        var health = await _aiIntegrationService.GetHealthAsync(cancellationToken);
        var aiAvailable = health.Succeeded && health.Data is not null;

        var resolve = await _requirementService.ResolveAsync(new ResolveStrategyRequirementsRequest
        {
            StrategyIds = request.StrategyIds.Distinct().ToList(),
            SymbolIds = request.SymbolIds.Distinct().ToList(),
            BenchmarkFromDate = request.FromDate,
            BenchmarkToDate = request.ToDate,
            Mode = request.Mode,
            ExecutionScope = "PreferredOnly"
        }, cancellationToken);
        if (!resolve.Succeeded || resolve.Data is null)
        {
            return ServiceResult<AiSetupAdvisorResponseDto>.Fail(resolve.ErrorMessage ?? "Failed to resolve strategy requirements.");
        }

        var symbols = new List<(long Id, string Name)>();
        foreach (var symbolId in request.SymbolIds.Distinct())
        {
            var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
            if (symbol is not null)
            {
                symbols.Add((symbol.Id, symbol.SymbolName));
            }
        }

        var selectedStrategies = (await _strategyRepository.GetAllAsync(cancellationToken))
            .Where(strategy => request.StrategyIds.Contains(strategy.Id))
            .ToList();

        var dataWarnings = new List<string>(resolve.Data.Warnings);
        var riskWarnings = new List<string>();
        var blockingIssues = new List<string>(resolve.Data.BlockingIssues);
        var suggestions = new List<string>();

        if (request.RiskProfileId.HasValue)
        {
            var riskProfile = await _riskProfileRepository.GetByIdAsync(request.RiskProfileId.Value, cancellationToken);
            if (riskProfile is null)
            {
                blockingIssues.Add("Selected risk profile was not found.");
            }
        }
        else
        {
            riskWarnings.Add("Risk profile is missing; run settings may be rejected by risk checks.");
        }

        var estimatedRuns = symbols.Count * resolve.Data.ExecutionPlan.Sum(item => item.ExecutionTimeframes.Count);
        if (estimatedRuns <= 5)
        {
            suggestions.Add("Expected runtime is low. Preferred-only execution scope is suitable for baseline comparison.");
        }
        else if (estimatedRuns <= 20)
        {
            suggestions.Add("Expected runtime is medium. Consider keeping import and indicator recalculation enabled.");
        }
        else
        {
            suggestions.Add("Expected runtime is high. Consider reducing symbols or using preferred-only execution scope.");
        }

        if (string.Equals(request.ExecutionMode, "MarketFill", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.Add("Use MarketFill for baseline strategy comparison.");
        }

        if (!request.UseAiScoring)
        {
            suggestions.Add("Keep AI scoring off for the first benchmark baseline.");
        }

        foreach (var item in resolve.Data.ExecutionPlan.Where(item => item.AnchorTimeframes.Count > 0))
        {
            suggestions.Add($"{item.StrategyCode} requires {string.Join(", ", item.AnchorTimeframes)} anchor data and {string.Join(", ", item.ExecutionTimeframes)} execution data.");
        }

        if (!aiAvailable)
        {
            dataWarnings.Add("Python AI service is unavailable. Returned rule-based setup guidance.");
        }
        else
        {
            suggestions.Add("Python AI service is available; advisory suggestions were generated with AI health checks enabled.");
        }

        var importPlan = resolve.Data.ImportPlan
            .Select(item => new AiSetupAdvisorPlanItemDto
            {
                SymbolId = item.SymbolId,
                Symbol = item.Symbol,
                Timeframe = item.Timeframe,
                Reason = item.Reason
            })
            .ToList();

        var indicatorPlan = resolve.Data.RequiredTimeframes
            .SelectMany(timeframe => symbols.Select(symbol => new AiSetupAdvisorPlanItemDto
            {
                SymbolId = symbol.Id,
                Symbol = symbol.Name,
                Timeframe = timeframe,
                Reason = "Recalculate indicators for selected strategy setup"
            }))
            .ToList();

        var summary = $"Recommended {request.Mode} setup for {symbols.Count} symbol(s) and {selectedStrategies.Count} strategy(ies).";
        return ServiceResult<AiSetupAdvisorResponseDto>.Ok(new AiSetupAdvisorResponseDto
        {
            Summary = summary,
            RecommendedExecutionScope = "PreferredOnly",
            RecommendedStrategies = selectedStrategies.Where(strategy => strategy.IsEnabled).Select(strategy => strategy.Id).ToList(),
            RequiredTimeframes = resolve.Data.RequiredTimeframes,
            ImportPlan = importPlan,
            IndicatorPlan = indicatorPlan,
            RiskWarnings = riskWarnings,
            DataWarnings = dataWarnings,
            ExpectedRuntime = estimatedRuns <= 5 ? "Low" : estimatedRuns <= 20 ? "Medium" : "High",
            EstimatedRunCount = estimatedRuns,
            Suggestions = suggestions.Distinct().ToList(),
            BlockingIssues = blockingIssues.Distinct().ToList()
        });
    }
}
