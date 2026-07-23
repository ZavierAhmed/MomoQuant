import { useState } from 'react';
import { PageHeader } from '@/components/common/PageHeader';
import { TabPanel } from '@/components/common/TabPanel';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { EmptyState } from '@/components/common/EmptyState';
import { MetricCard } from '@/components/common/MetricCard';
import { FormPanel } from '@/components/common/FormPanel';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { ValidationSummary } from '@/components/common/ValidationSummary';
import { SymbolWithExchangeLabel } from '@/components/common/SymbolWithExchangeLabel';
import { ExchangesTab } from '@/components/market-watch/ExchangesTab';
import { SymbolsTab } from '@/components/market-watch/SymbolsTab';
import { DataCleanupTab } from '@/components/market-watch/DataCleanupTab';
import { DataQualityTab } from '@/components/market-watch/DataQualityTab';
import { ExchangeSymbolSelector } from '@/components/strategies/ExchangeSymbolSelector';
import { LiveMarketTab } from '@/components/market-watch/LiveMarketTab';
import { DateRangeOnlySelector } from '@/components/forms/DateRangeOnlySelector';
import { SelectField } from '@/components/forms/fields';
import { FormActions } from '@/components/forms/FormActions';
import { TIMEFRAME_OPTIONS, normalizeTimeframe } from '@/constants/timeframes';
import { useAsync } from '@/hooks/useAsync';
import { useReferenceData } from '@/hooks/useReferenceData';
import { useRole } from '@/hooks/useRole';
import { marketDataApi } from '@/api/marketDataApi';
import type { MarketDataImport } from '@/api/domainTypes';
import { indicatorsApi } from '@/api/indicatorsApi';
import { monitoringApi } from '@/api/monitoringApi';
import { formatDate, formatNumber } from '@/components/common/utils';
import { parseApiClientError, applyFieldErrorsToForm } from '@/utils/apiError';
import { buildUtcRange, validateUtcRangeFields } from '@/utils/formHelpers';
import { requireNumber } from '@/utils/numbers';

export function MarketWatchPage() {
  const { canEdit, isAdmin } = useRole();
  const [tab, setTab] = useState('overview');
  const reference = useReferenceData();
  const pipeline = useAsync(() => monitoringApi.getTradingPipelineStatus(), []);
  const marketSettings = useAsync(() => marketDataApi.getSettings(), []);
  const serverImports = useAsync(() => marketDataApi.getRecentImports(20), []);

  const [snapshotExchangeId, setSnapshotExchangeId] = useState<number | ''>('');
  const [snapshotSymbolId, setSnapshotSymbolId] = useState<number | ''>('');
  const [snapshotTimeframe, setSnapshotTimeframe] = useState('15m');
  const snapshot = useAsync(
    () =>
      snapshotSymbolId
        ? marketDataApi.getSnapshot(Number(snapshotSymbolId), normalizeTimeframe(snapshotTimeframe))
        : Promise.resolve(null),
    [snapshotSymbolId, snapshotTimeframe],
  );

  const [importExchangeId, setImportExchangeId] = useState<number | ''>('');
  const [importSymbolId, setImportSymbolId] = useState<number | ''>('');
  const [importTimeframe, setImportTimeframe] = useState('3m');
  const [importFromUtc, setImportFromUtc] = useState('');
  const [importToUtc, setImportToUtc] = useState('');
  const [recalcExchangeId, setRecalcExchangeId] = useState<number | ''>('');
  const [recalcSymbolId, setRecalcSymbolId] = useState<number | ''>('');
  const [recalcTimeframe, setRecalcTimeframe] = useState('15m');
  const [recalcFromUtc, setRecalcFromUtc] = useState('');
  const [recalcToUtc, setRecalcToUtc] = useState('');
  const [formErrors, setFormErrors] = useState<Record<string, string>>({});
  const [actionError, setActionError] = useState<string | null>(null);
  const [actionMessage, setActionMessage] = useState<string | null>(null);
  const [lastImportResult, setLastImportResult] = useState<MarketDataImport | null>(null);

  const providerName = marketSettings.data?.historicalProvider ?? 'Unknown';
  const isBinanceProvider = providerName.toLowerCase() === 'binance';

  async function handleImport() {
    if (!canEdit) return;
    const errors: Record<string, string> = {};
    if (!importExchangeId) errors.importExchangeId = 'Exchange is required.';
    if (!importSymbolId) errors.importSymbolId = 'Symbol is required.';
    Object.assign(errors, validateUtcRangeFields(importFromUtc, importToUtc));
    if (errors.fromUtc) errors.importFromUtc = errors.fromUtc;
    if (errors.toUtc) errors.importToUtc = errors.toUtc;
    setFormErrors(errors);
    if (Object.keys(errors).length) return;

    setActionError(null);
    setActionMessage(null);
    setLastImportResult(null);
    const range = buildUtcRange(importFromUtc, importToUtc);
    try {
      const result = await marketDataApi.importCandles({
        exchangeId: requireNumber(importExchangeId, 'Exchange'),
        symbolId: requireNumber(importSymbolId, 'Symbol'),
        timeframe: normalizeTimeframe(importTimeframe),
        fromUtc: range.fromUtc,
        toUtc: range.toUtc,
        fromDate: importFromUtc,
        toDate: importToUtc,
      });
      setLastImportResult(result);
      setActionMessage(
        `Import completed: ${result.insertedCount} inserted, ${result.skippedDuplicateCount} duplicates skipped, ${result.totalReceived} received.`,
      );
      void serverImports.reload();
    } catch (error) {
      const parsed = parseApiClientError(error);
      setActionError(parsed.message);
      setFormErrors((current) => ({
        ...current,
        ...applyFieldErrorsToForm(parsed.fieldErrors, {
          exchangeId: 'importExchangeId',
          symbolId: 'importSymbolId',
          timeframe: 'importTimeframe',
          fromUtc: 'importFromUtc',
          toUtc: 'importToUtc',
        }),
      }));
    }
  }

  function applyImportToRecalc() {
    if (!lastImportResult) return;
    setRecalcSymbolId(lastImportResult.symbolId);
    setRecalcTimeframe(lastImportResult.timeframe);
    setRecalcFromUtc(lastImportResult.fromUtc.slice(0, 10));
    setRecalcToUtc(lastImportResult.toUtc.slice(0, 10));
    setTab('recalc');
  }

  async function handleRecalculate() {
    if (!canEdit) return;
    const errors: Record<string, string> = {};
    if (!recalcSymbolId) errors.recalcSymbolId = 'Symbol is required.';
    Object.assign(errors, validateUtcRangeFields(recalcFromUtc, recalcToUtc));
    if (errors.fromUtc) errors.recalcFromUtc = errors.fromUtc;
    if (errors.toUtc) errors.recalcToUtc = errors.toUtc;
    setFormErrors(errors);
    if (Object.keys(errors).length) return;

    setActionError(null);
    const range = buildUtcRange(recalcFromUtc, recalcToUtc);
    try {
      const result = await indicatorsApi.recalculate({
        symbolId: requireNumber(recalcSymbolId, 'Symbol'),
        timeframe: normalizeTimeframe(recalcTimeframe),
        fromUtc: range.fromUtc,
        toUtc: range.toUtc,
        fromDate: recalcFromUtc,
        toDate: recalcToUtc,
        autoImportMissingCandles: true,
      });
      setActionMessage(`Indicators recalculated: ${result.snapshotsUpdated} snapshots updated.`);
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  const activeSymbolCount = reference.allSymbols.filter((symbol) => symbol.isActive).length;
  const recentImports = serverImports.data ?? [];

  return (
    <div>
      <PageHeader title="Exchange & Symbol Management" description="Market watch, exchanges, symbols, candles, and snapshots." />
      <ApiErrorAlert message={actionError} />
      <ValidationSummary errors={formErrors} />
      {actionMessage ? <p className="mb-4 text-sm text-emerald-300">{actionMessage}</p> : null}

      {reference.loading ? <LoadingState /> : null}
      {reference.error ? <ErrorState message={reference.error} onRetry={reference.reloadAll} /> : null}

      <TabPanel
        active={tab}
        onChange={setTab}
        tabs={[
          { id: 'overview', label: 'Overview' },
          { id: 'exchanges', label: 'Exchanges' },
          { id: 'symbols', label: 'Symbols' },
          { id: 'live', label: 'Live Market' },
          { id: 'import', label: 'Candle Import' },
          { id: 'quality', label: 'Data Quality' },
          { id: 'recalc', label: 'Indicator Recalculation' },
          { id: 'snapshot', label: 'Market Snapshot' },
          ...(isAdmin ? [{ id: 'cleanup', label: 'Data Cleanup' }] : []),
        ]}
      >
        {tab === 'live' ? <LiveMarketTab /> : null}
        {tab === 'overview' ? (
          <div className="space-y-6">
            <div className="grid gap-4 md:grid-cols-4">
              <MetricCard label="Exchanges" value={reference.exchanges.length} />
              <MetricCard label="Active Symbols" value={activeSymbolCount} />
              <MetricCard label="Candles Available" value={snapshot.data?.candleCountAvailable ?? '—'} />
              <MetricCard label="Market Data" value={pipeline.data?.marketDataAvailable ? 'Available' : 'Unavailable'} />
            </div>
            {(pipeline.data?.warnings ?? []).length > 0 ? (
              <div className="rounded-xl border border-amber-500/30 bg-amber-500/10 p-4 text-sm text-amber-200">
                <p className="font-medium">Warnings</p>
                <ul className="mt-2 list-disc pl-5">
                  {pipeline.data?.warnings.map((warning) => (
                    <li key={warning}>{warning}</li>
                  ))}
                </ul>
              </div>
            ) : null}
            {snapshot.data ? (
              <div className="grid gap-3 md:grid-cols-4">
                <MetricCard label="Latest Symbol" value={snapshot.data.symbol} />
                <MetricCard label="Latest Price" value={formatNumber(snapshot.data.latestPrice)} />
                <MetricCard label="Timeframe" value={snapshot.data.timeframe} />
                <MetricCard label="Updated" value={formatDate(snapshot.data.latestUpdateTimeUtc) ?? '—'} />
              </div>
            ) : (
              <EmptyState title="No snapshot selected" description="Open the Market Snapshot tab to load the latest market snapshot." />
            )}
          </div>
        ) : null}

        {tab === 'exchanges' ? <ExchangesTab /> : null}
        {tab === 'symbols' ? <SymbolsTab /> : null}

        {tab === 'import' ? (
          canEdit ? (
            <div className="space-y-6">
              <FormPanel title="Import Candles" description="Import historical candles for backtesting and replay.">
                <div className="mb-4 rounded-lg border border-slate-800 bg-slate-900/50 px-4 py-3 text-sm text-slate-300">
                  <p>
                    Provider: <span className="font-medium text-slate-100">{providerName}</span>
                  </p>
                  {isBinanceProvider ? (
                    <p className="mt-2 text-slate-400">
                      Using Binance public market data. No API key is required. No real orders are placed.
                    </p>
                  ) : null}
                  <p className="mt-2 text-slate-400">
                    Tip: start with a small test import — BTCUSDT, 3m, 1 day — before importing larger ranges.
                  </p>
                </div>
                <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
                  <ExchangeSymbolSelector
                    selectedExchangeId={importExchangeId}
                    selectedSymbolIds={importSymbolId ? [Number(importSymbolId)] : []}
                    onExchangeChange={setImportExchangeId}
                    onSymbolsChange={(ids) => setImportSymbolId(ids[0] ?? '')}
                    multiSelect={false}
                    required
                    exchangeError={formErrors.importExchangeId}
                    symbolsError={formErrors.importSymbolId}
                  />
                  <SelectField label="Timeframe" value={importTimeframe} onChange={(value) => setImportTimeframe(value || '15m')} options={TIMEFRAME_OPTIONS} required />
                  <DateRangeOnlySelector
                    fromDate={importFromUtc}
                    toDate={importToUtc}
                    onChange={({ fromDate, toDate }) => {
                      setImportFromUtc(fromDate);
                      setImportToUtc(toDate);
                    }}
                    required
                    errors={{ fromDate: formErrors.importFromUtc, toDate: formErrors.importToUtc }}
                  />
                </div>
                <FormActions>
                  <button type="button" onClick={() => void handleImport()} className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950">
                    Import Candles
                  </button>
                </FormActions>
              </FormPanel>

              {lastImportResult ? (
                <FormPanel title="Import Result" description="Summary from the most recent import in this session.">
                  <div className="grid gap-3 md:grid-cols-4 text-sm text-slate-300">
                    <p>Inserted: <span className="text-slate-100">{lastImportResult.insertedCount}</span></p>
                    <p>Skipped duplicates: <span className="text-slate-100">{lastImportResult.skippedDuplicateCount}</span></p>
                    <p>Total received: <span className="text-slate-100">{lastImportResult.totalReceived}</span></p>
                    <p>Status: <span className="text-slate-100">{lastImportResult.status}</span></p>
                  </div>
                  <FormActions>
                    <button type="button" onClick={() => setTab('quality')} className="rounded-lg border border-slate-600 px-4 py-2 text-sm text-slate-200">
                      Open Data Quality
                    </button>
                    <button type="button" onClick={applyImportToRecalc} className="rounded-lg border border-slate-600 px-4 py-2 text-sm text-slate-200">
                      Recalculate Indicators for Same Range
                    </button>
                  </FormActions>
                </FormPanel>
              ) : null}

              {serverImports.loading ? <LoadingState /> : null}
              {recentImports.length > 0 ? (
                <FormPanel title="Recent Imports" description="Latest candle imports recorded by the backend.">
                  <div className="overflow-x-auto">
                    <table className="min-w-full text-left text-sm text-slate-200">
                      <thead className="border-b border-slate-700 text-slate-400">
                        <tr>
                          <th className="px-3 py-2">Symbol</th>
                          <th className="px-3 py-2">Timeframe</th>
                          <th className="px-3 py-2">Inserted</th>
                          <th className="px-3 py-2">Skipped</th>
                          <th className="px-3 py-2">Status</th>
                          <th className="px-3 py-2">Started</th>
                        </tr>
                      </thead>
                      <tbody>
                        {recentImports.map((item) => (
                          <tr key={item.importId} className="border-b border-slate-800">
                            <td className="px-3 py-2">
                              <SymbolWithExchangeLabel symbolId={item.symbolId} symbols={reference.allSymbols} exchanges={reference.exchanges} />
                            </td>
                            <td className="px-3 py-2">{item.timeframe}</td>
                            <td className="px-3 py-2">{item.insertedCount}</td>
                            <td className="px-3 py-2">{item.skippedDuplicateCount}</td>
                            <td className="px-3 py-2">{item.status}</td>
                            <td className="px-3 py-2">{formatDate(item.startedAtUtc)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </FormPanel>
              ) : null}
            </div>
          ) : (
            <EmptyState title="Read-only access" description="Candle import requires Admin or Trader role." />
          )
        ) : null}

        {tab === 'quality' ? <DataQualityTab /> : null}

        {tab === 'recalc' ? (
          canEdit ? (
            <FormPanel title="Recalculate Indicators" description="Rebuild indicator snapshots for a symbol range.">
              <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
                <ExchangeSymbolSelector
                  selectedExchangeId={recalcExchangeId}
                  selectedSymbolIds={recalcSymbolId ? [Number(recalcSymbolId)] : []}
                  onExchangeChange={setRecalcExchangeId}
                  onSymbolsChange={(ids) => setRecalcSymbolId(ids[0] ?? '')}
                  multiSelect={false}
                  required
                />
                <SelectField label="Timeframe" value={recalcTimeframe} onChange={(value) => setRecalcTimeframe(value || '15m')} options={TIMEFRAME_OPTIONS} required />
                <DateRangeOnlySelector
                  fromDate={recalcFromUtc}
                  toDate={recalcToUtc}
                  onChange={({ fromDate, toDate }) => {
                    setRecalcFromUtc(fromDate);
                    setRecalcToUtc(toDate);
                  }}
                  required
                  errors={{ fromDate: formErrors.recalcFromUtc, toDate: formErrors.recalcToUtc }}
                />
              </div>
              <FormActions>
                <button type="button" onClick={() => void handleRecalculate()} className="rounded-lg border border-slate-600 px-4 py-2 text-sm text-slate-200">
                  Recalculate Indicators
                </button>
              </FormActions>
            </FormPanel>
          ) : (
            <EmptyState title="Read-only access" description="Indicator recalculation requires Admin or Trader role." />
          )
        ) : null}

        {tab === 'snapshot' ? (
          <div className="space-y-6">
            <FormPanel title="Market Snapshot Filters" description="Load the latest market snapshot for a symbol and timeframe.">
              <div className="grid gap-4 md:grid-cols-2">
                <ExchangeSymbolSelector
                  selectedExchangeId={snapshotExchangeId}
                  selectedSymbolIds={snapshotSymbolId ? [Number(snapshotSymbolId)] : []}
                  onExchangeChange={setSnapshotExchangeId}
                  onSymbolsChange={(ids) => setSnapshotSymbolId(ids[0] ?? '')}
                  multiSelect={false}
                />
                <SelectField label="Timeframe" value={snapshotTimeframe} onChange={(value) => setSnapshotTimeframe(value || '15m')} options={TIMEFRAME_OPTIONS} />
              </div>
            </FormPanel>
            {snapshot.loading ? <LoadingState /> : null}
            {snapshot.data ? (
              <div className="grid gap-3 md:grid-cols-4">
                <MetricCard label="Symbol" value={snapshot.data.symbol} />
                <MetricCard label="Latest Price" value={formatNumber(snapshot.data.latestPrice)} />
                <MetricCard label="Timeframe" value={snapshot.data.timeframe} />
                <MetricCard label="Candles Available" value={snapshot.data.candleCountAvailable ?? '—'} />
                <MetricCard label="Updated" value={formatDate(snapshot.data.latestUpdateTimeUtc) ?? '—'} />
              </div>
            ) : !snapshotSymbolId ? (
              <EmptyState title="Select a symbol" description="Choose a symbol and timeframe to view the latest snapshot." />
            ) : null}
          </div>
        ) : null}

        {tab === 'cleanup' && isAdmin ? <DataCleanupTab /> : null}
      </TabPanel>
    </div>
  );
}
