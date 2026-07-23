using System.Globalization;
using MomoQuant.Application.Optimization.Dtos;

namespace MomoQuant.Application.Common;

public static class DateOnlyRangeNormalizer
{
    public static DateTime StartOfUtcDay(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    public static DateTime EndOfUtcDay(DateTime value) =>
        DateTime.SpecifyKind(value.Date.AddDays(1).AddMilliseconds(-1), DateTimeKind.Utc);

    public static bool TryParseDateOnly(string? value, out DateTime utcDate)
    {
        utcDate = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            utcDate = DateTime.SpecifyKind(dateOnly.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            return true;
        }

        return false;
    }

    public static DateTime ResolveFromUtc(DateTime fromUtc, string? fromDate)
    {
        if (TryParseDateOnly(fromDate, out var parsed))
        {
            return StartOfUtcDay(parsed);
        }

        return fromUtc == default ? fromUtc : StartOfUtcDay(fromUtc);
    }

    public static DateTime ResolveToUtc(DateTime toUtc, string? toDate)
    {
        if (TryParseDateOnly(toDate, out var parsed))
        {
            return EndOfUtcDay(parsed);
        }

        return toUtc == default ? toUtc : EndOfUtcDay(toUtc);
    }

    public static RunStrategyValidationRequest Normalize(RunStrategyValidationRequest request) =>
        new()
        {
            StrategyCode = request.StrategyCode,
            ExchangeId = request.ExchangeId,
            SymbolId = request.SymbolId,
            Timeframe = request.Timeframe,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            FromUtc = ResolveFromUtc(request.FromUtc, request.FromDate),
            ToUtc = ResolveToUtc(request.ToUtc, request.ToDate),
            ValidationMode = request.ValidationMode,
            ParameterSetId = request.ParameterSetId,
            Parameters = request.Parameters,
            RiskProfileId = request.RiskProfileId,
            InitialBalance = request.InitialBalance,
            MaxDrawdownPercent = request.MaxDrawdownPercent
        };

    public static RunParameterOptimizationRequest Normalize(RunParameterOptimizationRequest request) =>
        new()
        {
            StrategyCode = request.StrategyCode,
            ExchangeId = request.ExchangeId,
            SymbolId = request.SymbolId,
            Timeframe = request.Timeframe,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            FromUtc = ResolveFromUtc(request.FromUtc, request.FromDate),
            ToUtc = ResolveToUtc(request.ToUtc, request.ToDate),
            ValidationMode = request.ValidationMode,
            OptimizationMode = request.OptimizationMode,
            ObjectivePreset = request.ObjectivePreset,
            MaxCombinations = request.MaxCombinations,
            MaxRuntimeMinutes = request.MaxRuntimeMinutes,
            MinTradesTraining = request.MinTradesTraining,
            MinTradesValidation = request.MinTradesValidation,
            MaxDrawdownPercent = request.MaxDrawdownPercent,
            SaveBestParameterSet = request.SaveBestParameterSet,
            ParameterSetName = request.ParameterSetName,
            ParameterRangeOverrides = request.ParameterRangeOverrides,
            FixedParameters = request.FixedParameters,
            RiskProfileId = request.RiskProfileId,
            InitialBalance = request.InitialBalance
        };

    public static TargetOptimizationRequest Normalize(TargetOptimizationRequest request) =>
        new()
        {
            StrategyCode = request.StrategyCode,
            ExchangeId = request.ExchangeId,
            SymbolId = request.SymbolId,
            Timeframe = request.Timeframe,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            FromUtc = ResolveFromUtc(request.FromUtc, request.FromDate),
            ToUtc = ResolveToUtc(request.ToUtc, request.ToDate),
            ValidationSplitMode = request.ValidationSplitMode,
            ParameterSearchMode = request.ParameterSearchMode,
            TargetRules = request.TargetRules,
            ParameterRanges = request.ParameterRanges,
            MaxCombinations = request.MaxCombinations,
            MaxAttempts = request.MaxAttempts,
            MaxRuntimeMinutes = request.MaxRuntimeMinutes,
            InitialBalance = request.InitialBalance,
            RiskProfileId = request.RiskProfileId,
            MakerFeeRate = request.MakerFeeRate,
            TakerFeeRate = request.TakerFeeRate,
            SlippagePercent = request.SlippagePercent,
            ExecutionMode = request.ExecutionMode,
            OrderExpiryCandles = request.OrderExpiryCandles,
            UseAiScoring = request.UseAiScoring,
            MinimumConfidenceScore = request.MinimumConfidenceScore,
            AutoImportMissingCandles = request.AutoImportMissingCandles,
            SaveBestIfPassed = request.SaveBestIfPassed,
            AutoApproveIfPassed = request.AutoApproveIfPassed,
            FixedParameters = request.FixedParameters,
            VgResearchProfile = request.VgResearchProfile
        };
}
