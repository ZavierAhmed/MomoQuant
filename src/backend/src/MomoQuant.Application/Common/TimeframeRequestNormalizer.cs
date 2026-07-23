using MomoQuant.Application.Backtesting.Dtos;
using MomoQuant.Application.Indicators.Dtos;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.MarketData.Dtos;
using MomoQuant.Application.Optimization.Dtos;
using MomoQuant.Application.Replay.Dtos;
using MomoQuant.Application.Strategies.Dtos;

namespace MomoQuant.Application.Common;

public static class TimeframeRequestNormalizer
{
    public static bool TryNormalizeTimeframe(string? input, out string canonical) =>
        TimeframeNormalizer.TryNormalize(input, out canonical);

    public static IReadOnlyList<string> NormalizeTimeframes(IEnumerable<string> values)
    {
        var normalized = new List<string>();
        foreach (var value in values)
        {
            if (!TimeframeNormalizer.TryNormalize(value, out var canonical))
            {
                throw new ArgumentException(TimeframeNormalizer.UnsupportedTimeframeMessage(value));
            }

            if (!normalized.Contains(canonical, StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add(canonical);
            }
        }

        return normalized;
    }

    public static string NormalizeTimeframe(string input) => TimeframeNormalizer.Normalize(input);

    public static RunBacktestRequest Normalize(RunBacktestRequest request) =>
        new()
        {
            Name = request.Name,
            ExchangeId = request.ExchangeId,
            SymbolIds = request.SymbolIds,
            Timeframes = NormalizeTimeframes(request.Timeframes),
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            FromUtc = DateOnlyRangeNormalizer.ResolveFromUtc(request.FromUtc, request.FromDate),
            ToUtc = DateOnlyRangeNormalizer.ResolveToUtc(request.ToUtc, request.ToDate),
            InitialBalance = request.InitialBalance,
            RiskProfileId = request.RiskProfileId,
            StrategyIds = request.StrategyIds,
            ExecutionMode = request.ExecutionMode,
            MakerFeeRate = request.MakerFeeRate,
            TakerFeeRate = request.TakerFeeRate,
            OrderExpiryCandles = request.OrderExpiryCandles,
            UseAiScoring = request.UseAiScoring,
            StrictAiRequired = request.StrictAiRequired,
            MinConfidenceScore = request.MinConfidenceScore,
            SlippagePercent = request.SlippagePercent,
            EvaluationMode = request.EvaluationMode,
            EnableShadowTradeAnalysis = request.EnableShadowTradeAnalysis,
            SameCandleExitPolicy = request.SameCandleExitPolicy,
            RunAnyway = request.RunAnyway,
            AutoImportMissingCandles = request.AutoImportMissingCandles,
            BenchmarkRunId = request.BenchmarkRunId,
            BenchmarkRunItemId = request.BenchmarkRunItemId,
            BenchmarkStrategyCode = request.BenchmarkStrategyCode,
            BenchmarkSymbol = request.BenchmarkSymbol,
            BenchmarkTimeframe = request.BenchmarkTimeframe,
            RequestedByUserId = request.RequestedByUserId
        };

    public static StrategyEvaluationRequest Normalize(StrategyEvaluationRequest request)
    {
        request.Timeframe = NormalizeTimeframe(request.Timeframe);
        return request;
    }

    public static StrategyEvaluateLatestRequest Normalize(StrategyEvaluateLatestRequest request)
    {
        request.Timeframe = NormalizeTimeframe(request.Timeframe);
        return request;
    }

    public static ImportCandlesRequest Normalize(ImportCandlesRequest request) =>
        new()
        {
            ExchangeId = request.ExchangeId,
            SymbolId = request.SymbolId,
            Timeframe = NormalizeTimeframe(request.Timeframe),
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            FromUtc = DateOnlyRangeNormalizer.ResolveFromUtc(request.FromUtc, request.FromDate),
            ToUtc = DateOnlyRangeNormalizer.ResolveToUtc(request.ToUtc, request.ToDate)
        };

    public static RunStrategyValidationRequest NormalizeValidation(RunStrategyValidationRequest request) =>
        new()
        {
            StrategyCode = request.StrategyCode,
            ExchangeId = request.ExchangeId,
            SymbolId = request.SymbolId,
            Timeframe = NormalizeTimeframe(request.Timeframe),
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            FromUtc = DateOnlyRangeNormalizer.ResolveFromUtc(request.FromUtc, request.FromDate),
            ToUtc = DateOnlyRangeNormalizer.ResolveToUtc(request.ToUtc, request.ToDate),
            ValidationMode = request.ValidationMode,
            ParameterSetId = request.ParameterSetId,
            Parameters = request.Parameters,
            RiskProfileId = request.RiskProfileId,
            InitialBalance = request.InitialBalance,
            MaxDrawdownPercent = request.MaxDrawdownPercent,
            ExecutionMode = request.ExecutionMode,
            MakerFeeRate = request.MakerFeeRate,
            TakerFeeRate = request.TakerFeeRate,
            OrderExpiryCandles = request.OrderExpiryCandles,
            UseAiScoring = request.UseAiScoring,
            MinConfidenceScore = request.MinConfidenceScore,
            SlippagePercent = request.SlippagePercent,
            AutoImportCandles = request.AutoImportCandles,
            VgResearchProfile = request.VgResearchProfile
        };

    public static RunParameterOptimizationRequest NormalizeOptimization(RunParameterOptimizationRequest request)
    {
        var normalized = DateOnlyRangeNormalizer.Normalize(request);
        return new RunParameterOptimizationRequest
        {
            StrategyCode = normalized.StrategyCode,
            ExchangeId = normalized.ExchangeId,
            SymbolId = normalized.SymbolId,
            Timeframe = NormalizeTimeframe(request.Timeframe),
            FromDate = normalized.FromDate,
            ToDate = normalized.ToDate,
            FromUtc = normalized.FromUtc,
            ToUtc = normalized.ToUtc,
            ValidationMode = normalized.ValidationMode,
            OptimizationMode = normalized.OptimizationMode,
            ObjectivePreset = normalized.ObjectivePreset,
            MaxCombinations = normalized.MaxCombinations,
            MaxRuntimeMinutes = normalized.MaxRuntimeMinutes,
            MinTradesTraining = normalized.MinTradesTraining,
            MinTradesValidation = normalized.MinTradesValidation,
            MaxDrawdownPercent = normalized.MaxDrawdownPercent,
            SaveBestParameterSet = normalized.SaveBestParameterSet,
            ParameterSetName = normalized.ParameterSetName,
            ParameterRangeOverrides = normalized.ParameterRangeOverrides,
            FixedParameters = normalized.FixedParameters,
            RiskProfileId = normalized.RiskProfileId,
            InitialBalance = normalized.InitialBalance,
            ExecutionMode = request.ExecutionMode,
            MakerFeeRate = request.MakerFeeRate,
            TakerFeeRate = request.TakerFeeRate,
            OrderExpiryCandles = request.OrderExpiryCandles,
            UseAiScoring = request.UseAiScoring,
            MinConfidenceScore = request.MinConfidenceScore,
            SlippagePercent = request.SlippagePercent,
            AutoImportCandles = request.AutoImportCandles,
            VgResearchProfile = request.VgResearchProfile
        };
    }

    public static TargetOptimizationRequest NormalizeTargetOptimization(TargetOptimizationRequest request)
    {
        var normalized = DateOnlyRangeNormalizer.Normalize(request);
        return new TargetOptimizationRequest
        {
            StrategyCode = normalized.StrategyCode,
            ExchangeId = normalized.ExchangeId,
            SymbolId = normalized.SymbolId,
            Timeframe = NormalizeTimeframe(request.Timeframe),
            FromDate = normalized.FromDate,
            ToDate = normalized.ToDate,
            FromUtc = normalized.FromUtc,
            ToUtc = normalized.ToUtc,
            ValidationSplitMode = normalized.ValidationSplitMode,
            ParameterSearchMode = normalized.ParameterSearchMode,
            TargetRules = normalized.TargetRules,
            ParameterRanges = normalized.ParameterRanges,
            MaxCombinations = normalized.MaxCombinations,
            MaxAttempts = normalized.MaxAttempts,
            MaxRuntimeMinutes = normalized.MaxRuntimeMinutes,
            InitialBalance = normalized.InitialBalance,
            RiskProfileId = normalized.RiskProfileId,
            MakerFeeRate = normalized.MakerFeeRate,
            TakerFeeRate = normalized.TakerFeeRate,
            SlippagePercent = normalized.SlippagePercent,
            ExecutionMode = normalized.ExecutionMode,
            OrderExpiryCandles = normalized.OrderExpiryCandles,
            UseAiScoring = normalized.UseAiScoring,
            MinimumConfidenceScore = normalized.MinimumConfidenceScore,
            AutoImportMissingCandles = normalized.AutoImportMissingCandles,
            SaveBestIfPassed = normalized.SaveBestIfPassed,
            AutoApproveIfPassed = normalized.AutoApproveIfPassed,
            FixedParameters = normalized.FixedParameters,
            VgResearchProfile = normalized.VgResearchProfile
        };
    }

    public static RecalculateIndicatorsRequest NormalizeRecalculate(RecalculateIndicatorsRequest request) =>
        new()
        {
            SymbolId = request.SymbolId,
            Timeframe = NormalizeTimeframe(request.Timeframe),
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            FromUtc = DateOnlyRangeNormalizer.ResolveFromUtc(request.FromUtc, request.FromDate),
            ToUtc = DateOnlyRangeNormalizer.ResolveToUtc(request.ToUtc, request.ToDate),
            AutoImportMissingCandles = request.AutoImportMissingCandles
        };

    public static CreateReplaySessionRequest NormalizeReplay(CreateReplaySessionRequest request) =>
        new()
        {
            Name = request.Name,
            ExchangeId = request.ExchangeId,
            SymbolId = request.SymbolId,
            Timeframe = NormalizeTimeframe(request.Timeframe),
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            FromUtc = DateOnlyRangeNormalizer.ResolveFromUtc(request.FromUtc, request.FromDate),
            ToUtc = DateOnlyRangeNormalizer.ResolveToUtc(request.ToUtc, request.ToDate),
            AutoImportMissingCandles = request.AutoImportMissingCandles,
            InitialBalance = request.InitialBalance,
            RiskProfileId = request.RiskProfileId,
            StrategyIds = request.StrategyIds,
            ExecutionMode = request.ExecutionMode,
            MakerFeeRate = request.MakerFeeRate,
            TakerFeeRate = request.TakerFeeRate,
            OrderExpiryCandles = request.OrderExpiryCandles,
            UseAiScoring = request.UseAiScoring,
            StrictAiRequired = request.StrictAiRequired,
            MinConfidenceScore = request.MinConfidenceScore,
            SlippagePercent = request.SlippagePercent,
            Speed = request.Speed
        };

    public static string NormalizeQueryTimeframe(string input) => NormalizeTimeframe(input);
}
