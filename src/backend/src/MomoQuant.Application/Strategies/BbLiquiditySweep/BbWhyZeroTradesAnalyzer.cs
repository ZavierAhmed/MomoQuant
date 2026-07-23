using MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;

namespace MomoQuant.Application.Strategies.BbLiquiditySweep;

public static class BbWhyZeroTradesAnalyzer
{
    public static string Analyze(BbLiquiditySweepFunnelCounts funnel, bool useRsiFilter)
    {
        if (funnel.FinalCandidateSignals > 0)
        {
            if (funnel.TradesCreated == 0)
            {
                return "Candidates were detected but no trades were created. Review risk engine, confidence gate, or DetectorCalibration mode.";
            }

            return "Candidates and trades were produced. Review profitability after detector calibration is satisfactory.";
        }

        if (funnel.DetectorCalibrationMode)
        {
            return "Detector calibration only — not a final strategy result. Review funnel counts before judging profitability.";
        }

        var bbSweeps = funnel.BollingerBandUpperWickBreaks + funnel.BollingerBandLowerWickBreaks;
        if (bbSweeps == 0)
        {
            return "Price did not sweep outside the Bollinger Bands during this period. Try a longer date range or lower BB strictness.";
        }

        if (funnel.OneMinuteLiquidityLevelsDetected + funnel.FiveMinuteLiquidityLevelsDetected == 0)
        {
            return "No liquidity levels were detected. Liquidity-line approximation may be too strict — try BalancedResearch or DetectorCalibration and tune swing/tolerance settings.";
        }

        if (funnel.BuySideLiquiditySweeps + funnel.SellSideLiquiditySweeps == 0)
        {
            return "BB sweeps occurred, but no matching MOMO liquidity level was close enough. Liquidity-line approximation may be too strict.";
        }

        if (funnel.CisdCandidates == 0)
        {
            return "Liquidity sweeps were detected, but no CISD candidate structure formed. Inspect CISD lookback and displacement settings.";
        }

        if (funnel.CisdConfirmed == 0)
        {
            return "Sweeps were detected, but no CISD confirmation occurred within maxBarsAfterSweep. Increase maxBarsAfterSweep or inspect CISD logic.";
        }

        if (useRsiFilter && funnel.RsiPrimedEvaluations > 0 && funnel.RsiPrimedPassed == 0)
        {
            return "Base setup exists, but RSI Primed filter rejected it. Review RSI signal values and thresholds.";
        }

        if (funnel.TargetPassedMinimumR == 0)
        {
            return "Setups formed, but targets did not provide minimum R. Try lowering research min R or inspect target source.";
        }

        if (funnel.TargetPassed3R == 0 && funnel.TargetPassedMinimumR > 0)
        {
            return "Setups met research minimum R but not 3R. OriginalStrict requires 3R — try BalancedResearch or lower MinRewardRisk.";
        }

        return "Detection produced no qualifying candidates. Review detector calibration before judging profitability.";
    }
}
