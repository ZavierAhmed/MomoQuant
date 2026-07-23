import type { ReplayFrame } from '@/api/domainTypes';

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' ? (value as Record<string, unknown>) : {};
}

export function ReplayDecisionPath({ frame, frameIndex }: { frame: ReplayFrame | null; frameIndex: number }) {
  if (frameIndex < 0 || !frame) {
    return (
      <div className="rounded-xl border border-slate-700 bg-slate-900/40 p-4 text-sm text-slate-400">
        Start replay to see the decision path for each frame.
      </div>
    );
  }

  const indicator = asRecord(frame.indicatorSnapshot);
  const strategies = Array.isArray(frame.strategyResults) ? frame.strategyResults : [];
  const entrySignals = strategies.filter((item) => {
    const signalType = String(asRecord(item).signalType ?? '').toLowerCase();
    return signalType === 'entry';
  });
  const ai = asRecord(frame.aiDecision);
  const risk = asRecord(frame.riskDecision);
  const hasOrder = Boolean(frame.simulatedOrder || frame.simulatedFill);
  const hasMissed = Boolean(frame.missedOrder);

  const steps = [
    { label: 'Candle loaded', ok: Boolean(frame.candle) },
    { label: 'Indicators loaded', ok: Boolean(indicator.ema20 != null || indicator.vwap != null) },
    { label: `Market regime: ${String(frame.marketRegime ?? 'Unknown')}`, ok: true },
    { label: `Strategies evaluated: ${strategies.length}`, ok: strategies.length > 0 },
    { label: `Entry signals: ${entrySignals.length}`, ok: entrySignals.length > 0 },
    {
      label: ai.id != null ? `AI scored (${String(ai.confidenceScore ?? '—')})` : 'AI not evaluated',
      ok: ai.id != null,
    },
    {
      label: risk.decision ? `Risk: ${String(risk.decision)}` : 'Risk not evaluated',
      ok: Boolean(risk.decision),
    },
    {
      label: hasOrder ? 'Order simulated' : hasMissed ? 'Order missed' : 'No execution',
      ok: hasOrder || hasMissed || entrySignals.length === 0,
    },
  ];

  return (
    <div className="rounded-xl border border-slate-700 bg-slate-900/40 p-4">
      <h3 className="mb-3 text-sm font-semibold text-slate-100">Decision Path</h3>
      <ol className="space-y-2 text-sm">
        {steps.map((step) => (
          <li key={step.label} className="flex items-start gap-2">
            <span className={step.ok ? 'text-emerald-400' : 'text-slate-500'}>{step.ok ? '✓' : '○'}</span>
            <span className="text-slate-300">{step.label}</span>
          </li>
        ))}
      </ol>
      {entrySignals.length === 0 && strategies.length > 0 ? (
        <p className="mt-3 text-xs text-slate-400">
          Reason: No entry signals. Review strategy NoTrade reasons in the side panel.
        </p>
      ) : null}
    </div>
  );
}
