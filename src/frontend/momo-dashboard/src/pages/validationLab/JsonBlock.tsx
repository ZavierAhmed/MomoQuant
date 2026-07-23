import { tryParseJson } from '@/pages/validationLab/validationLabDetailHelpers';

/**
 * Renders a titled, best-effort-pretty-printed JSON/text snapshot block.
 * Distinct from `components/common/utils`'s `JsonBlock`, which takes an
 * already-parsed value rather than a raw JSON string.
 */
export function JsonBlock({ title, value }: { title: string; value?: string | null }) {
  const parsed = tryParseJson(value);
  const text =
    typeof parsed === 'string'
      ? parsed
      : parsed == null
        ? '—'
        : JSON.stringify(parsed, null, 2);

  return (
    <div className="rounded-lg border border-slate-800 p-3">
      <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">{title}</div>
      <pre className="max-h-80 overflow-auto whitespace-pre-wrap text-xs text-slate-300">{text}</pre>
    </div>
  );
}
