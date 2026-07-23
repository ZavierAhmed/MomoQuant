import { useNavigate } from 'react-router-dom';
import { useAuth } from '@/auth/useAuth';
import { HealthIndicator } from '@/components/common/HealthIndicator';
import { StatusBadge } from '@/components/common/StatusBadge';

export function TopBar() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  async function handleLogout() {
    await logout();
    navigate('/signin', { replace: true });
  }

  return (
    <header className="flex flex-wrap items-center justify-between gap-4 border-b border-slate-800 bg-slate-950/90 px-6 py-4">
      <div>
        <p className="text-sm font-semibold text-slate-100">MOMO Quant</p>
        <p className="text-xs text-slate-500">Infrastructure control panel</p>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <StatusBadge label="Simulation Only" />
        <HealthIndicator />
        <button
          type="button"
          disabled
          title="Emergency stop placeholder — not connected to live trading."
          className="rounded-lg border border-red-500/30 bg-red-500/10 px-3 py-1.5 text-xs font-medium text-red-300 opacity-70"
        >
          Emergency Stop (Placeholder)
        </button>
      </div>

      <div className="flex items-center gap-3 text-sm text-slate-300">
        <div className="text-right">
          <p className="font-medium text-slate-100">{user?.fullName ?? 'Unknown user'}</p>
          <p className="text-xs text-slate-500">
            {user?.email} · {user?.role ?? 'No role'}
          </p>
        </div>
        <button
          type="button"
          onClick={() => void handleLogout()}
          className="rounded-lg border border-slate-700 px-3 py-1.5 text-xs font-medium text-slate-200 transition hover:bg-slate-800"
        >
          Logout
        </button>
      </div>
    </header>
  );
}
