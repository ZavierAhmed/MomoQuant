import { useMemo, useState } from 'react';
import type { PipelineDiagnostics } from '@/api/domainTypes';

type LayerKey = 'bb' | 'liquidity' | 'sweeps' | 'cisd' | 'candidates' | 'rejections' | 'trades';

type Props = {
  diagnostics: PipelineDiagnostics | null;
};

export function BbDiagnosticChartOverlay({ diagnostics }: Props) {
  const [layers, setLayers] = useState<Record<LayerKey, boolean>>({
    bb: true,
    liquidity: true,
    sweeps: true,
    cisd: true,
    candidates: true,
    rejections: true,
    trades: true,
  });

  const bbDiagnostics = diagnostics?.bbLiquiditySweep;
  const funnel = bbDiagnostics?.funnelCounts as Record<string, unknown> | undefined;
  const samples = bbDiagnostics?.sampleRejectedEvaluations ?? [];

  const num = (key: string) => Number(funnel?.[key] ?? 0);

  const summary = useMemo(() => {
    if (!funnel) {
      return 'BB diagnostics will appear after a BB strategy backtest or benchmark run completes.';
    }

    return bbDiagnostics?.pipelineSummary ?? 'BB pipeline summary unavailable.';
  }, [bbDiagnostics?.pipelineSummary, funnel]);

  if (!funnel) {
    return <p className="text-sm text-slate-400">{summary}</p>;
  }

  function toggleLayer(key: LayerKey) {
    setLayers((current) => ({ ...current, [key]: !current[key] }));
  }

  return (
    <div className="space-y-3 rounded-lg border border-slate-800 bg-slate-950/40 p-4">
      <div>
        <h3 className="text-sm font-medium text-slate-200">BB Diagnostic Overlay</h3>
        <p className="text-xs text-slate-400">{summary}</p>
        {bbDiagnostics?.whyZeroTradesAnalysis ? (
          <p className="mt-2 rounded border border-amber-700/40 bg-amber-950/20 p-2 text-xs text-amber-100">{bbDiagnostics.whyZeroTradesAnalysis}</p>
        ) : null}
      </div>

      <div className="flex flex-wrap gap-2">
        {(Object.keys(layers) as LayerKey[]).map((key) => (
          <button
            key={key}
            type="button"
            onClick={() => toggleLayer(key)}
            className={`rounded border px-2 py-1 text-xs ${layers[key] ? 'border-emerald-600 text-emerald-200' : 'border-slate-700 text-slate-400'}`}
          >
            {key.toUpperCase()}
          </button>
        ))}
      </div>

      <div className="grid gap-2 text-xs text-slate-300 md:grid-cols-3">
        {layers.bb ? <Metric label="BB upper wick breaks" value={num('bollingerBandUpperWickBreaks')} /> : null}
        {layers.bb ? <Metric label="BB lower wick breaks" value={num('bollingerBandLowerWickBreaks')} /> : null}
        {layers.liquidity ? <Metric label="1m liquidity levels" value={num('oneMinuteLiquidityLevelsDetected')} /> : null}
        {layers.liquidity ? <Metric label="5m liquidity levels" value={num('fiveMinuteLiquidityLevelsDetected')} /> : null}
        {layers.sweeps ? <Metric label="Buy-side sweeps" value={num('buySideLiquiditySweeps')} /> : null}
        {layers.sweeps ? <Metric label="Sell-side sweeps" value={num('sellSideLiquiditySweeps')} /> : null}
        {layers.cisd ? <Metric label="CISD candidates" value={num('cisdCandidates')} /> : null}
        {layers.cisd ? <Metric label="CISD confirmed" value={num('cisdConfirmed')} /> : null}
        {layers.candidates ? <Metric label="Final candidates" value={num('finalCandidateSignals')} /> : null}
        {layers.trades ? <Metric label="Trades created" value={num('tradesCreated')} /> : null}
        {layers.rejections ? <Metric label="RSI passed" value={num('rsiPrimedPassed')} /> : null}
        {layers.candidates ? <Metric label="Target >= 3R" value={num('targetPassed3R')} /> : null}
      </div>

      {layers.rejections && samples.length > 0 ? (
        <div>
          <p className="mb-1 text-xs font-medium text-slate-200">Sample rejected evaluations</p>
          <ul className="max-h-48 space-y-1 overflow-y-auto text-xs text-slate-400">
            {samples.slice(0, 20).map((sample, index) => (
              <li key={`${sample.candleTimeUtc}-${index}`}>
                {sample.candleTimeUtc}: {sample.stagedRejectionCode ?? 'Unknown'} — {sample.displayReason ?? '—'}
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </div>
  );
}

function Metric({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded border border-slate-800 bg-slate-900/50 p-2">
      <p className="text-slate-400">{label}</p>
      <p className="text-sm font-medium text-slate-100">{value}</p>
    </div>
  );
}
