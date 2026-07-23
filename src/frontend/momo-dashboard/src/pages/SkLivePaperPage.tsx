import { useEffect, useState } from 'react';
import { PageHeader } from '@/components/common/PageHeader';
import { SimulationBanner } from '@/components/common/SimulationBanner';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { DataTable } from '@/components/common/DataTable';
import { FormPanel } from '@/components/common/FormPanel';
import { StatusPill } from '@/components/common/StatusPill';
import { FormActions } from '@/components/forms/FormActions';
import { CheckboxField, NumberField, SelectField, TextField } from '@/components/forms/fields';
import { formatDate, formatNumber } from '@/components/common/utils';
import { useAsync } from '@/hooks/useAsync';
import { useRole } from '@/hooks/useRole';
import {
  skLivePaperApi,
  type CreateSkLivePaperSessionRequest,
  type SkLivePaperSessionSummary,
  type SkLivePaperStatus,
} from '@/api/skLivePaperApi';
import { parseApiClientError } from '@/utils/apiError';
import { formatPrice } from '@/utils/priceFormat';

import { ExchangeSymbolSelector } from '@/components/strategies/ExchangeSymbolSelector';
import {
  getHtfLtfValidationError,
  getSelectedPairHelperText,
  RECOMMENDED_SK_TIMEFRAME_PAIRS,
  SUPPORTED_MARKET_TIMEFRAMES,
} from '@/constants/timeframes';

const TIMEFRAME_SELECT_OPTIONS = SUPPORTED_MARKET_TIMEFRAMES.map((tf) => ({
  value: tf.value,
  label: tf.label,
}));

export function SkLivePaperPage() {
  const { canEdit } = useRole();
  const defaults = useAsync(() => skLivePaperApi.getDefaults(), []);
  const sessions = useAsync(() => skLivePaperApi.listSessions(), []);
  const [selectedSessionId, setSelectedSessionId] = useState<number | null>(null);
  const [status, setStatus] = useState<SkLivePaperStatus | null>(null);
  const [statusLoading, setStatusLoading] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [formErrors, setFormErrors] = useState<Record<string, string>>({});
  const [form, setForm] = useState<CreateSkLivePaperSessionRequest>({
    sessionName: 'SK LivePaper Session',
    exchangeId: 0,
    symbolId: 0,
  });

  const higherTimeframe = form.higherTimeframe ?? '4h';
  const primaryTimeframe = form.primaryTimeframe ?? '1h';
  const timeframeError = getHtfLtfValidationError(higherTimeframe, primaryTimeframe);
  const timeframeHelper = getSelectedPairHelperText(higherTimeframe, primaryTimeframe);

  useEffect(() => {
    if (!defaults.data) return;
    const d = defaults.data;
    setForm((prev) => ({
      ...prev,
      higherTimeframe: d.higherTimeframe,
      primaryTimeframe: d.primaryTimeframe,
      startingBalance: d.startingBalance,
      riskPerPaperTradePercent: d.riskPerPaperTradePercent,
      maxPaperTradesPerDay: d.maxPaperTradesPerDay,
      maxOpenPaperPositions: d.maxOpenPaperPositions,
      allowLong: d.allowLong,
      allowShort: d.allowShort,
      requireHtfAgreement: d.requireHtfAgreement,
      minClarityScore: d.minClarityScore,
      minUsefulnessScore: d.minUsefulnessScore,
      requireReactionConfirmation: d.requireReactionConfirmation,
      confirmationMode: d.confirmationMode,
      simulatedLeverage: d.simulatedLeverage,
    }));
  }, [defaults.data]);

  useEffect(() => {
    if (!selectedSessionId) {
      setStatus(null);
      return;
    }

    let cancelled = false;
    async function loadStatus() {
      setStatusLoading(true);
      try {
        const next = await skLivePaperApi.getStatus(selectedSessionId!);
        if (!cancelled) setStatus(next);
      } catch {
        if (!cancelled) setStatus(null);
      } finally {
        if (!cancelled) setStatusLoading(false);
      }
    }

    void loadStatus();
    const timer = window.setInterval(() => void loadStatus(), 5000);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [selectedSessionId]);

  const [candidateRows, setCandidateRows] = useState<Awaited<ReturnType<typeof skLivePaperApi.getCandidates>>>([]);
  const [tradeRows, setTradeRows] = useState<Awaited<ReturnType<typeof skLivePaperApi.getTrades>>>([]);
  const [eventRows, setEventRows] = useState<Awaited<ReturnType<typeof skLivePaperApi.getEvents>>>([]);

  useEffect(() => {
    if (!selectedSessionId) {
      setCandidateRows([]);
      setTradeRows([]);
      setEventRows([]);
      return;
    }

    let cancelled = false;
    async function loadDetails() {
      try {
        const [c, t, e] = await Promise.all([
          skLivePaperApi.getCandidates(selectedSessionId!),
          skLivePaperApi.getTrades(selectedSessionId!),
          skLivePaperApi.getEvents(selectedSessionId!),
        ]);
        if (!cancelled) {
          setCandidateRows(c);
          setTradeRows(t);
          setEventRows(e);
        }
      } catch {
        // Ignore — status card still updates.
      }
    }

    void loadDetails();
    const timer = window.setInterval(() => void loadDetails(), 5000);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [selectedSessionId]);

  async function refreshSessions(selectId?: number) {
    sessions.reload();
    if (selectId) setSelectedSessionId(selectId);
  }

  async function createSession() {
    if (!canEdit || !form.exchangeId || !form.symbolId) return;

    const errors: Record<string, string> = {};
    if (timeframeError) {
      errors.timeframe = timeframeError;
    }
    setFormErrors(errors);
    if (Object.keys(errors).length > 0) {
      return;
    }

    setActionError(null);
    try {
      const created = await skLivePaperApi.createSession(form);
      await refreshSessions(created.id);
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  async function runAction(action: 'start' | 'pause' | 'resume' | 'stop') {
    if (!canEdit || !selectedSessionId) return;

    if (action === 'start' || action === 'resume') {
      const sessionHigher = status?.session.higherTimeframe ?? higherTimeframe;
      const sessionPrimary = status?.session.primaryTimeframe ?? primaryTimeframe;
      const startError = getHtfLtfValidationError(sessionHigher, sessionPrimary);
      if (startError) {
        setActionError(startError);
        return;
      }
    }

    setActionError(null);
    try {
      const fn =
        action === 'start'
          ? skLivePaperApi.startSession
          : action === 'pause'
            ? skLivePaperApi.pauseSession
            : action === 'resume'
              ? skLivePaperApi.resumeSession
              : skLivePaperApi.stopSession;
      await fn(selectedSessionId);
      setStatus(await skLivePaperApi.getStatus(selectedSessionId));
      sessions.reload();
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  async function manualClose(tradeId: number) {
    if (!canEdit || !selectedSessionId) return;
    setActionError(null);
    try {
      await skLivePaperApi.manualCloseTrade(selectedSessionId, tradeId);
      setStatus(await skLivePaperApi.getStatus(selectedSessionId));
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  if (defaults.loading) return <LoadingState message="Loading SK LivePaper defaults…" />;
  if (defaults.error) return <ErrorState message="Could not load SK LivePaper defaults." onRetry={() => defaults.reload()} />;

  return (
    <div>
      <PageHeader
        title="SK System LivePaper"
        description="Simulate SK System scenarios using live public market data. Paper simulation only — no real orders."
      />

      <SimulationBanner
        message={
          defaults.data?.safetyDisclaimer ??
          'SK LivePaper uses simulated orders only. It does not connect to your Binance account and cannot place real trades.'
        }
      />

      {actionError ? <ApiErrorAlert message={actionError} /> : null}

      <div className="grid gap-6 xl:grid-cols-2">
        <FormPanel title="Session setup" description="Create a new SK LivePaper simulation session.">
          <div className="grid gap-3 md:grid-cols-2">
            <TextField
              label="Session name"
              value={form.sessionName}
              onChange={(value) => setForm((prev) => ({ ...prev, sessionName: value }))}
            />
            <ExchangeSymbolSelector
              selectedExchangeId={form.exchangeId || ''}
              selectedSymbolIds={form.symbolId ? [form.symbolId] : []}
              onExchangeChange={(exchangeId) =>
                setForm((prev) => ({ ...prev, exchangeId: exchangeId === '' ? 0 : Number(exchangeId), symbolId: 0 }))
              }
              onSymbolsChange={(symbolIds) => setForm((prev) => ({ ...prev, symbolId: symbolIds[0] ?? 0 }))}
              multiSelect={false}
            />
            <SelectField
              label="Bigger picture chart"
              value={higherTimeframe}
              onChange={(value) => setForm((prev) => ({ ...prev, higherTimeframe: value }))}
              options={TIMEFRAME_SELECT_OPTIONS}
              hint="Used for market direction and major zones."
              error={formErrors.timeframe}
            />
            <SelectField
              label="Analysis / reaction chart"
              value={primaryTimeframe}
              onChange={(value) => setForm((prev) => ({ ...prev, primaryTimeframe: value }))}
              options={TIMEFRAME_SELECT_OPTIONS}
              hint="Used to detect SK reaction areas and paper-trade decisions."
              error={formErrors.timeframe}
            />
            <div className="md:col-span-2">
              <p className={`text-xs ${timeframeError ? 'text-rose-300' : 'text-slate-500'}`}>
                {timeframeHelper}
              </p>
              <p className="mt-1 text-xs text-slate-600">
                Recommended pairs:{' '}
                {RECOMMENDED_SK_TIMEFRAME_PAIRS.map((pair) => `${pair.higher} / ${pair.primary}`).join(' · ')}
              </p>
            </div>
            <NumberField
              label="Starting balance"
              value={form.startingBalance ?? 10000}
              onChange={(value) => setForm((prev) => ({ ...prev, startingBalance: Number(value) }))}
            />
            <NumberField
              label="Risk per paper trade %"
              value={form.riskPerPaperTradePercent ?? 0.5}
              onChange={(value) => setForm((prev) => ({ ...prev, riskPerPaperTradePercent: Number(value) }))}
            />
            <NumberField
              label="Max paper trades per day"
              value={form.maxPaperTradesPerDay ?? 3}
              onChange={(value) => setForm((prev) => ({ ...prev, maxPaperTradesPerDay: Number(value) }))}
            />
            <NumberField
              label="Max open paper positions"
              value={form.maxOpenPaperPositions ?? 1}
              onChange={(value) => setForm((prev) => ({ ...prev, maxOpenPaperPositions: Number(value) }))}
            />
            <NumberField
              label="Simulated leverage"
              value={form.simulatedLeverage ?? 3}
              onChange={(value) => setForm((prev) => ({ ...prev, simulatedLeverage: Number(value) }))}
            />
            <NumberField
              label="Minimum clarity score"
              value={form.minClarityScore ?? 60}
              onChange={(value) => setForm((prev) => ({ ...prev, minClarityScore: Number(value) }))}
            />
            <NumberField
              label="Minimum usefulness score"
              value={form.minUsefulnessScore ?? 60}
              onChange={(value) => setForm((prev) => ({ ...prev, minUsefulnessScore: Number(value) }))}
            />
          </div>
          <div className="mt-3 flex flex-wrap gap-4">
            <CheckboxField
              label="Allow long"
              checked={form.allowLong ?? true}
              onChange={(checked) => setForm((prev) => ({ ...prev, allowLong: checked }))}
            />
            <CheckboxField
              label="Allow short"
              checked={form.allowShort ?? true}
              onChange={(checked) => setForm((prev) => ({ ...prev, allowShort: checked }))}
            />
            <CheckboxField
              label="Require HTF agreement"
              checked={form.requireHtfAgreement ?? true}
              onChange={(checked) => setForm((prev) => ({ ...prev, requireHtfAgreement: checked }))}
            />
            <CheckboxField
              label="Require reaction confirmation"
              checked={form.requireReactionConfirmation ?? true}
              onChange={(checked) => setForm((prev) => ({ ...prev, requireReactionConfirmation: checked }))}
            />
          </div>
          <FormActions>
            <button
              type="button"
              onClick={() => void createSession()}
              disabled={!canEdit || !form.exchangeId || !form.symbolId || !!timeframeError}
              className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950 disabled:opacity-50"
            >
              Create session
            </button>
          </FormActions>
        </FormPanel>

        <FormPanel title="Sessions" description="Select a session to monitor or control.">
          {sessions.loading ? (
            <LoadingState message="Loading sessions…" />
          ) : (
            <div className="space-y-2">
              {(sessions.data ?? []).map((session: SkLivePaperSessionSummary) => (
                <button
                  key={session.id}
                  type="button"
                  onClick={() => setSelectedSessionId(session.id)}
                  className={`w-full rounded-lg border px-3 py-2 text-left text-sm ${
                    selectedSessionId === session.id
                      ? 'border-sky-500/50 bg-sky-500/10 text-sky-100'
                      : 'border-slate-800 bg-slate-900/40 text-slate-300 hover:bg-slate-900'
                  }`}
                >
                  <div className="flex items-center justify-between gap-2">
                    <span>{session.sessionName}</span>
                    <StatusPill status={session.status} />
                  </div>
                  <p className="mt-1 text-xs text-slate-500">
                    {session.symbol} · Simulated balance {formatNumber(session.currentBalance)} · PnL{' '}
                    {formatNumber(session.netSimulatedPnl)}
                  </p>
                </button>
              ))}
            </div>
          )}
        </FormPanel>
      </div>

      {selectedSessionId ? (
        <>
          <section className="mt-6 rounded-xl border border-slate-800 bg-slate-900/40 p-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <h3 className="text-sm font-medium text-slate-200">Session controls</h3>
              <div className="flex flex-wrap gap-2">
                {(['start', 'pause', 'resume', 'stop'] as const).map((action) => (
                  <button
                    key={action}
                    type="button"
                    disabled={!canEdit}
                    onClick={() => void runAction(action)}
                    className="rounded-lg border border-slate-700 bg-slate-800/60 px-3 py-1.5 text-xs text-slate-200 hover:bg-slate-800 disabled:opacity-50"
                  >
                    {action.charAt(0).toUpperCase() + action.slice(1)}
                  </button>
                ))}
              </div>
            </div>

            {statusLoading && !status ? <LoadingState message="Loading session status…" /> : null}

            {status ? (
              <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                <div className="rounded-lg border border-slate-800 bg-slate-950/40 p-3 text-xs">
                  <p className="text-slate-500">Session status</p>
                  <p className="mt-1 text-slate-200">{status.session.status}</p>
                </div>
                <div className="rounded-lg border border-slate-800 bg-slate-950/40 p-3 text-xs">
                  <p className="text-slate-500">Simulated balance</p>
                  <p className="mt-1 text-slate-200">{formatNumber(status.session.currentBalance)}</p>
                </div>
                <div className="rounded-lg border border-slate-800 bg-slate-950/40 p-3 text-xs">
                  <p className="text-slate-500">Net simulated PnL</p>
                  <p className="mt-1 text-slate-200">{formatNumber(status.netSimulatedPnl)}</p>
                </div>
                <div className="rounded-lg border border-slate-800 bg-slate-950/40 p-3 text-xs">
                  <p className="text-slate-500">Open / closed paper trades</p>
                  <p className="mt-1 text-slate-200">
                    {status.openTrades} open · {status.closedTrades} closed
                  </p>
                </div>
                <div className="rounded-lg border border-slate-800 bg-slate-950/40 p-3 text-xs">
                  <p className="text-slate-500">Last candle analyzed</p>
                  <p className="mt-1 text-slate-200">
                    {status.session.lastAnalyzedCandleUtc
                      ? formatDate(status.session.lastAnalyzedCandleUtc)
                      : '—'}
                  </p>
                </div>
                <div className="rounded-lg border border-slate-800 bg-slate-950/40 p-3 text-xs">
                  <p className="text-slate-500">Last error</p>
                  <p className="mt-1 text-rose-200">{status.session.lastError ?? '—'}</p>
                </div>
              </div>
            ) : null}
          </section>

          <section className="mt-6 grid gap-6 xl:grid-cols-2">
            <div>
              <h3 className="mb-2 text-sm font-medium text-slate-300">Candidate feed</h3>
              <DataTable
                emptyMessage="No candidates yet."
                columns={[
                  { key: 'time', header: 'Time', render: (row) => formatDate(row.createdAtUtc) },
                  { key: 'direction', header: 'Direction', render: (row) => row.direction },
                  { key: 'clarity', header: 'Clarity', render: (row) => row.clarityScore },
                  { key: 'usefulness', header: 'Usefulness', render: (row) => row.usefulnessScore },
                  { key: 'status', header: 'Status', render: (row) => row.candidateStatus },
                  { key: 'reason', header: 'Rejection', render: (row) => row.rejectionReason ?? '—' },
                ]}
                rows={candidateRows}
              />
            </div>

            <div>
              <h3 className="mb-2 text-sm font-medium text-slate-300">Paper trades (simulated)</h3>
              <DataTable
                emptyMessage="No simulated trades yet."
                columns={[
                  { key: 'open', header: 'Open time', render: (row) => formatDate(row.entryTimeUtc) },
                  { key: 'dir', header: 'Direction', render: (row) => row.direction },
                  { key: 'entry', header: 'Entry', render: (row) => formatPrice(row.entryPrice) },
                  { key: 'stop', header: 'Stop', render: (row) => formatPrice(row.stopLoss) },
                  { key: 'target', header: 'Target', render: (row) => formatPrice(row.takeProfit1) },
                  { key: 'pnl', header: 'Net PnL', render: (row) => formatNumber(row.netPnl) },
                  { key: 'status', header: 'Status', render: (row) => `${row.status} (${row.simulationMode})` },
                  {
                    key: 'action',
                    header: '',
                    render: (row) =>
                      row.status === 'Open' && canEdit ? (
                        <button
                          type="button"
                          className="text-xs text-sky-300 hover:underline"
                          onClick={() => void manualClose(row.id)}
                        >
                          Close simulated
                        </button>
                      ) : null,
                  },
                ]}
                rows={tradeRows}
              />
            </div>
          </section>

          <section className="mt-6 grid gap-6 xl:grid-cols-2">
            <div>
              <h3 className="mb-2 text-sm font-medium text-slate-300">Event log</h3>
              <DataTable
                emptyMessage="No events yet."
                columns={[
                  { key: 'time', header: 'Time', render: (row) => formatDate(row.createdAtUtc) },
                  { key: 'type', header: 'Type', render: (row) => row.eventType },
                  { key: 'msg', header: 'Message', render: (row) => row.message },
                ]}
                rows={eventRows}
              />
            </div>

            {status ? (
              <div className="rounded-xl border border-slate-800 bg-slate-900/40 p-4">
                <h3 className="text-sm font-medium text-slate-200">Diagnostics</h3>
                <dl className="mt-3 grid gap-2 text-xs text-slate-400 md:grid-cols-2">
                  <div>WebSocket: {status.diagnostics.webSocketStatus}</div>
                  <div>Closed candles processed: {status.diagnostics.closedCandlesProcessed}</div>
                  <div>SK analyses run: {status.diagnostics.skAnalysesRun}</div>
                  <div>Candidates detected: {status.diagnostics.candidatesDetected}</div>
                  <div>Candidates rejected: {status.diagnostics.candidatesRejected}</div>
                  <div>Paper trades opened: {status.diagnostics.paperTradesOpened}</div>
                  <div>Paper trades closed: {status.diagnostics.paperTradesClosed}</div>
                  <div>
                    Last heartbeat:{' '}
                    {status.diagnostics.lastHeartbeatUtc
                      ? formatDate(status.diagnostics.lastHeartbeatUtc)
                      : '—'}
                  </div>
                </dl>
                <p className="mt-3 text-xs text-amber-300">
                  All trades on this page are simulated paper trades. No real Binance orders are placed.
                </p>
              </div>
            ) : null}
          </section>
        </>
      ) : null}
    </div>
  );
}
