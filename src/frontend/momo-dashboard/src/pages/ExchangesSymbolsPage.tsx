import { useMemo, useState } from 'react';
import { PageHeader } from '@/components/common/PageHeader';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { EmptyState } from '@/components/common/EmptyState';
import { DataTable } from '@/components/common/DataTable';
import { StatusPill } from '@/components/common/StatusPill';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { FormPanel } from '@/components/common/FormPanel';
import { formatDate } from '@/components/common/utils';
import { useAsync } from '@/hooks/useAsync';
import { useRole } from '@/hooks/useRole';
import { exchangesApi } from '@/api/exchangesApi';
import { symbolsApi } from '@/api/symbolsApi';
import {
  binanceFuturesSymbolsApi,
  type DiscoveredBinanceFuturesSymbol,
} from '@/api/binanceFuturesSymbolsApi';
import { parseApiClientError } from '@/utils/apiError';

const BINANCE_FUTURES_CODE = 'BINANCE_FUTURES';

function formatNumber(value: number, maximumFractionDigits = 2): string {
  return value.toLocaleString(undefined, { maximumFractionDigits });
}

export function ExchangesSymbolsPage() {
  const { canEdit, role } = useRole();
  const isAdmin = role === 'Admin';

  const exchanges = useAsync(() => exchangesApi.list({ page: 1, pageSize: 100 }), []);
  const binanceExchange = useMemo(
    () => exchanges.data?.items.find((exchange) => exchange.code === BINANCE_FUTURES_CODE) ?? null,
    [exchanges.data],
  );

  const symbols = useAsync(
    () =>
      binanceExchange
        ? symbolsApi.list({ page: 1, pageSize: 100, exchangeId: binanceExchange.id })
        : Promise.resolve(null),
    [binanceExchange?.id],
  );

  const [discovered, setDiscovered] = useState<DiscoveredBinanceFuturesSymbol[]>([]);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [discovering, setDiscovering] = useState(false);
  const [adding, setAdding] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  async function handleDiscover() {
    setActionError(null);
    setMessage(null);
    setDiscovering(true);
    try {
      const results = await binanceFuturesSymbolsApi.discover(100);
      setDiscovered(results);
      setSelected(new Set());
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    } finally {
      setDiscovering(false);
    }
  }

  function toggle(symbol: string) {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(symbol)) {
        next.delete(symbol);
      } else {
        next.add(symbol);
      }
      return next;
    });
  }

  function selectTop(count: number) {
    const top = discovered
      .filter((item) => !item.alreadyAdded)
      .slice(0, count)
      .map((item) => item.symbol);
    setSelected(new Set(top));
  }

  async function handleAddSelected() {
    if (selected.size === 0) {
      return;
    }
    setActionError(null);
    setMessage(null);
    setAdding(true);
    try {
      const result = await binanceFuturesSymbolsApi.addSymbols(Array.from(selected));
      setMessage(
        `Added ${result.addedCount} symbol(s). Skipped ${result.skippedCount} (already added).` +
          (result.unknownSymbols.length > 0 ? ` Unknown: ${result.unknownSymbols.join(', ')}.` : ''),
      );
      setSelected(new Set());
      symbols.reload();
      // Refresh discovery so alreadyAdded flags update.
      await handleDiscover();
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    } finally {
      setAdding(false);
    }
  }

  async function disableSymbol(id: number) {
    if (!canEdit) return;
    setActionError(null);
    try {
      await symbolsApi.updateStatus(id, false);
      symbols.reload();
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  return (
    <div>
      <PageHeader
        title="Exchanges & Symbols"
        description="Manage the Binance Futures exchange and discover public market symbols. Simulation only — no real orders are placed."
      />
      <ApiErrorAlert message={actionError} />
      {message ? <p className="mb-4 text-sm text-emerald-300">{message}</p> : null}

      {exchanges.loading ? <LoadingState /> : null}
      {exchanges.error ? <ErrorState message={exchanges.error} onRetry={exchanges.reload} /> : null}

      {!exchanges.loading && !binanceExchange ? (
        <EmptyState
          title="No Binance Futures exchange"
          description="Run the clean baseline reset from Admin → System Cleanup to create the Binance Futures exchange."
        />
      ) : null}

      {binanceExchange ? (
        <section className="mb-6 rounded-xl border border-slate-800 bg-slate-900/40 p-4">
          <div className="flex items-center justify-between">
            <h2 className="text-lg font-medium text-slate-100">{binanceExchange.name}</h2>
            <StatusPill status={binanceExchange.isActive ? 'Active' : 'Inactive'} />
          </div>
          <dl className="mt-3 grid gap-3 text-sm text-slate-300 md:grid-cols-2 xl:grid-cols-4">
            <div>
              <dt className="text-xs uppercase text-slate-500">Code</dt>
              <dd>{binanceExchange.code}</dd>
            </div>
            <div>
              <dt className="text-xs uppercase text-slate-500">REST URL</dt>
              <dd className="break-all">{binanceExchange.baseUrl}</dd>
            </div>
            <div>
              <dt className="text-xs uppercase text-slate-500">Access</dt>
              <dd>Public market data only</dd>
            </div>
            <div>
              <dt className="text-xs uppercase text-slate-500">Trading</dt>
              <dd>Disabled (simulation only)</dd>
            </div>
          </dl>
        </section>
      ) : null}

      {binanceExchange ? (
        <section className="mb-6">
          <h2 className="mb-3 text-sm font-medium text-slate-300">Added Symbols</h2>
          {symbols.loading ? <LoadingState /> : null}
          {(symbols.data?.items ?? []).length === 0 && !symbols.loading ? (
            <EmptyState
              title="No symbols added"
              description="No symbols added. Discover Binance Futures symbols below to add symbols."
            />
          ) : (
            <DataTable
              columns={[
                { key: 'symbol', header: 'Symbol', render: (row) => row.symbol },
                { key: 'base', header: 'Base', render: (row) => row.baseAsset },
                { key: 'quote', header: 'Quote', render: (row) => row.quoteAsset },
                { key: 'status', header: 'Status', render: (row) => <StatusPill status={row.isActive ? 'Active' : 'Inactive'} /> },
                { key: 'precision', header: 'Precision', render: (row) => `${row.pricePrecision}/${row.quantityPrecision}` },
                { key: 'added', header: 'Added At', render: (row) => formatDate(row.createdAtUtc) },
                {
                  key: 'actions',
                  header: 'Actions',
                  render: (row) =>
                    canEdit && row.isActive ? (
                      <button
                        type="button"
                        onClick={() => void disableSymbol(row.id)}
                        className="rounded-md border border-rose-500/40 px-2 py-1 text-xs text-rose-300"
                      >
                        Disable
                      </button>
                    ) : null,
                },
              ]}
              rows={symbols.data?.items ?? []}
            />
          )}
        </section>
      ) : null}

      {binanceExchange ? (
        <FormPanel
          title="Discover Top 100 Symbols"
          description="Fetches the top Binance USD-M perpetual USDT symbols by 24h quote volume (public data)."
        >
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => void handleDiscover()}
              disabled={discovering || !canEdit}
              className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950 hover:bg-white disabled:opacity-50"
            >
              {discovering ? 'Discovering…' : 'Discover Top 100 Symbols'}
            </button>
            {discovered.length > 0 ? (
              <>
                <button type="button" onClick={() => selectTop(5)} className="rounded-lg border border-slate-600 px-3 py-2 text-sm text-slate-200 hover:bg-slate-800">Select top 5</button>
                <button type="button" onClick={() => selectTop(10)} className="rounded-lg border border-slate-600 px-3 py-2 text-sm text-slate-200 hover:bg-slate-800">Select top 10</button>
                <button type="button" onClick={() => selectTop(20)} className="rounded-lg border border-slate-600 px-3 py-2 text-sm text-slate-200 hover:bg-slate-800">Select top 20</button>
                <button type="button" onClick={() => setSelected(new Set())} className="rounded-lg border border-slate-600 px-3 py-2 text-sm text-slate-200 hover:bg-slate-800">Clear selection</button>
                {isAdmin ? (
                  <button
                    type="button"
                    onClick={() => void handleAddSelected()}
                    disabled={adding || selected.size === 0}
                    className="rounded-lg bg-emerald-500/90 px-4 py-2 text-sm font-medium text-slate-950 hover:bg-emerald-400 disabled:opacity-50"
                  >
                    {adding ? 'Adding…' : `Add selected symbols (${selected.size})`}
                  </button>
                ) : null}
              </>
            ) : null}
          </div>

          {discovered.length > 0 ? (
            <div className="mt-4">
              <DataTable
                columns={[
                  {
                    key: 'select',
                    header: '',
                    render: (row) => (
                      <input
                        type="checkbox"
                        checked={selected.has(row.symbol)}
                        disabled={row.alreadyAdded}
                        onChange={() => toggle(row.symbol)}
                      />
                    ),
                  },
                  { key: 'rank', header: 'Rank', render: (row) => row.rank },
                  { key: 'symbol', header: 'Symbol', render: (row) => row.symbol },
                  { key: 'base', header: 'Base', render: (row) => row.baseAsset },
                  { key: 'quote', header: 'Quote', render: (row) => row.quoteAsset },
                  { key: 'price', header: 'Last Price', render: (row) => formatNumber(row.lastPrice, 6) },
                  { key: 'change', header: '24h Change %', render: (row) => `${formatNumber(row.priceChangePercent24h)}%` },
                  { key: 'volume', header: '24h Quote Volume', render: (row) => formatNumber(row.quoteVolume24h, 0) },
                  { key: 'trades', header: 'Trades 24h', render: (row) => formatNumber(row.trades24h, 0) },
                  { key: 'added', header: 'Already Added', render: (row) => (row.alreadyAdded ? 'Yes' : 'No') },
                ]}
                rows={discovered}
              />
            </div>
          ) : null}
        </FormPanel>
      ) : null}
    </div>
  );
}
