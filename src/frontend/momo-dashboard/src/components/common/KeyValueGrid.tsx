import type { ReactNode } from 'react';
import { formatDate, formatNumber } from '@/components/common/utils';

export function KeyValueGrid({
  items,
}: {
  items: Array<{ label: string; value: ReactNode }>;
}) {
  return (
    <dl className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
      {items.map((item) => (
        <div key={item.label} className="rounded-lg border border-slate-800 bg-slate-950/40 px-3 py-2">
          <dt className="text-xs uppercase tracking-wide text-slate-500">{item.label}</dt>
          <dd className="mt-1 text-sm text-slate-100">{item.value ?? '—'}</dd>
        </div>
      ))}
    </dl>
  );
}

export function formatKvDate(value?: string | null) {
  return formatDate(value);
}

export function formatKvNumber(value?: number | null, digits = 2) {
  return formatNumber(value, digits);
}
