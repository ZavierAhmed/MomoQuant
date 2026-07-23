import type { ReactNode } from 'react';

export function FormPanel({
  title,
  description,
  children,
}: {
  title: string;
  description?: string;
  children: ReactNode;
}) {
  return (
    <section className="mb-6 rounded-xl border border-slate-700/60 bg-slate-900/60 p-5 shadow-sm">
      <div className="mb-4 border-b border-slate-800 pb-3">
        <h2 className="text-base font-semibold text-slate-100">{title}</h2>
        {description ? <p className="mt-1 text-sm text-slate-400">{description}</p> : null}
      </div>
      {children}
    </section>
  );
}

export function FilterBar({ children }: { children: ReactNode }) {
  return (
    <div className="mb-6 rounded-xl border border-slate-800 bg-slate-900/40 p-4">
      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">{children}</div>
    </div>
  );
}
