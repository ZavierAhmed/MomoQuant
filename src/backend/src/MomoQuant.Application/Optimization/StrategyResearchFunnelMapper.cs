using MomoQuant.Application.Strategies.VolatilityGatedSuperTrend.Dtos;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Constants;

namespace MomoQuant.Application.Optimization;

public static class StrategyResearchFunnelMapper
{
    public static StrategyFunnelDiagnosticsDto? MapFromContext(
        string strategyCode,
        VolatilityGatedSuperTrendFunnelCounts? vgFunnel,
        int riskRejectedCount)
    {
        if (!string.Equals(strategyCode, StrategyCodes.VolatilityGatedSupertrendMomentum, StringComparison.OrdinalIgnoreCase)
            || vgFunnel is null)
        {
            return null;
        }

        return MapVg(vgFunnel, riskRejectedCount);
    }

    public static StrategyFunnelDiagnosticsDto MapVg(
        VolatilityGatedSuperTrendFunnelCounts funnel,
        int riskRejectedCount)
    {
        var topReason = funnel.RejectionReasonBreakdown
            .OrderByDescending(pair => pair.Value)
            .Select(pair => pair.Key)
            .FirstOrDefault();

        var dto = new StrategyFunnelDiagnosticsDto
        {
            Evaluations = funnel.Evaluations,
            SuperTrendBullishCount = funnel.SuperTrendBullishCount,
            SuperTrendBearishCount = funnel.SuperTrendBearishCount,
            VolatilityGatePassedCount = funnel.VolatilityGatePassed,
            VolatilityGateFailedCount = funnel.VolatilityGateFailed,
            MomentumPassedCount = funnel.MomentumPassed,
            MomentumFailedCount = funnel.MomentumFailed,
            RetestDetectedCount = funnel.RetestCount,
            RetestMissingCount = funnel.RetestMissing,
            ConfirmationDetectedCount = funnel.ConfirmationCount,
            ConfirmationMissingCount = funnel.ConfirmationMissing,
            CandidateSignals = funnel.CandidateSignals,
            RiskRejectedCount = riskRejectedCount,
            TradesCreated = funnel.TradesCreated,
            TopRejectionReason = topReason,
            RejectionReasonBreakdown = funnel.RejectionReasonBreakdown
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            PipelineSummary = BuildPipelineSummary(funnel)
        };

        return dto;
    }

    public static string BuildPipelineSummary(VolatilityGatedSuperTrendFunnelCounts funnel) =>
        $"Evaluations {funnel.Evaluations} → Volatility passed {funnel.VolatilityGatePassed} → " +
        $"Momentum passed {funnel.MomentumPassed} → Retest {funnel.RetestCount} → " +
        $"Confirmation {funnel.ConfirmationCount} → Trades {funnel.TradesCreated}";
}
