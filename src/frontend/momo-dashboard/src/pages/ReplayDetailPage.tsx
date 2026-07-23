import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { SimulationBanner } from '@/components/common/SimulationBanner';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { TabPanel } from '@/components/common/TabPanel';
import { KeyValueGrid, formatKvNumber } from '@/components/common/KeyValueGrid';
import { JsonViewerCollapsed } from '@/components/common/JsonViewerCollapsed';
import { REPLAY_SPEED_OPTIONS } from '@/constants/tradingOptions';
import { SelectField } from '@/components/forms/fields';
import { ReplayFramePanel, CandleView, IndicatorSnapshotView, StrategyResultView, AiDecisionView, RiskDecisionView } from '@/components/formatters/TradingViews';
import { ReplayChart } from '@/components/charts/ReplayChart';
import { ReplayFrameSummary } from '@/components/replay/ReplayFrameSummary';
import { ReplayDecisionPath } from '@/components/replay/ReplayDecisionPath';
import { useAsync } from '@/hooks/useAsync';
import { useReplayAutoplay } from '@/hooks/useReplayAutoplay';
import { useRole } from '@/hooks/useRole';
import { replayApi } from '@/api/replayApi';
import { PipelineDiagnosticsPanel } from '@/components/trading/PipelineDiagnosticsPanel';
import { parseApiClientError } from '@/utils/apiError';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import type { ReplayFrame } from '@/api/domainTypes';

export function ReplayDetailPage() {
  const { id } = useParams();
  const sessionId = Number(id);
  const { canEdit } = useRole();
  const [tab, setTab] = useState('frame');
  const [speed, setSpeed] = useState('ManualStep');
  const [showFutureContext, setShowFutureContext] = useState(false);
  const [autoFollow, setAutoFollow] = useState(true);
  const [actionError, setActionError] = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);

  const session = useAsync(() => (sessionId ? replayApi.get(sessionId) : Promise.resolve(null)), [sessionId, refreshKey]);
  const frame = useAsync(() => (sessionId ? replayApi.getCurrentFrame(sessionId) : Promise.resolve(null)), [sessionId]);
  const diagnostics = useAsync(() => (sessionId ? replayApi.getDiagnostics(sessionId) : Promise.resolve(null)), [sessionId, refreshKey]);

  const frameIndex = session.data?.currentFrameIndex ?? -1;
  const chart = useAsync(
    () =>
      sessionId && frameIndex >= 0
        ? replayApi.getChartWindow(sessionId, {
            currentFrameIndex: frameIndex,
            candlesBefore: 150,
            candlesAfter: showFutureContext ? 25 : 0,
            includeFutureContext: showFutureContext,
          })
        : Promise.resolve(null),
    [sessionId, frameIndex, showFutureContext],
  );

  const refreshLightweight = useCallback(async () => {
    await Promise.all([session.reloadSilent(), frame.reloadSilent(), chart.reloadSilent()]);
  }, [session, frame, chart]);

  const reloadAll = useCallback(async () => {
    setRefreshKey((value) => value + 1);
    await Promise.all([session.reload(), frame.reload(), chart.reload(), diagnostics.reload()]);
  }, [session, frame, chart, diagnostics]);

  const stepForward = useCallback(async () => {
    if (!canEdit || !sessionId) return;
    await replayApi.stepForward(sessionId);
    await refreshLightweight();
  }, [canEdit, sessionId, refreshLightweight]);

  const totalFrames = session.data?.totalFrames ?? 0;
  const statusLabel = String(session.data?.status ?? '');
  const activeSpeed = session.data?.speed ?? speed;

  const { isAutoplayActive, autoplayLabel } = useReplayAutoplay({
    enabled: canEdit,
    status: statusLabel,
    speed: activeSpeed,
    currentFrameIndex: frameIndex,
    totalFrames,
    onStep: stepForward,
    onError: (error) => {
      setActionError(parseApiClientError(error).message);
    },
  });

  useEffect(() => {
    if (isAutoplayActive) {
      setAutoFollow(true);
    }
  }, [isAutoplayActive]);

  async function runControl(action: 'start' | 'pause' | 'resume' | 'stop' | 'forward' | 'backward') {
    if (!canEdit || !sessionId) return;
    setActionError(null);
    try {
      if (action === 'start') await replayApi.start(sessionId);
      if (action === 'pause') await replayApi.pause(sessionId);
      if (action === 'resume') await replayApi.resume(sessionId);
      if (action === 'stop') await replayApi.stop(sessionId);
      if (action === 'forward') await replayApi.stepForward(sessionId);
      if (action === 'backward') await replayApi.stepBackward(sessionId);
      await reloadAll();
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  async function applySpeed() {
    if (!canEdit || !sessionId) return;
    try {
      await replayApi.setSpeed(sessionId, speed);
      await session.reloadSilent();
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  if (!sessionId) return <ErrorState message="Invalid replay session id." />;

  const frameData = (frame.data as ReplayFrame | null) ?? null;
  const frameLabel = frameIndex < 0 ? 'Not started' : `${frameIndex} / ${totalFrames}`;
  const progressPercent = totalFrames > 0 && frameIndex >= 0 ? Math.round(((frameIndex + 1) / totalFrames) * 100) : 0;
  const canStart = statusLabel === 'Created' || statusLabel === 'Paused';
  const canPause = statusLabel === 'Running';
  const canResume = statusLabel === 'Paused';
  const canStop = statusLabel === 'Running' || statusLabel === 'Paused' || statusLabel === 'Created';
  const canForward = statusLabel === 'Running' && frameIndex < totalFrames - 1 && !isAutoplayActive;
  const canBackward = (statusLabel === 'Running' || statusLabel === 'Paused') && frameIndex > 0;

  return (
    <div>
      <PageHeader title={session.data?.name ?? 'Replay Detail'} description="Historical debugging session with visual chart replay." />
      <Link to="/replay" className="mb-4 inline-block text-sm text-slate-400 underline">
        Back to replay sessions
      </Link>
      <SimulationBanner message="Replay is for historical debugging only. No real orders are placed." />
      <ApiErrorAlert message={actionError} />

      {session.loading && !session.data ? <LoadingState /> : null}
      {session.error ? <ErrorState message={session.error} onRetry={session.reload} /> : null}

      {session.data ? (
        <>
          {autoplayLabel ? (
            <p className="mb-2 rounded-lg border border-sky-700 bg-sky-950/40 px-3 py-2 text-xs text-sky-200">{autoplayLabel}</p>
          ) : null}

          <KeyValueGrid
            items={[
              { label: 'Status', value: session.data.status },
              { label: 'Symbol', value: session.data.symbol },
              { label: 'Timeframe', value: session.data.timeframe },
              { label: 'Frame', value: frameLabel },
              { label: 'Balance', value: formatKvNumber(Number(session.data.currentBalance)) },
              { label: 'Equity', value: formatKvNumber(Number(session.data.currentEquity)) },
            ]}
          />

          <div className="my-3">
            <div className="mb-1 flex justify-between text-xs text-slate-400">
              <span>Replay progress</span>
              <span>{progressPercent}%</span>
            </div>
            <div className="h-2 overflow-hidden rounded-full bg-slate-800">
              <div className="h-full bg-sky-500 transition-all" style={{ width: `${progressPercent}%` }} />
            </div>
          </div>

          {canEdit ? (
            <div className="my-4 flex flex-wrap items-end gap-2">
              <button type="button" disabled={!canStart} onClick={() => void runControl('start')} className="rounded-lg border border-slate-600 px-3 py-1.5 text-xs text-slate-200 disabled:opacity-40">
                Start
              </button>
              <button type="button" disabled={!canPause} onClick={() => void runControl('pause')} className="rounded-lg border border-slate-600 px-3 py-1.5 text-xs text-slate-200 disabled:opacity-40">
                Pause
              </button>
              <button type="button" disabled={!canResume} onClick={() => void runControl('resume')} className="rounded-lg border border-slate-600 px-3 py-1.5 text-xs text-slate-200 disabled:opacity-40">
                Resume
              </button>
              <button type="button" disabled={!canStop} onClick={() => void runControl('stop')} className="rounded-lg border border-slate-600 px-3 py-1.5 text-xs text-slate-200 disabled:opacity-40">
                Stop
              </button>
              <button type="button" disabled={!canForward} onClick={() => void runControl('forward')} className="rounded-lg border border-slate-600 px-3 py-1.5 text-xs text-slate-200 disabled:opacity-40">
                Step Forward
              </button>
              <button type="button" disabled={!canBackward} onClick={() => void runControl('backward')} className="rounded-lg border border-slate-600 px-3 py-1.5 text-xs text-slate-200 disabled:opacity-40">
                Step Back
              </button>
              <SelectField label="Speed" value={speed} onChange={(v) => setSpeed(v || 'ManualStep')} options={REPLAY_SPEED_OPTIONS} />
              <button type="button" onClick={() => void applySpeed()} className="rounded-lg border border-slate-600 px-3 py-2 text-xs text-slate-200">
                Apply Speed
              </button>
              <label className="flex items-center gap-2 text-xs text-slate-300">
                <input
                  type="checkbox"
                  checked={showFutureContext}
                  onChange={(event) => setShowFutureContext(event.target.checked)}
                />
                Show full range context (visual only)
              </label>
              <label className="flex items-center gap-2 text-xs text-slate-300">
                <input
                  type="checkbox"
                  checked={autoFollow}
                  onChange={(event) => setAutoFollow(event.target.checked)}
                />
                Auto Follow Current Candle
              </label>
            </div>
          ) : null}

          <div className="my-4 grid gap-4 xl:grid-cols-[minmax(0,2fr)_minmax(280px,1fr)]">
            <div className="space-y-4">
              {chart.loading && !chart.data ? <LoadingState /> : null}
              {chart.error ? <ErrorState message={chart.error} onRetry={chart.reload} /> : null}
              {chart.data ? (
                <ReplayChart
                  chartData={chart.data}
                  currentFrameIndex={frameIndex}
                  strictReplayMode={!showFutureContext}
                  autoFollow={autoFollow}
                  onUserInteraction={() => setAutoFollow(false)}
                />
              ) : null}
            </div>
            <div className="space-y-4">
              <ReplayFrameSummary
                sessionSymbol={session.data.symbol}
                sessionTimeframe={session.data.timeframe}
                exchange={chart.data?.exchange}
                frame={frameData}
                frameIndex={frameIndex}
                totalFrames={totalFrames}
              />
              <ReplayDecisionPath frame={frameData} frameIndex={frameIndex} />
            </div>
          </div>

          <TabPanel
            active={tab}
            onChange={setTab}
            tabs={[
              { id: 'frame', label: 'Frame' },
              { id: 'candle', label: 'Candle' },
              { id: 'indicators', label: 'Indicators' },
              { id: 'strategy', label: 'Strategy Results' },
              { id: 'ai', label: 'AI Decision' },
              { id: 'risk', label: 'Risk Decision' },
              { id: 'explanation', label: 'Explanation' },
              { id: 'diagnostics', label: 'Pipeline Diagnostics' },
              { id: 'raw', label: 'Raw Data' },
            ]}
          >
            {frame.loading && tab !== 'diagnostics' && tab !== 'raw' && !frameData ? <LoadingState /> : null}
            {frame.error && frameIndex >= 0 ? <ErrorState message={frame.error} onRetry={frame.reload} /> : null}
            {frameIndex < 0 && tab !== 'diagnostics' && tab !== 'raw' ? (
              <p className="mb-4 text-sm text-slate-400">Replay has not advanced to a frame yet. Start the session to process frame 0.</p>
            ) : null}
            {tab === 'frame' && frameData ? <ReplayFramePanel frame={frameData} /> : null}
            {tab === 'candle' && frameData ? <CandleView candle={frameData.candle} /> : null}
            {tab === 'indicators' && frameData ? <IndicatorSnapshotView snapshot={frameData.indicatorSnapshot} /> : null}
            {tab === 'strategy' && frameData && Array.isArray(frameData.strategyResults)
              ? frameData.strategyResults.map((item, index) => <StrategyResultView key={index} result={item} />)
              : null}
            {tab === 'ai' && frameData ? <AiDecisionView decision={frameData.aiDecision} /> : null}
            {tab === 'risk' && frameData ? <RiskDecisionView decision={frameData.riskDecision} /> : null}
            {tab === 'explanation' && frameData ? (
              <p className="text-sm text-slate-300">{String(frameData.humanReadableExplanation ?? '—')}</p>
            ) : null}
            {tab === 'diagnostics' ? (
              <PipelineDiagnosticsPanel diagnostics={diagnostics.data} loading={diagnostics.loading} error={diagnostics.error} />
            ) : null}
            {tab === 'raw' ? (
              <JsonViewerCollapsed value={{ session: session.data, frame: frameData, chart: chart.data }} label="Show Raw Data" />
            ) : null}
          </TabPanel>
        </>
      ) : null}
    </div>
  );
}
