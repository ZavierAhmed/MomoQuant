import { KeyValueGrid, formatKvDate, formatKvNumber } from '@/components/common/KeyValueGrid';
import type { ReplayFrame } from '@/api/domainTypes';

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' ? (value as Record<string, unknown>) : {};
}

export function ReplayFrameSummary({
  sessionSymbol,
  sessionTimeframe,
  exchange,
  frame,
  frameIndex,
  totalFrames,
}: {
  sessionSymbol?: string;
  sessionTimeframe?: string | number;
  exchange?: string;
  frame: ReplayFrame | null;
  frameIndex: number;
  totalFrames: number;
}) {
  const candle = asRecord(frame?.candle);
  const indicator = asRecord(frame?.indicatorSnapshot);
  const strategyResults = Array.isArray(frame?.strategyResults) ? frame.strategyResults : [];
  const ai = asRecord(frame?.aiDecision);
  const risk = asRecord(frame?.riskDecision);

  return (
    <div className="rounded-xl border border-slate-700 bg-slate-900/40 p-4">
      <h3 className="mb-3 text-sm font-semibold text-slate-100">Current Frame Summary</h3>
      <KeyValueGrid
        items={[
          { label: 'Frame', value: frameIndex < 0 ? 'Not started' : `${frameIndex} / ${totalFrames}` },
          { label: 'Symbol', value: sessionSymbol ?? '—' },
          { label: 'Exchange', value: exchange ?? '—' },
          { label: 'Timeframe', value: String(sessionTimeframe ?? '—') },
          { label: 'Candle Time', value: formatKvDate(String(candle.closeTimeUtc ?? '')) },
          { label: 'Close', value: formatKvNumber(Number(candle.close)) },
          { label: 'Market Regime', value: String(frame?.marketRegime ?? '—') },
          { label: 'EMA20', value: formatKvNumber(Number(indicator.ema20)) },
          { label: 'EMA50', value: formatKvNumber(Number(indicator.ema50)) },
          { label: 'EMA200', value: formatKvNumber(Number(indicator.ema200)) },
          { label: 'VWAP', value: formatKvNumber(Number(indicator.vwap)) },
          { label: 'RSI14', value: formatKvNumber(Number(indicator.rsi14)) },
          { label: 'ATR14', value: formatKvNumber(Number(indicator.atr14)) },
          { label: 'AI Confidence', value: formatKvNumber(Number(ai.confidenceScore)) },
          { label: 'Risk Decision', value: String(risk.decision ?? '—') },
          { label: 'Balance / Equity', value: `${formatKvNumber(Number(frame?.balance))} / ${formatKvNumber(Number(frame?.equity))}` },
        ]}
      />

      {strategyResults.length > 0 ? (
        <div className="mt-4 space-y-2">
          <h4 className="text-xs font-semibold uppercase tracking-wide text-slate-400">Strategy Results</h4>
          {strategyResults.map((item, index) => {
            const result = asRecord(item);
            return (
              <div key={index} className="rounded border border-slate-700 bg-slate-950/50 p-2 text-xs text-slate-300">
                <div className="font-medium text-slate-100">
                  {String(result.strategyName ?? result.strategyCode ?? 'Strategy')}: {String(result.signalType ?? '—')}{' '}
                  {String(result.direction ?? '')}
                </div>
                <div className="text-slate-400">Reason: {String(result.reason ?? '—')}</div>
              </div>
            );
          })}
        </div>
      ) : (
        <p className="mt-4 text-xs text-slate-400">No strategy results for this frame yet.</p>
      )}

      {frame?.humanReadableExplanation ? (
        <p className="mt-3 text-sm text-slate-300">{String(frame.humanReadableExplanation)}</p>
      ) : null}
    </div>
  );
}
