import { useState } from 'react';
import { PageHeader } from '@/components/common/PageHeader';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { MetricCard } from '@/components/common/MetricCard';
import { PaginatedTable } from '@/components/common/PaginatedTable';
import { KeyValueGrid, formatKvDate } from '@/components/common/KeyValueGrid';
import { JsonViewerCollapsed } from '@/components/common/JsonViewerCollapsed';
import { HealthComponentCard, resolveComponentDisplayStatus, resolveDegradedComponents } from '@/components/common/HealthComponentCard';
import { DataTable } from '@/components/common/DataTable';
import { Pagination } from '@/components/common/Pagination';
import { formatDate } from '@/components/common/utils';
import { useAsync } from '@/hooks/useAsync';
import { monitoringApi } from '@/api/monitoringApi';

export function MonitoringPage() {
  const [logPage, setLogPage] = useState(1);

  const health = useAsync(() => monitoringApi.getHealth(), []);
  const status = useAsync(() => monitoringApi.getStatus(), []);
  const pipeline = useAsync(() => monitoringApi.getTradingPipelineStatus(), []);
  const errors = useAsync(() => monitoringApi.getRecentErrors({ limit: 50 }), []);
  const safety = useAsync(() => monitoringApi.getSafetyEvents({ limit: 50 }), []);
  const logs = useAsync(() => monitoringApi.getSystemHealthLogs({ page: logPage, pageSize: 25 }), [logPage]);

  const degradedComponents = health.data ? resolveDegradedComponents(health.data.components) : [];

  return (
    <div>
      <PageHeader title="Monitoring" description="Platform health, pipeline status, and system logs." />

      {health.loading || status.loading ? <LoadingState /> : null}
      {health.error ? <ErrorState message={health.error} onRetry={health.reload} /> : null}

      {health.data && status.data ? (
        <>
          <div className="mb-6 grid gap-4 md:grid-cols-2 xl:grid-cols-5">
            <MetricCard label="Overall Health" value={health.data.status} />
            <MetricCard label="API" value={resolveComponentDisplayStatus(status.data.apiStatus)} />
            <MetricCard label="Database" value={status.data.databaseStatus} />
            <MetricCard
              label="Redis"
              value={resolveComponentDisplayStatus(
                status.data.redisStatus,
                health.data.components.find((component) => component.name === 'Redis')?.message,
              )}
            />
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
            <MetricCard label="Active Paper Sessions" value={status.data.activePaperSessions} />
            <MetricCard label="Running Backtests" value={status.data.runningBacktests} />
            <MetricCard label="Recent Critical Errors" value={status.data.recentCriticalErrors} />
            <MetricCard label="Recent Risk Rejections" value={status.data.recentRiskRejections} />
          </div>
        </>
      ) : null}

      {pipeline.data ? (
        <section className="mb-6 rounded-xl border border-slate-800 bg-slate-900/40 p-4">
          <h2 className="mb-3 text-sm font-medium text-slate-300">Trading Pipeline Status</h2>
          <KeyValueGrid
            items={[
              { label: 'Market Data', value: pipeline.data.marketDataAvailable ? 'Available' : 'Unavailable' },
              { label: 'Indicators', value: pipeline.data.indicatorsAvailable ? 'Available' : 'Unavailable' },
              { label: 'Strategies Enabled', value: String(pipeline.data.strategiesEnabled) },
              { label: 'Risk Profiles', value: pipeline.data.riskProfilesAvailable ? 'Available' : 'Unavailable' },
              { label: 'AI Service', value: pipeline.data.aiServiceAvailable ? 'Available' : 'Unavailable' },
              { label: 'Backtesting', value: pipeline.data.backtestingAvailable ? 'Available' : 'Unavailable' },
              { label: 'Replay', value: pipeline.data.replayAvailable ? 'Available' : 'Unavailable' },
              { label: 'Paper Trading', value: pipeline.data.paperTradingAvailable ? 'Available' : 'Unavailable' },
              { label: 'Latest Candle', value: formatKvDate(pipeline.data.latestCandleTimeUtc ?? '') },
              { label: 'Latest Indicators', value: formatKvDate(pipeline.data.latestIndicatorSnapshotTimeUtc ?? '') },
              { label: 'Open Paper Positions', value: String(pipeline.data.openPaperPositions) },
              { label: 'Emergency Stop', value: pipeline.data.emergencyStopEnabled ? 'Enabled' : 'Disabled' },
            ]}
          />
          {(pipeline.data.warnings ?? []).length > 0 ? (
            <div className="mt-4">
              <p className="text-xs uppercase text-amber-400">Warnings</p>
              <ul className="mt-1 list-disc pl-5 text-sm text-amber-200">
                {pipeline.data.warnings.map((warning) => (
                  <li key={warning}>{warning}</li>
                ))}
              </ul>
            </div>
          ) : null}
          <JsonViewerCollapsed value={pipeline.data} />
        </section>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-2">
        <section>
          <h2 className="mb-3 text-sm font-medium text-slate-300">Recent Errors</h2>
          <PaginatedTable
            rows={errors.data ?? []}
            columns={[
              { key: 'subsystem', header: 'Subsystem', render: (row) => row.subsystem },
              { key: 'severity', header: 'Severity', render: (row) => row.severity },
              { key: 'message', header: 'Message', render: (row) => row.message },
              { key: 'at', header: 'Occurred', render: (row) => formatDate(row.occurredAtUtc) },
            ]}
          />
        </section>

        <section>
          <h2 className="mb-3 text-sm font-medium text-slate-300">Safety Events</h2>
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

      <section className="mt-6">
        <h2 className="mb-3 text-sm font-medium text-slate-300">System Health Logs</h2>
        {logs.loading ? <LoadingState /> : null}
        <DataTable
          columns={[
            { key: 'subsystem', header: 'Subsystem', render: (row) => row.subsystem },
            { key: 'status', header: 'Status', render: (row) => row.status },
            { key: 'severity', header: 'Severity', render: (row) => row.severity },
            { key: 'message', header: 'Message', render: (row) => row.message },
            { key: 'checked', header: 'Checked', render: (row) => formatDate(row.checkedAtUtc) },
          ]}
          rows={logs.data?.items ?? []}
        />
        {logs.data ? (
          <Pagination page={logs.data.page} totalPages={logs.data.totalPages} onPageChange={setLogPage} />
        ) : null}
      </section>
    </div>
  );
}
