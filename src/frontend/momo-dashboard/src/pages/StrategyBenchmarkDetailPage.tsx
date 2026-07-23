import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { SimulationBanner } from '@/components/common/SimulationBanner';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { MetricCard } from '@/components/common/MetricCard';
import { StatusPill } from '@/components/common/StatusPill';
import { DataTable } from '@/components/common/DataTable';
import { TabPanel } from '@/components/common/TabPanel';
import { formatDate, formatNumber } from '@/components/common/utils';
import { usePolling } from '@/hooks/useSessionPolling';
import { useRole } from '@/hooks/useRole';
import { parseApiClientError } from '@/utils/apiError';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import {
  strategyBenchmarksApi,
  type StrategyBenchmarkDiagnostics,
  type StrategyBenchmarkProgress,
  type StrategyBenchmarkReport,
  type StrategyBenchmarkRun,
  type StrategyBenchmarkRunItem,
} from '@/api/strategyBenchmarksApi';

const ACTIVE_STATUSES = ['Pending', 'ImportingData', 'CheckingDataQuality', 'RecalculatingIndicators', 'RunningBacktests', 'GeneratingReport'];

export function StrategyBenchmarkDetailPage() {
  const { id } = useParams();
  const runId = Number(id);
  const { canEdit } = useRole();
  const [tab, setTab] = useState('overview');
  const [run, setRun] = useState<StrategyBenchmarkRun | null>(null);
  const [progress, setProgress] = useState<StrategyBenchmarkProgress | null>(null);
  const [report, setReport] = useState<StrategyBenchmarkReport | null>(null);
  const [runItems, setRunItems] = useState<StrategyBenchmarkRunItem[]>([]);
  const [diagnostics, setDiagnostics] = useState<StrategyBenchmarkDiagnostics | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [selectedNoTradeRow, setSelectedNoTradeRow] = useState<StrategyBenchmarkReport['noTradeAnalysis'][number] | null>(null);
  const [selectedStrategyCode, setSelectedStrategyCode] = useState<string | null>(null);

  const status = progress?.status ?? run?.status ?? '';
  const isActive = ACTIVE_STATUSES.includes(status);
  const heartbeatAgeSeconds = progress?.lastHeartbeatAtUtc
    ? Math.max(0, Math.floor((Date.now() - new Date(progress.lastHeartbeatAtUtc).getTime()) / 1000))
    : null;
  const showStaleHeartbeatWarning = isActive && heartbeatAgeSeconds != null && heartbeatAgeSeconds > 15;

  const refresh = useCallback(async () => {
    if (!runId) return;
    try {
      const [nextRun, nextProgress, nextItems, nextDiagnostics] = await Promise.all([
        strategyBenchmarksApi.getById(runId),
        strategyBenchmarksApi.getProgress(runId),
        strategyBenchmarksApi.getRunItems(runId),
        strategyBenchmarksApi.getDiagnostics(runId),
      ]);
      setRun(nextRun);
      setProgress(nextProgress);
      setRunItems(nextItems);
      setDiagnostics(nextDiagnostics);

      if (['Completed', 'CompletedWithWarnings', 'Failed', 'Cancelled', 'Stalled'].includes(nextProgress.status)) {
        const nextReport = await strategyBenchmarksApi.getReport(runId);
        setReport(nextReport);
      }

      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load benchmark.');
    } finally {
      setLoading(false);
    }
  }, [runId]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  usePolling(() => void refresh(), 2000, Boolean(runId) && isActive);

  async function runAction(action: 'cancel' | 'resume' | 'restart' | 'retryFailed' | 'markStalled') {
    if (!canEdit || !runId) return;
    if (action === 'cancel' && !window.confirm('Cancel this benchmark? Completed run results will be kept.')) return;
    if (action === 'restart' && !window.confirm('Restarting will rerun this benchmark from the beginning. Existing results may be replaced. Continue?')) return;

    setActionError(null);
    try {
      if (action === 'cancel') await strategyBenchmarksApi.cancel(runId);
      if (action === 'resume') await strategyBenchmarksApi.resume(runId);
      if (action === 'restart') await strategyBenchmarksApi.restart(runId);
      if (action === 'retryFailed') await strategyBenchmarksApi.retryFailed(runId);
      if (action === 'markStalled') await strategyBenchmarksApi.markStalledFailed(runId);
      await refresh();
    } catch (err) {
      setActionError(parseApiClientError(err).message);
    }
  }

  if (!runId) return <ErrorState message="Invalid benchmark id." />;
  if (loading && !run) return <LoadingState />;
  if (error && !run) return <ErrorState message={error} onRetry={() => void refresh()} />;
  if (!run) return <ErrorState message="Benchmark run was not found." />;

  const percent = progress?.percentComplete ?? run.percentComplete;
  const failedCount = progress?.failedRuns ?? runItems.filter((item) => item.status === 'Failed').length;

  return (
    <div>
      <PageHeader title={run.name} description="Strategy benchmark progress, run items, and ranking report." />
      <Link to="/strategy-benchmarks" className="mb-4 inline-block text-sm text-slate-400 underline">
        Back to strategy benchmarks
      </Link>
      <SimulationBanner message="Benchmark results are simulated from public historical data. No real orders are placed." />
      <ApiErrorAlert message={actionError} />

      {canEdit ? (
        <div className="mb-4 flex flex-wrap gap-2">
          {isActive ? (
            <button type="button" onClick={() => void runAction('cancel')} className="rounded-lg border border-slate-600 px-3 py-1.5 text-xs text-slate-200">
              Cancel
            </button>
          ) : null}
          {['Failed', 'Stalled', 'Cancelled'].includes(status) ? (
            <>
              <button type="button" onClick={() => void runAction('resume')} className="rounded-lg border border-slate-600 px-3 py-1.5 text-xs text-slate-200">
                Resume
              </button>
              <button type="button" onClick={() => void runAction('restart')} className="rounded-lg border border-slate-600 px-3 py-1.5 text-xs text-slate-200">
                Restart
              </button>
            </>
          ) : null}
          {failedCount > 0 ? (
            <button type="button" onClick={() => void runAction('retryFailed')} className="rounded-lg border border-slate-600 px-3 py-1.5 text-xs text-slate-200">
              Retry Failed
            </button>
          ) : null}
          {isActive ? (
            <button type="button" onClick={() => void runAction('markStalled')} className="rounded-lg border border-amber-700 px-3 py-1.5 text-xs text-amber-100">
              Mark Stalled Failed
            </button>
          ) : null}
          <button type="button" onClick={() => void refresh()} className="rounded-lg border border-slate-600 px-3 py-1.5 text-xs text-slate-200">
            Refresh
          </button>
          <button type="button" onClick={() => setTab('diagnostics')} className="rounded-lg border border-slate-600 px-3 py-1.5 text-xs text-slate-200">
            View Diagnostics
          </button>
          <button type="button" onClick={() => void refresh()} className="rounded-lg border border-slate-600 px-3 py-1.5 text-xs text-slate-200">
            Refresh Diagnostics
          </button>
        </div>
      ) : null}

      <div className="mb-4 grid gap-3 md:grid-cols-3 xl:grid-cols-6">
        <MetricCard label="Status" value={status} />
        <MetricCard label="Overall Progress" value={`${formatNumber(percent)}%`} />
        <MetricCard label="Data Prep" value={`${formatNumber(progress?.dataPreparationPercent ?? 0)}%`} />
        <MetricCard label="Backtests" value={`${formatNumber(progress?.backtestPercent ?? 0)}%`} />
        <MetricCard label="Completed Runs" value={`${progress?.completedRuns ?? run.completedRuns}/${progress?.totalRuns ?? run.totalRuns}`} />
        <MetricCard label="Failed Runs" value={String(failedCount)} />
      </div>

      <div className="mb-4 grid gap-3 md:grid-cols-3 xl:grid-cols-6">
        <MetricCard label="Stage" value={progress?.currentStage ?? run.currentStage ?? '—'} />
        <MetricCard label="Current Symbol" value={progress?.currentSymbol ?? run.currentSymbol ?? '—'} />
        <MetricCard label="Current Timeframe" value={progress?.currentTimeframe ?? run.currentTimeframe ?? '—'} />
        <MetricCard label="Current Strategy" value={progress?.currentStrategy ?? run.currentStrategy ?? '—'} />
        <MetricCard label="Last Heartbeat" value={formatDate(progress?.lastHeartbeatAtUtc)} />
        <MetricCard label="Pending Runs" value={String(progress?.pendingRuns ?? 0)} />
      </div>

      {(progress?.currentStage === 'ImportingData' || (progress?.totalImportChunks ?? 0) > 0) ? (
        <div className="mb-4 grid gap-3 md:grid-cols-3 xl:grid-cols-6">
          <MetricCard
            label="Import Chunk"
            value={
              progress?.currentChunkFromUtc && progress?.currentChunkToUtc
                ? `${formatDate(progress.currentChunkFromUtc)} → ${formatDate(progress.currentChunkToUtc)}`
                : '—'
            }
          />
          <MetricCard label="Chunks" value={`${progress?.completedImportChunks ?? 0}/${progress?.totalImportChunks ?? 0}`} />
          <MetricCard label="Inserted Candles" value={String(progress?.insertedCandles ?? 0)} />
          <MetricCard label="Skipped Duplicates" value={String(progress?.skippedDuplicateCandles ?? 0)} />
        </div>
      ) : null}

      <div className="mb-4 rounded-xl border border-slate-800 bg-slate-950/50 p-4">
        <div className="mb-2 flex items-center justify-between text-sm text-slate-300">
          <span>Run progress</span>
          <StatusPill status={status} />
        </div>
        <div className="h-2 overflow-hidden rounded-full bg-slate-800">
          <div className="h-full bg-emerald-500 transition-all duration-500" style={{ width: `${Math.min(Number(percent), 100)}%` }} />
        </div>
        <p className="mt-2 text-sm text-slate-400">{progress?.message ?? run.message ?? '—'}</p>
        <p className="mt-2 text-xs text-slate-500">Backend logs are written to C:\momo_quants_logs</p>
        {showStaleHeartbeatWarning ? (
          <p className="mt-2 text-sm text-amber-200">No benchmark heartbeat for {heartbeatAgeSeconds} seconds.</p>
        ) : null}
        {run.errorMessage ? <p className="mt-2 text-sm text-rose-300">{run.errorMessage}</p> : null}
      </div>

      <TabPanel
        active={tab}
        onChange={setTab}
        tabs={[
          { id: 'overview', label: 'Overview' },
          { id: 'run-items', label: 'Run Items' },
          { id: 'report', label: 'Report' },
          { id: 'pipeline-funnel', label: 'Pipeline Funnel' },
          { id: 'candidate-trades', label: 'Candidate Trades' },
          { id: 'executed-trades', label: 'Executed Trades' },
          { id: 'rejected-candidates', label: 'Rejected Candidates' },
          { id: 'shadow-trades', label: 'Shadow Trades' },
          { id: 'rejection-quality', label: 'Rejection Quality' },
          { id: 'risk-calibration', label: 'Risk & Confidence Calibration' },
          { id: 'no-trade', label: 'NoTrade Analysis' },
          { id: 'risk-rejections', label: 'Risk Rejections' },
          { id: 'diagnostics', label: 'Diagnostics' },
        ]}
      >
        {tab === 'overview' ? (
          <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4 text-sm text-slate-300">
            <div>Symbols: {run.symbols.join(', ')}</div>
            <div>Timeframes: {run.timeframes.join(', ')}</div>
            <div>Strategies: {run.strategyIds.length}</div>
            <div>Warmup from: {formatDate(run.warmupFromUtc)}</div>
            <div>
              Benchmark: {formatDate(run.benchmarkFromUtc)} → {formatDate(run.benchmarkToUtc)}
            </div>
          </div>
        ) : null}

        {tab === 'run-items' ? (
          <DataTable
            columns={[
              { key: 'status', header: 'Status', render: (row) => <StatusPill status={row.status} /> },
              { key: 'strategy', header: 'Strategy', render: (row) => `${row.strategyName} (${row.strategyCode})` },
              { key: 'symbol', header: 'Symbol', render: (row) => row.symbol },
              { key: 'tf', header: 'TF', render: (row) => row.timeframe },
              { key: 'started', header: 'Started', render: (row) => formatDate(row.startedAtUtc) },
              { key: 'heartbeat', header: 'Last Heartbeat', render: (row) => formatDate(row.lastHeartbeatAtUtc) },
              { key: 'lastCandleTime', header: 'Last Candle', render: (row) => formatDate(row.lastProcessedCandleTimeUtc) },
              {
                key: 'candleProgress',
                header: 'Candle Progress',
                render: (row) =>
                  row.lastProcessedCandleIndex != null && row.totalCandles != null
                    ? `${row.lastProcessedCandleIndex}/${row.totalCandles}`
                    : '—',
              },
              { key: 'completed', header: 'Completed', render: (row) => formatDate(row.completedAtUtc) },
              { key: 'duration', header: 'Duration', render: (row) => (row.durationSeconds != null ? `${row.durationSeconds}s` : '—') },
              { key: 'backtest', header: 'BacktestRunId', render: (row) => (row.backtestRunId != null ? String(row.backtestRunId) : '—') },
              { key: 'error', header: 'Error', render: (row) => row.errorMessage ?? '—' },
            ]}
            rows={runItems}
          />
        ) : null}

        {tab === 'report' ? (
          report ? (
            <>
              <section className="mb-6">
                <h2 className="mb-3 text-sm font-medium text-slate-300">Summary</h2>
                <div className="grid gap-3 md:grid-cols-3">
                  <MetricCard label="Best Overall" value={report.summary.bestOverallStrategy ?? '—'} />
                  <MetricCard label="Completed Backtests" value={String(report.summary.completedRuns)} />
                  <MetricCard label="Failed / Skipped" value={String(report.summary.failedRuns)} />
                </div>
                {report.decisionRecommendations.length > 0 ? (
                  <ul className="mt-3 list-disc pl-5 text-sm text-amber-100">
                    {report.decisionRecommendations.map((item) => (
                      <li key={item}>{item}</li>
                    ))}
                  </ul>
                ) : null}
              </section>
              <section className="mb-6">
                <h2 className="mb-3 text-sm font-medium text-slate-300">Strategy Ranking</h2>
                <DataTable
                  columns={[
                    { key: 'rank', header: 'Rank', render: (row) => String(row.rank) },
                    { key: 'strategy', header: 'Strategy', render: (row) => `${row.strategyName} (${row.strategyCode})` },
                    { key: 'grade', header: 'Grade', render: (row) => <StatusPill status={row.grade} /> },
                    { key: 'score', header: 'Score', render: (row) => formatNumber(row.score) },
                    { key: 'trades', header: 'Trades', render: (row) => String(row.totalTrades) },
                    { key: 'pnl', header: 'Net PnL %', render: (row) => formatNumber(row.netPnlPercent) },
                    { key: 'dd', header: 'Max DD %', render: (row) => formatNumber(row.maxDrawdownPercent) },
                    { key: 'pf', header: 'Profit Factor', render: (row) => formatNumber(row.profitFactor) },
                    { key: 'wr', header: 'Win Rate %', render: (row) => formatNumber(row.winRatePercent) },
                    { key: 'reason', header: 'Result Reason', render: (row) => row.resultReason ?? '—' },
                    { key: 'candidate', header: 'Candidate Signals', render: (row) => String(row.candidateSignals ?? 0) },
                    { key: 'confidenceRejected', header: 'Confidence Rejected', render: (row) => String(row.confidenceRejected ?? 0) },
                    { key: 'riskRejections', header: 'Risk Rejections', render: (row) => String(row.riskRejections ?? 0) },
                    { key: 'shadowNetPnl', header: 'Shadow Net PnL %', render: (row) => formatNumber(row.shadowNetPnlPercent ?? 0) },
                    { key: 'falseReject', header: 'False Reject Rate %', render: (row) => formatNumber(row.falseRejectRatePercent ?? 0) },
                    { key: 'noTrade', header: 'NoTrade Count', render: (row) => String(row.noTradeCount ?? 0) },
                    { key: 'topNoTrade', header: 'Top NoTrade Reason', render: (row) => row.topNoTradeReason ?? '—' },
                    { key: 'pipeline', header: 'Pipeline', render: (row) => row.pipelineSummary ?? '—' },
                    {
                      key: 'details',
                      header: 'Details',
                      render: (row) => (
                        <button
                          type="button"
                          className="rounded border border-slate-600 px-2 py-1 text-xs text-slate-200"
                          onClick={() => setSelectedStrategyCode(row.strategyCode)}
                        >
                          View
                        </button>
                      ),
                    },
                  ]}
                  rows={report.strategyRanking}
                />
                {selectedStrategyCode ? (
                  <div className="mt-4 rounded-lg border border-slate-800 bg-slate-950/40 p-3 text-sm text-slate-300">
                    {(() => {
                      const ranking = report.strategyRanking.find((item) => item.strategyCode === selectedStrategyCode);
                      const noTradeRows = report.noTradeAnalysis.filter((item) => item.strategyCode === selectedStrategyCode);
                      const detailRows = report.strategyDetails.filter((item) => item.strategyCode === selectedStrategyCode);
                      const riskRows = report.riskRejections.filter((item) => item.strategyCode === selectedStrategyCode);
                      const averageWin = detailRows.length > 0 ? detailRows.reduce((sum, row) => sum + row.averageWin, 0) / detailRows.length : 0;
                      const averageLoss = detailRows.length > 0 ? detailRows.reduce((sum, row) => sum + row.averageLoss, 0) / detailRows.length : 0;
                      const worstTrade = detailRows.length > 0 ? Math.min(...detailRows.map((row) => row.largestLoss)) : 0;
                      const averageRewardRisk = detailRows.length > 0 ? detailRows.reduce((sum, row) => sum + row.averageRewardRisk, 0) / detailRows.length : 0;
                      if (!ranking) {
                        return null;
                      }

                      return (
                        <>
                          <div className="mb-2 flex items-center justify-between">
                            <p className="font-medium text-slate-100">
                              {ranking.strategyName} ({ranking.strategyCode})
                            </p>
                            <button
                              type="button"
                              className="rounded border border-slate-600 px-2 py-1 text-xs text-slate-200"
                              onClick={() => setSelectedStrategyCode(null)}
                            >
                              Close
                            </button>
                          </div>
                          <p className="mb-1">Result reason: {ranking.resultReason ?? '—'}</p>
                          <p className="mb-1">
                            Candidate signals: {ranking.candidateSignals} · Risk rejections: {ranking.riskRejections} · NoTrade count: {ranking.noTradeCount}
                          </p>
                          <p className="mb-2">Top NoTrade reason: {ranking.topNoTradeReason ?? '—'}</p>
                          <p className="mb-2">
                            Avg win: {formatNumber(averageWin)} · Avg loss: {formatNumber(averageLoss)} · Worst trade: {formatNumber(worstTrade)} · Avg R/R:{' '}
                            {formatNumber(averageRewardRisk)}
                          </p>
                          <p className="mb-1 font-medium text-slate-100">Per-symbol/timeframe results</p>
                          <ul className="mb-2 list-disc pl-5">
                            {detailRows.map((row) => (
                              <li key={`${row.symbol}-${row.timeframe}`}>
                                {row.symbol} / {row.timeframe}: trades {row.totalTrades}, PnL {formatNumber(row.netPnlPercent)}%, PF {formatNumber(row.profitFactor)}
                              </li>
                            ))}
                          </ul>
                          <p className="mb-1 font-medium text-slate-100">Risk rejection breakdown</p>
                          <ul className="mb-2 list-disc pl-5">
                            {riskRows.length > 0 ? (
                              riskRows.map((row) => (
                                <li key={`${row.symbol}-${row.executionTimeframe}`}>
                                  {row.symbol} / {row.executionTimeframe}: {row.riskRejections}/{row.totalCandidateSignals} ({formatNumber(row.rejectionPercent)}%) -{' '}
                                  {row.topRiskReason ?? '—'}
                                </li>
                              ))
                            ) : (
                              <li>No risk rejections recorded.</li>
                            )}
                          </ul>
                          <details>
                            <summary className="cursor-pointer text-slate-200">Raw JSON</summary>
                            <pre className="mt-2 max-h-64 overflow-auto rounded bg-slate-900 p-2 text-xs">
                              {JSON.stringify({ ranking, noTradeRows, detailRows, riskRows }, null, 2)}
                            </pre>
                          </details>
                        </>
                      );
                    })()}
                  </div>
                ) : null}
              </section>
            </>
          ) : (
            <p className="text-sm text-slate-400">Report will appear when the benchmark finishes.</p>
          )
        ) : null}

        {tab === 'no-trade' ? (
          report ? (
            <>
              <DataTable
              columns={[
                { key: 'strategy', header: 'Strategy', render: (row) => `${row.strategyName} (${row.strategyCode})` },
                { key: 'symbol', header: 'Symbol', render: (row) => row.symbol },
                { key: 'tf', header: 'Execution Timeframe', render: (row) => row.executionTimeframe },
                { key: 'eval', header: 'Evaluations', render: (row) => String(row.evaluations) },
                { key: 'nt', header: 'NoTrade Count', render: (row) => String(row.noTradeCount) },
                { key: 'candidate', header: 'Candidate Signals', render: (row) => String(row.candidateSignals) },
                { key: 'trades', header: 'Trades', render: (row) => String(row.trades) },
                { key: 'reason', header: 'Top NoTrade Reason', render: (row) => row.topNoTradeReason ?? '—' },
                { key: 'topNoTradeCount', header: 'Top Reason Count', render: (row) => String(row.topNoTradeReasonCount) },
                { key: 'missingData', header: 'Missing Data', render: (row) => String(row.missingDataCount) },
                { key: 'missingInd', header: 'Missing Indicators', render: (row) => String(row.missingIndicatorsCount) },
                { key: 'riskRej', header: 'Risk Rejections', render: (row) => String(row.riskRejections) },
                { key: 'topRiskReason', header: 'Top Risk Rejection Reason', render: (row) => row.topRiskRejectionReason ?? '—' },
                { key: 'resultReason', header: 'Result Reason', render: (row) => row.resultReason ?? '—' },
                { key: 'rec', header: 'Recommendation', render: (row) => row.recommendation },
                {
                  key: 'details',
                  header: 'Details',
                  render: (row) => (
                    <button
                      type="button"
                      className="rounded border border-slate-600 px-2 py-1 text-xs text-slate-200"
                      onClick={() => setSelectedNoTradeRow(row)}
                    >
                      View
                    </button>
                  ),
                },
              ]}
                rows={report.noTradeAnalysis}
              />
              {selectedNoTradeRow ? (
                <div className="mt-4 rounded-lg border border-slate-800 bg-slate-950/40 p-3 text-sm text-slate-300">
                  <div className="mb-2 flex items-center justify-between">
                    <p className="font-medium text-slate-100">
                      {selectedNoTradeRow.strategyName} ({selectedNoTradeRow.strategyCode}) · {selectedNoTradeRow.symbol} · {selectedNoTradeRow.executionTimeframe}
                    </p>
                    <button
                      type="button"
                      className="rounded border border-slate-600 px-2 py-1 text-xs text-slate-200"
                      onClick={() => setSelectedNoTradeRow(null)}
                    >
                      Close
                    </button>
                  </div>
                  <p className="mb-2">Recommendation: {selectedNoTradeRow.recommendation}</p>
                  {selectedNoTradeRow.whyZeroTradesAnalysis ? (
                    <p className="mb-2 rounded border border-amber-700/40 bg-amber-950/20 p-2 text-amber-100">{selectedNoTradeRow.whyZeroTradesAnalysis}</p>
                  ) : null}
                  {selectedNoTradeRow.pipelineSummary ? <p className="mb-2">Pipeline: {selectedNoTradeRow.pipelineSummary}</p> : null}
                  <p className="mb-2">Result reason: {selectedNoTradeRow.resultReason ?? '—'}</p>
                  <p className="mb-2">Funnel diagnostics:</p>
                  <ul className="mb-3 list-disc pl-5">
                    {selectedNoTradeRow.funnel.map((step) => (
                      <li key={step.stepName}>
                        {step.stepName}: pass {step.passedCount}, fail {step.failedCount}
                        {step.failReason ? ` (${step.failReason})` : ''}
                      </li>
                    ))}
                  </ul>
                  <p className="mb-2">Tuning suggestions:</p>
                  <ul className="list-disc pl-5">
                    {selectedNoTradeRow.tuningSuggestions.map((item) => (
                      <li key={item}>{item}</li>
                    ))}
                  </ul>
                </div>
              ) : null}
            </>
          ) : (
            <p className="text-sm text-slate-400">NoTrade analysis appears after report generation.</p>
          )
        ) : null}

        {tab === 'risk-rejections' ? (
          report ? (
            <DataTable
              columns={[
                { key: 'strategy', header: 'Strategy', render: (row) => `${row.strategyName} (${row.strategyCode})` },
                { key: 'symbol', header: 'Symbol', render: (row) => row.symbol },
                { key: 'tf', header: 'Timeframe', render: (row) => row.executionTimeframe },
                { key: 'candidates', header: 'Total Candidate Signals', render: (row) => String(row.totalCandidateSignals) },
                { key: 'riskRej', header: 'Risk Rejections', render: (row) => String(row.riskRejections) },
                { key: 'topReason', header: 'Top Risk Reason', render: (row) => row.topRiskReason ?? '—' },
                { key: 'rejPct', header: 'Rejection %', render: (row) => `${formatNumber(row.rejectionPercent)}%` },
                { key: 'rec', header: 'Recommendation', render: (row) => row.recommendation },
              ]}
              rows={report.riskRejections}
            />
          ) : (
            <p className="text-sm text-slate-400">Risk rejection analysis appears after report generation.</p>
          )
        ) : null}

        {tab === 'pipeline-funnel' ? (
          report ? (
            <DataTable
              columns={[
                { key: 'strategy', header: 'Strategy', render: (row) => `${row.strategyName} (${row.strategyCode})` },
                { key: 'symbol', header: 'Symbol', render: (row) => row.symbol },
                { key: 'tf', header: 'Timeframe', render: (row) => row.timeframe },
                { key: 'eval', header: 'Evaluations', render: (row) => String(row.evaluations) },
                { key: 'candidate', header: 'Candidate Signals', render: (row) => String(row.candidateSignals) },
                { key: 'confOk', header: 'Confidence Approved', render: (row) => String(row.confidenceApproved) },
                { key: 'confRej', header: 'Confidence Rejected', render: (row) => String(row.confidenceRejected) },
                { key: 'riskOk', header: 'Risk Approved', render: (row) => String(row.riskApproved) },
                { key: 'riskRej', header: 'Risk Rejected', render: (row) => String(row.riskRejected) },
                { key: 'exec', header: 'Executed Trades', render: (row) => String(row.executedTrades) },
                { key: 'shadow', header: 'Shadow Trades', render: (row) => String(row.shadowTrades) },
                { key: 'pnl', header: 'Final Net PnL', render: (row) => formatNumber(row.finalNetPnl) },
                { key: 'shadowPnl', header: 'Shadow Net PnL', render: (row) => formatNumber(row.shadowNetPnl) },
              ]}
              rows={report.pipelineFunnel}
            />
          ) : (
            <p className="text-sm text-slate-400">Pipeline funnel appears after report generation.</p>
          )
        ) : null}

        {tab === 'candidate-trades' ? (
          report ? (
            <DataTable
              columns={[
                { key: 'time', header: 'Time', render: (row) => formatDate(row.signalTimeUtc) },
                { key: 'strategy', header: 'Strategy', render: (row) => row.strategyCode },
                { key: 'symbol', header: 'Symbol', render: (row) => row.symbol },
                { key: 'direction', header: 'Direction', render: (row) => row.direction },
                { key: 'entry', header: 'Entry', render: (row) => formatNumber(row.entryPrice) },
                { key: 'stop', header: 'Stop', render: (row) => formatNumber(row.stopLoss) },
                { key: 'target', header: 'Target', render: (row) => formatNumber(row.takeProfit) },
                { key: 'confidence', header: 'Confidence', render: (row) => formatNumber(row.combinedConfidence) },
                { key: 'risk', header: 'Risk %', render: (row) => formatNumber(row.riskPercent) },
                { key: 'lev', header: 'Leverage', render: (row) => formatNumber(row.leverage) },
                { key: 'margin', header: 'Margin', render: (row) => formatNumber(row.marginUsed) },
                { key: 'notional', header: 'Notional', render: (row) => formatNumber(row.notionalValue) },
                { key: 'decision', header: 'Final Decision', render: (row) => row.finalDecision },
                { key: 'reason', header: 'Reason', render: (row) => row.finalDecisionReason },
              ]}
              rows={report.candidateTrades}
            />
          ) : (
            <p className="text-sm text-slate-400">Candidate trades appear after report generation.</p>
          )
        ) : null}

        {tab === 'executed-trades' ? (
          report ? (
            <DataTable
              columns={[
                { key: 'entryTime', header: 'Entry Time', render: (row) => formatDate(row.entryTimeUtc) },
                { key: 'exitTime', header: 'Exit Time', render: (row) => formatDate(row.exitTimeUtc) },
                { key: 'strategy', header: 'Strategy', render: (row) => row.strategyCode },
                { key: 'symbol', header: 'Symbol', render: (row) => row.symbol },
                { key: 'direction', header: 'Direction', render: (row) => row.direction },
                { key: 'lev', header: 'Leverage', render: (row) => formatNumber(row.leverage) },
                { key: 'margin', header: 'Margin', render: (row) => formatNumber(row.marginUsed) },
                { key: 'notional', header: 'Notional', render: (row) => formatNumber(row.notionalValue) },
                { key: 'entry', header: 'Entry', render: (row) => formatNumber(row.entryPrice) },
                { key: 'exit', header: 'Exit', render: (row) => formatNumber(row.exitPrice ?? 0) },
                { key: 'net', header: 'Net PnL', render: (row) => formatNumber(row.netPnl) },
                { key: 'netPct', header: 'Net PnL %', render: (row) => formatNumber(row.netPnlPercent) },
                { key: 'exitReason', header: 'Exit Reason', render: (row) => row.exitReason ?? '—' },
              ]}
              rows={report.executedTrades}
            />
          ) : (
            <p className="text-sm text-slate-400">Executed trades appear after report generation.</p>
          )
        ) : null}

        {tab === 'rejected-candidates' ? (
          report ? (
            <DataTable
              columns={[
                { key: 'time', header: 'Time', render: (row) => formatDate(row.signalTimeUtc) },
                { key: 'strategy', header: 'Strategy', render: (row) => row.strategyCode },
                { key: 'symbol', header: 'Symbol', render: (row) => row.symbol },
                { key: 'direction', header: 'Direction', render: (row) => row.direction },
                { key: 'decision', header: 'Rejected By', render: (row) => row.finalDecision },
                { key: 'confidence', header: 'Confidence', render: (row) => formatNumber(row.combinedConfidence) },
                { key: 'risk', header: 'Risk %', render: (row) => formatNumber(row.riskPercent) },
                { key: 'reason', header: 'Reason', render: (row) => row.finalDecisionReason },
              ]}
              rows={report.rejectedCandidates}
            />
          ) : (
            <p className="text-sm text-slate-400">Rejected candidates appear after report generation.</p>
          )
        ) : null}

        {tab === 'shadow-trades' ? (
          report ? (
            <DataTable
              columns={[
                { key: 'time', header: 'Signal Time', render: (row) => formatDate(row.signalTimeUtc) },
                { key: 'strategy', header: 'Strategy', render: (row) => row.strategyCode },
                { key: 'symbol', header: 'Symbol', render: (row) => row.symbol },
                { key: 'direction', header: 'Direction', render: (row) => row.direction },
                { key: 'rejBy', header: 'Rejected By', render: (row) => row.rejectedBy },
                { key: 'outcome', header: 'Outcome', render: (row) => row.outcomeClassification },
                { key: 'exit', header: 'Shadow Exit', render: (row) => row.shadowExitReason ?? '—' },
                { key: 'shadowNet', header: 'Shadow Net PnL', render: (row) => formatNumber(row.shadowNetPnl) },
                { key: 'mfe', header: 'MFE', render: (row) => formatNumber(row.maxFavorableExcursion) },
                { key: 'mae', header: 'MAE', render: (row) => formatNumber(row.maxAdverseExcursion) },
                { key: 'duration', header: 'Duration', render: (row) => String(row.durationCandles) },
              ]}
              rows={report.shadowTrades}
            />
          ) : (
            <p className="text-sm text-slate-400">Shadow trades appear after report generation.</p>
          )
        ) : null}

        {tab === 'rejection-quality' ? (
          report ? (
            <DataTable
              columns={[
                { key: 'strategy', header: 'Strategy', render: (row) => row.strategyCode },
                { key: 'symbol', header: 'Symbol', render: (row) => row.symbol },
                { key: 'tf', header: 'Timeframe', render: (row) => row.timeframe },
                { key: 'rejected', header: 'Rejected', render: (row) => String(row.rejectedCandidateCount) },
                { key: 'confRejected', header: 'By Confidence', render: (row) => String(row.rejectedByConfidenceCount) },
                { key: 'riskRejected', header: 'By Risk', render: (row) => String(row.rejectedByRiskCount) },
                { key: 'won', header: 'Would Have Won', render: (row) => String(row.rejectedWouldHaveWon) },
                { key: 'lost', header: 'Would Have Lost', render: (row) => String(row.rejectedWouldHaveLost) },
                { key: 'shadowPnl', header: 'Shadow Net PnL', render: (row) => formatNumber(row.shadowNetPnl) },
                { key: 'confFalse', header: 'Confidence False Reject', render: (row) => String(row.confidenceFalseRejectCount) },
                { key: 'riskFalse', header: 'Risk False Reject', render: (row) => String(row.riskFalseRejectCount) },
              ]}
              rows={report.rejectionQuality}
            />
          ) : (
            <p className="text-sm text-slate-400">Rejection quality appears after report generation.</p>
          )
        ) : null}

        {tab === 'risk-calibration' ? (
          report ? (
            <div className="space-y-3 text-sm text-slate-300">
              <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                <MetricCard label="Confidence False Reject %" value={formatNumber(report.riskConfidenceCalibration.confidenceFalseRejectionRatePercent)} />
                <MetricCard label="Risk False Reject %" value={formatNumber(report.riskConfidenceCalibration.riskFalseRejectionRatePercent)} />
                <MetricCard label="Confidence Correct Reject %" value={formatNumber(report.riskConfidenceCalibration.confidenceCorrectRejectionRatePercent)} />
                <MetricCard label="Risk Correct Reject %" value={formatNumber(report.riskConfidenceCalibration.riskCorrectRejectionRatePercent)} />
              </div>
              <p>
                Recommended confidence threshold:{' '}
                {report.riskConfidenceCalibration.confidenceThresholdRecommendation != null
                  ? formatNumber(report.riskConfidenceCalibration.confidenceThresholdRecommendation)
                  : 'No change'}
              </p>
              <div>
                <p className="font-medium text-slate-100">Risk recommendations</p>
                <ul className="list-disc pl-5">
                  {report.riskConfidenceCalibration.riskRuleRecommendations.map((item) => (
                    <li key={item}>{item}</li>
                  ))}
                </ul>
              </div>
              <div>
                <p className="font-medium text-slate-100">Evidence summary</p>
                <ul className="list-disc pl-5">
                  {report.riskConfidenceCalibration.evidenceSummary.map((item) => (
                    <li key={item}>{item}</li>
                  ))}
                </ul>
              </div>
            </div>
          ) : (
            <p className="text-sm text-slate-400">Calibration summary appears after report generation.</p>
          )
        ) : null}

        {tab === 'diagnostics' ? (
          <div className="space-y-3 text-sm text-slate-300">
            <div className="grid gap-3 md:grid-cols-3">
              <MetricCard label="Completed" value={String(diagnostics?.completedRuns ?? 0)} />
              <MetricCard label="Failed" value={String(diagnostics?.failedRuns ?? 0)} />
              <MetricCard label="Pending" value={String(diagnostics?.pendingRuns ?? 0)} />
            </div>
            {diagnostics?.runningItem ? (
              <div className="rounded-lg border border-slate-800 p-3">
                <p className="font-medium text-slate-100">Running item</p>
                <p>Run item id: {diagnostics.runningItem.id}</p>
                <p>
                  {diagnostics.runningItem.strategyCode} · {diagnostics.runningItem.symbol} · {diagnostics.runningItem.timeframe}
                </p>
                <p>Started: {formatDate(diagnostics.runningItem.startedAtUtc)}</p>
                <p>Last heartbeat: {formatDate(diagnostics.runningItem.lastHeartbeatAtUtc)}</p>
                <p>Duration: {diagnostics.runningItem.durationSeconds != null ? `${diagnostics.runningItem.durationSeconds}s` : '—'}</p>
                <p>Last processed candle: {formatDate(diagnostics.runningItem.lastProcessedCandleTimeUtc)}</p>
                <p>
                  Candle progress:{' '}
                  {diagnostics.runningItem.lastProcessedCandleIndex != null && diagnostics.runningItem.totalCandles != null
                    ? `${diagnostics.runningItem.lastProcessedCandleIndex}/${diagnostics.runningItem.totalCandles}`
                    : '—'}
                </p>
              </div>
            ) : null}
            {diagnostics?.lastError ? <p className="text-rose-300">{diagnostics.lastError}</p> : null}
            {(diagnostics?.warnings ?? []).map((warning) => (
              <p key={warning} className="text-amber-200">
                {warning}
              </p>
            ))}
          </div>
        ) : null}
      </TabPanel>
    </div>
  );
}
