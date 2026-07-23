using MomoQuant.Application.Common;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Optimization;
using MomoQuant.Application.Optimization.Dtos;
using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend;
using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend.Dtos;
using MomoQuant.Application.Validation;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Constants;
using MomoQuant.Domain.Enums;

namespace MomoQuant.UnitTests.Strategies;

public class StrategyValidationDiagnosticsTests
{
    [Fact]
    public void ZeroTradeAnalyzer_ReturnsNoCandlesLoaded_WhenEvaluationCandleCountZero()
    {
        var result = ZeroTradeAnalyzer.Analyze(
            StrategyCodes.VolatilityGatedSupertrendMomentum,
            funnel: null,
            tradeCount: 0,
            evaluationCandleCount: 0,
            warmupCandles: 500,
            evaluationCount: 0,
            riskRejectedCount: 0);

        Assert.NotNull(result);
        Assert.Equal(ZeroTradeAnalyzer.ReasonNoCandlesLoaded, result!.ReasonCode);
    }

    [Fact]
    public void ZeroTradeAnalyzer_ReturnsNotEnoughAfterWarmup_WhenCandlesBelowWarmup()
    {
        var result = ZeroTradeAnalyzer.Analyze(
            StrategyCodes.VolatilityGatedSupertrendMomentum,
            funnel: null,
            tradeCount: 0,
            evaluationCandleCount: 100,
            warmupCandles: 500,
            evaluationCount: 0,
            riskRejectedCount: 0);

        Assert.NotNull(result);
        Assert.Equal(ZeroTradeAnalyzer.ReasonNotEnoughAfterWarmup, result!.ReasonCode);
    }

    [Fact]
    public void ZeroTradeAnalyzer_ReturnsEngineEvaluationBug_WhenCandlesExistButNoEvaluations()
    {
        var result = ZeroTradeAnalyzer.Analyze(
            StrategyCodes.VolatilityGatedSupertrendMomentum,
            funnel: new StrategyFunnelDiagnosticsDto { Evaluations = 0 },
            tradeCount: 0,
            evaluationCandleCount: 1200,
            warmupCandles: 500,
            evaluationCount: 0,
            riskRejectedCount: 0,
            engineEvaluationBug: true);

        Assert.NotNull(result);
        Assert.Equal(ZeroTradeAnalyzer.ReasonEngineEvaluationBug, result!.ReasonCode);
        Assert.Contains("engine", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ZeroTradeAnalyzer_ReturnsVolatilityGateFailed_WhenVolatilityPassCountZero()
    {
        var funnel = StrategyResearchFunnelMapper.MapVg(new VolatilityGatedSuperTrendFunnelCounts
        {
            Evaluations = 100,
            VolatilityGatePassed = 0
        }, 0);

        var result = ZeroTradeAnalyzer.Analyze(
            StrategyCodes.VolatilityGatedSupertrendMomentum,
            funnel,
            tradeCount: 0,
            evaluationCandleCount: 1200,
            warmupCandles: 500,
            evaluationCount: 100,
            riskRejectedCount: 0);

        Assert.NotNull(result);
        Assert.Equal(ZeroTradeAnalyzer.ReasonVolatilityGateFailed, result!.ReasonCode);
    }

    [Fact]
    public void ZeroTradeAnalyzer_ReturnsMomentumFailed_WhenMomentumPassCountZero()
    {
        var funnel = StrategyResearchFunnelMapper.MapVg(new VolatilityGatedSuperTrendFunnelCounts
        {
            Evaluations = 100,
            VolatilityGatePassed = 40,
            MomentumPassed = 0
        }, 0);

        var result = ZeroTradeAnalyzer.Analyze(
            StrategyCodes.VolatilityGatedSupertrendMomentum,
            funnel,
            tradeCount: 0,
            evaluationCandleCount: 1200,
            warmupCandles: 500,
            evaluationCount: 100,
            riskRejectedCount: 0);

        Assert.NotNull(result);
        Assert.Equal(ZeroTradeAnalyzer.ReasonMomentumFailed, result!.ReasonCode);
    }

    [Fact]
    public void ZeroTradeAnalyzer_ReturnsNoRetest_WhenRetestCountZero()
    {
        var funnel = StrategyResearchFunnelMapper.MapVg(new VolatilityGatedSuperTrendFunnelCounts
        {
            Evaluations = 100,
            VolatilityGatePassed = 40,
            MomentumPassed = 20,
            RetestCount = 0
        }, 0);

        var result = VgWhyZeroTradesAnalyzer.Analyze(
            new VolatilityGatedSuperTrendFunnelCounts
            {
                Evaluations = 100,
                VolatilityGatePassed = 40,
                MomentumPassed = 20,
                RetestCount = 0
            },
            0,
            1200,
            500,
            100);

        Assert.Equal(ZeroTradeAnalyzer.ReasonNoRetest, result.ReasonCode);
    }

    [Fact]
    public void ZeroTradeAnalyzer_ReturnsNoConfirmation_WhenConfirmationCountZero()
    {
        var result = VgWhyZeroTradesAnalyzer.Analyze(
            new VolatilityGatedSuperTrendFunnelCounts
            {
                Evaluations = 100,
                VolatilityGatePassed = 40,
                MomentumPassed = 20,
                RetestCount = 5,
                ConfirmationCount = 0
            },
            0,
            1200,
            500,
            100);

        Assert.Equal(ZeroTradeAnalyzer.ReasonNoConfirmation, result.ReasonCode);
    }

    [Fact]
    public void VgResearchProfile_Exploratory_IsLessStrictThanConservative()
    {
        var conservative = VgResearchProfilePresets.Apply(VgResearchProfilePresets.Conservative, new Dictionary<string, string>());
        var exploratory = VgResearchProfilePresets.Apply(VgResearchProfilePresets.Exploratory, new Dictionary<string, string>());

        Assert.Equal("true", conservative["requireRetest"]);
        Assert.Equal("false", exploratory["requireRetest"]);
        Assert.True(decimal.Parse(conservative["minVolatilityRatio"]) > decimal.Parse(exploratory["minVolatilityRatio"]));
        Assert.True(decimal.Parse(conservative["retestAtrTolerance"]) < decimal.Parse(exploratory["retestAtrTolerance"]));
    }

    [Fact]
    public async Task SaveParameterSet_RejectsApprove_WhenValidationFailed()
    {
        var repository = new InMemoryStrategyParameterSetRepository();
        var service = new StrategyParameterSetService(repository);

        var result = await service.SaveAsync(new SaveStrategyParameterSetRequest
        {
            Name = "Failed set",
            StrategyCode = StrategyCodes.VolatilityGatedSupertrendMomentum,
            Timeframe = "15m",
            Parameters = new Dictionary<string, string>(),
            Approve = true,
            ValidationStatus = ValidationStatus.Failed.ToString(),
            ValidationTradeCount = 0
        });

        Assert.False(result.Succeeded);
        Assert.Contains("failed validation", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveParameterSet_FailedResearch_DoesNotApprove()
    {
        var repository = new InMemoryStrategyParameterSetRepository();
        var service = new StrategyParameterSetService(repository);

        var result = await service.SaveAsync(new SaveStrategyParameterSetRequest
        {
            Name = "Research note",
            StrategyCode = StrategyCodes.VolatilityGatedSupertrendMomentum,
            Timeframe = "15m",
            Parameters = new Dictionary<string, string> { ["minVolatilityRatio"] = "0.90" },
            SaveAsFailedResearch = true,
            Approve = true,
            ValidationStatus = ValidationStatus.Failed.ToString(),
            ValidationTradeCount = 0
        });

        Assert.True(result.Succeeded);
        Assert.False(result.Data!.IsApproved);
    }

    [Fact]
    public void FunnelMapper_BuildsPipelineSummary()
    {
        var summary = StrategyResearchFunnelMapper.BuildPipelineSummary(new VolatilityGatedSuperTrendFunnelCounts
        {
            Evaluations = 500,
            VolatilityGatePassed = 130,
            MomentumPassed = 80,
            RetestCount = 11,
            ConfirmationCount = 2,
            TradesCreated = 0
        });

        Assert.Contains("Evaluations 500", summary);
        Assert.Contains("Volatility passed 130", summary);
        Assert.Contains("Trades 0", summary);
    }

    [Fact]
    public void OptimizationScorer_ReturnsZero_ForZeroValidationTrades()
    {
        var scorer = new ParameterOptimizationScorer();
        var training = SampleMetrics(tradeCount: 20, pnl: 10m, pf: 2m);
        var validation = SampleMetrics(tradeCount: 0, pnl: 0m, pf: 0m);

        Assert.Equal(0m, scorer.Score(training, validation, "Balanced", 20, 10));
    }

    private static StrategyPerformanceMetricsDto SampleMetrics(int tradeCount, decimal pnl, decimal pf) => new()
    {
        NetPnlPercent = pnl,
        WinRate = 55m,
        ProfitFactor = pf,
        MaxDrawdownPercent = 5m,
        TradeCount = tradeCount,
        AverageR = 1m,
        Expectancy = 1m,
        RecoveryFactor = 1m,
        LargestLoss = -5m,
        ConsecutiveLosses = 1
    };
}

internal sealed class InMemoryStrategyParameterSetRepository : MomoQuant.Application.Abstractions.IStrategyParameterSetRepository
{
    private readonly List<MomoQuant.Domain.Strategies.StrategyParameterSet> _items = [];
    private long _nextId = 1;

    public Task AddAsync(MomoQuant.Domain.Strategies.StrategyParameterSet entity, CancellationToken cancellationToken = default)
    {
        entity.Id = _nextId++;
        _items.Add(entity);
        return Task.CompletedTask;
    }

    public Task<MomoQuant.Domain.Strategies.StrategyParameterSet?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_items.FirstOrDefault(item => item.Id == id));

    public Task<MomoQuant.Domain.Strategies.StrategyParameterSet?> GetByIdForUpdateAsync(long id, CancellationToken cancellationToken = default) =>
        GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<MomoQuant.Domain.Strategies.StrategyParameterSet>> ListAsync(
        string? strategyCode,
        long? symbolId,
        string? timeframe,
        CancellationToken cancellationToken = default)
    {
        var query = _items.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(strategyCode))
        {
            query = query.Where(item => string.Equals(item.StrategyCode, strategyCode, StringComparison.OrdinalIgnoreCase));
        }

        if (symbolId.HasValue)
        {
            query = query.Where(item => item.SymbolId == symbolId);
        }

        if (!string.IsNullOrWhiteSpace(timeframe))
        {
            query = query.Where(item => string.Equals(item.Timeframe, timeframe, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult<IReadOnlyList<MomoQuant.Domain.Strategies.StrategyParameterSet>>(query.ToList());
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task UpdateAsync(MomoQuant.Domain.Strategies.StrategyParameterSet entity, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
