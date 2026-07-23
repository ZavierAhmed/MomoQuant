import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { Badge } from '@/components/common/Badge';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { EmptyState } from '@/components/common/EmptyState';
import { DataTable } from '@/components/common/DataTable';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { ValidationSummary } from '@/components/common/ValidationSummary';
import { StatusPill } from '@/components/common/StatusPill';
import { FormPanel } from '@/components/common/FormPanel';
import { KeyValueGrid } from '@/components/common/KeyValueGrid';
import { FormActions } from '@/components/forms/FormActions';
import { SelectField, TextField, CheckboxField } from '@/components/forms/fields';
import { DateRangeOnlySelector } from '@/components/forms/DateRangeOnlySelector';
import { StrategyResultView } from '@/components/formatters/TradingViews';
import { JsonViewerCollapsed } from '@/components/common/JsonViewerCollapsed';
import { ExchangeSymbolSelector } from '@/components/strategies/ExchangeSymbolSelector';
import { TIMEFRAME_OPTIONS, normalizeTimeframe, timeframeLabel } from '@/constants/timeframes';
import { MARKET_REGIME_OPTIONS } from '@/constants/tradingOptions';
import { useAsync } from '@/hooks/useAsync';
import { useRole } from '@/hooks/useRole';
import { strategiesApi } from '@/api/strategiesApi';
import { marketDataApi } from '@/api/marketDataApi';
import { aiApi, type AiSetupAdvisorResponse } from '@/api/aiApi';
import { parseApiClientError } from '@/utils/apiError';
import { requireNumber } from '@/utils/numbers';
import { buildUtcRange } from '@/utils/formHelpers';
import type { MarketCandle, Strategy, StrategyParameter } from '@/api/domainTypes';

export function StrategiesPage() {
  const { canEdit } = useRole();
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [saveMessage, setSaveMessage] = useState<string | null>(null);
  const [formErrors, setFormErrors] = useState<Record<string, string>>({});
  const [evaluateResults, setEvaluateResults] = useState<unknown[]>([]);
  const [evaluateRaw, setEvaluateRaw] = useState<Record<string, unknown> | null>(null);
  const [advisor, setAdvisor] = useState<AiSetupAdvisorResponse | null>(null);
  const [advisorLoading, setAdvisorLoading] = useState(false);
  const [parameterDrafts, setParameterDrafts] = useState<StrategyParameter[]>([]);
  const [evaluateForm, setEvaluateForm] = useState({
    exchangeId: '' as number | '',
    symbolId: '' as number | '',
    timeframe: '3m',
    fromUtc: '',
    toUtc: '',
    candleId: '' as number | '',
    marketRegime: 'Trending',
    evaluateAllEnabled: false,
  });
  const [loadedCandles, setLoadedCandles] = useState<MarketCandle[]>([]);
  const [candlesMessage, setCandlesMessage] = useState<string | null>(null);
  const [loadingCandles, setLoadingCandles] = useState(false);

  const strategies = useAsync(() => strategiesApi.list(), []);
  const detail = useAsync(
    () => (selectedId ? strategiesApi.get(selectedId) : Promise.resolve(null)),
    [selectedId],
  );
  const parameters = useAsync(
    () => (selectedId ? strategiesApi.getParameters(selectedId) : Promise.resolve([] as StrategyParameter[])),
    [selectedId],
  );

  useEffect(() => {
    setParameterDrafts(parameters.data ?? []);
  }, [parameters.data]);

  async function toggleStrategy(strategy: Strategy, enable: boolean) {
    if (!canEdit) return;
    setActionError(null);
    setSaveMessage(null);
    try {
      if (enable) await strategiesApi.enable(strategy.id);
      else await strategiesApi.disable(strategy.id);
      strategies.reload();
      if (selectedId === strategy.id) detail.reload();
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  async function saveParameters() {
    if (!canEdit || !selectedId) return;
    setActionError(null);
    setSaveMessage(null);
    try {
      await strategiesApi.updateParameters(
        selectedId,
        parameterDrafts.map((parameter) => ({
          parameterKey: parameter.parameterKey,
          parameterValue: parameter.parameterValue,
          timeframe: parameter.timeframe ?? '3m',
          symbolId: parameter.symbolId,
          valueType: parameter.valueType,
        })),
      );
      parameters.reload();
      setSaveMessage('Parameters updated successfully.');
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  async function handleLoadCandles() {
    const errors: Record<string, string> = {};
    if (!evaluateForm.symbolId) errors.symbolId = 'Symbol is required.';
    if (!evaluateForm.timeframe) errors.timeframe = 'Timeframe is required.';
    if (!evaluateForm.fromUtc) errors.fromUtc = 'From date is required.';
    if (!evaluateForm.toUtc) errors.toUtc = 'To date is required.';
    setFormErrors(errors);
    if (Object.keys(errors).length) return;

    setActionError(null);
    setCandlesMessage(null);
    setLoadingCandles(true);
    try {
      const range = buildUtcRange(evaluateForm.fromUtc, evaluateForm.toUtc);
      const candles = await marketDataApi.getCandles({
        symbolId: requireNumber(evaluateForm.symbolId, 'Symbol'),
        timeframe: normalizeTimeframe(evaluateForm.timeframe),
        fromUtc: range.fromUtc,
        toUtc: range.toUtc,
        limit: 600,
      });
      setLoadedCandles(candles);
      if (candles.length === 0) {
        setCandlesMessage('No candles found for this symbol/timeframe/date range. Import candles first from Market Watch.');
        setEvaluateForm((current) => ({ ...current, candleId: '' }));
      } else {
        setEvaluateForm((current) => ({ ...current, candleId: candles[candles.length - 1].id }));
        setCandlesMessage(`${candles.length} candles loaded. Latest candle selected by default.`);
      }
    } catch (error) {
      setLoadedCandles([]);
      setEvaluateForm((current) => ({ ...current, candleId: '' }));
      setActionError(parseApiClientError(error).message);
    } finally {
      setLoadingCandles(false);
    }
  }

  async function handleEvaluateLatest() {
    if (!canEdit || !selectedId) return;
    const errors: Record<string, string> = {};
    if (!evaluateForm.symbolId) errors.symbolId = 'Symbol is required.';
    if (!evaluateForm.timeframe) errors.timeframe = 'Timeframe is required.';
    if (!evaluateForm.marketRegime) errors.marketRegime = 'Market regime is required.';
    setFormErrors(errors);
    if (Object.keys(errors).length) return;

    setActionError(null);
    try {
      const result = await strategiesApi.evaluateLatest({
        symbolId: requireNumber(evaluateForm.symbolId, 'Symbol'),
        timeframe: normalizeTimeframe(evaluateForm.timeframe),
        marketRegime: evaluateForm.marketRegime,
        strategyIds: evaluateForm.evaluateAllEnabled ? undefined : [selectedId],
      });
      setEvaluateRaw(result);
      const results = Array.isArray(result.results) ? result.results : [];
      setEvaluateResults(results);
    } catch (error) {
      setEvaluateResults([]);
      setEvaluateRaw(null);
      setActionError(parseApiClientError(error).message);
    }
  }

  async function handleEvaluate() {
    if (!canEdit || !selectedId) return;
    const errors: Record<string, string> = {};
    if (!evaluateForm.symbolId) errors.symbolId = 'Symbol is required.';
    if (!evaluateForm.timeframe) errors.timeframe = 'Timeframe is required.';
    if (!evaluateForm.marketRegime) errors.marketRegime = 'Market regime is required.';
    if (!evaluateForm.candleId) errors.candleId = 'Select a candle before running diagnostic evaluate.';
    setFormErrors(errors);
    if (Object.keys(errors).length) return;

    setActionError(null);
    try {
      const result = await strategiesApi.evaluate({
        symbolId: requireNumber(evaluateForm.symbolId, 'Symbol'),
        timeframe: normalizeTimeframe(evaluateForm.timeframe),
        candleId: requireNumber(evaluateForm.candleId, 'Candle'),
        marketRegime: evaluateForm.marketRegime,
        strategyIds: evaluateForm.evaluateAllEnabled ? undefined : [selectedId],
      });
      setEvaluateRaw(result);
      const results = Array.isArray(result.results) ? result.results : [];
      setEvaluateResults(results);
    } catch (error) {
      setEvaluateResults([]);
      setEvaluateRaw(null);
      setActionError(parseApiClientError(error).message);
    }
  }

  async function askAdvisorForDiagnostic() {
    if (!selectedId || !evaluateForm.symbolId) {
      setActionError('Select strategy and symbol before asking AI advisor.');
      return;
    }

    setAdvisorLoading(true);
    setActionError(null);
    try {
      const response = await aiApi.setupAdvisor({
        mode: 'StrategyDiagnostic',
        symbolIds: [requireNumber(evaluateForm.symbolId, 'Symbol')],
        strategyIds: [selectedId],
        fromDate: evaluateForm.fromUtc ? evaluateForm.fromUtc.slice(0, 10) : undefined,
        toDate: evaluateForm.toUtc ? evaluateForm.toUtc.slice(0, 10) : undefined,
        useAiScoring: false,
      });
      setAdvisor(response);
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    } finally {
      setAdvisorLoading(false);
    }
  }

  return (
    <div>
      <PageHeader title="Strategies" description="Strategy catalog, parameters, and diagnostics." />
      <ApiErrorAlert message={actionError} />
      {saveMessage ? <p className="mb-4 text-sm text-emerald-300">{saveMessage}</p> : null}

      {canEdit && selectedId ? (
        <FormPanel title="Diagnostic Evaluate" description="Run a one-off strategy evaluation against stored Binance candle data.">
          <ValidationSummary errors={formErrors} />
          <div className="grid gap-4 md:grid-cols-3">
            <ExchangeSymbolSelector
              selectedExchangeId={evaluateForm.exchangeId}
              selectedSymbolIds={evaluateForm.symbolId ? [Number(evaluateForm.symbolId)] : []}
              onExchangeChange={(exchangeId) =>
                setEvaluateForm((current) => ({ ...current, exchangeId, symbolId: '', candleId: '' }))
              }
              onSymbolsChange={(symbolIds) =>
                setEvaluateForm((current) => ({ ...current, symbolId: symbolIds[0] ?? '', candleId: '' }))
              }
              multiSelect={false}
              required
              exchangeError={formErrors.exchangeId}
              symbolsError={formErrors.symbolId}
            />
            <SelectField
              label="Timeframe"
              value={evaluateForm.timeframe}
              onChange={(value) => setEvaluateForm((current) => ({ ...current, timeframe: value || '15m', candleId: '' }))}
              options={TIMEFRAME_OPTIONS}
              required
              error={formErrors.timeframe}
            />
            <DateRangeOnlySelector
              fromDate={evaluateForm.fromUtc}
              toDate={evaluateForm.toUtc}
              onChange={({ fromDate, toDate }) =>
                setEvaluateForm((current) => ({ ...current, fromUtc: fromDate, toUtc: toDate, candleId: '' }))
              }
              required
              errors={{ fromDate: formErrors.fromUtc, toDate: formErrors.toUtc }}
            />
            <SelectField
              label="Market Regime"
              value={evaluateForm.marketRegime}
              onChange={(value) => setEvaluateForm((current) => ({ ...current, marketRegime: value || 'Trending' }))}
              options={MARKET_REGIME_OPTIONS}
              required
              error={formErrors.marketRegime}
            />
          </div>
          <FormActions>
            <button
              type="button"
              onClick={() => void handleLoadCandles()}
              disabled={loadingCandles}
              className="rounded-lg border border-slate-600 px-4 py-2 text-sm text-slate-200 hover:bg-slate-800 disabled:opacity-50"
            >
              {loadingCandles ? 'Loading Candles...' : 'Load Candles'}
            </button>
          </FormActions>
          {candlesMessage ? <p className="mt-2 text-sm text-slate-400">{candlesMessage}</p> : null}
          {loadedCandles.length > 0 ? (
            <div className="mt-4">
              <SelectField
                label="Candle"
                value={evaluateForm.candleId}
                onChange={(value) => setEvaluateForm((current) => ({ ...current, candleId: value }))}
                options={loadedCandles.map((candle) => ({
                  label: `${new Date(candle.openTimeUtc).toISOString()} | close ${candle.close} | vol ${candle.volume}`,
                  value: candle.id,
                }))}
                required
                error={formErrors.candleId}
              />
            </div>
          ) : null}
          <div className="mt-4">
            <CheckboxField
              label="Evaluate all enabled strategies"
              checked={evaluateForm.evaluateAllEnabled}
              onChange={(checked) => setEvaluateForm((current) => ({ ...current, evaluateAllEnabled: checked }))}
            />
          </div>
          <FormActions>
            <button
              type="button"
              onClick={() => void handleEvaluate()}
              className="rounded-lg border border-slate-600 px-4 py-2 text-sm text-slate-200 hover:bg-slate-800"
            >
              Run Diagnostic Evaluate
            </button>
            <button
              type="button"
              onClick={() => void handleEvaluateLatest()}
              className="rounded-lg border border-slate-600 px-4 py-2 text-sm text-slate-200 hover:bg-slate-800"
            >
              Evaluate Latest Candle
            </button>
            <button
              type="button"
              onClick={() => void askAdvisorForDiagnostic()}
              className="rounded-lg border border-slate-600 px-4 py-2 text-sm text-slate-200 hover:bg-slate-800"
            >
              {advisorLoading ? 'Asking AI…' : 'Ask AI Setup Advisor'}
            </button>
          </FormActions>
          {advisor ? (
            <div className="mt-3 rounded-lg border border-slate-800 p-3 text-xs text-slate-300">
              <p className="font-medium text-slate-100">{advisor.summary}</p>
              <p>Required timeframes: {advisor.requiredTimeframes.join(', ')}</p>
            </div>
          ) : null}
          {evaluateResults.length > 0 ? (
            <div className="mt-4 space-y-4">
              {evaluateRaw ? (
                <KeyValueGrid
                  items={[
                    { label: 'Candle Open', value: String((evaluateRaw as Record<string, unknown>).candleOpenTimeUtc ?? '—') },
                    { label: 'Candle Close', value: String((evaluateRaw as Record<string, unknown>).candleCloseTimeUtc ?? '—') },
                    { label: 'Candle Close Price', value: String((evaluateRaw as Record<string, unknown>).candleClose ?? '—') },
                  ]}
                />
              ) : null}
              {evaluateResults.map((result, index) => (
                <StrategyResultView key={index} result={result} />
              ))}
              {evaluateRaw ? <JsonViewerCollapsed value={evaluateRaw} label="Show Raw Data" /> : null}
            </div>
          ) : null}
        </FormPanel>
      ) : null}

      {strategies.loading ? <LoadingState /> : null}
      {strategies.error ? <ErrorState message={strategies.error} onRetry={strategies.reload} /> : null}

      {(strategies.data ?? []).length === 0 && !strategies.loading ? (
        <EmptyState title="No strategies" description="No strategies are configured yet." />
      ) : (
        <DataTable
          columns={[
            { key: 'name', header: 'Name', render: (row) => row.name },
            { key: 'code', header: 'Code', render: (row) => row.code },
            { key: 'version', header: 'Version', render: (row) => row.version },
            { key: 'enabled', header: 'Status', render: (row) => <StatusPill status={row.isEnabled ? 'Enabled' : 'Disabled'} /> },
            {
              key: 'actions',
              header: 'Actions',
              render: (row) => (
                <div className="flex gap-2">
                  <button type="button" onClick={() => setSelectedId(row.id)} className="text-xs underline">View</button>
                  <Link to={`/strategies/${row.code}`} className="text-xs underline text-sky-300">View details</Link>
                  {canEdit ? (
                    <button
                      type="button"
                      onClick={() => void toggleStrategy(row, !row.isEnabled)}
                      className={`rounded-md px-2 py-1 text-xs ${row.isEnabled ? 'border border-rose-500/40 text-rose-300' : 'border border-emerald-500/40 text-emerald-300'}`}
                    >
                      {row.isEnabled ? 'Disable' : 'Enable'}
                    </button>
                  ) : null}
                </div>
              ),
            },
          ]}
          rows={strategies.data ?? []}
        />
      )}

      {selectedId && detail.data ? (
        <section className="mt-6 space-y-4">
          <div className="rounded-xl border border-slate-800 bg-slate-900/40 p-4">
            <h2 className="text-lg font-medium text-slate-100">{detail.data.name}</h2>
            <p className="mt-1 text-sm text-slate-400">{detail.data.description}</p>
            <div className="mt-3 flex flex-wrap gap-2">
              <span className="text-xs text-slate-500">Supported regimes:</span>
              {(detail.data.supportedRegimes ?? []).length === 0 ? (
                <Badge>None listed</Badge>
              ) : (
                detail.data.supportedRegimes?.map((regime) => <Badge key={regime}>{String(regime)}</Badge>)
              )}
            </div>
            <div className="mt-2 flex flex-wrap gap-2">
              <span className="text-xs text-slate-500">Supported timeframes:</span>
              {(detail.data.supportedTimeframes ?? []).length === 0 ? (
                <Badge>None listed</Badge>
              ) : (
                detail.data.supportedTimeframes?.map((timeframe) => (
                  <Badge key={String(timeframe)} tone="info">{timeframeLabel(timeframe)}</Badge>
                ))
              )}
            </div>
          </div>

          <FormPanel title="Strategy Parameters" description="Edit parameter values for the selected strategy.">
            {parameters.loading ? <LoadingState /> : null}
            {(parameterDrafts.length ?? 0) === 0 && !parameters.loading ? (
              <EmptyState title="No parameters" description="This strategy has no editable parameters." />
            ) : (
              <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
                {parameterDrafts.map((parameter, index) => (
                  <TextField
                    key={parameter.id}
                    label={`${parameter.parameterKey}${parameter.timeframe ? ` (${parameter.timeframe})` : ''}`}
                    value={parameter.parameterValue}
                    onChange={(value) =>
                      setParameterDrafts((current) =>
                        current.map((item, itemIndex) => (itemIndex === index ? { ...item, parameterValue: value } : item)),
                      )
                    }
                    hint={`Type: ${parameter.valueType}`}
                  />
                ))}
              </div>
            )}
            {canEdit && parameterDrafts.length > 0 ? (
              <FormActions>
                <button type="button" onClick={() => void saveParameters()} className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950 hover:bg-white">
                  Save Parameters
                </button>
              </FormActions>
            ) : null}
          </FormPanel>
        </section>
      ) : null}
    </div>
  );
}
