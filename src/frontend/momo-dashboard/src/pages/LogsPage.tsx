import { useState } from 'react';
import { Navigate } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { DataTable } from '@/components/common/DataTable';
import { Pagination } from '@/components/common/Pagination';
import { MetricCard } from '@/components/common/MetricCard';
import { formatDate } from '@/components/common/utils';
import { useAsync } from '@/hooks/useAsync';
import { useRole } from '@/hooks/useRole';
import { auditApi } from '@/api/auditApi';
import { monitoringApi } from '@/api/monitoringApi';

export function LogsPage() {
  const { isAdmin, role } = useRole();
  const [auditPage, setAuditPage] = useState(1);
  const [systemPage, setSystemPage] = useState(1);

  const auditSummary = useAsync(() => (isAdmin ? auditApi.getSummary({ pageSize: 50 }) : Promise.resolve(null)), [isAdmin]);
  const auditLogs = useAsync(
    () => (isAdmin ? auditApi.getLogs({ page: auditPage, pageSize: 20 }) : Promise.resolve(null)),
    [isAdmin, auditPage],
  );
  const canViewSystemLogs = role === 'Admin' || role === 'Trader';
  const systemLogs = useAsync(
    () =>
      canViewSystemLogs
        ? monitoringApi.getSystemHealthLogs({ page: systemPage, pageSize: 20 })
        : Promise.resolve(null),
    [canViewSystemLogs, systemPage],
  );

  if (!canViewSystemLogs) {
    return <Navigate to="/dashboard" replace />;
  }

  return (
    <div>
      <PageHeader title="Logs" description="Audit logs and system health logs." />

      {isAdmin && auditSummary.data ? (
        <div className="mb-6 grid gap-4 md:grid-cols-4">
          <MetricCard label="Total Audit Logs" value={auditSummary.data.totalLogs} />
          <MetricCard label="Critical" value={auditSummary.data.criticalCount} />
          <MetricCard label="Errors" value={auditSummary.data.errorCount} />
          <MetricCard label="Warnings" value={auditSummary.data.warningCount} />
        </div>
      ) : null}

      {isAdmin ? (
        <section className="mb-6">
          <h2 className="mb-3 text-sm font-medium text-slate-300">Audit Logs (Admin)</h2>
          {auditLogs.loading ? <LoadingState /> : null}
          {auditLogs.error ? <ErrorState message={auditLogs.error} onRetry={auditLogs.reload} /> : null}
          <DataTable
            columns={[
              { key: 'action', header: 'Action', render: (row) => row.action },
              { key: 'severity', header: 'Severity', render: (row) => row.severity },
              { key: 'user', header: 'User', render: (row) => row.userEmail ?? row.userId ?? '—' },
              { key: 'created', header: 'Created', render: (row) => formatDate(row.createdAt) },
            ]}
            rows={auditLogs.data?.items ?? []}
          />
          {auditLogs.data ? (
            <Pagination page={auditLogs.data.page} totalPages={auditLogs.data.totalPages} onPageChange={setAuditPage} />
          ) : null}
        </section>
      ) : null}

      <section>
        <h2 className="mb-3 text-sm font-medium text-slate-300">System Health Logs</h2>
        {systemLogs.loading ? <LoadingState /> : null}
        {systemLogs.error ? <ErrorState message={systemLogs.error} onRetry={systemLogs.reload} /> : null}
        <DataTable
          columns={[
            { key: 'subsystem', header: 'Subsystem', render: (row) => row.subsystem },
            { key: 'severity', header: 'Severity', render: (row) => row.severity },
            { key: 'message', header: 'Message', render: (row) => row.message },
            { key: 'checked', header: 'Checked', render: (row) => formatDate(row.checkedAtUtc) },
          ]}
          rows={systemLogs.data?.items ?? []}
        />
        {systemLogs.data ? (
          <Pagination page={systemLogs.data.page} totalPages={systemLogs.data.totalPages} onPageChange={setSystemPage} />
        ) : null}
      </section>
    </div>
  );
}
