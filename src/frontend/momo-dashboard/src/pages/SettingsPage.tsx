import { PageHeader } from '@/components/common/PageHeader';
import { ModeBadge } from '@/components/common/ModeBadge';
import { MetricCard } from '@/components/common/MetricCard';
import { getApiBaseUrl } from '@/api/apiClient';
import { useAuth } from '@/auth/useAuth';
import { Link } from 'react-router-dom';

export function SettingsPage() {
  const { user } = useAuth();

  return (
    <div>
      <PageHeader title="Settings" description="Current user and application configuration." />

      <div className="mb-6">
        <ModeBadge />
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <MetricCard label="Full Name" value={user?.fullName ?? '—'} />
        <MetricCard label="Email" value={user?.email ?? '—'} />
        <MetricCard label="Role" value={user?.role ?? '—'} />
        <MetricCard label="API Base URL" value={getApiBaseUrl()} />
      </div>

      <section className="mt-6 rounded-xl border border-slate-800 bg-slate-900/40 p-4 text-sm text-slate-400">
        <p>Simulation-only status is enforced across the dashboard.</p>
        <p className="mt-2">No secrets, exchange API keys, or password hashes are displayed in settings.</p>
        <Link to="/settings/trading" className="mt-3 inline-block text-slate-200 underline">
          Open Trading Settings
        </Link>
      </section>
    </div>
  );
}
