import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { SimulationBanner } from '@/components/common/SimulationBanner';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { KeyValueGrid } from '@/components/common/KeyValueGrid';
import { Badge } from '@/components/common/Badge';
import { formatDate, formatNumber } from '@/components/common/utils';
import { StrategyLabDiagnosticChart } from '@/components/strategies/StrategyLabDiagnosticChart';
import { StrategyLabCandidateGrid } from '@/components/strategies/StrategyLabCandidateGrid';
import { StrategyLabGateAnalysisPanel } from '@/components/strategies/StrategyLabGateAnalysisPanel';
import { StrategyLabRiskAnalysisPanel } from '@/components/strategies/StrategyLabRiskAnalysisPanel';
import { strategyLabApi, type CandidateFunnel, type StrategyLabRunDetail } from '@/api/strategyLabApi';
import { parseApiClientError } from '@/utils/apiError';

type ResultTab = 'summary' | 'candidates' | 'funnel' | 'gateAnalysis' | 'riskAnalysis' | 'diagnostics' | 'experiment';

const TABS: { id: ResultTab; label: string }[] = [
  { id: 'summary', label: 'Summary' },
  { id: 'candidates', label: 'Candidates' },
  { id: 'funnel', label: 'Funnel' },
  { id: 'gateAnalysis', label: 'Gate Analysis' },
  { id: 'riskAnalysis', label: 'Risk Analysis' },
  { id: 'diagnostics', label: 'Diagnostics' },
  { id: 'experiment', label: 'Experiment Details' },
];

const ACTIVE_STATUSES = new Set([
  'Created',
  'PreparingData',
  'Running',
  'CheckingCoverage',
  'ImportingCandles',
  'VerifyingCoverage',
  'PreparingStrategy',
  'Evaluating',
  'SimulatingOutcomes',
]);

function pct(numerator: number, denominator: number) {
  if (!denominator) return '—';
  return `${formatNumber((numerator / denominator) * 100)}%`;
}

function FunnelStage({ label, count, prior }: { label: string; count: number; prior?: number }) {
  return (
    <div className="rounded-lg border border-slate-800 px-3 py-2">
      <div className="text-xs uppercase tracking-wide text-slate-500">{label}</div>
      <div className="text-lg font-semibold text-slate-100">{formatNumber(count)}</div>
      {prior != null ? <div className="text-xs text-slate-400">of prior: {pct(count, prior)}</div> : null}
    </div>
  );
}

function BreakoutFunnel({ funnel }: { funnel: CandidateFunnel }) {
  const candles = funnel.candlesEvaluated ?? 0;
  const swings = funnel.confirmedSwings ?? ((funnel.confirmedSwingHighs ?? 0) + (funnel.confirmedSwingLows ?? 0));
  const breakoutChecks = funnel.breakoutChecks
    ?? ((funnel.bullishBreakoutChecks ?? 0) + (funnel.bearishBreakoutChecks ?? 0));
  const breakouts = funnel.breakoutsDetected ?? ((funnel.bullishBreakoutsDetected ?? 0) + (funnel.bearishBreakoutsDetected ?? 0));
  const retestChecks = funnel.retestChecks ?? 0;
  const retests = funnel.retestsDetected ?? funnel.validRetests ?? 0;
  const confirmationChecks = funnel.confirmationChecks ?? 0;
  const confirmations = funnel.confirmationsPassed ?? 0;
  const candidates = funnel.rawCandidates ?? 0;
  const simValid = funnel.simulationValidCandidates ?? 0;
  const closed = funnel.closedRawTrades ?? 0;

  return (
    <div className="space-y-4">
      <div className="grid gap-3 md:grid-cols-4">
        <FunnelStage label="Candles Evaluated" count={candles} />
        <FunnelStage label="Confirmed Swings" count={swings} prior={candles} />
        <FunnelStage label="Breakouts Detected" count={breakouts} prior={swings} />
        <FunnelStage label="Retests Detected" count={retests} prior={breakouts} />
        <FunnelStage label="Confirmations Passed" count={confirmations} prior={retests} />
        <FunnelStage label="Raw Candidates" count={candidates} prior={confirmations} />
        <FunnelStage label="Valid Simulations" count={simValid} prior={candidates} />
        <FunnelStage label="Closed Trades" count={closed} prior={simValid} />
      </div>
      <div>
        <h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Diagnostic activity (checks)</h4>
        <div className="grid gap-3 md:grid-cols-4">
          <FunnelStage label="Breakout Checks" count={breakoutChecks} />
          <FunnelStage label="Retest Checks" count={retestChecks} />
          <FunnelStage label="Confirmation Checks" count={confirmationChecks} />
        </div>
      </div>
    </div>
  );
}

function LiquidityFunnel({ funnel }: { funnel: CandidateFunnel }) {
  const candles = funnel.candlesEvaluated ?? 0;
  const levelsCreated = funnel.liquidityLevelsCreated
    ?? funnel.confirmedSwings
    ?? ((funnel.confirmedSwingHighs ?? 0) + (funnel.confirmedSwingLows ?? 0));
  const levels = funnel.liquidityLevels
    ?? ((funnel.activeBuySideLiquidityLevels ?? 0) + (funnel.activeSellSideLiquidityLevels ?? 0));
  const sweepChecks = funnel.sweepChecks
    ?? ((funnel.buySideSweepChecks ?? 0) + (funnel.sellSideSweepChecks ?? 0));
  const sweeps = funnel.sweepsDetected ?? ((funnel.buySideSweepsDetected ?? 0) + (funnel.sellSideSweepsDetected ?? 0));
  const reclaims = funnel.reclaimsDetected ?? ((funnel.sameCandleReclaims ?? 0) + (funnel.delayedReclaims ?? 0));
  const candidates = funnel.rawCandidates ?? 0;
  const simValid = funnel.simulationValidCandidates ?? 0;
  const closed = funnel.closedRawTrades ?? 0;

  return (
    <div className="space-y-4">
      <div className="grid gap-3 md:grid-cols-4">
        <FunnelStage label="Candles Evaluated" count={candles} />
        <FunnelStage label="Liquidity Levels Created" count={levelsCreated} prior={candles} />
        <FunnelStage label="Active Level Checks" count={levels} prior={levelsCreated} />
        <FunnelStage label="Unique Sweeps Detected" count={sweeps} prior={levels} />
        <FunnelStage label="Reclaims Detected" count={reclaims} prior={sweeps} />
        <FunnelStage label="Raw Candidates" count={candidates} prior={reclaims} />
        <FunnelStage label="Valid Simulations" count={simValid} prior={candidates} />
        <FunnelStage label="Closed Trades" count={closed} prior={simValid} />
      </div>
      <div>
        <h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Diagnostic activity (checks)</h4>
        <div className="grid gap-3 md:grid-cols-4">
          <FunnelStage label="Sweep Checks" count={sweepChecks} />
          <FunnelStage label="Unique Sweeps Detected" count={sweeps} />
          <FunnelStage label="Same-Candle Reclaims" count={funnel.sameCandleReclaims ?? 0} />
          <FunnelStage label="Delayed Reclaims" count={funnel.delayedReclaims ?? 0} />
        </div>
      </div>
    </div>
  );
}

export function StrategyLabRunDetailPage() {
  const { runId = '' } = useParams();
  const navigate = useNavigate();
  const [detail, setDetail] = useState<StrategyLabRunDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<ResultTab>('summary');

  const load = useCallback(async () => {
    try {
      const res = await strategyLabApi.getRunDetail(Number(runId));
      setDetail(res ?? null);
      setError(null);
    } catch (err) {
      setError(parseApiClientError(err).message);
    } finally {
      setLoading(false);
    }
  }, [runId]);

  useEffect(() => {
    load();
    const timer = setInterval(() => {
      if (detail?.run.status && ACTIVE_STATUSES.has(detail.run.status)) {
        load();
      }
    }, 3000);
    return () => clearInterval(timer);
  }, [load, detail?.run.status]);

  const handleRerun = async () => {
    const config = await strategyLabApi.getRerunConfig(Number(runId));
    if (!config) return;
    const created = await strategyLabApi.createRun(config);
    if (created.id) navigate(`/strategy-lab/runs/${created.id}`);
  };

  if (loading) return <LoadingState />;
  if (error || !detail) return <ErrorState message={error ?? 'Run not found.'} onRetry={load} />;

  const { run, summary, funnel, gatedComparison, warnings, coverageDiagnostics, zeroCandidateExplanation, diagnosticEvents } = detail;
  const isLiquidity = (funnel.strategyFamily ?? '').includes('Liquidity')
    || run.strategyCode.includes('LIQUIDITY_SWEEP');

  return (
    <div>
      <div className="mb-6 flex items-start justify-between gap-4">
        <PageHeader title={run.name} description={run.experimentFingerprint} />
        <div className="flex gap-2">
          <button type="button" onClick={handleRerun} className="rounded-lg border border-slate-700 px-3 py-1.5 text-sm">Re-run Exactly</button>
          <Link to="/strategy-lab" className="rounded-lg border border-slate-700 px-3 py-1.5 text-sm">New Run</Link>
        </div>
      </div>

      <SimulationBanner message="Raw strategy outcomes are preserved even when confidence or risk would reject candidates." />

      {run.status === 'Failed' ? (
        <div className="mb-4 rounded-lg border border-rose-800 bg-rose-950/40 px-4 py-3 text-sm text-rose-100">
          <div className="font-medium">Strategy Laboratory run failed</div>
          <div>{run.errorMessage ?? 'Unknown failure.'}</div>
          {coverageDiagnostics?.importError ? <div className="mt-1">Import: {coverageDiagnostics.importError}</div> : null}
        </div>
      ) : null}

      {run.strategyVersionChanged ? (
        <div className="mb-4 rounded-lg border border-amber-700 bg-amber-950/40 px-4 py-3 text-sm text-amber-100">
          This experiment used strategy version {run.strategyVersion}. Current version is {run.currentStrategyVersion}.
        </div>
      ) : null}

      <div className="mb-4 flex flex-wrap items-center gap-2">
        <Badge tone={run.status === 'Completed' ? 'success' : run.status === 'Failed' ? 'warning' : 'info'}>{run.status}</Badge>
        <Badge tone="neutral">{run.executionMode}</Badge>
        <span className="text-sm text-slate-400">{run.currentStage ?? '—'} ({formatNumber(run.percentComplete)}%)</span>
      </div>

      {warnings.length > 0 ? (
        <div className="mb-4 rounded-lg border border-amber-800 bg-amber-950/30 px-4 py-3 text-sm">
          {warnings.map((w) => <div key={w}>{w}</div>)}
        </div>
      ) : null}

      {zeroCandidateExplanation && summary.rawCandidates === 0 ? (
        <div className="mb-4 rounded-lg border border-sky-800 bg-sky-950/30 px-4 py-3 text-sm text-sky-100">
          <div className="font-medium">Why no candidates?</div>
          <div>Primary blocker: {zeroCandidateExplanation.primaryBlocker ?? zeroCandidateExplanation.classification}</div>
          {zeroCandidateExplanation.details ? <div className="mt-1">{zeroCandidateExplanation.details}</div> : null}
          {zeroCandidateExplanation.suggestedNextAction ? (
            <div className="mt-1 text-sky-200">Suggested next action: {zeroCandidateExplanation.suggestedNextAction}</div>
          ) : null}
        </div>
      ) : null}

      <div className="mb-4 flex flex-wrap gap-2 border-b border-slate-800 pb-3">
        {TABS.map((tab) => (
          <button
            key={tab.id}
            type="button"
            onClick={() => setActiveTab(tab.id)}
            className={`rounded-lg px-3 py-1.5 text-sm ${activeTab === tab.id ? 'bg-slate-700 text-white' : 'text-slate-300'}`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {activeTab === 'summary' ? (
        <div className="space-y-4">
          <KeyValueGrid
            items={[
              { label: 'Strategy', value: `${run.strategyCode} v${run.strategyVersion}` },
              { label: 'Symbol', value: `${run.symbol} ${run.timeframe}` },
              { label: 'Date Range', value: `${formatDate(run.fromUtc)} → ${formatDate(run.toUtc)}` },
              { label: 'Candles Loaded', value: String(funnel.candlesLoaded ?? '—') },
              { label: 'Warmup Candles', value: String(funnel.warmupCandlesLoaded ?? '—') },
              { label: 'Test Range Candles', value: String(funnel.testRangeCandles ?? '—') },
              { label: 'Raw Candidates', value: String(summary.rawCandidates) },
              { label: 'Closed Raw Trades', value: String(summary.rawClosedTrades) },
              { label: 'Win Rate', value: `${formatNumber(summary.winRate)}%` },
              { label: summary.netPnlLabel ?? 'Independent Setup PnL', value: formatNumber(summary.netPnl) },
              {
                label: summary.pnlPercentLabel ?? 'Independent Setup PnL % of Initial Balance',
                value: `${formatNumber(summary.pnlPercent)}%`,
              },
              { label: 'Profit Factor', value: formatNumber(summary.profitFactor) },
              {
                label: summary.maxDrawdownLabel ?? 'Independent Setup Sequence Drawdown %',
                value: `${formatNumber(summary.maxDrawdownPercent)}%`,
              },
              {
                label: 'Portfolio Metrics',
                value: summary.portfolioMetricsAvailable
                  ? 'Available'
                  : (summary.portfolioMetricsNote ?? 'Portfolio metrics unavailable'),
              },
              { label: 'Opportunity Rate', value: `${formatNumber(summary.opportunity.candidatesPer1000Candles)} / 1,000 candles` },
              { label: 'Evidence Quality', value: `${summary.evidenceQualityLabel} (${summary.rawClosedTrades} closed trades)` },
            ]}
          />
          {(summary.metricWarnings?.length ?? 0) > 0 ? (
            <div className="rounded-lg border border-amber-800 bg-amber-950/30 px-4 py-3 text-sm text-amber-100">
              <div className="mb-1 font-medium">Metric warnings</div>
              {summary.metricWarnings!.map((w) => <div key={w}>{w}</div>)}
            </div>
          ) : null}
          {gatedComparison ? (
            <div className="rounded-lg border border-slate-800 p-4">
              <h3 className="mb-2 text-sm font-semibold text-slate-200">Raw vs Gated Comparison</h3>
              {gatedComparison.interpretations.map((line) => <p key={line} className="text-sm text-slate-300">{line}</p>)}
              <div className="mt-3 grid gap-3 md:grid-cols-3">
                <ComparisonCard title="Raw" data={gatedComparison.raw} />
                <ComparisonCard title="Confidence Approved" data={gatedComparison.confidenceApproved} />
                <ComparisonCard title="Confidence Rejected" data={gatedComparison.confidenceRejected} />
                <ComparisonCard title="Risk Approved" data={gatedComparison.riskApproved} />
                <ComparisonCard title="Risk Rejected" data={gatedComparison.riskRejected} />
                <ComparisonCard title="Full Pipeline" data={gatedComparison.fullPipeline} />
              </div>
            </div>
          ) : null}
        </div>
      ) : null}

      {activeTab === 'candidates' ? (
        <StrategyLabCandidateGrid runId={Number(runId)} />
      ) : null}

      {activeTab === 'funnel' ? (
        <div className="space-y-4">
          {funnel.primaryBlocker ? (
            <div className="rounded-lg border border-amber-800 bg-amber-950/20 px-4 py-3 text-sm">
              <div className="font-medium text-amber-100">Top blocker: {funnel.primaryBlocker}</div>
              {funnel.primaryBlockerDetails ? <div className="text-slate-300">{funnel.primaryBlockerDetails}</div> : null}
            </div>
          ) : null}
          {isLiquidity
            && ((funnel.sweepChecks ?? 0) === 0)
            && ((funnel.sweepsDetected ?? 0) > 0) ? (
            <div className="rounded-lg border border-amber-800 bg-amber-950/20 px-4 py-3 text-sm text-amber-100">
              Sweep Checks were not recorded for this legacy run. Unique Sweeps Detected counts unique
              wick-through events (not reclaim candidates). Re-run the experiment to capture Sweep Checks separately.
            </div>
          ) : null}
          {isLiquidity ? <LiquidityFunnel funnel={funnel} /> : <BreakoutFunnel funnel={funnel} />}
          <KeyValueGrid
            items={[
              { label: 'Detected In Memory', value: String(funnel.candidatesDetectedInMemory ?? 0) },
              { label: 'Duplicate Suppressed', value: String(funnel.candidatesRejectedAsDuplicate ?? 0) },
              { label: 'Simulation Invalid', value: String(funnel.candidatesSimulationInvalid ?? 0) },
              { label: 'Persisted', value: String(funnel.candidatesPersisted ?? funnel.rawCandidates) },
              { label: 'Classification', value: funnel.zeroCandidateClassification ?? '—' },
            ]}
          />
        </div>
      ) : null}

      {activeTab === 'gateAnalysis' ? (
        <StrategyLabGateAnalysisPanel runId={Number(runId)} />
      ) : null}

      {activeTab === 'riskAnalysis' ? (
        <StrategyLabRiskAnalysisPanel
          runId={Number(runId)}
          riskOnlyShadowPortfolio={detail.riskOnlyShadowPortfolio}
          fullPipelineShadowPortfolio={detail.fullPipelineShadowPortfolio}
          portfolioPathDivergence={detail.portfolioPathDivergence}
          pathDiagnostics={detail.pathDiagnostics}
          riskPathAssessmentVersion={detail.riskPathAssessmentVersion}
        />
      ) : null}

      {activeTab === 'diagnostics' ? (
        <div className="space-y-4">
          {coverageDiagnostics ? (
            <KeyValueGrid
              items={[
                { label: 'Existing Candles', value: String(coverageDiagnostics.existingCandleCount) },
                { label: 'Missing Estimate', value: String(coverageDiagnostics.missingCandleCountEstimate) },
                { label: 'Auto Import Attempted', value: coverageDiagnostics.autoImportAttempted ? 'Yes' : 'No' },
                { label: 'Imported Candles', value: String(coverageDiagnostics.importedCandleCount) },
                { label: 'Final Coverage', value: coverageDiagnostics.finalCoverageStatus },
                { label: 'Import Error', value: coverageDiagnostics.importError ?? '—' },
              ]}
            />
          ) : null}
          <StrategyLabDiagnosticChart
            exchangeId={run.exchangeId}
            symbolId={run.symbolId}
            timeframe={run.timeframe}
            events={diagnosticEvents ?? []}
          />
        </div>
      ) : null}

      {activeTab === 'experiment' ? (
        <KeyValueGrid
          items={[
            { label: 'Experiment Fingerprint', value: run.experimentFingerprint },
            { label: 'Strategy Version', value: run.strategyVersion },
            { label: 'Execution Mode', value: run.executionMode },
            { label: 'Initial Balance', value: formatNumber(run.initialBalance) },
            { label: 'Parameters', value: run.parametersJson },
            { label: 'Created', value: formatDate(run.createdAtUtc) },
            { label: 'Completed', value: run.completedAtUtc ? formatDate(run.completedAtUtc) : '—' },
            { label: 'Error', value: run.errorMessage ?? '—' },
          ]}
        />
      ) : null}
    </div>
  );
}

function ComparisonCard({
  title,
  data,
}: {
  title: string;
  data: {
    candidateCount: number;
    closedTradeCount: number;
    netPnl: number;
    profitFactor: number;
    winRate?: number;
    maxDrawdownPercent?: number;
    averageConfidence?: number | null;
    averageRiskScore?: number | null;
  };
}) {
  return (
    <div className="rounded-lg border border-slate-800 p-3 text-sm">
      <div className="font-medium text-slate-200">{title}</div>
      <div className="text-slate-400">Candidates: {data.candidateCount}</div>
      <div className="text-slate-400">Closed: {data.closedTradeCount}</div>
      <div className="text-slate-400">Win rate: {data.winRate != null ? `${formatNumber(data.winRate)}%` : '—'}</div>
      <div className="text-slate-400">Net PnL: {formatNumber(data.netPnl)}</div>
      <div className="text-slate-400">PF: {formatNumber(data.profitFactor)}</div>
      <div className="text-slate-400">Max DD: {data.maxDrawdownPercent != null ? `${formatNumber(data.maxDrawdownPercent)}%` : '—'}</div>
      <div className="text-slate-400">Avg confidence: {data.averageConfidence != null ? formatNumber(data.averageConfidence) : '—'}</div>
      <div className="text-slate-400">Avg risk: {data.averageRiskScore != null ? formatNumber(data.averageRiskScore) : '—'}</div>
    </div>
  );
}
