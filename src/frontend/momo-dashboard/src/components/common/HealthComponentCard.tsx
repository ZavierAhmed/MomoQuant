import { StatusPill } from '@/components/common/StatusPill';
import { formatDate } from '@/components/common/utils';

export function HealthComponentCard({
  name,
  status,
  message,
  latencyMs,
  checkedAtUtc,
  isDegradedCause,
}: {
  name: string;
  status: string;
  message?: string | null;
  latencyMs?: number | null;
  checkedAtUtc?: string | null;
  isDegradedCause?: boolean;
}) {
  return (
    <div
      className={`rounded-xl border p-4 ${
        isDegradedCause ? 'border-amber-500/50 bg-amber-500/5' : 'border-slate-800 bg-slate-900/50'
      }`}
    >
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-sm font-medium text-slate-200">{name}</p>
          {isDegradedCause ? (
            <p className="mt-1 text-xs text-amber-300">Contributing to overall degraded status</p>
          ) : null}
        </div>
        <StatusPill status={status} />
      </div>
      {message ? <p className="mt-2 text-sm text-slate-400">{message}</p> : null}
      <div className="mt-3 flex flex-wrap gap-4 text-xs text-slate-500">
        {latencyMs != null ? <span>Latency: {latencyMs} ms</span> : null}
        {checkedAtUtc ? <span>Checked: {formatDate(checkedAtUtc)}</span> : null}
      </div>
    </div>
  );
}

export function resolveDegradedComponents(components: { name: string; status: string }[]): string[] {
  return components
    .filter((component) => {
      const normalized = component.status.toLowerCase();
      return normalized.includes('unhealthy') || normalized.includes('degraded') || normalized === 'unknown';
    })
    .map((component) => component.name);
}

export function resolveComponentDisplayStatus(
  status: string,
  message?: string | null,
): string {
  if (status === 'Unknown' && message?.toLowerCase().includes('not configured')) {
    return 'Not Configured';
  }
  return status;
}
