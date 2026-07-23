using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using StrategyEntity = MomoQuant.Domain.Strategies.Strategy;

namespace MomoQuant.Application.Strategies;

public interface IStrategyDataRequirementService
{
    Task<ServiceResult<IReadOnlyList<StrategyDataRequirementDto>>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<StrategyDataRequirementDto>> GetByStrategyIdAsync(long strategyId, CancellationToken cancellationToken = default);
    Task<ServiceResult<ResolveStrategyRequirementsResponse>> ResolveAsync(
        ResolveStrategyRequirementsRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class StrategyDataRequirementService : IStrategyDataRequirementService
{
    private readonly IStrategyRepository _strategyRepository;
    private readonly ISymbolRepository _symbolRepository;

    public StrategyDataRequirementService(
        IStrategyRepository strategyRepository,
        ISymbolRepository symbolRepository)
    {
        _strategyRepository = strategyRepository;
        _symbolRepository = symbolRepository;
    }

    public async Task<ServiceResult<IReadOnlyList<StrategyDataRequirementDto>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var strategies = await _strategyRepository.GetAllAsync(cancellationToken);
        var requirements = strategies.Select(BuildRequirement).ToList();
        return ServiceResult<IReadOnlyList<StrategyDataRequirementDto>>.Ok(requirements);
    }

    public async Task<ServiceResult<StrategyDataRequirementDto>> GetByStrategyIdAsync(long strategyId, CancellationToken cancellationToken = default)
    {
        var strategy = await _strategyRepository.GetByIdAsync(strategyId, cancellationToken);
        if (strategy is null)
        {
            return ServiceResult<StrategyDataRequirementDto>.Fail("Strategy was not found.");
        }

        return ServiceResult<StrategyDataRequirementDto>.Ok(BuildRequirement(strategy));
    }

    public async Task<ServiceResult<ResolveStrategyRequirementsResponse>> ResolveAsync(
        ResolveStrategyRequirementsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.StrategyIds.Count == 0)
        {
            return ServiceResult<ResolveStrategyRequirementsResponse>.Fail("At least one strategy is required.", "strategyIds");
        }

        var distinctStrategyIds = request.StrategyIds.Distinct().ToList();
        var allStrategies = await _strategyRepository.GetAllAsync(cancellationToken);
        var strategyMap = allStrategies.ToDictionary(strategy => strategy.Id);
        var selectedStrategies = new List<StrategyEntity>();
        foreach (var strategyId in distinctStrategyIds)
        {
            if (!strategyMap.TryGetValue(strategyId, out var strategy))
            {
                return ServiceResult<ResolveStrategyRequirementsResponse>.Fail(
                    $"Strategy {strategyId} was not found.",
                    "strategyIds");
            }

            selectedStrategies.Add(strategy);
        }

        var mode = NormalizeMode(request.Mode);
        var warnings = new List<string>();
        var blockingIssues = new List<string>();
        var executionPlan = new List<StrategyExecutionPlanItemDto>();

        foreach (var strategy in selectedStrategies)
        {
            var requirement = BuildRequirement(strategy);
            if (!SupportsMode(requirement, mode))
            {
                blockingIssues.Add($"{requirement.StrategyCode} does not support {mode}.");
                continue;
            }

            var executionTimeframes = ResolveExecutionTimeframes(requirement, request, warnings, blockingIssues);
            if (executionTimeframes.Count == 0)
            {
                continue;
            }

            var requiredDataTimeframes = ResolveRequiredDataTimeframes(requirement, executionTimeframes);
            var requiredIndicatorTimeframes = ResolveRequiredIndicatorTimeframes(requirement, executionTimeframes);

            executionPlan.Add(new StrategyExecutionPlanItemDto
            {
                StrategyId = requirement.StrategyId,
                StrategyCode = requirement.StrategyCode,
                StrategyName = requirement.StrategyName,
                PreferredExecutionTimeframe = requirement.PreferredExecutionTimeframe,
                ExecutionTimeframes = executionTimeframes,
                RequiredDataTimeframes = requiredDataTimeframes,
                RequiredIndicatorTimeframes = requiredIndicatorTimeframes,
                AnchorTimeframes = requirement.AnchorTimeframes
            });
        }

        var requiredTimeframes = executionPlan
            .SelectMany(item => item.RequiredDataTimeframes.Concat(item.ExecutionTimeframes))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var symbols = new List<(long SymbolId, string SymbolName)>();
        foreach (var symbolId in request.SymbolIds.Distinct())
        {
            var symbol = await _symbolRepository.GetByIdAsync(symbolId, cancellationToken);
            if (symbol is null)
            {
                warnings.Add($"Symbol {symbolId} was not found while building import plan.");
                continue;
            }

            symbols.Add((symbol.Id, symbol.SymbolName));
        }

        var importPlan = BuildImportPlan(symbols, executionPlan);
        return ServiceResult<ResolveStrategyRequirementsResponse>.Ok(new ResolveStrategyRequirementsResponse
        {
            RequiredTimeframes = requiredTimeframes,
            ExecutionPlan = executionPlan,
            ImportPlan = importPlan,
            Warnings = warnings.Distinct().ToList(),
            BlockingIssues = blockingIssues.Distinct().ToList()
        });
    }

    private static IReadOnlyList<StrategyImportPlanItemDto> BuildImportPlan(
        IReadOnlyList<(long SymbolId, string SymbolName)> symbols,
        IReadOnlyList<StrategyExecutionPlanItemDto> executionPlan)
    {
        var plan = new List<StrategyImportPlanItemDto>();
        if (symbols.Count == 0)
        {
            foreach (var item in executionPlan)
            {
                foreach (var timeframe in item.ExecutionTimeframes.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    plan.Add(new StrategyImportPlanItemDto
                    {
                        SymbolId = null,
                        Symbol = null,
                        Timeframe = timeframe,
                        Reason = $"Required by {item.StrategyCode} execution"
                    });
                }

                foreach (var timeframe in item.AnchorTimeframes.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    plan.Add(new StrategyImportPlanItemDto
                    {
                        SymbolId = null,
                        Symbol = null,
                        Timeframe = timeframe,
                        Reason = $"Required by {item.StrategyCode} anchor range"
                    });
                }
            }

            return plan
                .GroupBy(item => new { item.Symbol, item.Timeframe, item.Reason })
                .Select(group => group.First())
                .ToList();
        }

        foreach (var symbol in symbols)
        {
            foreach (var item in executionPlan)
            {
                foreach (var timeframe in item.ExecutionTimeframes.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    plan.Add(new StrategyImportPlanItemDto
                    {
                        SymbolId = symbol.SymbolId,
                        Symbol = symbol.SymbolName,
                        Timeframe = timeframe,
                        Reason = $"Required by {item.StrategyCode} execution"
                    });
                }

                foreach (var timeframe in item.AnchorTimeframes.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    plan.Add(new StrategyImportPlanItemDto
                    {
                        SymbolId = symbol.SymbolId,
                        Symbol = symbol.SymbolName,
                        Timeframe = timeframe,
                        Reason = $"Required by {item.StrategyCode} anchor range"
                    });
                }
            }
        }

        return plan
            .GroupBy(item => new { item.SymbolId, item.Symbol, item.Timeframe, item.Reason })
            .Select(group => group.First())
            .ToList();
    }

    private static IReadOnlyList<string> ResolveExecutionTimeframes(
        StrategyDataRequirementDto requirement,
        ResolveStrategyRequirementsRequest request,
        List<string> warnings,
        List<string> blockingIssues)
    {
        var scope = NormalizeExecutionScope(request.ExecutionScope);
        if (scope == "ManualOverride")
        {
            var manual = (request.ManualExecutionTimeframes ?? [])
                .Select(value => value.Trim().ToLowerInvariant())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct()
                .ToList();
            if (manual.Count == 0)
            {
                blockingIssues.Add($"Manual override requires at least one execution timeframe for {requirement.StrategyCode}.");
                return [];
            }

            var unsupported = manual
                .Where(value => !requirement.AllowedExecutionTimeframes.Contains(value, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (unsupported.Count > 0)
            {
                if (string.Equals(requirement.StrategyCode, "FOUR_HOUR_RANGE_REENTRY", StringComparison.OrdinalIgnoreCase))
                {
                    blockingIssues.Add("FOUR_HOUR_RANGE_REENTRY only supports 5m execution. It requires 4h anchor data.");
                    return [];
                }

                blockingIssues.Add(
                    $"{requirement.StrategyCode} only supports {string.Join(", ", requirement.AllowedExecutionTimeframes)} execution. Unsupported overrides: {string.Join(", ", unsupported)}.");
                return [];
            }

            return manual;
        }

        if (scope == "AllSupported")
        {
            return requirement.AllowedExecutionTimeframes;
        }

        if (requirement.AllowedExecutionTimeframes.Count == 0)
        {
            warnings.Add($"{requirement.StrategyCode} has no configured execution timeframes. Falling back to preferred timeframe.");
            return [requirement.PreferredExecutionTimeframe];
        }

        if (requirement.AllowedExecutionTimeframes.Contains(requirement.PreferredExecutionTimeframe, StringComparer.OrdinalIgnoreCase))
        {
            return [requirement.PreferredExecutionTimeframe];
        }

        warnings.Add(
            $"{requirement.StrategyCode} preferred timeframe {requirement.PreferredExecutionTimeframe} is not allowed. Falling back to {requirement.AllowedExecutionTimeframes[0]}.");
        return [requirement.AllowedExecutionTimeframes[0]];
    }

    private static IReadOnlyList<string> ResolveRequiredDataTimeframes(
        StrategyDataRequirementDto requirement,
        IReadOnlyList<string> executionTimeframes)
    {
        if (string.Equals(requirement.StrategyCode, "FOUR_HOUR_RANGE_REENTRY", StringComparison.OrdinalIgnoreCase))
        {
            return requirement.RequiredDataTimeframes;
        }

        if (string.Equals(requirement.StrategyCode, StrategyCodes.BbLiquiditySweepCisd, StringComparison.OrdinalIgnoreCase)
            || string.Equals(requirement.StrategyCode, StrategyCodes.BbLiquiditySweepCisdRsiPrimed, StringComparison.OrdinalIgnoreCase))
        {
            return requirement.RequiredDataTimeframes;
        }

        return executionTimeframes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> ResolveRequiredIndicatorTimeframes(
        StrategyDataRequirementDto requirement,
        IReadOnlyList<string> executionTimeframes)
    {
        if (requirement.RequiredIndicatorTimeframes.Count > 0)
        {
            return requirement.RequiredIndicatorTimeframes;
        }

        if (!requirement.RequiresIndicators)
        {
            return [];
        }

        return executionTimeframes.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool SupportsMode(StrategyDataRequirementDto requirement, string mode) =>
        mode switch
        {
            "Backtest" => requirement.SupportsBacktest,
            "Replay" => requirement.SupportsReplay,
            "HistoricalPaper" => requirement.SupportsHistoricalPaper,
            "LivePaper" => requirement.SupportsLivePaper,
            "StrategyDiagnostic" => true,
            _ => true
        };

    private static string NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "Benchmark";
        }

        return mode.Trim() switch
        {
            "Benchmark" => "Benchmark",
            "Backtest" => "Backtest",
            "Replay" => "Replay",
            "HistoricalPaper" => "HistoricalPaper",
            "LivePaper" => "LivePaper",
            "StrategyDiagnostic" => "StrategyDiagnostic",
            _ => "Benchmark"
        };
    }

    private static string NormalizeExecutionScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return "PreferredOnly";
        }

        return scope.Trim() switch
        {
            "PreferredOnly" => "PreferredOnly",
            "AllSupported" => "AllSupported",
            "ManualOverride" => "ManualOverride",
            _ => "PreferredOnly"
        };
    }

    private static StrategyDataRequirementDto BuildRequirement(StrategyEntity strategy)
    {
        var code = strategy.Code.ToCode();
        var profile = strategy.Code switch
        {
            StrategyCode.EmaPullback => new RequirementProfile(
                preferredExecutionTimeframe: "5m",
                allowedExecutionTimeframes: ["3m", "5m", "15m"],
                requiredDataTimeframes: ["5m"],
                optionalDataTimeframes: ["15m"],
                requiredIndicators: ["EMA20", "EMA50", "EMA200", "RSI", "ATR", "VolumeSMA"],
                requiredIndicatorTimeframes: ["5m"],
                notes: null),
            StrategyCode.VwapMeanReversion => new RequirementProfile(
                preferredExecutionTimeframe: "5m",
                allowedExecutionTimeframes: ["3m", "5m"],
                requiredDataTimeframes: ["5m"],
                optionalDataTimeframes: [],
                requiredIndicators: ["VWAP", "RSI", "ATR", "VolumeSMA"],
                requiredIndicatorTimeframes: ["5m"],
                notes: null),
            StrategyCode.LiquiditySweep => new RequirementProfile(
                preferredExecutionTimeframe: "5m",
                allowedExecutionTimeframes: ["3m", "5m"],
                requiredDataTimeframes: ["5m"],
                optionalDataTimeframes: [],
                requiredIndicators: ["ATR", "VolumeSMA", "SwingHigh", "SwingLow"],
                requiredIndicatorTimeframes: ["5m"],
                notes: null),
            StrategyCode.BollingerSqueezeBreakout => new RequirementProfile(
                preferredExecutionTimeframe: "15m",
                allowedExecutionTimeframes: ["5m", "15m"],
                requiredDataTimeframes: ["15m"],
                optionalDataTimeframes: [],
                requiredIndicators: ["BollingerBands", "ATR", "VolumeSMA"],
                requiredIndicatorTimeframes: ["15m"],
                notes: null),
            StrategyCode.DonchianBreakout => new RequirementProfile(
                preferredExecutionTimeframe: "15m",
                allowedExecutionTimeframes: ["5m", "15m"],
                requiredDataTimeframes: ["15m"],
                optionalDataTimeframes: [],
                requiredIndicators: ["DonchianHigh", "DonchianLow", "ATR"],
                requiredIndicatorTimeframes: ["15m"],
                notes: null),
            StrategyCode.RsiDivergenceReversal => new RequirementProfile(
                preferredExecutionTimeframe: "15m",
                allowedExecutionTimeframes: ["5m", "15m"],
                requiredDataTimeframes: ["15m"],
                optionalDataTimeframes: [],
                requiredIndicators: ["RSI", "SwingHigh", "SwingLow"],
                requiredIndicatorTimeframes: ["15m"],
                notes: null),
            StrategyCode.MacdMomentumContinuation => new RequirementProfile(
                preferredExecutionTimeframe: "15m",
                allowedExecutionTimeframes: ["5m", "15m"],
                requiredDataTimeframes: ["15m"],
                optionalDataTimeframes: [],
                requiredIndicators: ["MACD", "EMA", "ATR"],
                requiredIndicatorTimeframes: ["15m"],
                notes: null),
            StrategyCode.AtrVolatilityBreakout => new RequirementProfile(
                preferredExecutionTimeframe: "15m",
                allowedExecutionTimeframes: ["5m", "15m"],
                requiredDataTimeframes: ["15m"],
                optionalDataTimeframes: [],
                requiredIndicators: ["ATR", "VolumeSMA"],
                requiredIndicatorTimeframes: ["15m"],
                notes: null),
            StrategyCode.SupportResistanceBreakoutRetest => new RequirementProfile(
                preferredExecutionTimeframe: "15m",
                allowedExecutionTimeframes: ["5m", "15m"],
                requiredDataTimeframes: ["15m"],
                optionalDataTimeframes: [],
                requiredIndicators: ["SupportResistanceZones", "ATR", "VolumeSMA"],
                requiredIndicatorTimeframes: ["15m"],
                notes: null),
            StrategyCode.SupertrendContinuation => new RequirementProfile(
                preferredExecutionTimeframe: "15m",
                allowedExecutionTimeframes: ["5m", "15m"],
                requiredDataTimeframes: ["15m"],
                optionalDataTimeframes: [],
                requiredIndicators: ["Supertrend", "ATR"],
                requiredIndicatorTimeframes: ["15m"],
                notes: null),
            StrategyCode.FourHourRangeReEntry => new RequirementProfile(
                preferredExecutionTimeframe: "5m",
                allowedExecutionTimeframes: ["5m"],
                requiredDataTimeframes: ["5m", "4h"],
                optionalDataTimeframes: [],
                anchorTimeframes: ["4h"],
                requiredIndicators: [],
                requiredIndicatorTimeframes: [],
                notes: "Uses first 4-hour New York trading-day range as anchor and 5m candles for breakout/re-entry execution."),
            StrategyCode.BbLiquiditySweepCisd => new RequirementProfile(
                preferredExecutionTimeframe: "3m",
                allowedExecutionTimeframes: ["3m"],
                requiredDataTimeframes: ["1m", "3m", "5m"],
                optionalDataTimeframes: [],
                anchorTimeframes: ["1m", "5m"],
                requiredIndicators: ["BollingerBands", "LiquidityLines", "CISD", "Sessions", "ATR"],
                requiredIndicatorTimeframes: ["1m", "3m", "5m"],
                warmupCandles: 500,
                supportsOptimization: false,
                supportsValidation: true,
                notes: "3m execution with 1m LTF and 5m reference liquidity lines. #itsimpossible-inspired MOMO approximation."),
            StrategyCode.BbLiquiditySweepCisdRsiPrimed => new RequirementProfile(
                preferredExecutionTimeframe: "3m",
                allowedExecutionTimeframes: ["3m"],
                requiredDataTimeframes: ["1m", "3m", "5m"],
                optionalDataTimeframes: [],
                anchorTimeframes: ["1m", "5m"],
                requiredIndicators: ["BollingerBands", "LiquidityLines", "CISD", "Sessions", "ATR", "RsiPrimedChartPrime"],
                requiredIndicatorTimeframes: ["1m", "3m", "5m"],
                warmupCandles: 500,
                supportsOptimization: false,
                supportsValidation: true,
                notes: "BB Liquidity Sweep CISD with RSI Primed [ChartPrime] MOMO port filter."),
            StrategyCode.VolatilityGatedSupertrendMomentum => new RequirementProfile(
                preferredExecutionTimeframe: "15m",
                allowedExecutionTimeframes: ["3m", "5m", "15m", "30m", "1h", "4h"],
                preferredTimeframes: ["5m", "15m", "1h"],
                requiredDataTimeframes: ["15m"],
                optionalDataTimeframes: [],
                requiredIndicators: ["SuperTrend", "ATR", "MACD", "VolatilityGate"],
                requiredIndicatorTimeframes: ["15m"],
                warmupCandles: 500,
                supportsOptimization: true,
                supportsValidation: true,
                notes: "Volatility-gated SuperTrend momentum with fast/slow ATR regime filter and MACD histogram confirmation."),
            StrategyCode.PriceStructureBreakoutRetest => new RequirementProfile(
                preferredExecutionTimeframe: "15m",
                allowedExecutionTimeframes: ["5m", "15m", "30m", "1h", "4h"],
                preferredTimeframes: ["15m"],
                requiredDataTimeframes: [],
                optionalDataTimeframes: [],
                requiredIndicators: [],
                requiredIndicatorTimeframes: [],
                warmupCandles: 100,
                supportsOptimization: false,
                supportsValidation: false,
                notes: "Price structure breakout and retest — OHLC candles only. Use Strategy Laboratory before validation."),
            StrategyCode.PriceStructureLiquiditySweepReclaim => new RequirementProfile(
                preferredExecutionTimeframe: "15m",
                allowedExecutionTimeframes: ["5m", "15m", "30m", "1h", "4h"],
                preferredTimeframes: ["15m"],
                requiredDataTimeframes: [],
                optionalDataTimeframes: [],
                requiredIndicators: [],
                requiredIndicatorTimeframes: [],
                warmupCandles: 100,
                supportsOptimization: false,
                supportsValidation: false,
                notes: "Price structure liquidity sweep and reclaim — OHLC candles only. Use Strategy Laboratory before validation."),
            _ => new RequirementProfile(
                preferredExecutionTimeframe: "5m",
                allowedExecutionTimeframes: ["5m"],
                requiredDataTimeframes: ["5m"],
                optionalDataTimeframes: [],
                requiredIndicators: [],
                requiredIndicatorTimeframes: [],
                notes: null)
        };

        return new StrategyDataRequirementDto
        {
            StrategyId = strategy.Id,
            StrategyCode = code,
            StrategyName = strategy.Name,
            PreferredExecutionTimeframe = profile.PreferredExecutionTimeframe,
            AllowedExecutionTimeframes = profile.AllowedExecutionTimeframes,
            RequiredDataTimeframes = profile.RequiredDataTimeframes,
            OptionalDataTimeframes = profile.OptionalDataTimeframes,
            AnchorTimeframes = profile.AnchorTimeframes,
            HigherTimeframeFilters = profile.HigherTimeframeFilters,
            WarmupCandles = profile.WarmupCandles,
            MinBenchmarkDays = profile.MinBenchmarkDays,
            RecommendedBenchmarkDays = profile.RecommendedBenchmarkDays,
            RequiresIndicators = profile.RequiredIndicators.Count > 0,
            RequiredIndicators = profile.RequiredIndicators,
            RequiredIndicatorTimeframes = profile.RequiredIndicatorTimeframes,
            SupportsBacktest = true,
            SupportsReplay = true,
            SupportsHistoricalPaper = true,
            SupportsLivePaper = true,
            SupportsBenchmark = true,
            SupportsValidation = profile.SupportsValidation,
            SupportsOptimization = profile.SupportsOptimization,
            SupportsStrategyLab = strategy.Code is StrategyCode.PriceStructureBreakoutRetest
                or StrategyCode.PriceStructureLiquiditySweepReclaim,
            PreferredTimeframes = profile.PreferredTimeframes,
            Notes = profile.Notes,
            Warnings = profile.Warnings
        };
    }

    private sealed class RequirementProfile
    {
        public RequirementProfile(
            string preferredExecutionTimeframe,
            IReadOnlyList<string> allowedExecutionTimeframes,
            IReadOnlyList<string> requiredDataTimeframes,
            IReadOnlyList<string> optionalDataTimeframes,
            IReadOnlyList<string> requiredIndicators,
            IReadOnlyList<string> requiredIndicatorTimeframes,
            string? notes,
            IReadOnlyList<string>? anchorTimeframes = null,
            IReadOnlyList<string>? higherTimeframeFilters = null,
            IReadOnlyList<string>? preferredTimeframes = null,
            int warmupCandles = 600,
            int minBenchmarkDays = 3,
            int recommendedBenchmarkDays = 10,
            bool supportsValidation = true,
            bool supportsOptimization = false,
            IReadOnlyList<string>? warnings = null)
        {
            PreferredExecutionTimeframe = preferredExecutionTimeframe;
            AllowedExecutionTimeframes = allowedExecutionTimeframes;
            PreferredTimeframes = preferredTimeframes ?? [preferredExecutionTimeframe];
            RequiredDataTimeframes = requiredDataTimeframes;
            OptionalDataTimeframes = optionalDataTimeframes;
            AnchorTimeframes = anchorTimeframes ?? [];
            HigherTimeframeFilters = higherTimeframeFilters ?? [];
            WarmupCandles = warmupCandles;
            MinBenchmarkDays = minBenchmarkDays;
            RecommendedBenchmarkDays = recommendedBenchmarkDays;
            RequiredIndicators = requiredIndicators;
            RequiredIndicatorTimeframes = requiredIndicatorTimeframes;
            SupportsValidation = supportsValidation;
            SupportsOptimization = supportsOptimization;
            Notes = notes;
            Warnings = warnings ?? [];
        }

        public string PreferredExecutionTimeframe { get; }
        public IReadOnlyList<string> AllowedExecutionTimeframes { get; }
        public IReadOnlyList<string> PreferredTimeframes { get; }
        public IReadOnlyList<string> RequiredDataTimeframes { get; }
        public IReadOnlyList<string> OptionalDataTimeframes { get; }
        public IReadOnlyList<string> AnchorTimeframes { get; }
        public IReadOnlyList<string> HigherTimeframeFilters { get; }
        public int WarmupCandles { get; }
        public int MinBenchmarkDays { get; }
        public int RecommendedBenchmarkDays { get; }
        public IReadOnlyList<string> RequiredIndicators { get; }
        public IReadOnlyList<string> RequiredIndicatorTimeframes { get; }
        public bool SupportsValidation { get; }
        public bool SupportsOptimization { get; }
        public string? Notes { get; }
        public IReadOnlyList<string> Warnings { get; }
    }
}
