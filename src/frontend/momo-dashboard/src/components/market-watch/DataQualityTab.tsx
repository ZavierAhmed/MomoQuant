import { useState } from 'react';
import { FormPanel } from '@/components/common/FormPanel';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { LoadingState } from '@/components/common/LoadingState';
import { EmptyState } from '@/components/common/EmptyState';
import { MetricCard } from '@/components/common/MetricCard';
import { ValidationSummary } from '@/components/common/ValidationSummary';
import { SymbolWithExchangeLabel } from '@/components/common/SymbolWithExchangeLabel';
import { ExchangeSelect, SymbolSelect } from '@/components/selects/EntitySelects';
import { DateTimeField, SelectField } from '@/components/forms/fields';
import { FormActions } from '@/components/forms/FormActions';
import { TIMEFRAME_OPTIONS } from '@/constants/tradingOptions';
import { useReferenceData } from '@/hooks/useReferenceData';
import { marketDataApi, type MarketDataQuality } from '@/api/marketDataApi';
import { parseApiClientError } from '@/utils/apiError';
import { buildUtcRange, validateUtcRangeFields } from '@/utils/formHelpers';
import { requireNumber } from '@/utils/numbers';
import { formatDate, formatNumber } from '@/components/common/utils';

export function DataQualityTab() {
  const [exchangeId, setExchangeId] = useState<number | ''>('');
  const reference = useReferenceData(exchangeId || null);
  const [symbolId, setSymbolId] = useState<number | ''>('');
  const [timeframe, setTimeframe] = useState('3m');
  const [fromUtc, setFromUtc] = useState('');
  const [toUtc, setToUtc] = useState('');
  const [formErrors, setFormErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [quality, setQuality] = useState<MarketDataQuality | null>(null);

  async function handleCheck() {
    const errors: Record<string, string> = {};
    if (!exchangeId) errors.exchangeId = 'Exchange is required.';
    if (!symbolId) errors.symbolId = 'Symbol is required.';
    Object.assign(errors, validateUtcRangeFields(fromUtc, toUtc));
    setFormErrors(errors);
    if (Object.keys(errors).length) return;

    setLoading(true);
    setError(null);
    try {
      const range = buildUtcRange(fromUtc, toUtc);
      const result = await marketDataApi.getQuality({
        exchangeId: requireNumber(exchangeId, 'Exchange'),
        symbolId: requireNumber(symbolId, 'Symbol'),
        timeframe,
        fromUtc: range.fromUtc,
        toUtc: range.toUtc,
      });
      setQuality(result);
    } catch (checkError) {
      setError(parseApiClientError(checkError).message);
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="space-y-6">
      <ApiErrorAlert message={error} />
      <ValidationSummary errors={formErrors} />

      <FormPanel title="Data Quality Check" description="Inspect candle coverage, duplicates, and gaps for a symbol range.">
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          <ExchangeSelect label="Exchange" value={exchangeId} onChange={(value) => { setExchangeId(value); setSymbolId(''); }} options={reference.exchangeOptions} required error={formErrors.exchangeId} />
          <SymbolSelect label="Symbol" value={symbolId} onChange={setSymbolId} options={reference.symbolOptions} required error={formErrors.symbolId} />
          <SelectField label="Timeframe" value={timeframe} onChange={(value) => setTimeframe(value || '3m')} options={TIMEFRAME_OPTIONS} required />
          <DateTimeField label="From (UTC)" value={fromUtc} onChange={setFromUtc} required error={formErrors.fromUtc} />
          <DateTimeField label="To (UTC)" value={toUtc} onChange={setToUtc} required error={formErrors.toUtc} />
        </div>
        <FormActions>
          <button type="button" onClick={() => void handleCheck()} disabled={loading} className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950">
            Run Quality Check
          </button>
        </FormActions>
      </FormPanel>

      {loading ? <LoadingState /> : null}

      {quality ? (
        <div className="space-y-6">
          <div className="grid gap-3 md:grid-cols-3 xl:grid-cols-6">
            <MetricCard label="Total Candles" value={quality.totalCandles} />
            <MetricCard label="Expected Candles" value={quality.expectedCandles} />
            <MetricCard label="Missing Candles" value={quality.missingCandles} />
            <MetricCard label="Duplicate Candles" value={quality.duplicateCandles} />
            <MetricCard label="Coverage" value={`${formatNumber(quality.coveragePercent)}%`} />
            <div className="rounded-xl border border-slate-800 bg-slate-900/60 p-4">
              <p className="text-xs uppercase tracking-wide text-slate-400">Symbol</p>
              <p className="mt-2 text-sm font-medium text-slate-100">
                <SymbolWithExchangeLabel symbolId={quality.symbolId} symbols={reference.allSymbols} exchanges={reference.exchanges} />
              </p>
            </div>
          </div>

          <div className="grid gap-3 md:grid-cols-2">
            <MetricCard label="First Open Time" value={formatDate(quality.firstOpenTimeUtc) ?? '—'} />
            <MetricCard label="Last Open Time" value={formatDate(quality.lastOpenTimeUtc) ?? '—'} />
          </div>

          <FormPanel title="Gaps" description="Missing candle intervals detected in the selected range.">
            {quality.gaps.length === 0 ? (
              <EmptyState title="No gaps detected" description="Candle coverage appears continuous for the selected range." />
            ) : (
              <div className="overflow-x-auto">
                <table className="min-w-full text-left text-sm text-slate-200">
                  <thead className="border-b border-slate-700 text-slate-400">
                    <tr>
                      <th className="px-3 py-2">From (UTC)</th>
                      <th className="px-3 py-2">To (UTC)</th>
                      <th className="px-3 py-2">Missing Count</th>
                    </tr>
                  </thead>
                  <tbody>
                    {quality.gaps.map((gap) => (
                      <tr key={`${gap.fromUtc}-${gap.toUtc}`} className="border-b border-slate-800">
                        <td className="px-3 py-2">{formatDate(gap.fromUtc)}</td>
                        <td className="px-3 py-2">{formatDate(gap.toUtc)}</td>
                        <td className="px-3 py-2">{gap.missingCount}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </FormPanel>
        </div>
      ) : !loading ? (
        <EmptyState title="No quality report yet" description="Select a symbol range and run a quality check." />
      ) : null}
    </div>
  );
}
