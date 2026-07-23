import { Link } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { EmptyState } from '@/components/common/EmptyState';
import { useAsync } from '@/hooks/useAsync';
import { tradingSystemsApi } from '@/api/tradingSystemsApi';

const SYSTEM_ROUTES: Record<string, string> = {
  SK_SYSTEM: '/trading-systems/sk',
};

export function TradingSystemsPage() {
  const systems = useAsync(() => tradingSystemsApi.listSystems(), []);

  return (
    <div>
      <PageHeader
        title="Trading Systems"
        description="Analytical frameworks for chart diagnosis, sequence detection, and scenario planning."
      />

      <div className="mb-6">
        <EmptyState
          title="Analysis only"
          description="Trading systems provide chart analysis and scenario planning. They do not execute trades."
        />
      </div>

      {systems.loading ? <LoadingState /> : null}
      {systems.error ? <ErrorState message={systems.error} onRetry={systems.reload} /> : null}

      {!systems.loading && !systems.error ? (
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {(systems.data ?? []).map((system) => {
            const route = SYSTEM_ROUTES[system.code];
            return (
              <div
                key={system.code}
                className="flex flex-col rounded-xl border border-slate-800 bg-slate-900/40 p-5"
              >
                <div className="flex items-center justify-between">
                  <h2 className="text-lg font-medium text-slate-100">{system.name}</h2>
                  <span className="rounded-full border border-sky-500/40 bg-sky-500/10 px-2.5 py-0.5 text-xs text-sky-300">
                    Analysis only
                  </span>
                </div>
                <p className="mt-1 text-xs uppercase tracking-wide text-slate-500">{system.category}</p>
                <p className="mt-3 flex-1 text-sm text-slate-400">{system.description}</p>
                <div className="mt-3 text-xs text-slate-500">
                  Primary: {system.supportedPrimaryTimeframes.join(', ')} · Higher:{' '}
                  {system.supportedHigherTimeframes.join(', ')}
                </div>
                {route ? (
                  <Link
                    to={route}
                    className="mt-4 inline-flex w-fit rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950 hover:bg-white"
                  >
                    Open analyzer
                  </Link>
                ) : (
                  <span className="mt-4 text-sm text-slate-500">Coming soon</span>
                )}
              </div>
            );
          })}
        </div>
      ) : null}
    </div>
  );
}
