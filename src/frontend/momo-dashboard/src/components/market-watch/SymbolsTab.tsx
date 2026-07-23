import { useMemo, useState } from 'react';
import { PaginatedTable } from '@/components/common/PaginatedTable';
import { FormPanel } from '@/components/common/FormPanel';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { ConfirmDialog } from '@/components/common/ConfirmDialog';
import { StatusPill } from '@/components/common/StatusPill';
import { SymbolWithExchangeLabel } from '@/components/common/SymbolWithExchangeLabel';
import { ExchangeSelect } from '@/components/selects/EntitySelects';
import { TextField, SelectField } from '@/components/forms/fields';
import { FormActions } from '@/components/forms/FormActions';
import { symbolsApi } from '@/api/symbolsApi';
import type { Symbol } from '@/api/domainTypes';
import { useAsync } from '@/hooks/useAsync';
import { useReferenceData } from '@/hooks/useReferenceData';
import { useRole } from '@/hooks/useRole';
import { parseApiClientError } from '@/utils/apiError';
import { formatDate, formatNumber } from '@/components/common/utils';

export function SymbolsTab() {
  const { isAdmin } = useRole();
  const reference = useReferenceData();
  const [filterExchangeId, setFilterExchangeId] = useState<number | ''>('');
  const [search, setSearch] = useState('');
  const [activeFilter, setActiveFilter] = useState<'all' | 'active' | 'inactive'>('all');
  const [syncExchangeId, setSyncExchangeId] = useState<number | ''>('');
  const [actionError, setActionError] = useState<string | null>(null);
  const [actionMessage, setActionMessage] = useState<string | null>(null);
  const [pendingDeactivate, setPendingDeactivate] = useState<Symbol | null>(null);

  const symbols = useAsync(
    () =>
      symbolsApi.list({
        page: 1,
        pageSize: 500,
        exchangeId: filterExchangeId || undefined,
        search: search.trim() || undefined,
      }),
    [filterExchangeId, search],
  );

  const filteredRows = useMemo(() => {
    const items = symbols.data?.items ?? [];
    if (activeFilter === 'active') return items.filter((item) => item.isActive);
    if (activeFilter === 'inactive') return items.filter((item) => !item.isActive);
    return items;
  }, [symbols.data, activeFilter]);

  async function syncSymbols() {
    if (!isAdmin || !syncExchangeId) {
      setActionError('Select an exchange before syncing symbols.');
      return;
    }
    setActionError(null);
    setActionMessage(null);
    try {
      const result = await symbolsApi.sync(Number(syncExchangeId));
      setActionMessage(
        `Sync completed: ${result.createdCount} inserted, ${result.updatedCount} updated, ${result.totalCount} total from provider.`,
      );
      symbols.reload();
      reference.reloadSymbols();
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  async function updateStatus(symbol: Symbol, isActive: boolean) {
    if (!isAdmin) return;
    setActionError(null);
    try {
      await symbolsApi.updateStatus(symbol.id, isActive);
      setActionMessage(`Symbol ${symbol.symbol} is now ${isActive ? 'active' : 'inactive'}.`);
      symbols.reload();
      reference.reloadSymbols();
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  return (
    <div>
      <ApiErrorAlert message={actionError} />
      {actionMessage ? <p className="mb-4 text-sm text-emerald-300">{actionMessage}</p> : null}

      {isAdmin ? (
        <FormPanel title="Symbol Sync" description="Import or refresh symbols from the selected exchange provider.">
          <div className="grid gap-4 md:grid-cols-2">
            <ExchangeSelect
              label="Exchange"
              value={syncExchangeId}
              onChange={setSyncExchangeId}
              options={reference.allExchangeOptions}
              loading={reference.loading}
              required
            />
          </div>
          <FormActions>
            <button type="button" onClick={() => void syncSymbols()} className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950">
              Sync Symbols
            </button>
          </FormActions>
        </FormPanel>
      ) : null}

      <FormPanel title="Symbols" description="Browse and manage tradable symbols with exchange context.">
        <div className="mb-4 grid gap-4 md:grid-cols-3">
          <ExchangeSelect
            label="Filter Exchange"
            value={filterExchangeId}
            onChange={setFilterExchangeId}
            options={reference.allExchangeOptions}
            loading={reference.loading}
            placeholder="All exchanges"
          />
          <TextField label="Search Symbol" value={search} onChange={setSearch} hint="Search by symbol, base, or quote asset." />
          <SelectField
            label="Active Status"
            value={activeFilter}
            onChange={(v) => setActiveFilter((v as typeof activeFilter) || 'all')}
            options={[
              { label: 'All', value: 'all' },
              { label: 'Active only', value: 'active' },
              { label: 'Inactive only', value: 'inactive' },
            ]}
          />
        </div>

        <PaginatedTable
          rows={filteredRows}
          columns={[
            { key: 'exchange', header: 'Exchange', render: (row) => row.exchangeName ?? '—' },
            { key: 'exchangeCode', header: 'Exchange Code', render: (row) => row.exchangeCode ?? '—' },
            { key: 'symbol', header: 'Symbol', render: (row) => <SymbolWithExchangeLabel symbol={row} exchanges={reference.exchanges} /> },
            { key: 'base', header: 'Base Asset', render: (row) => row.baseAsset },
            { key: 'quote', header: 'Quote Asset', render: (row) => row.quoteAsset },
            { key: 'contract', header: 'Contract Type', render: (row) => String(row.contractType) },
            { key: 'pricePrec', header: 'Price Precision', render: (row) => row.pricePrecision },
            { key: 'qtyPrec', header: 'Quantity Precision', render: (row) => row.quantityPrecision },
            { key: 'minQty', header: 'Min Qty', render: (row) => formatNumber(row.minQty) },
            { key: 'minNotional', header: 'Min Notional', render: (row) => formatNumber(row.minNotional) },
            { key: 'tick', header: 'Tick Size', render: (row) => formatNumber(row.tickSize) },
            { key: 'step', header: 'Step Size', render: (row) => formatNumber(row.stepSize) },
            { key: 'maker', header: 'Maker Fee', render: (row) => formatNumber(row.makerFeeRate) },
            { key: 'taker', header: 'Taker Fee', render: (row) => formatNumber(row.takerFeeRate) },
            { key: 'active', header: 'Active', render: (row) => <StatusPill status={row.isActive ? 'Active' : 'Inactive'} /> },
            { key: 'updated', header: 'Updated At', render: (row) => formatDate(row.updatedAtUtc) },
            {
              key: 'actions',
              header: 'Actions',
              render: (row) =>
                isAdmin ? (
                  <div className="flex gap-2">
                    {row.isActive ? (
                      <button type="button" className="text-xs underline" onClick={() => setPendingDeactivate(row)}>
                        Deactivate
                      </button>
                    ) : (
                      <button type="button" className="text-xs underline" onClick={() => void updateStatus(row, true)}>
                        Activate
                      </button>
                    )}
                  </div>
                ) : (
                  '—'
                ),
            },
          ]}
        />
      </FormPanel>

      <ConfirmDialog
        open={pendingDeactivate !== null}
        title="Deactivate Symbol"
        message={pendingDeactivate ? `Deactivate ${pendingDeactivate.symbol}? This does not delete the symbol.` : ''}
        confirmLabel="Deactivate"
        onConfirm={() => {
          if (pendingDeactivate) void updateStatus(pendingDeactivate, false);
          setPendingDeactivate(null);
        }}
        onCancel={() => setPendingDeactivate(null)}
      />
    </div>
  );
}
