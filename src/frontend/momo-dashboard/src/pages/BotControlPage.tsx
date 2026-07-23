import { Link } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { SimulationSafetyBanner } from '@/components/common/SimulationSafetyBanner';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { MetricCard } from '@/components/common/MetricCard';
import { StatusPill } from '@/components/common/StatusPill';
import { PaginatedTable } from '@/components/common/PaginatedTable';
import { HealthComponentCard, resolveComponentDisplayStatus, resolveDegradedComponents } from '@/components/common/HealthComponentCard';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { useAsync } from '@/hooks/useAsync';
import { useRole } from '@/hooks/useRole';
import { monitoringApi } from '@/api/monitoringApi';
import { paperTradingApi } from '@/api/paperTradingApi';
import { replayApi } from '@/api/replayApi';
import { backtestsApi } from '@/api/backtestsApi';
import { parseApiClientError } from '@/utils/apiError';
import { formatDate } from '@/components/common/utils';
import { getPaperSessionActions, paperSessionActionLabel } from '@/utils/formValidation';
import { exchangeLabel } from '@/utils/referenceLookups';
import { useReferenceData } from '@/hooks/useReferenceData';
import { useState } from 'react';

function isRunningStatus(status: string | number): boolean {
  const value = String(status).toLowerCase();
  return value.includes('running') || value.includes('active') || value === '2' || value === '3';
}

export function BotControlPage() {
  const { canEdit } = useRole();
  const reference = useReferenceData();
  const [actionError, setActionError] = useState<string | null>(null);

  const health = useAsync(() => monitoringApi.getHealth(), []);
  const status = useAsync(() => monitoringApi.getStatus(), []);
  const pipeline = useAsync(() => monitoringApi.getTradingPipelineStatus(), []);
  const safety = useAsync(() => monitoringApi.getSafetyEvents({ limit: 20 }), []);
  const backtests = useAsync(() => backtestsApi.list({ page: 1, pageSize: 50 }), []);
  const replays = useAsync(() => replayApi.list({ page: 1, pageSize: 50 }), []);
  const paperSessions = useAsync(() => paperTradingApi.listSessions({ page: 1, pageSize: 50 }), []);

  const degradedComponents = health.data ? resolveDegradedComponents(health.data.components) : [];

  async function runPaperAction(sessionId: number, action: 'start' | 'pause' | 'resume' | 'stop') {
    if (!canEdit) return;
    setActionError(null);
    try {
      if (action === 'start') await paperTradingApi.startSession(sessionId);
      if (action === 'pause') await paperTradingApi.pauseSession(sessionId);
      if (action === 'resume') await paperTradingApi.resumeSession(sessionId);
      if (action === 'stop') await paperTradingApi.stopSession(sessionId);
      paperSessions.reload();
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  async function runReplayAction(sessionId: number, action: 'start' | 'pause' | 'resume' | 'stop' | 'forward') {
    if (!canEdit) return;
    setActionError(null);
    try {
      if (action === 'start') await replayApi.start(sessionId);
      if (action === 'pause') await replayApi.pause(sessionId);
      if (action === 'resume') await replayApi.resume(sessionId);
      if (action === 'stop') await replayApi.stop(sessionId);
      if (action === 'forward') await replayApi.stepForward(sessionId);
      replays.reload();
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  const runningBacktests = (backtests.data?.items ?? []).filter((item) => isRunningStatus(item.status));
  const runningReplays = (replays.data?.items ?? []).filter((item) => isRunningStatus(item.status));
  const runningPaper = (paperSessions.data?.items ?? []).filter((item) => isRunningStatus(item.status));

  return (
    <div>
      <PageHeader title="Bot Control" description="Simulation-only control surface for backtests, replay, and paper trading." />
      <SimulationSafetyBanner message="Simulation only — live trading is disabled. No real exchange orders can be placed." />
      <ApiErrorAlert message={actionError} />

      {health.loading || status.loading ? <LoadingState /> : null}
      {health.error ? <ErrorState message={health.error} onRetry={health.reload} /> : null}

      {health.data && status.data ? (
        <>
          <div className="mb-6 grid gap-4 md:grid-cols-2 xl:grid-cols-5">
            <MetricCard label="Overall Status" value={health.data.status} />
            <MetricCard label="API" value={resolveComponentDisplayStatus(status.data.apiStatus)} />
            <MetricCard label="Database" value={status.data.databaseStatus} />
            <MetricCard label="Redis" value={resolveComponentDisplayStatus(status.data.redisStatus, health.data.components.find((c) => c.name === 'Redis')?.message)} />
            <MetricCard label="AI Service" value={status.data.aiServiceStatus} />
          </div>

          {health.data.status.toLowerCase().includes('degraded') && degradedComponents.length > 0 ? (
            <div className="mb-6 rounded-xl border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-sm text-amber-200">
              Overall health is degraded because: {degradedComponents.join(', ')}.
            </div>
          ) : null}

          <section className="mb-6">
            <h2 className="mb-3 text-sm font-medium text-slate-300">Component Health</h2>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              {health.data.components.map((component) => (
                <HealthComponentCard
                  key={component.name}
                  name={component.name}
                  status={resolveComponentDisplayStatus(component.status, component.message)}
                  message={component.message}
                  latencyMs={component.latencyMs}
                  checkedAtUtc={health.data?.checkedAtUtc}
                  isDegradedCause={degradedComponents.includes(component.name)}
                />
              ))}
            </div>
          </section>

          <div className="mb-6 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <MetricCard label="Backtesting" value={pipeline.data?.backtestingAvailable ? 'Available' : 'Unavailable'} />
            <MetricCard label="Replay" value={pipeline.data?.replayAvailable ? 'Available' : 'Unavailable'} />
            <MetricCard label="Paper Trading" value={pipeline.data?.paperTradingAvailable ? 'Available' : 'Unavailable'} />
            <MetricCard label="Live Trading" value="Disabled / Locked" />
          </div>

          <div className="mb-6 flex flex-wrap gap-2">
            <Link to="/backtesting" className="rounded-lg border border-slate-600 px-3 py-2 text-sm text-slate-200">Go to Backtesting</Link>
            <Link to="/replay" className="rounded-lg border border-slate-600 px-3 py-2 text-sm text-slate-200">Go to Replay</Link>
            <Link to="/paper-trading" className="rounded-lg border border-slate-600 px-3 py-2 text-sm text-slate-200">Go to Paper Trading</Link>
            <Link to="/risk-management" className="rounded-lg border border-slate-600 px-3 py-2 text-sm text-slate-200">Go to Risk Management</Link>
            <Link to="/monitoring" className="rounded-lg border border-slate-600 px-3 py-2 text-sm text-slate-200">Go to Monitoring</Link>
          </div>
        </>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-3">
        <section>
          <h2 className="mb-3 text-sm font-medium text-slate-300">Running Backtests</h2>
          <PaginatedTable
            rows={runningBacktests}
            columns={[
              { key: 'name', header: 'Name', render: (row) => row.name },
              { key: 'status', header: 'Status', render: (row) => <StatusPill status={String(row.status)} /> },
            ]}
            emptyMessage="No running backtests."
          />
        </section>
        <section>
          <h2 className="mb-3 text-sm font-medium text-slate-300">Running Replay Sessions</h2>
          <PaginatedTable
            rows={runningReplays}
            columns={[
              { key: 'name', header: 'Name', render: (row) => row.name },
              { key: 'status', header: 'Status', render: (row) => <StatusPill status={String(row.status)} /> },
            ]}
            emptyMessage="No running replay sessions."
          />
        </section>
        <section>
          <h2 className="mb-3 text-sm font-medium text-slate-300">Running Paper Sessions</h2>
          <PaginatedTable
            rows={runningPaper}
            columns={[
              { key: 'name', header: 'Name', render: (row) => row.name },
              { key: 'status', header: 'Status', render: (row) => <StatusPill status={String(row.status)} /> },
            ]}
            emptyMessage="No running paper sessions."
          />
        </section>
      </div>

      <section className="mt-6">
        <h2 className="mb-3 text-sm font-medium text-slate-300">Paper Session Controls (Simulated)</h2>
        <PaginatedTable
          rows={paperSessions.data?.items ?? []}
          columns={[
            { key: 'name', header: 'Name', render: (row) => row.name },
            { key: 'mode', header: 'Mode', render: (row) => row.mode },
            { key: 'status', header: 'Status', render: (row) => <StatusPill status={String(row.status)} /> },
            { key: 'exchange', header: 'Exchange', render: (row) => exchangeLabel(reference.exchanges, row.exchangeId) },
            {
              key: 'actions',
              header: 'Actions',
              render: (row) =>
                canEdit ? (
                  <div className="flex flex-wrap gap-2">
                    {getPaperSessionActions(row.status).map((action) => (
                      <button key={action} type="button" className="text-xs underline" onClick={() => void runPaperAction(row.id, action)}>
                        {paperSessionActionLabel(action)}
                      </button>
                    ))}
                  </div>
                ) : (
                  'Read-only'
                ),
            },
          ]}
        />
      </section>

      <section className="mt-6">
        <h2 className="mb-3 text-sm font-medium text-slate-300">Replay Session Controls (Historical Only)</h2>
        <PaginatedTable
          rows={replays.data?.items ?? []}
          columns={[
            { key: 'name', header: 'Name', render: (row) => row.name },
            { key: 'status', header: 'Status', render: (row) => <StatusPill status={String(row.status)} /> },
            {
              key: 'actions',
              header: 'Actions',
              render: (row) =>
                canEdit ? (
                  <div className="flex flex-wrap gap-2">
                    {(['start', 'pause', 'resume', 'stop', 'forward'] as const).map((action) => (
                      <button key={action} type="button" className="text-xs underline" onClick={() => void runReplayAction(row.id, action)}>
                        {action === 'forward' ? 'Step Forward' : action}
                      </button>
                    ))}
                  </div>
                ) : (
                  'Read-only'
                ),
            },
          ]}
        />
      </section>

      <section className="mt-6">
        <h2 className="mb-3 text-sm font-medium text-slate-300">Recent Safety Events</h2>
        <PaginatedTable
          rows={safety.data ?? []}
          columns={[
            { key: 'type', header: 'Event', render: (row) => row.eventType },
            { key: 'severity', header: 'Severity', render: (row) => row.severity },
            { key: 'message', header: 'Message', render: (row) => row.message },
            { key: 'at', header: 'Occurred', render: (row) => formatDate(row.occurredAtUtc) },
          ]}
        />
      </section>
    </div>
  );
}
