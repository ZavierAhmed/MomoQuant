export function Badge({
  children,
  tone = 'neutral',
}: {
  children: React.ReactNode;
  tone?: 'neutral' | 'success' | 'warning' | 'info';
}) {
  const toneClass =
    tone === 'success'
      ? 'border-emerald-500/40 bg-emerald-500/10 text-emerald-300'
      : tone === 'warning'
        ? 'border-amber-500/40 bg-amber-500/10 text-amber-300'
        : tone === 'info'
          ? 'border-sky-500/40 bg-sky-500/10 text-sky-300'
          : 'border-slate-600 bg-slate-800 text-slate-300';

  return (
    <span className={`inline-flex rounded-full border px-2.5 py-0.5 text-xs font-medium ${toneClass}`}>
      {children}
    </span>
  );
}
