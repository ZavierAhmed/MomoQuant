using MomoQuant.Application.MarketData;
using MomoQuant.Application.Strategies.Dtos;
using MomoQuant.Application.Strategies.Optimization;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies;

public static class StrategyCatalogContentProvider
{
    public static StrategyCatalogDetailDto BuildDetail(
        Strategy strategy,
        StrategyDataRequirementDto? requirement,
        IReadOnlyList<StrategyParameterDefinitionDto> parameterDefinitions,
        ITradingStrategy? plugin)
    {
        var code = strategy.Code.ToCode();
        var content = ResolveContent(strategy.Code, strategy, requirement);
        var catalog = StrategyCatalogMapper.MapToCatalogDto(
            strategy,
            requirement,
            parameterDefinitions.Count > 0);

        return new StrategyCatalogDetailDto
        {
            Id = catalog.Id,
            Code = catalog.Code,
            Name = catalog.Name,
            Description = catalog.Description,
            IsEnabled = catalog.IsEnabled,
            Version = catalog.Version,
            Category = catalog.Category,
            IsBuiltIn = catalog.IsBuiltIn,
            SupportedModes = catalog.SupportedModes,
            PreferredTimeframe = catalog.PreferredTimeframe,
            PreferredTimeframes = catalog.PreferredTimeframes,
            AllowedTimeframes = catalog.AllowedTimeframes,
            RequiredTimeframes = catalog.RequiredTimeframes,
            RequiredDataTimeframes = catalog.RequiredDataTimeframes,
            RequiredIndicators = catalog.RequiredIndicators,
            ParameterDefinitionsAvailable = catalog.ParameterDefinitionsAvailable,
            SupportsOptimization = catalog.SupportsOptimization,
            SupportsValidation = catalog.SupportsValidation,
            SupportsLivePaper = catalog.SupportsLivePaper,
            SupportsBacktest = catalog.SupportsBacktest,
            SupportsBenchmark = catalog.SupportsBenchmark,
            Status = strategy.IsEnabled ? "Enabled" : "Disabled",
            AnchorTimeframes = requirement?.AnchorTimeframes ?? [],
            WarmupCandles = requirement?.WarmupCandles ?? 600,
            SupportsHistoricalPaper = requirement?.SupportsHistoricalPaper ?? true,
            SupportsReplay = requirement?.SupportsReplay ?? true,
            ParameterDefinitions = parameterDefinitions,
            HowItWorks = content.HowItWorks,
            EntryLogic = content.EntryLogic,
            ExitLogic = content.ExitLogic,
            NoTradeConditions = content.NoTradeConditions,
            RiskManagement = content.RiskManagement,
            ApproximationNotes = content.ApproximationNotes,
            ImplementationNotes = content.ImplementationNotes ?? requirement?.Notes,
            RecommendedValidationMode = catalog.SupportsOptimization
                ? ValidationMode.InSampleOutOfSample70_30.ToString()
                : ValidationMode.None.ToString(),
            OptimizationGuardrails = BuildOptimizationGuardrails(catalog.SupportsOptimization),
            SupportedRegimes = plugin?.SupportedRegimes.Select(regime => regime.ToString()).ToList() ?? [],
            SupportedTimeframes = plugin?.SupportedTimeframes.Select(tf => TimeframeParser.ToApiString(tf)).ToList() ?? []
        };
    }

    private static IReadOnlyList<string> BuildOptimizationGuardrails(bool supportsOptimization) =>
        supportsOptimization
            ?
            [
                "Use 70/30 validation to avoid overfitting.",
                "Require minimum trades in both training and validation windows.",
                "Reject parameter sets with validation drawdown above guardrails.",
                "Approve only sets that pass out-of-sample validation."
            ]
            : ["This strategy does not support parameter optimization."];

    private static StrategyContent ResolveContent(
        StrategyCode code,
        Strategy strategy,
        StrategyDataRequirementDto? requirement)
    {
        return code switch
        {
            StrategyCode.VolatilityGatedSupertrendMomentum => new StrategyContent(
                HowItWorks: "Trades momentum continuation when SuperTrend direction aligns with a volatility regime filter and MACD histogram confirms impulse.",
                EntryLogic: "Enter long when SuperTrend is bullish, fast ATR volatility gate is open, and MACD histogram is positive. Enter short on mirrored bearish conditions.",
                ExitLogic: "Exit on SuperTrend flip, volatility gate closure, or risk-engine stop/target management.",
                NoTradeConditions: "No trade when volatility gate is closed, MACD histogram disagrees with trend, or required indicator warmup is incomplete.",
                RiskManagement: "Uses configured stop distance from ATR/SuperTrend context. All trades pass through the risk engine before simulated execution.",
                ApproximationNotes: "MOMO-native SuperTrend + ATR volatility gate implementation tuned for futures simulation.",
                ImplementationNotes: requirement?.Notes),
            StrategyCode.BbLiquiditySweepCisd => new StrategyContent(
                HowItWorks: "Watches BB liquidity sweeps with CISD confirmation on 3m execution using 1m/5m reference structure.",
                EntryLogic: "Enter after liquidity sweep beyond BB bands with CISD confirmation and session context alignment.",
                ExitLogic: "Exit on opposing liquidity reclaim, invalidation of sweep structure, or risk-managed stop/target.",
                NoTradeConditions: "No trade outside active session windows, when CISD is not confirmed, or when required multi-timeframe data is missing.",
                RiskManagement: "Simulation-only entries with risk-engine approval and ATR/session-aware sizing constraints.",
                ApproximationNotes: "#itsimpossible source logic is not provided. MOMO uses an internal BB liquidity sweep + CISD approximation.",
                ImplementationNotes: requirement?.Notes),
            StrategyCode.BbLiquiditySweepCisdRsiPrimed => new StrategyContent(
                HowItWorks: "BB liquidity sweep CISD strategy with an additional RSI Primed [ChartPrime] filter for higher-quality setups.",
                EntryLogic: "Same as BB Liquidity Sweep CISD, but entries require RSI Primed alignment on the execution timeframe.",
                ExitLogic: "Exit on structure invalidation, opposing RSI Primed signal, or configured stop/target.",
                NoTradeConditions: "No trade when RSI Primed filter fails, session rules block trading, or required 1m/3m/5m data is unavailable.",
                RiskManagement: "Risk engine must approve every simulated order. No live order placement.",
                ApproximationNotes: "RSI Primed [ChartPrime] is ported approximately for MOMO simulation; not a vendor-identical implementation.",
                ImplementationNotes: requirement?.Notes),
            StrategyCode.SupertrendContinuation => new StrategyContent(
                HowItWorks: "Trend-following continuation strategy that enters in the direction of an established SuperTrend regime.",
                EntryLogic: "Enter when price pulls back toward SuperTrend and resumes in trend direction with ATR-based confirmation.",
                ExitLogic: "Exit on SuperTrend reversal or risk-managed stop/target.",
                NoTradeConditions: "No trade during choppy SuperTrend flips or insufficient warmup candles.",
                RiskManagement: "ATR-informed stops with risk-engine gating on every simulated trade.",
                ApproximationNotes: "Standard MOMO SuperTrend continuation approximation for futures backtesting and paper trading.",
                ImplementationNotes: requirement?.Notes),
            StrategyCode.EmaPullback => new StrategyContent(
                HowItWorks: "Pullback continuation strategy that buys/sells retests of EMA structure in an established trend.",
                EntryLogic: "Enter when price pulls back to EMA support/resistance and resumes trend with confirmation candle logic.",
                ExitLogic: "Exit on EMA break, opposing signal, or configured stop/target.",
                NoTradeConditions: "No trade when EMA structure is flat, trend regime is unclear, or indicator warmup is incomplete.",
                RiskManagement: "Risk engine controls position sizing and stop placement in simulation modes only.",
                ApproximationNotes: "Generic EMA pullback implementation based on catalog metadata.",
                ImplementationNotes: requirement?.Notes),
            StrategyCode.FourHourRangeReEntry => new StrategyContent(
                HowItWorks: "Uses the first 4-hour New York session range as an anchor and trades 5m breakout/re-entry setups back inside the range.",
                EntryLogic: "After a range breakout, wait for price to close back inside the 4h range on 5m, then enter in the re-entry direction.",
                ExitLogic: "Exit when the daily range completes, structure invalidates, or stop/target is hit.",
                NoTradeConditions: "No trade before range is ready, after NY day end, or when 4h anchor candles are missing.",
                RiskManagement: "Range-based stop logic with risk-engine approval. Simulation only.",
                ApproximationNotes: "4h anchor uses New York session day boundaries; verify candle imports include 4h and 5m data.",
                ImplementationNotes: requirement?.Notes),
            StrategyCode.PriceStructureBreakoutRetest => new StrategyContent(
                HowItWorks: "Detects confirmed swing structure levels, waits for a close beyond the level, then enters after a valid retest and confirmation using OHLC candles only.",
                EntryLogic: "Enter at confirmation candle close after bullish/bearish breakout, retest of broken level, and reaction confirmation.",
                ExitLogic: "Fixed R-multiple target with stop beyond retest extreme plus buffer.",
                NoTradeConditions: "NoConfirmedSwing, NoBreakout, WaitingForRetest, RetestExpired, RetestInvalidated, NoConfirmation, InvalidStop, DuplicateSetup.",
                RiskManagement: "Strategy Laboratory simulates raw outcomes independently. Normal risk gating is observational only in lab modes.",
                ApproximationNotes: "Pure price-structure v1.0.0 with no indicator filters. Complete Strategy Laboratory testing before validation.",
                ImplementationNotes: requirement?.Notes),
            StrategyCode.PriceStructureLiquiditySweepReclaim => new StrategyContent(
                HowItWorks: "Detects swing liquidity levels, waits for a sweep through the level, and enters when price reclaims the level on the same or next candle.",
                EntryLogic: "Enter at reclaim candle close after sell-side (long) or buy-side (short) liquidity sweep and reclaim.",
                ExitLogic: "Fixed 2R target with stop beyond sweep wick plus buffer.",
                NoTradeConditions: "NoLiquidityLevel, NoSweep, SweepDidNotReclaim, ReclaimExpired, InvalidStop, DuplicateSetup.",
                RiskManagement: "Strategy Laboratory simulates raw outcomes independently. Normal risk gating is observational only in lab modes.",
                ApproximationNotes: "Pure price-structure v1.0.0 with no indicator filters.",
                ImplementationNotes: requirement?.Notes),
            _ => new StrategyContent(
                HowItWorks: strategy.Description,
                EntryLogic: "Entry rules are defined by the strategy plugin and configured parameters.",
                ExitLogic: "Exit rules follow strategy plugin stop/target and invalidation logic.",
                NoTradeConditions: "No trade when required data, indicators, or session filters are unavailable.",
                RiskManagement: "All simulated trades require risk-engine approval. Live trading remains disabled.",
                ApproximationNotes: "See strategy notes and benchmark results for implementation caveats.",
                ImplementationNotes: requirement?.Notes)
        };
    }

    private sealed record StrategyContent(
        string HowItWorks,
        string EntryLogic,
        string ExitLogic,
        string NoTradeConditions,
        string RiskManagement,
        string ApproximationNotes,
        string? ImplementationNotes);
}
