using System.Globalization;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Common;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.Settings;

public interface ITradingSettingsService
{
    Task<ServiceResult<TradingSettingsDto>> GetAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<TradingSettingsDto>> UpdateAsync(UpdateTradingSettingsRequest request, CancellationToken cancellationToken = default);

    Task<ServiceResult<TradingSettingsDto>> ResetDefaultsAsync(CancellationToken cancellationToken = default);
}

public sealed class TradingSettingsService : ITradingSettingsService
{
    private const string Prefix = "TradingSettings.";
    private static readonly TradingSettingsDto Defaults = new();
    private readonly ITradingSettingsRepository _repository;

    public TradingSettingsService(ITradingSettingsRepository repository)
    {
        _repository = repository;
    }

    public async Task<ServiceResult<TradingSettingsDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        var raw = await _repository.GetAllAsync(cancellationToken);
        return ServiceResult<TradingSettingsDto>.Ok(Map(raw));
    }

    public async Task<ServiceResult<TradingSettingsDto>> UpdateAsync(
        UpdateTradingSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(request);
        if (!validation.Succeeded)
        {
            return ServiceResult<TradingSettingsDto>.Fail(validation.ErrorMessage!, validation.ErrorField);
        }

        var values = ToDictionary(request);
        await _repository.UpsertAsync(values, cancellationToken);
        return ServiceResult<TradingSettingsDto>.Ok(request);
    }

    public async Task<ServiceResult<TradingSettingsDto>> ResetDefaultsAsync(CancellationToken cancellationToken = default)
    {
        await _repository.UpsertAsync(ToDictionary(Defaults), cancellationToken);
        return ServiceResult<TradingSettingsDto>.Ok(Defaults);
    }

    private static ServiceResult<bool> Validate(TradingSettingsDto request)
    {
        if (request.MaxLeverage < 1m)
        {
            return ServiceResult<bool>.Fail("MaxLeverage must be >= 1.", "maxLeverage");
        }

        if (request.DefaultLeverage < 1m || request.DefaultLeverage > request.MaxLeverage)
        {
            return ServiceResult<bool>.Fail("DefaultLeverage must be between 1 and MaxLeverage.", "defaultLeverage");
        }

        if (request.MaxRiskPerTradePercent <= 0m || request.MaxRiskPerTradePercent > 10m)
        {
            return ServiceResult<bool>.Fail("MaxRiskPerTradePercent must be > 0 and <= 10.", "maxRiskPerTradePercent");
        }

        if (request.MaxDailyLossPercent <= 0m || request.MaxDailyLossPercent > 50m)
        {
            return ServiceResult<bool>.Fail("MaxDailyLossPercent must be > 0 and <= 50.", "maxDailyLossPercent");
        }

        if (request.MaxOpenPositions < 1)
        {
            return ServiceResult<bool>.Fail("MaxOpenPositions must be >= 1.", "maxOpenPositions");
        }

        if (request.MakerFeeRate < 0m || request.TakerFeeRate < 0m)
        {
            return ServiceResult<bool>.Fail("Fees must be >= 0.", "makerFeeRate");
        }

        if (request.SlippagePercent < 0m)
        {
            return ServiceResult<bool>.Fail("SlippagePercent must be >= 0.", "slippagePercent");
        }

        if (request.MinRewardRiskRatio <= 0m)
        {
            return ServiceResult<bool>.Fail("MinRewardRiskRatio must be > 0.", "minRewardRiskRatio");
        }

        if (!Enum.TryParse<BenchmarkEvaluationMode>(request.DefaultBenchmarkEvaluationMode, true, out _))
        {
            return ServiceResult<bool>.Fail("DefaultBenchmarkEvaluationMode is invalid.", "defaultBenchmarkEvaluationMode");
        }

        return ServiceResult<bool>.Ok(true);
    }

    private static TradingSettingsDto Map(IReadOnlyDictionary<string, string> values)
    {
        return new TradingSettingsDto
        {
            MaxLeverage = GetDecimal(values, nameof(TradingSettingsDto.MaxLeverage), Defaults.MaxLeverage),
            DefaultLeverage = GetDecimal(values, nameof(TradingSettingsDto.DefaultLeverage), Defaults.DefaultLeverage),
            MaxRiskPerTradePercent = GetDecimal(values, nameof(TradingSettingsDto.MaxRiskPerTradePercent), Defaults.MaxRiskPerTradePercent),
            DefaultRiskPerTradePercent = GetDecimal(values, nameof(TradingSettingsDto.DefaultRiskPerTradePercent), Defaults.DefaultRiskPerTradePercent),
            MaxDailyLossPercent = GetDecimal(values, nameof(TradingSettingsDto.MaxDailyLossPercent), Defaults.MaxDailyLossPercent),
            MaxTotalDrawdownPercent = GetDecimal(values, nameof(TradingSettingsDto.MaxTotalDrawdownPercent), Defaults.MaxTotalDrawdownPercent),
            MaxOpenPositions = GetInt(values, nameof(TradingSettingsDto.MaxOpenPositions), Defaults.MaxOpenPositions),
            MaxTradesPerDay = GetInt(values, nameof(TradingSettingsDto.MaxTradesPerDay), Defaults.MaxTradesPerDay),
            MaxTradesPerSymbolPerDay = GetInt(values, nameof(TradingSettingsDto.MaxTradesPerSymbolPerDay), Defaults.MaxTradesPerSymbolPerDay),
            MaxPositionSizeUsd = GetDecimal(values, nameof(TradingSettingsDto.MaxPositionSizeUsd), Defaults.MaxPositionSizeUsd),
            MinRewardRiskRatio = GetDecimal(values, nameof(TradingSettingsDto.MinRewardRiskRatio), Defaults.MinRewardRiskRatio),
            DefaultRewardRiskRatio = GetDecimal(values, nameof(TradingSettingsDto.DefaultRewardRiskRatio), Defaults.DefaultRewardRiskRatio),
            MakerFeeRate = GetDecimal(values, nameof(TradingSettingsDto.MakerFeeRate), Defaults.MakerFeeRate),
            TakerFeeRate = GetDecimal(values, nameof(TradingSettingsDto.TakerFeeRate), Defaults.TakerFeeRate),
            SlippageModel = GetString(values, nameof(TradingSettingsDto.SlippageModel), Defaults.SlippageModel),
            SlippagePercent = GetDecimal(values, nameof(TradingSettingsDto.SlippagePercent), Defaults.SlippagePercent),
            OrderExpiryCandles = GetInt(values, nameof(TradingSettingsDto.OrderExpiryCandles), Defaults.OrderExpiryCandles),
            AllowLongTrades = GetBool(values, nameof(TradingSettingsDto.AllowLongTrades), Defaults.AllowLongTrades),
            AllowShortTrades = GetBool(values, nameof(TradingSettingsDto.AllowShortTrades), Defaults.AllowShortTrades),
            AllowMultiplePositionsPerSymbol = GetBool(values, nameof(TradingSettingsDto.AllowMultiplePositionsPerSymbol), Defaults.AllowMultiplePositionsPerSymbol),
            AllowPositionScaling = GetBool(values, nameof(TradingSettingsDto.AllowPositionScaling), Defaults.AllowPositionScaling),
            AllowReversePosition = GetBool(values, nameof(TradingSettingsDto.AllowReversePosition), Defaults.AllowReversePosition),
            DefaultBenchmarkEvaluationMode = GetString(values, nameof(TradingSettingsDto.DefaultBenchmarkEvaluationMode), Defaults.DefaultBenchmarkEvaluationMode),
            DefaultBenchmarkInitialBalance = GetDecimal(values, nameof(TradingSettingsDto.DefaultBenchmarkInitialBalance), Defaults.DefaultBenchmarkInitialBalance),
            DefaultBenchmarkRiskProfileId = GetLongNullable(values, nameof(TradingSettingsDto.DefaultBenchmarkRiskProfileId)),
            DefaultLivePaperRiskProfileId = GetLongNullable(values, nameof(TradingSettingsDto.DefaultLivePaperRiskProfileId)),
            DefaultConfidenceThreshold = GetDecimal(values, nameof(TradingSettingsDto.DefaultConfidenceThreshold), Defaults.DefaultConfidenceThreshold),
            ConfidenceHardGateDefault = GetBool(values, nameof(TradingSettingsDto.ConfidenceHardGateDefault), Defaults.ConfidenceHardGateDefault),
            UseAiScoringDefault = GetBool(values, nameof(TradingSettingsDto.UseAiScoringDefault), Defaults.UseAiScoringDefault),
            StrictAiRequiredDefault = GetBool(values, nameof(TradingSettingsDto.StrictAiRequiredDefault), Defaults.StrictAiRequiredDefault),
            EnableShadowTradeAnalysis = GetBool(values, nameof(TradingSettingsDto.EnableShadowTradeAnalysis), Defaults.EnableShadowTradeAnalysis),
            SameCandleExitPolicy = GetString(values, nameof(TradingSettingsDto.SameCandleExitPolicy), Defaults.SameCandleExitPolicy)
        };
    }

    private static Dictionary<string, string> ToDictionary(TradingSettingsDto settings) => new(StringComparer.OrdinalIgnoreCase)
    {
        [$"{Prefix}{nameof(settings.MaxLeverage)}"] = settings.MaxLeverage.ToString(CultureInfo.InvariantCulture),
        [$"{Prefix}{nameof(settings.DefaultLeverage)}"] = settings.DefaultLeverage.ToString(CultureInfo.InvariantCulture),
        [$"{Prefix}{nameof(settings.MaxRiskPerTradePercent)}"] = settings.MaxRiskPerTradePercent.ToString(CultureInfo.InvariantCulture),
        [$"{Prefix}{nameof(settings.DefaultRiskPerTradePercent)}"] = settings.DefaultRiskPerTradePercent.ToString(CultureInfo.InvariantCulture),
        [$"{Prefix}{nameof(settings.MaxDailyLossPercent)}"] = settings.MaxDailyLossPercent.ToString(CultureInfo.InvariantCulture),
        [$"{Prefix}{nameof(settings.MaxTotalDrawdownPercent)}"] = settings.MaxTotalDrawdownPercent.ToString(CultureInfo.InvariantCulture),
        [$"{Prefix}{nameof(settings.MaxOpenPositions)}"] = settings.MaxOpenPositions.ToString(CultureInfo.InvariantCulture),
        [$"{Prefix}{nameof(settings.MaxTradesPerDay)}"] = settings.MaxTradesPerDay.ToString(CultureInfo.InvariantCulture),
        [$"{Prefix}{nameof(settings.MaxTradesPerSymbolPerDay)}"] = settings.MaxTradesPerSymbolPerDay.ToString(CultureInfo.InvariantCulture),
        [$"{Prefix}{nameof(settings.MaxPositionSizeUsd)}"] = settings.MaxPositionSizeUsd.ToString(CultureInfo.InvariantCulture),
        [$"{Prefix}{nameof(settings.MinRewardRiskRatio)}"] = settings.MinRewardRiskRatio.ToString(CultureInfo.InvariantCulture),
        [$"{Prefix}{nameof(settings.DefaultRewardRiskRatio)}"] = settings.DefaultRewardRiskRatio.ToString(CultureInfo.InvariantCulture),
        [$"{Prefix}{nameof(settings.MakerFeeRate)}"] = settings.MakerFeeRate.ToString(CultureInfo.InvariantCulture),
        [$"{Prefix}{nameof(settings.TakerFeeRate)}"] = settings.TakerFeeRate.ToString(CultureInfo.InvariantCulture),
        [$"{Prefix}{nameof(settings.SlippageModel)}"] = settings.SlippageModel,
        [$"{Prefix}{nameof(settings.SlippagePercent)}"] = settings.SlippagePercent.ToString(CultureInfo.InvariantCulture),
        [$"{Prefix}{nameof(settings.OrderExpiryCandles)}"] = settings.OrderExpiryCandles.ToString(CultureInfo.InvariantCulture),
        [$"{Prefix}{nameof(settings.AllowLongTrades)}"] = settings.AllowLongTrades.ToString(),
        [$"{Prefix}{nameof(settings.AllowShortTrades)}"] = settings.AllowShortTrades.ToString(),
        [$"{Prefix}{nameof(settings.AllowMultiplePositionsPerSymbol)}"] = settings.AllowMultiplePositionsPerSymbol.ToString(),
        [$"{Prefix}{nameof(settings.AllowPositionScaling)}"] = settings.AllowPositionScaling.ToString(),
        [$"{Prefix}{nameof(settings.AllowReversePosition)}"] = settings.AllowReversePosition.ToString(),
        [$"{Prefix}{nameof(settings.DefaultBenchmarkEvaluationMode)}"] = settings.DefaultBenchmarkEvaluationMode,
        [$"{Prefix}{nameof(settings.DefaultBenchmarkInitialBalance)}"] = settings.DefaultBenchmarkInitialBalance.ToString(CultureInfo.InvariantCulture),
        [$"{Prefix}{nameof(settings.DefaultBenchmarkRiskProfileId)}"] = settings.DefaultBenchmarkRiskProfileId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        [$"{Prefix}{nameof(settings.DefaultLivePaperRiskProfileId)}"] = settings.DefaultLivePaperRiskProfileId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        [$"{Prefix}{nameof(settings.DefaultConfidenceThreshold)}"] = settings.DefaultConfidenceThreshold.ToString(CultureInfo.InvariantCulture),
        [$"{Prefix}{nameof(settings.ConfidenceHardGateDefault)}"] = settings.ConfidenceHardGateDefault.ToString(),
        [$"{Prefix}{nameof(settings.UseAiScoringDefault)}"] = settings.UseAiScoringDefault.ToString(),
        [$"{Prefix}{nameof(settings.StrictAiRequiredDefault)}"] = settings.StrictAiRequiredDefault.ToString(),
        [$"{Prefix}{nameof(settings.EnableShadowTradeAnalysis)}"] = settings.EnableShadowTradeAnalysis.ToString(),
        [$"{Prefix}{nameof(settings.SameCandleExitPolicy)}"] = settings.SameCandleExitPolicy
    };

    private static decimal GetDecimal(IReadOnlyDictionary<string, string> values, string key, decimal fallback) =>
        values.TryGetValue($"{Prefix}{key}", out var raw) && decimal.TryParse(raw, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static int GetInt(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue($"{Prefix}{key}", out var raw) && int.TryParse(raw, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static long? GetLongNullable(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue($"{Prefix}{key}", out var raw) && long.TryParse(raw, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static bool GetBool(IReadOnlyDictionary<string, string> values, string key, bool fallback) =>
        values.TryGetValue($"{Prefix}{key}", out var raw) && bool.TryParse(raw, out var parsed)
            ? parsed
            : fallback;

    private static string GetString(IReadOnlyDictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue($"{Prefix}{key}", out var raw) && !string.IsNullOrWhiteSpace(raw)
            ? raw
            : fallback;
}
