using System.Text.Json;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Risk;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.Implementations;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Application.Strategies.MomoRange;
using MomoQuant.Application.Strategies.PriceStructure;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.StrategyLab.Confidence;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Indicators;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;
using Moq;

namespace MomoQuant.UnitTests.Strategies;

internal static class Milestone231BParityFixtures
{
    private static readonly DateTime PsbrStartUtc = new(2026, 01, 01, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Remaps Adaptive M5/H1 price geometry onto H4/D1 timeframes while preserving OHLC sequence.
    /// </summary>
    public static (List<Candle> ltf, List<Candle> htf) RemapAdaptiveToH4D1((List<Candle> ltf, List<Candle> htf) source)
    {
        var start = source.ltf[0].OpenTimeUtc;
        var ltf = new List<Candle>(source.ltf.Count);
        for (var i = 0; i < source.ltf.Count; i++)
        {
            var c = source.ltf[i];
            var open = start.AddHours(i * 4L);
            ltf.Add(new Candle
            {
                Id = i + 1,
                SymbolId = c.SymbolId,
                ExchangeId = c.ExchangeId,
                Timeframe = Timeframe.H4,
                OpenTimeUtc = open,
                CloseTimeUtc = open.AddHours(4),
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume,
                IsClosed = true,
                CreatedAtUtc = open
            });
        }

        var htf = new List<Candle>(source.htf.Count);
        for (var i = 0; i < source.htf.Count; i++)
        {
            var c = source.htf[i];
            var ltfIndex = Math.Min((i + 1) * 12 - 1, ltf.Count - 1);
            var open = ltf[Math.Max(0, ltfIndex - 11)].OpenTimeUtc;
            var close = open.AddDays(1);
            htf.Add(new Candle
            {
                Id = 10_000 + i,
                SymbolId = c.SymbolId,
                ExchangeId = c.ExchangeId,
                Timeframe = Timeframe.D1,
                OpenTimeUtc = open,
                CloseTimeUtc = close,
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume,
                IsClosed = true,
                CreatedAtUtc = open
            });
        }

        return (ltf, htf);
    }

    public static void AssignSequentialIds(IList<Candle> candles, long idOffset = 1)
    {
        for (var i = 0; i < candles.Count; i++)
        {
            candles[i].Id = idOffset + i;
        }
    }

    public static List<Candle> PolluteHtf(IReadOnlyList<Candle> cleanHtf, DateTime evaluationTimeUtc, Timeframe htfTimeframe)
    {
        var polluted = cleanHtf.ToList();
        polluted.Add(new Candle
        {
            Id = 90_001,
            SymbolId = 1,
            ExchangeId = 1,
            Timeframe = htfTimeframe,
            OpenTimeUtc = evaluationTimeUtc.AddMinutes(-10),
            CloseTimeUtc = evaluationTimeUtc.AddMinutes(50),
            Open = 88888m,
            High = 90000m,
            Low = 87000m,
            Close = 89000m,
            Volume = 8888m,
            IsClosed = false,
            CreatedAtUtc = evaluationTimeUtc
        });
        polluted.Add(new Candle
        {
            Id = 90_002,
            SymbolId = 1,
            ExchangeId = 1,
            Timeframe = htfTimeframe,
            OpenTimeUtc = evaluationTimeUtc.AddHours(1),
            CloseTimeUtc = evaluationTimeUtc.AddHours(2),
            Open = 99999m,
            High = 100000m,
            Low = 99000m,
            Close = 99500m,
            Volume = 9999m,
            IsClosed = true,
            CreatedAtUtc = evaluationTimeUtc.AddHours(1)
        });
        return polluted;
    }

    /// <summary>Mirror of PriceStructureBreakoutRetestTests.BuildLongScenario (ReactionClose defaults).</summary>
    public static List<Candle> BuildPsbrLongScenario()
    {
        var candles = new List<Candle>();
        for (var i = 0; i < 18; i++)
        {
            var time = PsbrStartUtc.AddMinutes(i * 5L);
            if (i == 6)
            {
                candles.Add(CreatePsbrCandle(time, 99.60m, 100.00m, 99.55m, 99.80m));
                continue;
            }

            var open = 99.70m + ((i % 4) * 0.03m);
            var high = 99.92m;
            var low = 99.40m;
            var close = 99.75m + ((i % 3) * 0.02m);
            candles.Add(CreatePsbrCandle(time, open, high, low, close));
        }

        var breakoutTime = candles[^1].OpenTimeUtc.AddMinutes(5);
        candles.Add(CreatePsbrCandle(breakoutTime, 99.60m, 100.60m, 99.60m, 100.40m));
        var retestTime = breakoutTime.AddMinutes(5);
        candles.Add(CreatePsbrCandle(retestTime, 100.20m, 100.30m, 100.00m, 100.05m));
        var confirmTime = retestTime.AddMinutes(5);
        candles.Add(CreatePsbrCandle(confirmTime, 100.10m, 100.90m, 100.00m, 100.80m));
        AssignSequentialIds(candles);
        return candles;
    }

    private static Candle CreatePsbrCandle(DateTime openTimeUtc, decimal open, decimal high, decimal low, decimal close) =>
        new()
        {
            SymbolId = 1,
            ExchangeId = 1,
            Timeframe = Timeframe.M5,
            OpenTimeUtc = openTimeUtc,
            CloseTimeUtc = openTimeUtc.AddMinutes(5),
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = 100m,
            IsClosed = true,
            CreatedAtUtc = openTimeUtc
        };

    public static Dictionary<long, IndicatorSnapshot> BuildTrendingSnapshots(IReadOnlyList<Candle> candles)
    {
        var map = new Dictionary<long, IndicatorSnapshot>();
        foreach (var c in candles)
        {
            map[c.Id] = new IndicatorSnapshot
            {
                CandleId = c.Id,
                SymbolId = c.SymbolId,
                Timeframe = c.Timeframe,
                Ema20 = c.Close + 30m,
                Ema50 = c.Close + 20m,
                Ema200 = c.Close + 10m,
                Atr14 = Math.Max(1m, c.Close * 0.01m),
                CalculatedAtUtc = c.CloseTimeUtc,
                CreatedAtUtc = c.CloseTimeUtc
            };
        }

        return map;
    }

    public static Dictionary<long, IndicatorSnapshot> BuildRangingSnapshots(IReadOnlyList<Candle> candles)
    {
        var map = new Dictionary<long, IndicatorSnapshot>();
        foreach (var c in candles)
        {
            map[c.Id] = new IndicatorSnapshot
            {
                CandleId = c.Id,
                SymbolId = c.SymbolId,
                Timeframe = c.Timeframe,
                Ema20 = c.Close + 1m,
                Ema50 = c.Close + 5m,
                Ema200 = c.Close,
                Atr14 = Math.Max(0.5m, c.Close * 0.005m),
                CalculatedAtUtc = c.CloseTimeUtc,
                CreatedAtUtc = c.CloseTimeUtc
            };
        }

        return map;
    }

    public static StrategyLabRun CreateRun(long id, string code, string timeframe, DateTime from, DateTime to, string version = "1.0.0") =>
        new()
        {
            Id = id,
            Name = $"m231b-parity-{id}",
            StrategyCode = code,
            StrategyVersion = version,
            ExchangeId = 1,
            SymbolId = 1,
            Symbol = code.Contains("RANGE", StringComparison.Ordinal) ? "ETHUSDT" : "BTCUSDT",
            Timeframe = timeframe,
            FromUtc = from,
            ToUtc = to,
            ExecutionMode = StrategyLabExecutionMode.RawStrategy,
            ParametersJson = "{}",
            FeeSettingsJson = """{"takerFeeRate":0.0004}""",
            SlippageSettingsJson = """{"slippagePercent":0}""",
            InitialBalance = 10000m,
            Status = StrategyLabRunStatus.Created,
            CreatedAtUtc = DateTime.UtcNow,
            CandleLoadContractVersion = StrategyLabCandleLoadContractVersions.Current
        };

    public static StrategyLabRunner CreateRunner(
        StrategyLabRun run,
        ITradingStrategy plugin,
        StrategyCode code,
        string version,
        StrategyLabDataset dataset,
        List<StrategyResearchCandidate> sink)
    {
        var runRepo = new Mock<IStrategyLabRunRepository>();
        runRepo.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runRepo.Setup(r => r.UpdateAsync(It.IsAny<StrategyLabRun>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var candidateRepo = new Mock<IStrategyResearchCandidateRepository>();
        candidateRepo.Setup(c => c.AddRangeAsync(It.IsAny<IEnumerable<StrategyResearchCandidate>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<StrategyResearchCandidate>, CancellationToken>((items, _) => sink.AddRange(items))
            .Returns(Task.CompletedTask);

        var strategyRepo = new Mock<IStrategyRepository>();
        strategyRepo.Setup(s => s.GetByCodeAsync(code, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Strategy
            {
                Id = 1,
                Code = code,
                Name = plugin.Name,
                Version = version,
                IsEnabled = true,
                Description = plugin.Description
            });

        var registry = new Mock<IStrategyRegistry>();
        registry.Setup(r => r.GetByCode(code)).Returns(plugin);

        var requirements = new Mock<IStrategyDataRequirementService>();
        requirements.Setup(r => r.GetByStrategyIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Application.Common.ServiceResult<Application.Strategies.Dtos.StrategyDataRequirementDto>.Ok(
                new Application.Strategies.Dtos.StrategyDataRequirementDto
                {
                    StrategyId = 1,
                    StrategyCode = code.ToCode(),
                    StrategyName = plugin.Name,
                    PreferredExecutionTimeframe = run.Timeframe,
                    AllowedExecutionTimeframes = [run.Timeframe],
                    RequiredDataTimeframes = [run.Timeframe],
                    OptionalDataTimeframes = [],
                    AnchorTimeframes = [],
                    HigherTimeframeFilters = [],
                    WarmupCandles = 0,
                    RequiredIndicators = [],
                    RequiredIndicatorTimeframes = [],
                    Warnings = []
                }));

        var coverage = new Mock<IHistoricalCandleCoverageService>();
        coverage.Setup(c => c.EnsureCoverageAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<Func<HistoricalCoverageProgress, CancellationToken, Task>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Application.Common.ServiceResult<HistoricalCandleCoverageResult>.Ok(new HistoricalCandleCoverageResult
            {
                Coverage = new MomoQuant.Application.Validation.Dtos.CandleCoverageDto
                {
                    Symbol = run.Symbol,
                    Exchange = "BINANCE",
                    Timeframe = run.Timeframe,
                    MissingCandleCountEstimate = 0,
                    CoverageStatus = "Complete"
                },
                FinalCoverageStatus = "Complete",
                RequestedFromUtc = run.FromUtc,
                RequestedToUtc = run.ToUtc,
                RequestedTimeframe = run.Timeframe,
                ExistingCandleCount = dataset.Candles.Count,
                MissingRanges = []
            }));

        return new StrategyLabRunner(
            runRepo.Object,
            candidateRepo.Object,
            Mock.Of<IBacktestDataLoader>(),
            strategyRepo.Object,
            registry.Object,
            requirements.Object,
            coverage.Object,
            Mock.Of<IRiskRuleRepository>(),
            Mock.Of<IRiskProfileRepository>(),
            new PositionSizingService(),
            Mock.Of<ICandidateConfidenceScorer>(),
            standardCandleDataSource: new StandardStrategyLabCandleDataSource(Mock.Of<IBacktestDataLoader>()));
    }

    public static IStrategyParameterProvider CreateParameterProvider(IReadOnlyDictionary<string, string> parameters)
    {
        var provider = new Mock<IStrategyParameterProvider>();
        provider.Setup(p => p.GetParametersAsync(
                It.IsAny<long>(),
                It.IsAny<Timeframe>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(parameters);
        return provider.Object;
    }

    public static BacktestEngine CreateBacktestEngine(
        ClosedHtfCaptureHarness.RecordingStrategyEngine recording,
        IReadOnlyDictionary<string, string> parameters) =>
        new(
            recording,
            CreateParameterProvider(parameters),
            ClosedHtfCaptureHarness.CreateApprovingRiskEngine(),
            new Mock<Application.Ai.IAiIntegrationService>().Object,
            ClosedHtfCaptureHarness.CreateNoopExecutionProvider(),
            ClosedHtfCaptureHarness.CreatePassthroughEnricher());

    public static string? ExtractFingerprint(string? rawDataJson)
    {
        if (string.IsNullOrWhiteSpace(rawDataJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(rawDataJson);
        if (document.RootElement.TryGetProperty("setupFingerprint", out var fp))
        {
            return fp.GetString();
        }

        if (document.RootElement.TryGetProperty("SetupFingerprint", out fp))
        {
            return fp.GetString();
        }

        return null;
    }

    public static string? ExtractStrengthBreakdown(string? rawDataJson)
    {
        if (string.IsNullOrWhiteSpace(rawDataJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(rawDataJson);
        var root = document.RootElement;
        if (root.TryGetProperty("strengthBreakdown", out var breakdown))
        {
            return breakdown.GetRawText();
        }

        if (root.TryGetProperty("diagnostics", out var diagnostics)
            && diagnostics.TryGetProperty("strengthBreakdown", out breakdown))
        {
            return breakdown.GetRawText();
        }

        return null;
    }

    public static bool HasStrengthBreakdown(string? rawDataJson) =>
        ExtractStrengthBreakdown(rawDataJson) is not null;

    public sealed class FixedStrategyLabCandleDataSource : IStrategyLabCandleDataSource
    {
        private readonly StrategyLabDataset _dataset;

        public FixedStrategyLabCandleDataSource(StrategyLabDataset dataset) => _dataset = dataset;

        public Task<StrategyLabDataset> LoadAsync(StrategyLabRun run, int warmupCandles, CancellationToken cancellationToken = default) =>
            Task.FromResult(_dataset);
    }
}
