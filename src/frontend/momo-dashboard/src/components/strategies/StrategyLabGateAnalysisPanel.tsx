import { useEffect, useState } from 'react';
import { formatNumber } from '@/components/common/utils';
import { KeyValueGrid } from '@/components/common/KeyValueGrid';
import { strategyLabApi, type StrategyLabGateAnalysis } from '@/api/strategyLabApi';
import { parseApiClientError } from '@/utils/apiError';

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-slate-800 px-3 py-2">
      <div className="text-xs uppercase tracking-wide text-slate-500">{label}</div>
      <div className="text-sm font-medium text-slate-100">{value}</div>
    </div>
  );
}

function fmt(value?: number | null, suffix = '') {
  if (value == null) return '—';
  return `${formatNumber(value)}${suffix}`;
}

function SubsetCard({ title, data }: { title: string; data: StrategyLabGateAnalysis['confidenceApproved'] }) {
  return (
    <div className="rounded-lg border border-slate-800 p-3 text-sm">
      <div className="mb-1 font-medium text-slate-200">{title}</div>
      <div className="text-slate-400">Candidates: {data.candidateCount}</div>
      <div className="text-slate-400">Win rate: {fmt(data.winRate, '%')}</div>
      <div className="text-slate-400">Net PnL: {fmt(data.netPnl)}</div>
      <div className="text-slate-400">PF: {fmt(data.profitFactor)}</div>
      <div className="text-slate-400">Max DD: {fmt(data.maxDrawdownPercent, '%')}</div>
      <div className="text-slate-400">Avg confidence: {fmt(data.averageConfidence)}</div>
      <div className="text-slate-400">Avg risk: {fmt(data.averageRiskScore)}</div>
    </div>
  );
}

export function StrategyLabGateAnalysisPanel({ runId }: { runId: number }) {
  const [analysis, setAnalysis] = useState<StrategyLabGateAnalysis | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      setLoading(true);
      try {
        const data = await strategyLabApi.getGateAnalysis(runId);
        if (!cancelled) {
          setAnalysis(data);
          setError(null);
        }
      } catch (err) {
        if (!cancelled) setError(parseApiClientError(err).message);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [runId]);

  if (loading) return <div className="text-sm text-slate-400">Loading gate analysis…</div>;
  if (error) return <div className="text-sm text-rose-300">{error}</div>;
  if (!analysis) return <div className="text-sm text-slate-400">No gate analysis available.</div>;

  const conf = analysis.confidenceSummary;
  const risk = analysis.riskSummary;
  const cmp = analysis.overallWinnerLoserComparison;

  return (
    <div className="space-y-6">
      <div className="rounded-lg border border-sky-900/60 bg-sky-950/20 px-4 py-3 text-sm text-sky-100">
        {analysis.disclaimer}
      </div>

      {analysis.interpretations.length > 0 ? (
        <div className="rounded-lg border border-slate-800 p-4">
          <h3 className="mb-2 text-sm font-semibold text-slate-200">Interpretations</h3>
          {analysis.interpretations.map((line) => (
            <p key={line} className="text-sm text-slate-300">
              {line}
            </p>
          ))}
        </div>
      ) : null}

      {analysis.confidenceScoreDiagnostics?.degenerateWarningMessage ? (
        <div className="rounded-lg border border-rose-800 bg-rose-950/30 px-4 py-3 text-sm text-rose-100">
          <div className="font-medium">{analysis.confidenceScoreDiagnostics.degenerateWarningCode}</div>
          <div>{analysis.confidenceScoreDiagnostics.degenerateWarningMessage}</div>
        </div>
      ) : null}

      {analysis.riskScoreDiagnostics?.degenerateWarningMessage ? (
        <div className="rounded-lg border border-rose-800 bg-rose-950/30 px-4 py-3 text-sm text-rose-100">
          <div className="font-medium">{analysis.riskScoreDiagnostics.degenerateWarningCode}</div>
          <div>{analysis.riskScoreDiagnostics.degenerateWarningMessage}</div>
        </div>
      ) : null}

      {analysis.confidenceScoreDiagnostics ? (
        <div className="grid gap-2 md:grid-cols-4">
          <Metric label="Unique Confidence Scores" value={String(analysis.confidenceScoreDiagnostics.uniqueScoreCount)} />
          <Metric label="Score Std Dev" value={fmt(analysis.confidenceScoreDiagnostics.standardDeviation)} />
          <Metric
            label="Score Range"
            value={`${fmt(analysis.confidenceScoreDiagnostics.minScore)} – ${fmt(analysis.confidenceScoreDiagnostics.maxScore)}`}
          />
          <Metric label="Confidence Separation" value={fmt(analysis.confidenceSeparation)} />
        </div>
      ) : null}

      {conf ? (
        <section className="space-y-3">
          <h3 className="text-sm font-semibold uppercase tracking-wide text-slate-300">Confidence Summary</h3>
          <div className="grid gap-2 md:grid-cols-4">
            <Metric label="Evaluated" value={String(conf.evaluatedCount)} />
            <Metric label="Approved" value={String(conf.approvedCount)} />
            <Metric label="Rejected" value={String(conf.rejectedCount)} />
            <Metric label="Current Threshold" value={fmt(conf.currentThreshold)} />
            <Metric label="Approval Rate" value={fmt(conf.approvalRate, '%')} />
            <Metric label="Rejection Rate" value={fmt(conf.rejectionRate, '%')} />
            <Metric label="Avg Score" value={fmt(conf.averageScore)} />
            <Metric label="Median Score" value={fmt(conf.medianScore)} />
            <Metric label="Avg Winner Score" value={fmt(conf.averageWinnerScore)} />
            <Metric label="Median Winner Score" value={fmt(conf.medianWinnerScore)} />
            <Metric label="Avg Loser Score" value={fmt(conf.averageLoserScore)} />
            <Metric label="Median Loser Score" value={fmt(conf.medianLoserScore)} />
          </div>

          <div className="grid gap-3 md:grid-cols-2">
            <div className="rounded-lg border border-slate-800 p-3">
              <div className="mb-2 text-sm font-medium text-slate-200">Rejected Winners</div>
              <KeyValueGrid
                items={[
                  { label: 'Count', value: String(analysis.confidenceRejectedWinners?.count ?? 0) },
                  { label: '% of winners', value: fmt(analysis.confidenceRejectedWinners?.percentageOfOutcomeGroup, '%') },
                  { label: 'Avg score', value: fmt(analysis.confidenceRejectedWinners?.averageScore) },
                  { label: 'Median score', value: fmt(analysis.confidenceRejectedWinners?.medianScore) },
                  { label: 'Avg margin below', value: fmt(analysis.confidenceRejectedWinners?.averageMarginBelowThreshold) },
                  { label: 'Hypothetical PnL', value: fmt(analysis.confidenceRejectedWinners?.hypotheticalNetPnl) },
                  { label: 'Avg R', value: fmt(analysis.confidenceRejectedWinners?.hypotheticalAverageR) },
                ]}
              />
            </div>
            <div className="rounded-lg border border-slate-800 p-3">
              <div className="mb-2 text-sm font-medium text-slate-200">Rejected Losers</div>
              <KeyValueGrid
                items={[
                  { label: 'Count', value: String(analysis.confidenceRejectedLosers?.count ?? 0) },
                  { label: '% of losers', value: fmt(analysis.confidenceRejectedLosers?.percentageOfOutcomeGroup, '%') },
                  { label: 'Avg score', value: fmt(analysis.confidenceRejectedLosers?.averageScore) },
                  { label: 'Median score', value: fmt(analysis.confidenceRejectedLosers?.medianScore) },
                  { label: 'Avg margin below', value: fmt(analysis.confidenceRejectedLosers?.averageMarginBelowThreshold) },
                  { label: 'Hypothetical PnL', value: fmt(analysis.confidenceRejectedLosers?.hypotheticalNetPnl) },
                  { label: 'Avg R', value: fmt(analysis.confidenceRejectedLosers?.hypotheticalAverageR) },
                ]}
              />
            </div>
          </div>

          <div>
            <h4 className="mb-2 text-sm font-semibold text-slate-200">Confidence Score Buckets</h4>
            <div className="overflow-x-auto rounded-xl border border-slate-800">
              <table className="min-w-full divide-y divide-slate-800 text-sm">
                <thead className="bg-slate-900/80">
                  <tr>
                    {['Bucket', 'Count', 'Winners', 'Losers', 'Win Rate', 'Net PnL', 'PF', 'Avg R', 'Avg MFE', 'Avg MAE'].map((h) => (
                      <th key={h} className="px-3 py-2 text-left font-medium text-slate-400">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800">
                  {analysis.confidenceBuckets.map((b) => (
                    <tr key={b.label}>
                      <td className="px-3 py-2 text-slate-200">{b.label}</td>
                      <td className="px-3 py-2 text-slate-200">{b.candidateCount}</td>
                      <td className="px-3 py-2 text-slate-200">{b.winnerCount}</td>
                      <td className="px-3 py-2 text-slate-200">{b.loserCount}</td>
                      <td className="px-3 py-2 text-slate-200">{fmt(b.winRate, '%')}</td>
                      <td className="px-3 py-2 text-slate-200">{fmt(b.netPnl)}</td>
                      <td className="px-3 py-2 text-slate-200">{fmt(b.profitFactor)}</td>
                      <td className="px-3 py-2 text-slate-200">{fmt(b.averageR)}</td>
                      <td className="px-3 py-2 text-slate-200">{fmt(b.averageMfe)}</td>
                      <td className="px-3 py-2 text-slate-200">{fmt(b.averageMae)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          <div>
            <h4 className="mb-2 text-sm font-semibold text-slate-200">Confidence Threshold Simulation</h4>
            <p className="mb-2 text-xs text-slate-400">
              Retrospective research only — does not change saved confidence settings.
            </p>
            <div className="overflow-x-auto rounded-xl border border-slate-800">
              <table className="min-w-full divide-y divide-slate-800 text-sm">
                <thead className="bg-slate-900/80">
                  <tr>
                    {['Threshold', 'Accepted', 'Rejected', 'Win Rate', 'Net PnL', 'PF', 'Max DD', 'Avg R', '% Raw PnL'].map((h) => (
                      <th key={h} className="px-3 py-2 text-left font-medium text-slate-400">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-800">
                  {analysis.confidenceThresholdSimulation.map((row) => (
                    <tr key={row.threshold} className={row.isCurrentThreshold ? 'bg-sky-950/30' : undefined}>
                      <td className="px-3 py-2 text-slate-200">
                        {fmt(row.threshold)}
                        {row.isCurrentThreshold ? ' (current)' : ''}
                      </td>
                      <td className="px-3 py-2 text-slate-200">{row.acceptedCount}</td>
                      <td className="px-3 py-2 text-slate-200">{row.rejectedCount}</td>
                      <td className="px-3 py-2 text-slate-200">{fmt(row.acceptedWinRate, '%')}</td>
                      <td className="px-3 py-2 text-slate-200">{fmt(row.acceptedNetPnl)}</td>
                      <td className="px-3 py-2 text-slate-200">{fmt(row.acceptedProfitFactor)}</td>
                      <td className="px-3 py-2 text-slate-200">{fmt(row.acceptedMaxDrawdownPercent, '%')}</td>
                      <td className="px-3 py-2 text-slate-200">{fmt(row.acceptedAverageR)}</td>
                      <td className="px-3 py-2 text-slate-200">{fmt(row.percentOfRawPnlPreserved, '%')}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </section>
      ) : (
        <div className="text-sm text-slate-400">Confidence was not evaluated for this execution mode.</div>
      )}

      {risk ? (
        <section className="space-y-3">
          <h3 className="text-sm font-semibold uppercase tracking-wide text-slate-300">Risk Summary</h3>
          <div className="grid gap-2 md:grid-cols-4">
            <Metric label="Evaluated" value={String(risk.evaluatedCount)} />
            <Metric label="Approved" value={String(risk.approvedCount)} />
            <Metric label="Rejected" value={String(risk.rejectedCount)} />
            <Metric label="Approval Rate" value={fmt(risk.approvalRate, '%')} />
            <Metric label="Avg Risk Score" value={fmt(risk.averageScore)} />
            <Metric label="Avg Winner Risk" value={fmt(risk.averageWinnerScore)} />
            <Metric label="Avg Loser Risk" value={fmt(risk.averageLoserScore)} />
          </div>
          <div className="grid gap-3 md:grid-cols-2">
            <div className="rounded-lg border border-slate-800 p-3 text-sm text-slate-300">
              Rejected winners: {analysis.riskRejectedWinners?.count ?? 0} · Avg score{' '}
              {fmt(analysis.riskRejectedWinners?.averageScore)} · Hyp PnL{' '}
              {fmt(analysis.riskRejectedWinners?.hypotheticalNetPnl)} · Avg R{' '}
              {fmt(analysis.riskRejectedWinners?.hypotheticalAverageR)}
            </div>
            <div className="rounded-lg border border-slate-800 p-3 text-sm text-slate-300">
              Rejected losers: {analysis.riskRejectedLosers?.count ?? 0} · Avg score{' '}
              {fmt(analysis.riskRejectedLosers?.averageScore)} · Hyp PnL{' '}
              {fmt(analysis.riskRejectedLosers?.hypotheticalNetPnl)} · Avg R{' '}
              {fmt(analysis.riskRejectedLosers?.hypotheticalAverageR)}
            </div>
          </div>
          <div className="overflow-x-auto rounded-xl border border-slate-800">
            <table className="min-w-full divide-y divide-slate-800 text-sm">
              <thead className="bg-slate-900/80">
                <tr>
                  {['Reason', 'Rejected', 'Winners', 'Losers', 'Win Rate', 'Hyp PnL', 'Avg R'].map((h) => (
                    <th key={h} className="px-3 py-2 text-left font-medium text-slate-400">
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800">
                {analysis.riskReasonAnalysis.map((row) => (
                  <tr key={row.reason}>
                    <td className="px-3 py-2 text-slate-200">{row.reason}</td>
                    <td className="px-3 py-2 text-slate-200">{row.rejectedCount}</td>
                    <td className="px-3 py-2 text-slate-200">{row.winnerCount}</td>
                    <td className="px-3 py-2 text-slate-200">{row.loserCount}</td>
                    <td className="px-3 py-2 text-slate-200">{fmt(row.winRate, '%')}</td>
                    <td className="px-3 py-2 text-slate-200">{fmt(row.hypotheticalNetPnl)}</td>
                    <td className="px-3 py-2 text-slate-200">{fmt(row.averageR)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      ) : (
        <div className="text-sm text-slate-400">Risk was not evaluated for this execution mode.</div>
      )}

      <section className="space-y-3">
        <h3 className="text-sm font-semibold uppercase tracking-wide text-slate-300">Winner vs Loser Comparison</h3>
        <div className="overflow-x-auto rounded-xl border border-slate-800">
          <table className="min-w-full divide-y divide-slate-800 text-sm">
            <thead className="bg-slate-900/80">
              <tr>
                <th className="px-3 py-2 text-left text-slate-400">Metric</th>
                <th className="px-3 py-2 text-left text-slate-400">Winners</th>
                <th className="px-3 py-2 text-left text-slate-400">Losers</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800 text-slate-200">
              <tr>
                <td className="px-3 py-2">Count</td>
                <td className="px-3 py-2">{cmp.winnerCount}</td>
                <td className="px-3 py-2">{cmp.loserCount}</td>
              </tr>
              <tr>
                <td className="px-3 py-2">Average Confidence</td>
                <td className="px-3 py-2">{fmt(cmp.averageWinnerConfidence)}</td>
                <td className="px-3 py-2">{fmt(cmp.averageLoserConfidence)}</td>
              </tr>
              <tr>
                <td className="px-3 py-2">Median Confidence</td>
                <td className="px-3 py-2">{fmt(cmp.medianWinnerConfidence)}</td>
                <td className="px-3 py-2">{fmt(cmp.medianLoserConfidence)}</td>
              </tr>
              <tr>
                <td className="px-3 py-2">Average Risk Score</td>
                <td className="px-3 py-2">{fmt(cmp.averageWinnerRiskScore)}</td>
                <td className="px-3 py-2">{fmt(cmp.averageLoserRiskScore)}</td>
              </tr>
              <tr>
                <td className="px-3 py-2">Median Risk Score</td>
                <td className="px-3 py-2">{fmt(cmp.medianWinnerRiskScore)}</td>
                <td className="px-3 py-2">{fmt(cmp.medianLoserRiskScore)}</td>
              </tr>
              <tr>
                <td className="px-3 py-2">Average MFE</td>
                <td className="px-3 py-2">{fmt(cmp.averageWinnerMfe)}</td>
                <td className="px-3 py-2">{fmt(cmp.averageLoserMfe)}</td>
              </tr>
              <tr>
                <td className="px-3 py-2">Average MAE</td>
                <td className="px-3 py-2">{fmt(cmp.averageWinnerMae)}</td>
                <td className="px-3 py-2">{fmt(cmp.averageLoserMae)}</td>
              </tr>
              <tr>
                <td className="px-3 py-2">Average Stop Distance</td>
                <td className="px-3 py-2">{fmt(cmp.averageWinnerStopDistancePercent, '%')}</td>
                <td className="px-3 py-2">{fmt(cmp.averageLoserStopDistancePercent, '%')}</td>
              </tr>
              <tr>
                <td className="px-3 py-2">Average Reward:Risk</td>
                <td className="px-3 py-2">{fmt(cmp.averageWinnerRewardRisk)}</td>
                <td className="px-3 py-2">{fmt(cmp.averageLoserRewardRisk)}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <section className="space-y-3">
        <h3 className="text-sm font-semibold uppercase tracking-wide text-slate-300">Raw vs Gated</h3>
        <div className="grid gap-3 md:grid-cols-3">
          <SubsetCard title="Raw" data={analysis.raw} />
          <SubsetCard title="Confidence Approved" data={analysis.confidenceApproved} />
          <SubsetCard title="Confidence Rejected" data={analysis.confidenceRejected} />
          <SubsetCard title="Risk Approved" data={analysis.riskApproved} />
          <SubsetCard title="Risk Rejected" data={analysis.riskRejected} />
          <SubsetCard title="Full Pipeline" data={analysis.fullPipeline} />
        </div>
      </section>
    </div>
  );
}
