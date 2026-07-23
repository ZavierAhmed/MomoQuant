import { useState } from 'react';

export function JsonViewerCollapsed({ value, label = 'Show Raw Data' }: { value: unknown; label?: string }) {
  const [open, setOpen] = useState(false);

  return (
    <div className="mt-3">
      <button
        type="button"
        onClick={() => setOpen((current) => !current)}
        className="text-xs text-slate-400 underline hover:text-slate-200"
      >
        {open ? 'Hide Raw Data' : label}
      </button>
      {open ? (
        <pre className="mt-2 max-h-64 overflow-auto rounded-xl border border-slate-800 bg-slate-950 p-4 text-xs text-slate-300">
          {JSON.stringify(value, null, 2)}
        </pre>
      ) : null}
    </div>
  );
}
