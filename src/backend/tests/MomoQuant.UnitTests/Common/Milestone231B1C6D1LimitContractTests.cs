using Moq;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.TradingSystems;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.StrategyLab;

namespace MomoQuant.UnitTests.Common;

public sealed class Milestone231B1C6D1LimitContractTests
{
    public static IEnumerable<object[]> ContractCases()
    {
        foreach (var testCase in Cases(SkLivePaperQueryLimits.SessionsDefault, SkLivePaperQueryLimits.SessionsMaximum))
            yield return ["sessions", testCase.Requested, testCase.Expected];
        foreach (var testCase in Cases(SkLivePaperQueryLimits.CandidatesDefault, SkLivePaperQueryLimits.CandidatesMaximum))
            yield return ["candidates", testCase.Requested, testCase.Expected];
        foreach (var testCase in Cases(SkLivePaperQueryLimits.EventsDefault, SkLivePaperQueryLimits.EventsMaximum))
            yield return ["events", testCase.Requested, testCase.Expected];
        foreach (var testCase in Cases(StrategyLabQueryLimits.RecentRunsDefault, StrategyLabQueryLimits.RunsMaximum))
            yield return ["recent-runs", testCase.Requested, testCase.Expected];
        foreach (var testCase in Cases(StrategyLabQueryLimits.RunsByStrategyDefault, StrategyLabQueryLimits.RunsMaximum))
            yield return ["runs-by-strategy", testCase.Requested, testCase.Expected];
    }

    [Theory]
    [MemberData(nameof(ContractCases))]
    public void Normalizers_ApplyExactBoundaryContract(string route, int requested, int expected)
    {
        var actual = route switch
        {
            "sessions" => SkLivePaperQueryLimits.NormalizeSessions(requested),
            "candidates" => SkLivePaperQueryLimits.NormalizeCandidates(requested),
            "events" => SkLivePaperQueryLimits.NormalizeEvents(requested),
            "recent-runs" => StrategyLabQueryLimits.NormalizeRecentRuns(requested),
            "runs-by-strategy" => StrategyLabQueryLimits.NormalizeRunsByStrategy(requested),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task StrategyLabServices_NormalizeBeforeForwardingAndPreserveArguments()
    {
        var runRepository = new Mock<IStrategyLabRunRepository>();
        var strategyRepository = new Mock<IStrategyRepository>();
        strategyRepository.Setup(r => r.GetByCodeAsync(It.IsAny<StrategyCode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Strategy?)null);
        var service = new StrategyLabService(
            runRepository.Object,
            Mock.Of<IStrategyResearchCandidateRepository>(),
            strategyRepository.Object,
            Mock.Of<IStrategyRegistry>(),
            Mock.Of<ISymbolRepository>(),
            Mock.Of<IStrategyLabQueue>(),
            Mock.Of<IStrategyDataRequirementService>());
        var cancellation = new CancellationTokenSource();

        runRepository.Setup(r => r.GetRecentAsync(200, cancellation.Token))
            .ReturnsAsync([new StrategyLabRun { Id = 1, StrategyCode = "MOMO_ADAPTIVE_MTF_TREND_BREAKOUT" }]);
        var recent = await service.GetRecentRunsAsync(int.MaxValue, cancellation.Token);
        Assert.True(recent.Succeeded);
        Assert.Single(recent.Data!);
        runRepository.Verify(r => r.GetRecentAsync(200, cancellation.Token), Times.Once);
        runRepository.Verify(r => r.GetRecentAsync(int.MaxValue, It.IsAny<CancellationToken>()), Times.Never);

        runRepository.Setup(r => r.GetRecentAsync(50, cancellation.Token)).ReturnsAsync([]);
        await service.GetRecentRunsAsync(-1, cancellation.Token);
        runRepository.Verify(r => r.GetRecentAsync(50, cancellation.Token), Times.Once);

        const string strategyCode = "MOMO_ADAPTIVE_MTF_TREND_BREAKOUT";
        runRepository.Setup(r => r.GetByStrategyCodeAsync(strategyCode, 200, cancellation.Token))
            .ReturnsAsync([new StrategyLabRun { Id = 2, StrategyCode = strategyCode }]);
        var byStrategy = await service.GetRunsByStrategyAsync(strategyCode, int.MaxValue, cancellation.Token);
        Assert.True(byStrategy.Succeeded);
        Assert.Single(byStrategy.Data!);
        runRepository.Verify(r => r.GetByStrategyCodeAsync(strategyCode, 200, cancellation.Token), Times.Once);
        runRepository.Verify(r => r.GetByStrategyCodeAsync(strategyCode, int.MaxValue, It.IsAny<CancellationToken>()), Times.Never);

        runRepository.Setup(r => r.GetByStrategyCodeAsync(strategyCode, 20, cancellation.Token)).ReturnsAsync([]);
        await service.GetRunsByStrategyAsync(strategyCode, 0, cancellation.Token);
        runRepository.Verify(r => r.GetByStrategyCodeAsync(strategyCode, 20, cancellation.Token), Times.Once);
    }

    private static IEnumerable<(int Requested, int Expected)> Cases(int fallback, int maximum)
    {
        yield return (int.MinValue, fallback);
        yield return (-1, fallback);
        yield return (0, fallback);
        yield return (1, 1);
        yield return (fallback, fallback);
        yield return (maximum - 1, maximum - 1);
        yield return (maximum, maximum);
        yield return (maximum + 1, maximum);
        yield return (int.MaxValue, maximum);
    }
}
