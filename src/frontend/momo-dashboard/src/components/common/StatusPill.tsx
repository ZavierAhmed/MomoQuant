export function StatusPill({ status }: { status: string }) {
  const normalized = status.toLowerCase();
  const color =
    normalized.includes('healthy') || normalized.includes('ok')
      ? 'border-emerald-500/40 bg-emerald-500/10 text-emerald-300'
      : normalized.includes('degraded') || normalized.includes('warning')
        ? 'border-amber-500/40 bg-amber-500/10 text-amber-300'
        : normalized.includes('unhealthy') || normalized.includes('error') || normalized.includes('critical')
          ? 'border-red-500/40 bg-red-500/10 text-red-300'
          : 'border-slate-600 bg-slate-800 text-slate-300';

  return (
    <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-xs font-medium ${color}`}>
      {status}
    </span>
  );
}
