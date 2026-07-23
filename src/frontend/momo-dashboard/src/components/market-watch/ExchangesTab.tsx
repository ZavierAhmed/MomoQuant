import { useState } from 'react';
import { PaginatedTable } from '@/components/common/PaginatedTable';
import { DetailModal } from '@/components/common/DetailModal';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { FormPanel } from '@/components/common/FormPanel';
import { KeyValueGrid } from '@/components/common/KeyValueGrid';
import { StatusPill } from '@/components/common/StatusPill';
import { CheckboxField, TextField } from '@/components/forms/fields';
import { FormActions } from '@/components/forms/FormActions';
import { exchangesApi, type CreateExchangeRequest } from '@/api/exchangesApi';
import type { Exchange } from '@/api/domainTypes';
import { useAsync } from '@/hooks/useAsync';
import { useRole } from '@/hooks/useRole';
import { parseApiClientError } from '@/utils/apiError';
import { formatDate } from '@/components/common/utils';

const emptyForm: CreateExchangeRequest = {
  name: '',
  code: '',
  baseUrl: '',
  webSocketUrl: '',
  isActive: true,
};

export function ExchangesTab() {
  const { isAdmin } = useRole();
  const [modalMode, setModalMode] = useState<'create' | 'edit' | 'view' | null>(null);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [form, setForm] = useState<CreateExchangeRequest>(emptyForm);
  const [formErrors, setFormErrors] = useState<Record<string, string>>({});
  const [actionError, setActionError] = useState<string | null>(null);
  const [actionMessage, setActionMessage] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<Exchange | null>(null);

  const exchanges = useAsync(() => exchangesApi.list({ page: 1, pageSize: 100 }), []);
  const detail = useAsync(
    () => (selectedId && modalMode === 'view' ? exchangesApi.get(selectedId) : Promise.resolve(null)),
    [selectedId, modalMode],
  );

  function openCreate() {
    setForm(emptyForm);
    setFormErrors({});
    setSelectedId(null);
    setModalMode('create');
  }

  function openEdit(exchange: Exchange) {
    setSelectedId(exchange.id);
    setForm({
      name: exchange.name,
      code: exchange.code,
      baseUrl: exchange.baseUrl,
      webSocketUrl: exchange.webSocketUrl ?? '',
      isActive: exchange.isActive,
    });
    setFormErrors({});
    setModalMode('edit');
  }

  function openView(exchange: Exchange) {
    setSelectedId(exchange.id);
    setModalMode('view');
  }

  function validateForm() {
    const errors: Record<string, string> = {};
    if (!form.name.trim()) errors.name = 'Name is required.';
    if (!form.code.trim()) errors.code = 'Code is required.';
    if (!form.baseUrl.trim()) errors.baseUrl = 'Base URL is required.';
    if (!form.webSocketUrl.trim()) errors.webSocketUrl = 'WebSocket URL is required.';
    setFormErrors(errors);
    return Object.keys(errors).length === 0;
  }

  async function saveExchange() {
    if (!isAdmin || !validateForm()) return;
    setActionError(null);
    try {
      if (modalMode === 'create') {
        await exchangesApi.create(form);
        setActionMessage('Exchange created successfully.');
      } else if (modalMode === 'edit' && selectedId) {
        await exchangesApi.update(selectedId, form);
        setActionMessage('Exchange updated successfully.');
      }
      setModalMode(null);
      exchanges.reload();
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  async function testConnection(id: number) {
    if (!isAdmin) return;
    setActionError(null);
    setTestResult(null);
    try {
      const result = await exchangesApi.testConnection(id);
      setTestResult(
        `${result.message} REST latency: ${result.restLatencyMs ?? '—'} ms. WebSocket available: ${result.webSocketAvailable ? 'Yes' : 'No'}.`,
      );
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  async function deleteExchange() {
    if (!isAdmin || !deleteTarget) return;
    setActionError(null);
    try {
      const result = await exchangesApi.delete(deleteTarget.id);
      setDeleteTarget(null);
      setActionMessage(`Exchange "${result.exchangeCode}" deleted. ${result.symbolsDeleted} symbol(s) removed.`);
      exchanges.reload();
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  const rows = exchanges.data?.items ?? [];

  return (
    <div>
      <ApiErrorAlert message={actionError} />
      {actionMessage ? <p className="mb-4 text-sm text-emerald-300">{actionMessage}</p> : null}
      {testResult ? <p className="mb-4 text-sm text-sky-300">{testResult}</p> : null}

      <FormPanel title="Exchanges" description="Manage exchange connections used for market data and simulation.">
        {isAdmin ? (
          <FormActions>
            <button type="button" onClick={openCreate} className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950">
              Create Exchange
            </button>
          </FormActions>
        ) : null}

        <div className="mt-4">
          <PaginatedTable
            rows={rows}
            columns={[
              { key: 'name', header: 'Name', render: (row) => row.name },
              { key: 'code', header: 'Code', render: (row) => row.code },
              { key: 'baseUrl', header: 'Base URL', render: (row) => row.baseUrl },
              { key: 'ws', header: 'WebSocket URL', render: (row) => row.webSocketUrl ?? '—' },
              { key: 'active', header: 'Active', render: (row) => <StatusPill status={row.isActive ? 'Active' : 'Inactive'} /> },
              { key: 'created', header: 'Created At', render: (row) => formatDate(row.createdAtUtc) },
              { key: 'updated', header: 'Updated At', render: (row) => formatDate(row.updatedAtUtc) },
              {
                key: 'actions',
                header: 'Actions',
                render: (row) => (
                  <div className="flex flex-wrap gap-2">
                    <button type="button" className="text-xs underline" onClick={() => openView(row)}>View</button>
                    {isAdmin ? (
                      <>
                        <button type="button" className="text-xs underline" onClick={() => openEdit(row)}>Edit</button>
                        <button type="button" className="text-xs underline" onClick={() => void testConnection(row.id)}>Test Connection</button>
                        <button type="button" className="text-xs text-red-300 underline" onClick={() => setDeleteTarget(row)}>Delete</button>
                      </>
                    ) : null}
                  </div>
                ),
              },
            ]}
          />
        </div>
      </FormPanel>

      <DetailModal
        open={modalMode === 'view'}
        title="Exchange Details"
        onClose={() => setModalMode(null)}
      >
        {detail.data ? (
          <KeyValueGrid
            items={[
              { label: 'Name', value: detail.data.name },
              { label: 'Code', value: detail.data.code },
              { label: 'Base URL', value: detail.data.baseUrl },
              { label: 'WebSocket URL', value: detail.data.webSocketUrl ?? '—' },
              { label: 'Active', value: detail.data.isActive ? 'Yes' : 'No' },
              { label: 'Created', value: formatDate(detail.data.createdAtUtc) },
              { label: 'Updated', value: formatDate(detail.data.updatedAtUtc) },
            ]}
          />
        ) : null}
      </DetailModal>

      <DetailModal
        open={modalMode === 'create' || modalMode === 'edit'}
        title={modalMode === 'create' ? 'Create Exchange' : 'Edit Exchange'}
        onClose={() => setModalMode(null)}
        footer={
          isAdmin ? (
            <button type="button" onClick={() => void saveExchange()} className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950">
              Save Exchange
            </button>
          ) : null
        }
      >
        <div className="grid gap-4 md:grid-cols-2">
          <TextField label="Name" value={form.name} onChange={(v) => setForm((c) => ({ ...c, name: v }))} required error={formErrors.name} />
          <TextField label="Code" value={form.code} onChange={(v) => setForm((c) => ({ ...c, code: v }))} required error={formErrors.code} />
          <TextField label="Base URL" value={form.baseUrl} onChange={(v) => setForm((c) => ({ ...c, baseUrl: v }))} required error={formErrors.baseUrl} />
          <TextField label="WebSocket URL" value={form.webSocketUrl} onChange={(v) => setForm((c) => ({ ...c, webSocketUrl: v }))} required error={formErrors.webSocketUrl} />
          <CheckboxField label="Is Active" checked={form.isActive} onChange={(v) => setForm((c) => ({ ...c, isActive: v }))} />
        </div>
      </DetailModal>

      <DetailModal
        open={deleteTarget !== null}
        title="Delete Exchange"
        onClose={() => setDeleteTarget(null)}
        footer={
          isAdmin ? (
            <button
              type="button"
              onClick={() => void deleteExchange()}
              className="rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white"
            >
              Delete Exchange
            </button>
          ) : null
        }
      >
        {deleteTarget ? (
          <div className="space-y-3 text-sm text-slate-300">
            <p>
              Delete <span className="font-medium text-slate-100">{deleteTarget.name}</span> ({deleteTarget.code})?
            </p>
            <p>All symbols linked to this exchange will also be deleted.</p>
            <p className="text-amber-200">
              This cannot be undone. If the exchange has candles, imports, or trading data, deletion will be blocked.
            </p>
          </div>
        ) : null}
      </DetailModal>
    </div>
  );
}
