interface DateRangeFilterProps {
  fromUtc: string;
  toUtc: string;
  onChange: (field: 'fromUtc' | 'toUtc', value: string) => void;
}

export function DateRangeFilter({ fromUtc, toUtc, onChange }: DateRangeFilterProps) {
  return (
    <div className="flex flex-wrap gap-3">
      <label className="text-xs text-slate-400">
        From UTC
        <input
          type="datetime-local"
          value={fromUtc}
          onChange={(event) => onChange('fromUtc', event.target.value)}
          className="mt-1 block rounded-lg border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100"
        />
      </label>
      <label className="text-xs text-slate-400">
        To UTC
        <input
          type="datetime-local"
          value={toUtc}
          onChange={(event) => onChange('toUtc', event.target.value)}
          className="mt-1 block rounded-lg border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-slate-100"
        />
      </label>
    </div>
  );
}
