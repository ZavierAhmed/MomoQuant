using System.Collections.Concurrent;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;

namespace MomoQuant.Application.Strategies.BbLiquiditySweep;

public interface IBbLiquiditySweepContextService
{
    void Precompute(
        long symbolId,
        IReadOnlyList<Candle> executionCandles,
        IReadOnlyList<Candle>? oneMinuteCandles,
        IReadOnlyList<Candle>? fiveMinuteCandles,
        BbLiquiditySweepParameters? parameters = null);

    IReadOnlyList<Candle> GetCandlesUpTo(string timeframe, DateTime closeTimeUtc);

    IReadOnlyList<LiquidityLevelDto> GetLiquidityLevels(string timeframe, DateTime closeTimeUtc);

    RsiPrimedResultDto? GetRsiPrimedAt(DateTime closeTimeUtc);

    void BuildRsiSeries(IReadOnlyList<Candle> executionCandles, BbLiquiditySweepParameters parameters);

    int GetTotalLevelsDetected(string timeframe);
}

public sealed class BbLiquiditySweepContextService : IBbLiquiditySweepContextService
{
    private readonly IExternalLiquidityLineEngine _liquidityEngine;
    private readonly RsiPrimedChartPrimeIndicator _rsiPrimed;
    private readonly ConcurrentDictionary<string, PreparedContext> _contexts = new(StringComparer.OrdinalIgnoreCase);

    public BbLiquiditySweepContextService(
        IExternalLiquidityLineEngine? liquidityEngine = null,
        RsiPrimedChartPrimeIndicator? rsiPrimed = null)
    {
        _liquidityEngine = liquidityEngine ?? new MomoLiquidityLineEngine();
        _rsiPrimed = rsiPrimed ?? new RsiPrimedChartPrimeIndicator();
    }

    public void Precompute(
        long symbolId,
        IReadOnlyList<Candle> executionCandles,
        IReadOnlyList<Candle>? oneMinuteCandles,
        IReadOnlyList<Candle>? fiveMinuteCandles,
        BbLiquiditySweepParameters? parameters = null)
    {
        var key = symbolId.ToString();
        var settings = parameters ?? BbLiquiditySweepParameters.ApplyStrictnessProfile(BbStrategyStrictnessProfile.BalancedResearch);
        var oneMinute = oneMinuteCandles ?? executionCandles;
        var fiveMinute = fiveMinuteCandles ?? executionCandles;

        _contexts[key] = new PreparedContext
        {
            Parameters = settings,
            ExecutionCandles = executionCandles.ToList(),
            OneMinuteCandles = oneMinute.ToList(),
            FiveMinuteCandles = fiveMinute.ToList(),
            OneMinuteLevels = _liquidityEngine.CalculateLtfLiquidityLevels(oneMinute, "1m", settings).ToList(),
            FiveMinuteLevels = _liquidityEngine.CalculateLiquidityLevels(fiveMinute, "5m", settings).ToList(),
            RsiSeries = []
        };
    }

    public void BuildRsiSeries(IReadOnlyList<Candle> executionCandles, BbLiquiditySweepParameters parameters)
    {
        if (_contexts.IsEmpty)
        {
            return;
        }

        var context = _contexts.Values.First();
        context.RsiSeries = _rsiPrimed.CalculateSeries(
            executionCandles,
            parameters.RsiLength,
            parameters.RsiSmoothing,
            parameters.RsiUseHeikinAshi,
            parameters.RsiOverboughtLevel,
            parameters.RsiOversoldLevel,
            parameters.RsiPrimedSignalValueMode).ToList();
    }

    public IReadOnlyList<Candle> GetCandlesUpTo(string timeframe, DateTime closeTimeUtc)
    {
        var context = GetContext();
        var source = timeframe switch
        {
            "1m" => context.OneMinuteCandles,
            "5m" => context.FiveMinuteCandles,
            _ => context.ExecutionCandles
        };

        return source.Where(c => c.CloseTimeUtc <= closeTimeUtc).ToList();
    }

    public IReadOnlyList<LiquidityLevelDto> GetLiquidityLevels(string timeframe, DateTime closeTimeUtc)
    {
        var context = GetContext();
        var levels = timeframe == "1m" ? context.OneMinuteLevels : context.FiveMinuteLevels;
        var maxAge = context.Parameters.MaxLevelAgeCandles;
        return levels
            .Where(level => level.CreatedAtUtc <= closeTimeUtc && !level.IsSwept)
            .Where(level => maxAge <= 0 || (closeTimeUtc - level.CreatedAtUtc).TotalMinutes <= maxAge * 3)
            .ToList();
    }

    public int GetTotalLevelsDetected(string timeframe)
    {
        var context = GetContext();
        return timeframe == "1m" ? context.OneMinuteLevels.Count : context.FiveMinuteLevels.Count;
    }

    public RsiPrimedResultDto? GetRsiPrimedAt(DateTime closeTimeUtc)
    {
        var context = GetContext();
        return context.RsiSeries.LastOrDefault(item => item.TimeUtc <= closeTimeUtc);
    }

    private PreparedContext GetContext()
    {
        if (_contexts.IsEmpty)
        {
            throw new InvalidOperationException("BB Liquidity Sweep context has not been precomputed.");
        }

        return _contexts.Values.First();
    }

    private sealed class PreparedContext
    {
        public required BbLiquiditySweepParameters Parameters { get; init; }
        public required List<Candle> ExecutionCandles { get; init; }
        public required List<Candle> OneMinuteCandles { get; init; }
        public required List<Candle> FiveMinuteCandles { get; init; }
        public required List<LiquidityLevelDto> OneMinuteLevels { get; init; }
        public required List<LiquidityLevelDto> FiveMinuteLevels { get; init; }
        public List<RsiPrimedResultDto> RsiSeries { get; set; } = [];
    }
}

public sealed class BbLiquiditySweepContextLoader
{
    private readonly ICandleRepository? _candleRepository;

    public BbLiquiditySweepContextLoader(ICandleRepository? candleRepository = null) =>
        _candleRepository = candleRepository;

    public async Task<(IReadOnlyList<Candle> OneMinute, IReadOnlyList<Candle> FiveMinute)> LoadAuxiliaryCandlesAsync(
        long symbolId,
        DateTime fromUtc,
        DateTime toUtc,
        int warmUpCount,
        CancellationToken cancellationToken = default)
    {
        if (_candleRepository is null)
        {
            return ([], []);
        }

        var oneMinute = await _candleRepository.GetCandlesChronologicalAsync(
            symbolId,
            Timeframe.M1,
            fromUtc,
            toUtc,
            warmUpCount,
            cancellationToken);

        var fiveMinute = await _candleRepository.GetCandlesChronologicalAsync(
            symbolId,
            Timeframe.M5,
            fromUtc,
            toUtc,
            warmUpCount,
            cancellationToken);

        return (oneMinute, fiveMinute);
    }
}
