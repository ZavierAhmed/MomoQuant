import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { LoadingState } from '@/components/common/LoadingState';
import { EmptyState } from '@/components/common/EmptyState';
import { DataTable } from '@/components/common/DataTable';
import { StatusPill } from '@/components/common/StatusPill';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { FormPanel } from '@/components/common/FormPanel';
import { formatDate } from '@/components/common/utils';
import { SkAnalysisResultView } from '@/components/tradingSystems/SkAnalysisResultView';
import { useAsync } from '@/hooks/useAsync';
import { useRole } from '@/hooks/useRole';
import { exchangesApi } from '@/api/exchangesApi';
import { symbolsApi } from '@/api/symbolsApi';
import { tradingSystemsApi, type SkSystemAnalysisResult } from '@/api/tradingSystemsApi';
import { parseApiClientError } from '@/utils/apiError';
import { triggerBlobDownload } from '@/utils/download';

import {
  getHtfLtfValidationError,
  getSelectedPairHelperText,
  RECOMMENDED_SK_TIMEFRAME_PAIRS,
  SUPPORTED_MARKET_TIMEFRAMES,
} from '@/constants/timeframes';

const BINANCE_FUTURES_CODE = 'BINANCE_FUTURES';
const SENSITIVITIES = ['Conservative', 'Balanced', 'Aggressive'];
const DIRECTION_MODES = [
  { value: 'Auto', label: 'Auto' },
  { value: 'BullishOnly', label: 'Bullish only' },
  { value: 'BearishOnly', label: 'Bearish only' },
];
const EXPLANATION_MODES = [
  { value: 'Beginner', label: 'Beginner — simple explanation' },
  { value: 'Intermediate', label: 'Intermediate — trading terms with explanations' },
  { value: 'Expert', label: 'Expert — detailed SK / Fibonacci / sequence diagnostics' },
];

const labelClass = 'block text-xs font-medium uppercase tracking-wide text-slate-400';
const inputClass =
  'mt-1 w-full rounded-lg border border-slate-700 bg-slate-900 px-3 py-2 text-sm text-slate-100 focus:border-sky-500 focus:outline-none';

export function SkSystemAnalyzerPage() {
  const { canEdit } = useRole();

  const exchanges = useAsync(() => exchangesApi.list({ page: 1, pageSize: 100 }), []);
  const binanceExchange = useMemo(
    () => exchanges.data?.items.find((exchange) => exchange.code === BINANCE_FUTURES_CODE) ?? null,
    [exchanges.data],
  );

  const symbols = useAsync(
    () =>
      binanceExchange
        ? symbolsApi.list({ page: 1, pageSize: 200, exchangeId: binanceExchange.id })
        : Promise.resolve(null),
    [binanceExchange?.id],
  );

  const analyses = useAsync(() => tradingSystemsApi.listAnalyses(50), []);
  useAsync(() => tradingSystemsApi.getDefaults(), []);

  const [symbolId, setSymbolId] = useState<number | ''>('');
  const [primaryTimeframe, setPrimaryTimeframe] = useState('15m');
  const [higherTimeframe, setHigherTimeframe] = useState('4h');
  const [lookback, setLookback] = useState(500);
  const [sensitivity, setSensitivity] = useState('Balanced');
  const [directionMode, setDirectionMode] = useState('Auto');
  const [explanationMode, setExplanationMode] = useState('Beginner');
  const [useAiSummary, setUseAiSummary] = useState(true);

  const [analyzing, setAnalyzing] = useState(false);
  const [importing, setImporting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [result, setResult] = useState<SkSystemAnalysisResult | null>(null);

  const availableSymbols = symbols.data?.items ?? [];
  const missingData = error?.toLowerCase().includes('import data first') ?? false;
  const timeframeError = getHtfLtfValidationError(higherTimeframe, primaryTimeframe);
  const timeframeHelper = getSelectedPairHelperText(higherTimeframe, primaryTimeframe);

  const effectiveSymbolId = symbolId === '' ? availableSymbols[0]?.id : symbolId;

  async function handleAnalyze() {
    if (!binanceExchange || !effectiveSymbolId) {
      setError('Select an exchange and symbol first.');
      return;
    }
    if (timeframeError) {
      setError(timeframeError);
      return;
    }
    setError(null);
    setMessage(null);
    setAnalyzing(true);
    try {
      const analysis = await tradingSystemsApi.analyze({
        exchangeId: binanceExchange.id,
        symbolId: Number(effectiveSymbolId),
        primaryTimeframe,
        higherTimeframe,
        lookbackCandles: lookback,
        swingSensitivity: sensitivity,
        directionMode,
        useAiSummary,
        explanationMode,
        quickViewMode: 'Beginner',
        autoImportMissingCandles: true,
      });
      setResult(analysis);
      analyses.reload();
    } catch (err) {
      setError(parseApiClientError(err).message);
    } finally {
      setAnalyzing(false);
    }
  }

  async function handleImport() {
    if (!binanceExchange || !effectiveSymbolId) {
      return;
    }
    setError(null);
    setMessage(null);
    setImporting(true);
    try {
      const imports = await tradingSystemsApi.importRequiredData({
        exchangeId: binanceExchange.id,
        symbolId: Number(effectiveSymbolId),
        primaryTimeframe,
        higherTimeframe,
        lookbackCandles: lookback,
      });
      setMessage(
        `Started ${imports.length} import(s). Wait for imports to complete on the Market Watch page, then analyze again.`,
      );
    } catch (err) {
      setError(parseApiClientError(err).message);
    } finally {
      setImporting(false);
    }
  }

  const [downloadingId, setDownloadingId] = useState<number | null>(null);

  async function handleDownloadPdf(id: number) {
    setDownloadingId(id);
    setError(null);
    try {
      const { blob, fileName } = await tradingSystemsApi.exportAnalysisPdf(id);
      triggerBlobDownload(blob, fileName);
    } catch {
      setError('Could not generate PDF report. Please try again.');
    } finally {
      setDownloadingId(null);
    }
  }

  async function handleDelete(id: number) {
    if (!canEdit) return;
    try {
      await tradingSystemsApi.deleteAnalysis(id);
      if (result?.analysisId === id) {
        setResult(null);
      }
      analyses.reload();
    } catch (err) {
      setError(parseApiClientError(err).message);
    }
  }

  return (
    <div>
      <PageHeader
        title="SK System Analyzer"
        description="Analyze market structure using HTF/LTF context. Analysis only — not a trade signal."
      />

      <p className="mb-4 rounded-lg border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-sm text-amber-100">
        This is chart analysis only. It does not create trades, orders, benchmark runs, or bot signals.
      </p>

      <ApiErrorAlert message={error} />
      {message ? <p className="mb-4 text-sm text-emerald-300">{message}</p> : null}

      {exchanges.loading ? <LoadingState /> : null}

      {!exchanges.loading && !binanceExchange ? (
        <EmptyState
          title="No Binance Futures exchange"
          description="Configure the Binance Futures exchange from Admin → System Cleanup before running analysis."
        />
      ) : null}

      {binanceExchange ? (
        <FormPanel
          title="Analysis inputs"
          description="Trading systems do not execute trades. This produces chart analysis and scenarios only."
        >
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <div>
              <label className={labelClass}>Exchange</label>
              <input className={inputClass} value={binanceExchange.name} disabled />
            </div>
            <div>
              <label className={labelClass} htmlFor="sk-symbol">
                Symbol
              </label>
              <select
                id="sk-symbol"
                className={inputClass}
                value={effectiveSymbolId}
                onChange={(event) => setSymbolId(event.target.value === '' ? '' : Number(event.target.value))}
              >
                {availableSymbols.length === 0 ? <option value="">No symbols added yet</option> : null}
                {availableSymbols.map((symbol) => (
                  <option key={symbol.id} value={symbol.id}>
                    {symbol.symbol}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className={labelClass} htmlFor="sk-higher">
                Bigger picture chart (higher timeframe)
              </label>
              <p className="mb-1 text-xs text-slate-500">Used to understand the main direction and major zones.</p>
              <select
                id="sk-higher"
                className={inputClass}
                value={higherTimeframe}
                onChange={(event) => setHigherTimeframe(event.target.value)}
              >
                {SUPPORTED_MARKET_TIMEFRAMES.map((tf) => (
                  <option key={tf.value} value={tf.value}>
                    {tf.label}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className={labelClass} htmlFor="sk-primary">
                Analysis chart (primary timeframe)
              </label>
              <p className="mb-1 text-xs text-slate-500">Used to inspect the current setup and reaction areas.</p>
              <select
                id="sk-primary"
                className={inputClass}
                value={primaryTimeframe}
                onChange={(event) => setPrimaryTimeframe(event.target.value)}
              >
                {SUPPORTED_MARKET_TIMEFRAMES.map((tf) => (
                  <option key={tf.value} value={tf.value}>
                    {tf.label}
                  </option>
                ))}
              </select>
            </div>
            <div className="md:col-span-2">
              <p className={`text-xs ${timeframeError ? 'text-rose-300' : 'text-slate-500'}`}>{timeframeHelper}</p>
              <p className="mt-1 text-xs text-slate-600">
                Recommended pairs:{' '}
                {RECOMMENDED_SK_TIMEFRAME_PAIRS.map((pair) => `${pair.higher} / ${pair.primary}`).join(' · ')}
              </p>
            </div>
            <div>
              <label className={labelClass} htmlFor="sk-lookback">
                How much chart history to analyze
              </label>
              <input
                id="sk-lookback"
                type="number"
                min={50}
                max={1000}
                className={inputClass}
                value={lookback}
                onChange={(event) => setLookback(Number(event.target.value))}
              />
            </div>
            <div>
              <label className={labelClass} htmlFor="sk-sensitivity">
                Swing sensitivity
              </label>
              <select
                id="sk-sensitivity"
                className={inputClass}
                value={sensitivity}
                onChange={(event) => setSensitivity(event.target.value)}
              >
                {SENSITIVITIES.map((value) => (
                  <option key={value} value={value}>
                    {value}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className={labelClass} htmlFor="sk-direction">
                Sequence direction mode
              </label>
              <select
                id="sk-direction"
                className={inputClass}
                value={directionMode}
                onChange={(event) => setDirectionMode(event.target.value)}
              >
                {DIRECTION_MODES.map((mode) => (
                  <option key={mode.value} value={mode.value}>
                    {mode.label}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className={labelClass} htmlFor="sk-explanation">
                Explanation level
              </label>
              <select
                id="sk-explanation"
                className={inputClass}
                value={explanationMode}
                onChange={(event) => setExplanationMode(event.target.value)}
              >
                {EXPLANATION_MODES.map((mode) => (
                  <option key={mode.value} value={mode.value}>
                    {mode.label}
                  </option>
                ))}
              </select>
            </div>
            <div className="flex items-center gap-2 pt-6">
              <input
                id="sk-ai"
                type="checkbox"
                checked={useAiSummary}
                onChange={(event) => setUseAiSummary(event.target.checked)}
              />
              <label htmlFor="sk-ai" className="text-sm text-slate-300">
                Use AI summary
              </label>
            </div>
          </div>

          <details className="mt-4 rounded-lg border border-slate-800 bg-slate-950/40 p-3">
            <summary className="cursor-pointer text-sm font-medium text-slate-300">Advanced options</summary>
            <p className="mt-2 text-xs text-slate-500">
              Turn on Fibonacci detail levels only when comparing structures. “All possible setups” can crowd the chart.
            </p>
            <div className="mt-3 grid gap-3 md:grid-cols-2">
              <label className="flex items-center gap-2 text-sm text-slate-300">
                <input type="checkbox" checked={showAdvanced} onChange={(e) => setShowAdvanced(e.target.checked)} />
                Show advanced panel while analyzing
              </label>
            </div>
          </details>

          <p className="mt-3 rounded-lg border border-sky-500/30 bg-sky-500/10 px-3 py-2 text-xs text-sky-200">
            This is chart analysis only. It is not a trade signal. No orders, backtests, or paper
            sessions are created.
          </p>

          <div className="mt-4 flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => void handleAnalyze()}
              disabled={analyzing || !canEdit || availableSymbols.length === 0}
              className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950 hover:bg-white disabled:opacity-50"
            >
              {analyzing ? 'Analyzing…' : 'Analyze'}
            </button>
            {missingData ? (
              <button
                type="button"
                onClick={() => void handleImport()}
                disabled={importing || !canEdit}
                className="rounded-lg border border-sky-500/40 px-4 py-2 text-sm text-sky-200 hover:bg-sky-500/10 disabled:opacity-50"
              >
                {importing ? 'Starting import…' : 'Import required data'}
              </button>
            ) : null}
          </div>
          {availableSymbols.length === 0 ? (
            <p className="mt-2 text-xs text-amber-300">
              No symbols have been added yet. Go to Exchanges &amp; Symbols, discover Binance Futures
              symbols, and add BTCUSDT or another symbol first.
            </p>
          ) : null}
          {missingData ? (
            <p className="mt-2 text-xs text-amber-300">
              {availableSymbols.find((s) => s.id === Number(effectiveSymbolId))?.symbol ?? 'This symbol'}{' '}
              does not have enough {primaryTimeframe} or {higherTimeframe} candles for this analysis.
              Import the required public Binance data, then analyze again.
            </p>
          ) : null}
          {!canEdit ? (
            <p className="mt-2 text-xs text-slate-500">Analysis requires an Admin or Trader role.</p>
          ) : null}
        </FormPanel>
      ) : null}

      {result ? (
        <section className="mb-8">
          <SkAnalysisResultView result={result} />
        </section>
      ) : null}

      <section>
        <h2 className="mb-3 text-sm font-medium text-slate-300">Previous analyses</h2>
        {analyses.loading ? <LoadingState /> : null}
        {!analyses.loading && (analyses.data ?? []).length === 0 ? (
          <EmptyState
            title="No saved analyses"
            description="Run an analysis above. Saved analyses appear here and can be revisited later."
          />
        ) : null}
        {(analyses.data ?? []).length > 0 ? (
          <DataTable
            columns={[
              { key: 'date', header: 'Date', render: (row) => formatDate(row.createdAtUtc) },
              { key: 'symbol', header: 'Symbol', render: (row) => row.symbol },
              {
                key: 'charts',
                header: 'Charts',
                render: (row) => `${row.primaryTimeframe} + ${row.higherTimeframe}`,
              },
              { key: 'bias', header: 'Direction', render: (row) => <StatusPill status={row.marketBias} /> },
              { key: 'confidence', header: 'Clarity', render: (row) => row.confidenceLabel },
              {
                key: 'conclusion',
                header: 'Main conclusion',
                render: (row) => (
                  <span className="block max-w-md text-xs text-slate-400">{row.conclusion || '—'}</span>
                ),
              },
              {
                key: 'actions',
                header: 'Actions',
                render: (row) => (
                  <div className="flex gap-2">
                    <Link
                      to={`/trading-systems/sk/analyses/${row.id}`}
                      className="rounded-md border border-slate-600 px-2 py-1 text-xs text-slate-200 hover:bg-slate-800"
                    >
                      View
                    </Link>
                    <button
                      type="button"
                      onClick={() => void handleDownloadPdf(row.id)}
                      disabled={downloadingId === row.id}
                      className="rounded-md border border-sky-500/40 px-2 py-1 text-xs text-sky-200 hover:bg-sky-500/10 disabled:opacity-50"
                    >
                      {downloadingId === row.id ? 'Generating…' : 'Download PDF'}
                    </button>
                    {canEdit ? (
                      <button
                        type="button"
                        onClick={() => void handleDelete(row.id)}
                        className="rounded-md border border-rose-500/40 px-2 py-1 text-xs text-rose-300 hover:bg-rose-500/10"
                      >
                        Delete
                      </button>
                    ) : null}
                  </div>
                ),
              },
            ]}
            rows={analyses.data ?? []}
          />
        ) : null}
      </section>
    </div>
  );
}
