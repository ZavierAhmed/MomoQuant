import type { StrategyResearchCandidate } from '@/api/strategyLabApi';

export function ScoreBreakdownModal({
  candidate,
  onClose,
}: {
  candidate: StrategyResearchCandidate;
  onClose: () => void;
}) {
  let components: Record<string, { score?: number; max?: number; reason?: string; label?: string }> = {};
  try {
    components = candidate.confidenceComponentsJson
      ? JSON.parse(candidate.confidenceComponentsJson)
      : {};
  } catch {
    components = {};
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4">
      <div className="max-h-[80vh] w-full max-w-2xl overflow-auto rounded-xl border border-slate-700 bg-slate-950 p-4">
        <div className="mb-3 flex items-start justify-between gap-3">
          <div>
            <h3 className="text-sm font-semibold text-slate-100">Confidence Score Breakdown</h3>
            <div className="text-xs text-slate-400">
              Total: {candidate.confidenceScore ?? '—'} · Model: {candidate.confidenceModelVersion ?? '—'}
            </div>
          </div>
          <button type="button" onClick={onClose} className="rounded border border-slate-700 px-2 py-1 text-xs">
            Close
          </button>
        </div>
        <table className="min-w-full divide-y divide-slate-800 text-sm">
          <thead>
            <tr className="text-left text-slate-400">
              <th className="px-2 py-1">Component</th>
              <th className="px-2 py-1">Score</th>
              <th className="px-2 py-1">Max</th>
              <th className="px-2 py-1">Reason</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-800 text-slate-200">
            {Object.entries(components).map(([key, value]) => (
              <tr key={key}>
                <td className="px-2 py-1">{value.label ?? key}</td>
                <td className="px-2 py-1">{value.score ?? '—'}</td>
                <td className="px-2 py-1">{value.max ?? '—'}</td>
                <td className="px-2 py-1 text-slate-400">{value.reason ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
