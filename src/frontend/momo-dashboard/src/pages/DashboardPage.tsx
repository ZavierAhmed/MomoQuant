import { PageHeader } from '@/components/common/PageHeader';
import { ModeBadge } from '@/components/common/ModeBadge';
import { HealthComponentCard, resolveComponentDisplayStatus, resolveDegradedComponents } from '@/components/common/HealthComponentCard';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { useAsync } from '@/hooks/useAsync';
import { monitoringApi } from '@/api/monitoringApi';

export function DashboardPage() {
  const health = useAsync(() => monitoringApi.getHealth(), []);

  return (
    <div>
      <PageHeader title="Dashboard" description="System status and component health." />
      <div className="mb-6">
        <ModeBadge />
      </div>

      {health.loading ? <LoadingState /> : null}
      {health.error ? <ErrorState message={health.error} onRetry={() => health.reload()} /> : null}

      {health.data ? (
        <>
          {health.data.status.toLowerCase().includes('degraded') ? (
            <div className="mb-4 rounded-xl border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-sm text-amber-200">
              Overall health is degraded because: {resolveDegradedComponents(health.data.components).join(', ') || 'one or more components need attention'}.
            </div>
          ) : null}

          <div className="rounded-xl border border-slate-800 bg-slate-900/40 p-4">
            <h2 className="mb-3 text-sm font-medium text-slate-300">Component Health</h2>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              {(() => {
                const components = health.data.components;
                const checkedAt = health.data.checkedAtUtc;
                const degraded = resolveDegradedComponents(components);
                return components.map((component) => (
                  <HealthComponentCard
                    key={component.name}
                    name={component.name}
                    status={resolveComponentDisplayStatus(component.status, component.message)}
                    message={component.message}
                    latencyMs={component.latencyMs}
                    checkedAtUtc={checkedAt}
                    isDegradedCause={degraded.includes(component.name)}
                  />
                ));
              })()}
            </div>
          </div>
        </>
      ) : null}
    </div>
  );
}
