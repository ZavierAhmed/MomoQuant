using MomoQuant.Application.Backtesting;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.Application.Strategies;

public static class StrategyHigherTimeframeSupport
{
    public const string AdaptiveHtfMappingContractVersion = "AdaptiveHtfMapping/v1";

    public static bool UsesMomoAdaptiveMapping(ITradingStrategy strategy) =>
        strategy is MomoAdaptiveMultiTimeframeTrendBreakoutStrategy
        || strategy.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout;

    public static Timeframe ResolveHigherTimeframe(ITradingStrategy strategy, Timeframe executionTimeframe)
    {
        if (UsesMomoAdaptiveMapping(strategy))
        {
            return MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(executionTimeframe);
        }

        return ResolveGeneralHigherTimeframe(executionTimeframe);
    }

    public static Timeframe ResolveGeneralHigherTimeframe(Timeframe timeframe) => timeframe switch
    {
        Timeframe.M1 or Timeframe.M3 or Timeframe.M5 => Timeframe.M15,
        Timeframe.M15 or Timeframe.M30 => Timeframe.H1,
        Timeframe.H1 => Timeframe.H4,
        _ => Timeframe.D1
    };

    public static bool TryResolveHigherTimeframe(ITradingStrategy strategy, Timeframe executionTimeframe, out Timeframe higherTimeframe)
    {
        // Only the MTF flagship requires HTF candle series for evaluation/import planning.
        // Other strategies keep the legacy HigherTimeframe enum hint without loading HTF bars.
        if (!UsesMomoAdaptiveMapping(strategy))
        {
            higherTimeframe = default;
            return false;
        }

        if (executionTimeframe is not (Timeframe.M5 or Timeframe.M15 or Timeframe.H1 or Timeframe.H4))
        {
            higherTimeframe = default;
            return false;
        }

        higherTimeframe = MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(executionTimeframe);
        return true;
    }

    public static IReadOnlyCollection<Timeframe> CollectRequiredHigherTimeframes(
        IReadOnlyList<PreparedStrategy> strategies,
        Timeframe executionTimeframe)
    {
        var required = new HashSet<Timeframe>();
        foreach (var prepared in strategies)
        {
            if (TryResolveHigherTimeframe(prepared.Plugin, executionTimeframe, out var higherTimeframe))
            {
                required.Add(higherTimeframe);
            }
        }

        return required;
    }

    public static IReadOnlyList<Candle> SliceHigherTimeframeCandles(
        IReadOnlyDictionary<Timeframe, IReadOnlyList<Candle>> seriesByTimeframe,
        Timeframe higherTimeframe,
        DateTime evaluationCloseTimeUtc)
    {
        if (!seriesByTimeframe.TryGetValue(higherTimeframe, out var series) || series.Count == 0)
        {
            return Array.Empty<Candle>();
        }

        return HigherTimeframeCandleView.SliceClosedThrough(series, evaluationCloseTimeUtc);
    }

    public static (Timeframe HigherTimeframe, IReadOnlyList<Candle> HigherTimeframeCandles) BuildContextHigherTimeframe(
        ITradingStrategy strategy,
        Timeframe executionTimeframe,
        IReadOnlyDictionary<Timeframe, IReadOnlyList<Candle>> seriesByTimeframe,
        DateTime evaluationCloseTimeUtc)
    {
        if (!TryResolveHigherTimeframe(strategy, executionTimeframe, out var higherTimeframe))
        {
            return (ResolveGeneralHigherTimeframe(executionTimeframe), Array.Empty<Candle>());
        }

        return (
            higherTimeframe,
            SliceHigherTimeframeCandles(seriesByTimeframe, higherTimeframe, evaluationCloseTimeUtc));
    }
}
