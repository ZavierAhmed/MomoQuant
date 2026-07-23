import { useEffect, useState } from 'react';
import { formatNumber } from '@/components/common/utils';
import {
  strategyLabApi,
  type ShadowPortfolioSummary,
  type StrategyLabRiskAnalysis,
} from '@/api/strategyLabApi';
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

function ShadowPortfolioCard({ title, summary }: { title: string; summary: ShadowPortfolioSummary }) {
  return (
    <div className="rounded-lg border border-slate-800 p-3 text-sm">
      <div className="mb-2 font-medium text-slate-200">{title}</div>
      <div className="grid gap-1 text-slate-400">
        <div>Starting balance: {fmt(summary.startingBalance)}</div>
        <div>Ending balance: {fmt(summary.endingBalance)}</div>
        <div>Gross PnL / Gross return: {fmt(summary.grossPnl)} / {fmt(summary.grossReturnPercent, '%')}</div>
        <div>
          Costs (entry / exit / slip / total): {fmt(summary.entryFees)} / {fmt(summary.exitFees)} /{' '}
          {fmt(summary.slippageCost)} / {fmt(summary.totalTransactionCosts)}
        </div>
        <div>
          Net PnL / Net return after costs: {fmt(summary.realizedNetPnl)} /{' '}
          {fmt(summary.netReturnAfterCostsPercent ?? summary.netReturnPercent, '%')}
        </div>
        <div>
          Trades opened (P/L/BE): {summary.tradesOpened ?? summary.tradesAccepted ?? '—'} (
          {summary.profitableTrades ?? '—'} / {summary.losingTrades ?? '—'} / {summary.breakevenTrades ?? '—'})
        </div>
        <div>Trades rejected: {summary.tradesRejected ?? '—'}</div>
        <div>Max realized drawdown: {fmt(summary.maxRealizedDrawdownPercent, '%')}</div>
        <div>Peak margin usage: {fmt(summary.peakMarginUsagePercent, '%')}</div>
        <div>Peak notional exposure: {fmt(summary.peakNotionalExposurePercent, '%')}</div>
        <div>Peak concurrent risk: {fmt(summary.peakConcurrentRiskPercent, '%')}</div>
        <div>Peak open positions: {summary.peakOpenPositionCount ?? '—'}</div>
        {summary.costModelVersion ? <div>Cost model: {summary.costModelVersion}</div> : null}
        {summary.drawdownCalculationMode ? (
          <div>Drawdown mode: {summary.drawdownCalculationMode}</div>
        ) : null}
        {summary.ledger && summary.ledger.length > 0 ? (
          <div className="mt-2 overflow-x-auto">
            <div className="mb-1 text-slate-300">Shadow trade ledger ({summary.ledger.length})</div>
            <table className="min-w-full text-left text-xs">
              <thead className="text-slate-500">
                <tr>
                  <th className="pr-2">Entry</th>
                  <th className="pr-2">Exit</th>
                  <th className="pr-2">Gross</th>
                  <th className="pr-2">Cost</th>
                  <th className="pr-2">Net</th>
                  <th className="pr-2">Result</th>
                </tr>
              </thead>
              <tbody>
                {summary.ledger.slice(0, 25).map((row, idx) => (
                  <tr key={`${row.candidateId ?? idx}-${row.entryTimeUtc ?? idx}`} className="border-t border-slate-800">
                    <td className="pr-2 py-1">{row.entryTimeUtc ? new Date(row.entryTimeUtc).toISOString().slice(0, 16) : '—'}</td>
                    <td className="pr-2 py-1">{row.exitTimeUtc ? new Date(row.exitTimeUtc).toISOString().slice(0, 16) : '—'}</td>
                    <td className="pr-2 py-1">{fmt(row.grossPnl)}</td>
                    <td className="pr-2 py-1">{fmt(row.totalCost)}</td>
                    <td className="pr-2 py-1">{fmt(row.netPnl)}</td>
                    <td className="pr-2 py-1">{row.netResult ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </div>
    </div>
  );
}

export function StrategyLabRiskAnalysisPanel({
  runId,
  riskOnlyShadowPortfolio,
  fullPipelineShadowPortfolio,
  portfolioPathDivergence,
  pathDiagnostics,
  riskPathAssessmentVersion,
}: {
  runId: number;
  riskOnlyShadowPortfolio?: ShadowPortfolioSummary | null;
  fullPipelineShadowPortfolio?: ShadowPortfolioSummary | null;
  portfolioPathDivergence?: import('@/api/strategyLabApi').PortfolioPathDivergence | null;
  pathDiagnostics?: string[];
  riskPathAssessmentVersion?: string | null;
}) {
  const [analysis, setAnalysis] = useState<StrategyLabRiskAnalysis | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      setLoading(true);
      try {
        const data = await strategyLabApi.getRiskAnalysis(runId);
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

  if (loading) return <div className="text-sm text-slate-400">Loading risk analysis…</div>;
  if (error) return <div className="text-sm text-rose-300">{error}</div>;
  if (!analysis) return <div className="text-sm text-slate-400">No risk analysis available.</div>;

  const s = analysis.financialRiskSummary;
  const w = analysis.winnerLoserRiskComparison;
  const exposure = analysis.exposureAnalytics;
  const hasShadowPortfolios = riskOnlyShadowPortfolio || fullPipelineShadowPortfolio;

  return (
    <div className="space-y-6">
      <div className="rounded-lg border border-sky-900/60 bg-sky-950/20 px-4 py-3 text-sm text-sky-100">
        Financial risk is independent of confidence. Assessment version: {analysis.riskAssessmentVersion ?? '—'}
      </div>

      {analysis.diagnostics.length > 0 ? (
        <div className="rounded-lg border border-rose-800 bg-rose-950/30 px-4 py-3 text-sm text-rose-100 space-y-1">
          {analysis.diagnostics.map((d) => <div key={d}>{d}</div>)}
        </div>
      ) : null}

      <section className="space-y-3">
        <h3 className="text-sm font-semibold uppercase tracking-wide text-slate-300">Risk Summary</h3>
        <div className="grid gap-2 md:grid-cols-4">
          <Metric label="Evaluated" value={String(s.evaluatedCandidateCount)} />
          <Metric label="Financial Approved" value={String(s.approvedCount)} />
          <Metric label="Financial Rejected" value={String(s.rejectedCount)} />
          <Metric label="Approval Rate" value={fmt(s.approvalRate, '%')} />
          <Metric label="Avg Candidate Risk Score" value={fmt(s.averageCandidateRiskScore)} />
          <Metric label="Median Score" value={fmt(s.medianCandidateRiskScore)} />
          <Metric label="Score Range" value={`${fmt(s.minimumCandidateRiskScore)} – ${fmt(s.maximumCandidateRiskScore)}`} />
          <Metric label="Std Dev / Unique" value={`${fmt(s.standardDeviation)} / ${s.uniqueScoreCount}`} />
        </div>
      </section>

      <section className="space-y-3">
        <h3 className="text-sm font-semibold uppercase tracking-wide text-slate-300">Exposure Analytics</h3>
        <div className="grid gap-2 md:grid-cols-3">
          <Metric label="Avg Notional Exposure %" value={fmt(exposure?.averageNotionalExposurePercent, '%')} />
          <Metric label="Avg Margin Usage %" value={fmt(exposure?.averageMarginUsagePercent, '%')} />
          <Metric label="Avg Leverage" value={fmt(exposure?.averageLeverage, 'x')} />
          <Metric label="Avg Concurrent Risk %" value={fmt(exposure?.averageConcurrentRiskPercent, '%')} />
          <Metric label="Avg Initial Margin" value={fmt(exposure?.averageInitialMarginRequired)} />
          <Metric label="Avg Risk At Stop %" value={fmt(exposure?.averageRiskAtStopPercent, '%')} />
        </div>
        {!exposure ? (
          <p className="text-xs text-slate-500">Exposure averages will appear when the risk analysis calculator exposes them.</p>
        ) : null}
      </section>

      {hasShadowPortfolios ? (
        <section className="space-y-3">
          <h3 className="text-sm font-semibold uppercase tracking-wide text-slate-300">Chronological Shadow Portfolios</h3>
          <div className="text-xs text-slate-500">
            Path assessment: {riskPathAssessmentVersion ?? 'Legacy/Unavailable'}
          </div>
          <div className="grid gap-3 md:grid-cols-2">
            {riskOnlyShadowPortfolio ? (
              <ShadowPortfolioCard title="Risk-Only Shadow Portfolio" summary={riskOnlyShadowPortfolio} />
            ) : null}
            {fullPipelineShadowPortfolio ? (
              <ShadowPortfolioCard title="Full-Pipeline Shadow Portfolio" summary={fullPipelineShadowPortfolio} />
            ) : null}
          </div>
          {portfolioPathDivergence ? (
            <div className="rounded-lg border border-slate-800 p-3 text-sm text-slate-300">
              <div className="mb-2 font-medium text-slate-200">Portfolio State Divergence</div>
              <div className="grid gap-1 md:grid-cols-2">
                <div>First divergence: {portfolioPathDivergence.firstDivergenceAtUtc ?? '—'}</div>
                <div>Final balance Δ: {fmt(portfolioPathDivergence.finalBalanceDifference)}</div>
                <div>Max drawdown Δ: {fmt(portfolioPathDivergence.maxDrawdownDifference, '%')}</div>
                <div>Trade count Δ: {portfolioPathDivergence.tradeCountDifference ?? '—'}</div>
                <div>Different risk decisions: {portfolioPathDivergence.differentPortfolioRiskDecisions ?? '—'}</div>
                <div>Opened only Risk-Only: {portfolioPathDivergence.openedOnlyInRiskOnly ?? '—'}</div>
                <div>Opened only Full-Pipeline: {portfolioPathDivergence.openedOnlyInFullPipeline ?? '—'}</div>
                <div>Opened in both: {portfolioPathDivergence.openedInBoth ?? '—'}</div>
              </div>
            </div>
          ) : null}
          {pathDiagnostics && pathDiagnostics.length > 0 ? (
            <ul className="list-disc space-y-1 pl-5 text-xs text-amber-300">
              {pathDiagnostics.map((d) => (
                <li key={d}>{d}</li>
              ))}
            </ul>
          ) : null}
        </section>
      ) : null}

      <section className="space-y-3">
        <h3 className="text-sm font-semibold uppercase tracking-wide text-slate-300">Winner vs Loser Risk</h3>
        <div className="grid gap-2 md:grid-cols-5">
          <Metric label="Avg Winner" value={fmt(w.averageWinnerRiskScore)} />
          <Metric label="Median Winner" value={fmt(w.medianWinnerRiskScore)} />
          <Metric label="Avg Loser" value={fmt(w.averageLoserRiskScore)} />
          <Metric label="Median Loser" value={fmt(w.medianLoserRiskScore)} />
          <Metric label="Separation" value={fmt(w.riskScoreSeparation)} />
        </div>
      </section>

      <section className="grid gap-3 md:grid-cols-2">
        <div className="rounded-lg border border-slate-800 p-3 text-sm">
          <div className="mb-2 font-medium text-slate-200">Rejected Winners</div>
          <div>Count: {analysis.rejectedWinnerAnalysis.count}</div>
          <div>% of winners: {fmt(analysis.rejectedWinnerAnalysis.percentageOfOutcomeGroup, '%')}</div>
          <div>Avg score: {fmt(analysis.rejectedWinnerAnalysis.averageRiskScore)}</div>
          <div>Hypothetical PnL: {fmt(analysis.rejectedWinnerAnalysis.hypotheticalNetPnl)}</div>
          <div>Avg R: {fmt(analysis.rejectedWinnerAnalysis.averageR)}</div>
        </div>
        <div className="rounded-lg border border-slate-800 p-3 text-sm">
          <div className="mb-2 font-medium text-slate-200">Rejected Losers</div>
          <div>Count: {analysis.rejectedLoserAnalysis.count}</div>
          <div>% of losers: {fmt(analysis.rejectedLoserAnalysis.percentageOfOutcomeGroup, '%')}</div>
          <div>Avg score: {fmt(analysis.rejectedLoserAnalysis.averageRiskScore)}</div>
          <div>Hypothetical PnL: {fmt(analysis.rejectedLoserAnalysis.hypotheticalNetPnl)}</div>
          <div>Avg R: {fmt(analysis.rejectedLoserAnalysis.averageR)}</div>
        </div>
      </section>

      <section className="space-y-2">
        <h3 className="text-sm font-semibold uppercase tracking-wide text-slate-300">Risk Policy</h3>
        <div className="grid gap-2 md:grid-cols-3">
          <Metric label="Eligible" value={String(analysis.riskPolicySummary.eligibleCount)} />
          <Metric label="Ineligible" value={String(analysis.riskPolicySummary.ineligibleCount)} />
          <Metric label="Portfolio" value={analysis.portfolioRiskSummary.status} />
        </div>
        <p className="text-xs text-slate-400">{analysis.portfolioRiskSummary.note}</p>
      </section>

      <section>
        <h3 className="mb-2 text-sm font-semibold uppercase tracking-wide text-slate-300">Rule Effectiveness</h3>
        <div className="overflow-x-auto rounded-xl border border-slate-800">
          <table className="min-w-full divide-y divide-slate-800 text-sm">
            <thead className="bg-slate-900/80">
              <tr>
                {['Rule', 'Eval', 'Pass', 'Fail', 'Warn', 'Rej W', 'Rej L', 'Hyp PnL'].map((h) => (
                  <th key={h} className="px-3 py-2 text-left font-medium text-slate-400">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800">
              {analysis.ruleEffectiveness.map((r) => (
                <tr key={r.ruleKey}>
                  <td className="px-3 py-2 text-slate-200">{r.ruleName}</td>
                  <td className="px-3 py-2 text-slate-200">{r.evaluatedCount}</td>
                  <td className="px-3 py-2 text-slate-200">{r.passedCount}</td>
                  <td className="px-3 py-2 text-slate-200">{r.failedCount}</td>
                  <td className="px-3 py-2 text-slate-200">{r.warningCount}</td>
                  <td className="px-3 py-2 text-slate-200">{r.rejectedWinners}</td>
                  <td className="px-3 py-2 text-slate-200">{r.rejectedLosers}</td>
                  <td className="px-3 py-2 text-slate-200">{fmt(r.hypotheticalPnlOfRejected)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
}
