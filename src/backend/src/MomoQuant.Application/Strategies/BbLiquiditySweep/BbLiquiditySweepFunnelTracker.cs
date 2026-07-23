using MomoQuant.Application.Strategies.BbLiquiditySweep.Dtos;

namespace MomoQuant.Application.Strategies.BbLiquiditySweep;

public interface IBbLiquiditySweepFunnelTracker
{
    void Reset(BbStrategyStrictnessProfile profile, string engineMode, bool sourceCodeAvailable);
    void RecordCandleMetrics(BbLiquiditySweepCandleMetrics metrics);
    void RecordTradeCreated();
    void RecordSampleRejection(BbLiquiditySweepSampleEvaluation sample);
    BbLiquiditySweepFunnelCounts GetSnapshot();
    IReadOnlyList<BbLiquiditySweepSampleEvaluation> GetSampleRejections();
}

public sealed class BbLiquiditySweepFunnelTracker : IBbLiquiditySweepFunnelTracker
{
    private const int DefaultMaxSamples = 500;
    private readonly object _sync = new();
    private readonly BbLiquiditySweepFunnelCounts _counts = new();
    private readonly List<BbLiquiditySweepSampleEvaluation> _samples = [];
    private int _maxSamples = DefaultMaxSamples;

    public void Reset(BbStrategyStrictnessProfile profile, string engineMode, bool sourceCodeAvailable)
    {
        lock (_sync)
        {
            _counts.Evaluations = 0;
            _counts.CandlesInAllowedSession = 0;
            _counts.CandlesOutsideSession = 0;
            _counts.BollingerBandUpperWickBreaks = 0;
            _counts.BollingerBandLowerWickBreaks = 0;
            _counts.CandlesClosedBackInsideBb = 0;
            _counts.FiveMinuteLiquidityLevelsDetected = 0;
            _counts.OneMinuteLiquidityLevelsDetected = 0;
            _counts.BuySideLiquidityLevelsAvailable = 0;
            _counts.SellSideLiquidityLevelsAvailable = 0;
            _counts.BuySideLiquiditySweeps = 0;
            _counts.SellSideLiquiditySweeps = 0;
            _counts.CloseBackAcrossLiquidityLine = 0;
            _counts.CisdCandidates = 0;
            _counts.CisdConfirmed = 0;
            _counts.RsiPrimedEvaluations = 0;
            _counts.RsiPrimedPassed = 0;
            _counts.TargetPassed3R = 0;
            _counts.TargetPassedMinimumR = 0;
            _counts.FinalCandidateSignals = 0;
            _counts.TradesCreated = 0;
            _counts.NoTradeReasonBreakdown.Clear();
            _samples.Clear();

            _counts.StrictnessProfile = profile.ToString();
            _counts.LiquidityLineEngineMode = engineMode;
            _counts.SourceCodeAvailable = sourceCodeAvailable;
            _counts.DetectorCalibrationMode = profile == BbStrategyStrictnessProfile.DetectorCalibration;
        }
    }

    public void SetMaxSamples(int maxSamples) => _maxSamples = Math.Max(1, maxSamples);

    public void RecordCandleMetrics(BbLiquiditySweepCandleMetrics metrics)
    {
        lock (_sync)
        {
            _counts.Evaluations++;
            if (metrics.InAllowedSession)
            {
                _counts.CandlesInAllowedSession++;
            }
            else
            {
                _counts.CandlesOutsideSession++;
            }

            if (metrics.UpperBbWickBreak)
            {
                _counts.BollingerBandUpperWickBreaks++;
            }

            if (metrics.LowerBbWickBreak)
            {
                _counts.BollingerBandLowerWickBreaks++;
            }

            if (metrics.ClosedBackInsideBb)
            {
                _counts.CandlesClosedBackInsideBb++;
            }

            _counts.OneMinuteLiquidityLevelsDetected = Math.Max(_counts.OneMinuteLiquidityLevelsDetected, metrics.OneMinuteLevelsActive);
            _counts.FiveMinuteLiquidityLevelsDetected = Math.Max(_counts.FiveMinuteLiquidityLevelsDetected, metrics.FiveMinuteLevelsActive);
            _counts.BuySideLiquidityLevelsAvailable = Math.Max(_counts.BuySideLiquidityLevelsAvailable, metrics.BuySideLevelsAvailable);
            _counts.SellSideLiquidityLevelsAvailable = Math.Max(_counts.SellSideLiquidityLevelsAvailable, metrics.SellSideLevelsAvailable);

            if (metrics.BuySideSweep)
            {
                _counts.BuySideLiquiditySweeps++;
            }

            if (metrics.SellSideSweep)
            {
                _counts.SellSideLiquiditySweeps++;
            }

            if (metrics.CloseBackAcrossLiquidity)
            {
                _counts.CloseBackAcrossLiquidityLine++;
            }

            if (metrics.CisdCandidate)
            {
                _counts.CisdCandidates++;
            }

            if (metrics.CisdConfirmed)
            {
                _counts.CisdConfirmed++;
            }

            if (metrics.RsiEvaluated)
            {
                _counts.RsiPrimedEvaluations++;
            }

            if (metrics.RsiPassed)
            {
                _counts.RsiPrimedPassed++;
            }

            if (metrics.TargetPassedMinimumR)
            {
                _counts.TargetPassedMinimumR++;
            }

            if (metrics.TargetPassed3R)
            {
                _counts.TargetPassed3R++;
            }

            if (metrics.FinalCandidate)
            {
                _counts.FinalCandidateSignals++;
            }

            _counts.RecordRejection(metrics.StagedRejectionCode);
        }
    }

    public void RecordTradeCreated()
    {
        lock (_sync)
        {
            _counts.TradesCreated++;
        }
    }

    public void RecordSampleRejection(BbLiquiditySweepSampleEvaluation sample)
    {
        lock (_sync)
        {
            if (_samples.Count >= _maxSamples)
            {
                return;
            }

            _samples.Add(sample);
        }
    }

    public BbLiquiditySweepFunnelCounts GetSnapshot()
    {
        lock (_sync)
        {
            return _counts.Clone();
        }
    }

    public IReadOnlyList<BbLiquiditySweepSampleEvaluation> GetSampleRejections()
    {
        lock (_sync)
        {
            return _samples.ToList();
        }
    }
}
