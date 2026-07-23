import { KeyValueGrid, formatKvDate, formatKvNumber } from '@/components/common/KeyValueGrid';
import { JsonViewerCollapsed } from '@/components/common/JsonViewerCollapsed';

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' ? (value as Record<string, unknown>) : {};
}

export function CandleView({ candle }: { candle: unknown }) {
  const data = asRecord(candle);
  return (
    <>
      <KeyValueGrid
        items={[
          { label: 'Open Time', value: formatKvDate(String(data.openTimeUtc ?? '')) },
          { label: 'Close Time', value: formatKvDate(String(data.closeTimeUtc ?? '')) },
          { label: 'Open', value: formatKvNumber(Number(data.open)) },
          { label: 'High', value: formatKvNumber(Number(data.high)) },
          { label: 'Low', value: formatKvNumber(Number(data.low)) },
          { label: 'Close', value: formatKvNumber(Number(data.close)) },
          { label: 'Volume', value: formatKvNumber(Number(data.volume)) },
        ]}
      />
      <JsonViewerCollapsed value={candle} />
    </>
  );
}

export function IndicatorSnapshotView({ snapshot }: { snapshot: unknown }) {
  const data = asRecord(snapshot);
  return (
    <>
      <KeyValueGrid
        items={[
          { label: 'EMA20', value: formatKvNumber(Number(data.ema20)) },
          { label: 'EMA50', value: formatKvNumber(Number(data.ema50)) },
          { label: 'EMA200', value: formatKvNumber(Number(data.ema200)) },
          { label: 'VWAP', value: formatKvNumber(Number(data.vwap)) },
          { label: 'RSI14', value: formatKvNumber(Number(data.rsi14)) },
          { label: 'ATR14', value: formatKvNumber(Number(data.atr14)) },
          { label: 'Volume SMA20', value: formatKvNumber(Number(data.volumeSma20)) },
          { label: 'Swing High', value: formatKvNumber(Number(data.swingHigh)) },
          { label: 'Swing Low', value: formatKvNumber(Number(data.swingLow)) },
          { label: 'Market Structure', value: String(data.marketStructure ?? '—') },
        ]}
      />
      <JsonViewerCollapsed value={snapshot} />
    </>
  );
}

export function StrategyResultView({ result }: { result: unknown }) {
  const data = asRecord(result);
  return (
    <>
      <KeyValueGrid
        items={[
          { label: 'Strategy', value: String(data.strategyName ?? data.strategyCode ?? '—') },
          { label: 'Strategy Code', value: String(data.strategyCode ?? '—') },
          { label: 'Evaluated', value: data.evaluated === true ? 'Yes' : data.evaluated === false ? 'No' : '—' },
          { label: 'Skipped', value: data.skipped === true ? 'Yes' : data.skipped === false ? 'No' : '—' },
          { label: 'Skip Reason', value: String(data.skipReason ?? '—') },
          { label: 'Signal Type', value: String(data.signalType ?? '—') },
          { label: 'Direction', value: String(data.direction ?? '—') },
          { label: 'Strength', value: formatKvNumber(Number(data.strength)) },
          { label: 'Confidence Contribution', value: formatKvNumber(Number(data.confidenceContribution)) },
          { label: 'Regime', value: String(data.regime ?? '—') },
          { label: 'Timeframe', value: String(data.timeframe ?? '—') },
          { label: 'Entry Price', value: formatKvNumber(Number(data.entryPrice)) },
          { label: 'Stop Loss', value: formatKvNumber(Number(data.suggestedStopLoss ?? data.stopLoss)) },
          { label: 'Take Profit', value: formatKvNumber(Number(data.suggestedTakeProfit ?? data.takeProfit)) },
          { label: 'Reason', value: String(data.reason ?? '—') },
        ]}
      />
      <JsonViewerCollapsed value={result} />
    </>
  );
}

export function AiDecisionView({ decision }: { decision: unknown }) {
  const data = asRecord(decision);
  const reasons = Array.isArray(data.reasons) ? data.reasons : [];
  const warnings = Array.isArray(data.warnings) ? data.warnings : [];

  return (
    <>
      <KeyValueGrid
        items={[
          { label: 'Market Regime', value: String(data.marketRegime ?? '—') },
          { label: 'Confidence Score', value: formatKvNumber(Number(data.confidenceScore)) },
          { label: 'Classification', value: String(data.classification ?? '—') },
          { label: 'Anomaly Severity', value: String(data.severity ?? data.anomalySeverity ?? '—') },
          { label: 'Trade Allowed', value: data.tradeAllowed === true ? 'Yes' : data.tradeAllowed === false ? 'No' : '—' },
          { label: 'Summary', value: String(data.summary ?? '—') },
        ]}
      />
      {reasons.length > 0 ? (
        <div className="mt-3">
          <p className="text-xs uppercase text-slate-500">Reasons</p>
          <ul className="mt-1 list-disc pl-5 text-sm text-slate-300">
            {reasons.map((item) => (
              <li key={String(item)}>{String(item)}</li>
            ))}
          </ul>
        </div>
      ) : null}
      {warnings.length > 0 ? (
        <div className="mt-3">
          <p className="text-xs uppercase text-slate-500">Warnings</p>
          <ul className="mt-1 list-disc pl-5 text-sm text-amber-200">
            {warnings.map((item) => (
              <li key={String(item)}>{String(item)}</li>
            ))}
          </ul>
        </div>
      ) : null}
      <JsonViewerCollapsed value={decision} />
    </>
  );
}

export function RiskDecisionView({ decision }: { decision: unknown }) {
  const data = asRecord(decision);
  return (
    <>
      <KeyValueGrid
        items={[
          { label: 'Decision', value: String(data.decision ?? '—') },
          { label: 'Approved', value: data.approved === true ? 'Yes' : data.approved === false ? 'No' : '—' },
          { label: 'Rejected Rule', value: String(data.rejectedRuleKey ?? '—') },
          { label: 'Reason', value: String(data.reason ?? '—') },
          { label: 'Position Size', value: formatKvNumber(Number(data.positionSize)) },
          { label: 'Risk Amount', value: formatKvNumber(Number(data.riskAmount)) },
          { label: 'Stop Loss', value: formatKvNumber(Number(data.stopLoss)) },
          { label: 'Take Profit', value: formatKvNumber(Number(data.takeProfit)) },
        ]}
      />
      <JsonViewerCollapsed value={decision} />
    </>
  );
}

export function ReplayFramePanel({ frame }: { frame: Record<string, unknown> }) {
  return (
    <div className="space-y-6">
      <KeyValueGrid
        items={[
          { label: 'Symbol', value: String(frame.symbol ?? '—') },
          { label: 'Timeframe', value: String(frame.timeframe ?? '—') },
          { label: 'Timestamp', value: formatKvDate(String(frame.timestampUtc ?? '')) },
          { label: 'Market Regime', value: String(frame.marketRegime ?? '—') },
          { label: 'Balance', value: formatKvNumber(Number(frame.balance)) },
          { label: 'Equity', value: formatKvNumber(Number(frame.equity)) },
        ]}
      />
      <div>
        <h3 className="mb-2 text-sm font-medium text-slate-300">Explanation</h3>
        <p className="text-sm text-slate-300">{String(frame.humanReadableExplanation ?? '—')}</p>
      </div>
      <div>
        <h3 className="mb-2 text-sm font-medium text-slate-300">Candle</h3>
        <CandleView candle={frame.candle} />
      </div>
      {frame.indicatorSnapshot ? (
        <div>
          <h3 className="mb-2 text-sm font-medium text-slate-300">Indicators</h3>
          <IndicatorSnapshotView snapshot={frame.indicatorSnapshot} />
        </div>
      ) : null}
      {Array.isArray(frame.strategyResults) && frame.strategyResults.length > 0 ? (
        <div className="space-y-3">
          <h3 className="text-sm font-medium text-slate-300">Strategy Results</h3>
          {frame.strategyResults.map((result, index) => (
            <StrategyResultView key={index} result={result} />
          ))}
        </div>
      ) : null}
      {frame.aiDecision ? (
        <div>
          <h3 className="mb-2 text-sm font-medium text-slate-300">AI Decision</h3>
          <AiDecisionView decision={frame.aiDecision} />
        </div>
      ) : null}
      {frame.riskDecision ? (
        <div>
          <h3 className="mb-2 text-sm font-medium text-slate-300">Risk Decision</h3>
          <RiskDecisionView decision={frame.riskDecision} />
        </div>
      ) : null}
      <JsonViewerCollapsed value={frame} />
    </div>
  );
}
