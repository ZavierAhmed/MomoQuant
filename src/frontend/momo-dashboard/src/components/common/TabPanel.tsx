import type { ReactNode } from 'react';

export function TabPanel({
  tabs,
  active,
  onChange,
  children,
}: {
  tabs: Array<{ id: string; label: string }>;
  active: string;
  onChange: (id: string) => void;
  children: ReactNode;
}) {
  return (
    <div>
      <div className="mb-4 flex flex-wrap gap-2 border-b border-slate-800 pb-2">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            type="button"
            onClick={() => onChange(tab.id)}
            className={`rounded-lg px-3 py-1.5 text-sm ${
              active === tab.id
                ? 'bg-slate-100 text-slate-950'
                : 'text-slate-300 hover:bg-slate-800'
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>
      <div>{children}</div>
    </div>
  );
}
