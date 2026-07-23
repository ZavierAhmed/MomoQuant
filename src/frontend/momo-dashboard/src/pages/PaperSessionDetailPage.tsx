import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';
import { SimulationBanner } from '@/components/common/SimulationBanner';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { TabPanel } from '@/components/common/TabPanel';
import { PaginatedTable } from '@/components/common/PaginatedTable';
import { KeyValueGrid, formatKvDate, formatKvNumber } from '@/components/common/KeyValueGrid';
import { MetricCard } from '@/components/common/MetricCard';
import { AiDecisionView, RiskDecisionView } from '@/components/formatters/TradingViews';
import { LivePaperChart } from '@/components/charts/LivePaperChart';
import { useAsync } from '@/hooks/useAsync';
import { useRole } from '@/hooks/useRole';
import { paperTradingApi } from '@/api/paperTradingApi';
import { liveMarketApi, type LiveMarketDiagnostics } from '@/api/liveMarketApi';
import { PipelineDiagnosticsPanel } from '@/components/trading/PipelineDiagnosticsPanel';
import { useReferenceData } from '@/hooks/useReferenceData';
import { symbolLabel } from '@/utils/referenceLookups';
import { getPaperSessionActions, paperSessionActionLabel } from '@/utils/formValidation';
import { usePaperSessionPolling } from '@/hooks/useSessionPolling';
import { parseApiClientError } from '@/utils/apiError';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import type { LivePaperChartData, PaperSessionStatus } from '@/api/domainTypes';

export function PaperSessionDetailPage() {
  const { id } = useParams();
  const sessionId = Number(id);
  const { canEdit } = useRole();
  const reference = useReferenceData();
  const [tab, setTab] = useState('status');
  const [actionError, setActionError] = useState<string | null>(null);
  const [liveStatus, setLiveStatus] = useState<PaperSessionStatus | null>(null);
  const [liveChart, setLiveChart] = useState<LivePaperChartData | null>(null);
  const [liveDiagnostics, setLiveDiagnostics] = useState<LiveMarketDiagnostics | null>(null);
  const countsRef = useRef({ orders: 0, trades: 0, fills: 0, missed: 0 });

  const session = useAsync(() => (sessionId ? paperTradingApi.getSession(sessionId) : Promise.resolve(null)), [sessionId]);
  const orders = useAsync(() => (sessionId ? paperTradingApi.getOrders(sessionId) : Promise.resolve([])), [sessionId]);
  const fills = useAsync(() => (sessionId ? paperTradingApi.getFills(sessionId) : Promise.resolve([])), [sessionId]);
  const positions = useAsync(() => (sessionId ? paperTradingApi.getPositions(sessionId) : Promise.resolve([])), [sessionId]);
  const trades = useAsync(() => (sessionId ? paperTradingApi.getTrades(sessionId) : Promise.resolve([])), [sessionId]);
  const missed = useAsync(() => (sessionId ? paperTradingApi.getMissedOrders(sessionId) : Promise.resolve([])), [sessionId]);
  const equity = useAsync(() => (sessionId ? paperTradingApi.getEquityCurve(sessionId) : Promise.resolve([])), [sessionId]);
  const signals = useAsync(() => (sessionId ? paperTradingApi.getSignals(sessionId) : Promise.resolve([])), [sessionId]);
  const riskDecisions = useAsync(() => (sessionId ? paperTradingApi.getRiskDecisions(sessionId) : Promise.resolve([])), [sessionId]);
  const aiDecisions = useAsync(() => (sessionId ? paperTradingApi.getAiDecisions(sessionId) : Promise.resolve([])), [sessionId]);
  const diagnostics = useAsync(() => (sessionId ? paperTradingApi.getDiagnostics(sessionId) : Promise.resolve(null)), [sessionId]);

  const isLivePaper = (liveStatus?.mode ?? session.data?.mode) === 'LivePaper';
  const isRunning = (liveStatus?.status ?? session.data?.status) === 'Running';

  const pollStatus = useCallback(async () => {
    if (!sessionId) return;
    try {
      const status = isLivePaper
        ? await paperTradingApi.getSessionLiveStatus(sessionId)
        : await paperTradingApi.getSessionStatus(sessionId);
      setLiveStatus(status);

      if (isLivePaper) {
        const [chart, marketDiagnostics] = await Promise.all([
          paperTradingApi.getSessionLiveChart(sessionId, { limit: 300 }),
          liveMarketApi.getDiagnostics(),
        ]);
        setLiveChart(chart);
        setLiveDiagnostics(marketDiagnostics);
      }
    } catch {
      // Keep last known status during transient poll failures.
    }
  }, [sessionId, isLivePaper]);

  const reloadHeavyTables = useCallback(() => {
    orders.reloadSilent();
    fills.reloadSilent();
    positions.reloadSilent();
    trades.reloadSilent();
    missed.reloadSilent();
    diagnostics.reloadSilent();
  }, [orders, fills, positions, trades, missed, diagnostics]);

  const pollHeavyIfNeeded = useCallback(async () => {
    if (!sessionId) return;

    const nextCounts = {
      orders: liveStatus?.ordersCount ?? (orders.data ?? []).length,
      trades: liveStatus?.tradesCount ?? (trades.data ?? []).length,
      fills: (fills.data ?? []).length,
      missed: liveStatus?.missedOrdersCount ?? (missed.data ?? []).length,
    };

    const countsChanged =
      nextCounts.orders !== countsRef.current.orders ||
      nextCounts.trades !== countsRef.current.trades ||
      nextCounts.fills !== countsRef.current.fills ||
      nextCounts.missed !== countsRef.current.missed;

    const heavyTabActive = ['orders', 'fills', 'positions', 'trades', 'missed', 'diagnostics'].includes(tab);

    if (heavyTabActive || countsChanged) {
      await Promise.all([
        orders.reloadSilent(),
        fills.reloadSilent(),
        positions.reloadSilent(),
        trades.reloadSilent(),
        missed.reloadSilent(),
        diagnostics.reloadSilent(),
      ]);
      countsRef.current = nextCounts;
    }
  }, [sessionId, tab, orders, fills, positions, trades, missed, diagnostics, liveStatus]);

  usePaperSessionPolling({
    active: isRunning || isLivePaper,
    onStatusPoll: () => void pollStatus(),
    onHeavyPoll: () => void pollHeavyIfNeeded(),
    statusIntervalMs: 2000,
  });

  useEffect(() => {
    void pollStatus();
  }, [pollStatus]);

  async function runAction(action: 'start' | 'pause' | 'resume' | 'stop') {
    if (!canEdit || !sessionId) return;
    setActionError(null);
    try {
      if (action === 'start') await paperTradingApi.startSession(sessionId);
      if (action === 'pause') await paperTradingApi.pauseSession(sessionId);
      if (action === 'resume') await paperTradingApi.resumeSession(sessionId);
      if (action === 'stop') await paperTradingApi.stopSession(sessionId);
      await session.reload();
      await pollStatus();
      reloadHeavyTables();
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  if (!sessionId) return <ErrorState message="Invalid paper session id." />;

  const statusData = liveStatus;
  const primarySymbol = statusData?.symbolStatuses?.[0];
  const progressPercent = isLivePaper
    ? null
    : Number(
        statusData?.progressPercent ??
          ((session.data?.totalCandles ?? 0) > 0
            ? Math.round((((session.data?.currentCandleIndex ?? 0) + 1) / (session.data?.totalCandles ?? 1)) * 100)
            : 0),
      );

  const sessionStreamDiagnostics = liveDiagnostics?.subscriptions.filter((item) =>
    (statusData?.subscribedSymbols ?? []).includes(item.symbol),
  ) ?? liveDiagnostics?.subscriptions ?? [];

  return (
    <div>
      <PageHeader title={session.data?.name ?? 'Paper Session'} description="Simulated session details." />
      <Link to="/paper-trading" className="mb-4 inline-block text-sm text-slate-400 underline">
        Back to paper trading
      </Link>
      <SimulationBanner message="Paper trading is simulated. No real exchange orders are placed." />
      <ApiErrorAlert message={actionError} />

      {session.loading && !session.data ? <LoadingState /> : null}
      {session.error ? <ErrorState message={session.error} onRetry={session.reload} /> : null}

      {session.data ? (
        <>
          {canEdit ? (
            <div className="mb-4 flex flex-wrap gap-2">
              {getPaperSessionActions(session.data.status).map((action) => (
                <button key={action} type="button" onClick={() => void runAction(action)} className="rounded-lg border border-slate-600 px-3 py-1.5 text-xs text-slate-200">
                  {paperSessionActionLabel(action)}
                </button>
              ))}
            </div>
          ) : null}

          {isLivePaper ? (
            <div className="mb-4 grid gap-3 md:grid-cols-3 xl:grid-cols-6">
              <MetricCard label="Connection" value={statusData?.connected ? 'Connected' : 'Disconnected'} />
              <MetricCard label="Latest Price" value={statusData?.latestPrice != null ? formatKvNumber(Number(statusData.latestPrice)) : '—'} />
              <MetricCard label="Last Live Update" value={formatKvDate(statusData?.lastLiveUpdateUtc)} />
              <MetricCard label="Current Candle" value={formatKvDate(primarySymbol?.currentCandleOpenTimeUtc ?? statusData?.currentCandleTimeUtc)} />
              <MetricCard label="Last Closed Candle" value={formatKvDate(statusData?.lastClosedCandleUtc)} />
              <MetricCard label="Last Processed Candle" value={formatKvDate(statusData?.lastProcessedCandleUtc)} />
            </div>
          ) : null}

          {isLivePaper ? (
            <div className="mb-4">
              <LivePaperChart chartData={liveChart} />
            </div>
          ) : null}

          {(statusData?.warnings ?? []).length > 0 ? (
            <ul className="mb-4 list-disc pl-5 text-sm text-amber-200">
              {statusData?.warnings.map((warning) => (
                <li key={warning}>{warning}</li>
              ))}
            </ul>
          ) : null}

          <TabPanel
            active={tab}
            onChange={setTab}
            tabs={[
              { id: 'status', label: 'Status' },
              { id: 'orders', label: 'Orders' },
              { id: 'fills', label: 'Fills' },
              { id: 'positions', label: 'Positions' },
              { id: 'trades', label: 'Trades' },
              { id: 'missed', label: 'Missed Orders' },
              { id: 'equity', label: 'Equity Curve' },
              { id: 'signals', label: 'Signals' },
              { id: 'risk', label: 'Risk Decisions' },
              { id: 'ai', label: 'AI Decisions' },
              { id: 'diagnostics', label: 'Pipeline Diagnostics' },
              ...(isLivePaper ? [{ id: 'live-diagnostics', label: 'Live Diagnostics' }] : []),
            ]}
          >
            {tab === 'status' ? (
              <>
                <KeyValueGrid
                  items={[
                    { label: 'Status', value: statusData?.status ?? session.data.status },
                    { label: 'Mode', value: statusData?.mode ?? session.data.mode },
                    isLivePaper
                      ? {
                          label: 'Progress',
                          value: statusData?.progressLabel === 'Live' ? 'Live mode running' : 'Live',
                        }
                      : {
                          label: 'Progress',
                          value: `${statusData?.processedCandles ?? session.data.currentCandleIndex ?? 0}/${statusData?.totalCandles ?? session.data.totalCandles ?? 0}`,
                        },
                    ...(isLivePaper
                      ? [
                          { label: 'Connection', value: statusData?.connected ? 'Connected' : 'Disconnected' },
                          { label: 'Latest Price', value: statusData?.latestPrice != null ? formatKvNumber(Number(statusData.latestPrice)) : '—' },
                          { label: 'Last Live Update', value: formatKvDate(statusData?.lastLiveUpdateUtc) },
                          { label: 'Current Candle', value: formatKvDate(primarySymbol?.currentCandleOpenTimeUtc ?? statusData?.currentCandleTimeUtc) },
                          { label: 'Last Closed Candle', value: formatKvDate(statusData?.lastClosedCandleUtc) },
                          { label: 'Last Processed Candle', value: formatKvDate(statusData?.lastProcessedCandleUtc) },
                          { label: 'Stream', value: primarySymbol?.streamName ?? '—' },
                        ]
                      : [
                          {
                            label: 'Current Candle',
                            value: formatKvDate(statusData?.currentCandleTimeUtc ?? session.data.currentCandleTimeUtc),
                          },
                        ]),
                    { label: 'Started', value: formatKvDate(session.data.startedAtUtc) },
                    { label: 'Balance', value: formatKvNumber(Number(statusData?.currentBalance ?? 0)) },
                    { label: 'Equity', value: formatKvNumber(Number(statusData?.currentEquity ?? 0)) },
                    { label: 'Open Positions', value: String(statusData?.openPositionCount ?? 0) },
                    { label: 'Orders', value: String(statusData?.ordersCount ?? 0) },
                    { label: 'Trades', value: String(statusData?.tradesCount ?? 0) },
                    { label: 'Missed Orders', value: String(statusData?.missedOrdersCount ?? 0) },
                  ]}
                />
                {!isLivePaper ? (
                  <div className="mt-3">
                    <div className="mb-1 flex justify-between text-xs text-slate-400">
                      <span>Session progress</span>
                      <span>{progressPercent}%</span>
                    </div>
                    <div className="h-2 overflow-hidden rounded-full bg-slate-800">
                      <div className="h-full bg-emerald-500 transition-all duration-500" style={{ width: `${progressPercent}%` }} />
                    </div>
                  </div>
                ) : null}
              </>
            ) : null}
            {tab === 'orders' ? <PaginatedTable rows={orders.data ?? []} columns={[{ key: 'id', header: 'ID', render: (r) => String(r.id) }, { key: 'status', header: 'Status', render: (r) => String(r.status) }]} /> : null}
            {tab === 'fills' ? <PaginatedTable rows={fills.data ?? []} columns={[{ key: 'id', header: 'ID', render: (r) => String(r.id) }, { key: 'price', header: 'Price', render: (r) => formatKvNumber(Number(r.fillPrice)) }]} /> : null}
            {tab === 'positions' ? (
              <PaginatedTable
                rows={positions.data ?? []}
                columns={[
                  { key: 'symbol', header: 'Symbol', render: (r) => symbolLabel(reference.allSymbols, r.symbolId, reference.exchanges) },
                  { key: 'qty', header: 'Qty', render: (r) => formatKvNumber(Number(r.quantity)) },
                  { key: 'pnl', header: 'Unrealized', render: (r) => formatKvNumber(Number(r.unrealizedPnl)) },
                ]}
              />
            ) : null}
            {tab === 'trades' ? <PaginatedTable rows={trades.data ?? []} columns={[{ key: 'id', header: 'ID', render: (r) => String(r.id) }, { key: 'pnl', header: 'PnL', render: (r) => formatKvNumber(Number(r.netPnl)) }]} /> : null}
            {tab === 'missed' ? <PaginatedTable rows={missed.data ?? []} columns={[{ key: 'id', header: 'ID', render: (r) => String(r.id) }, { key: 'reason', header: 'Reason', render: (r) => String(r.reason) }]} /> : null}
            {tab === 'equity' ? <PaginatedTable rows={equity.data ?? []} columns={[{ key: 'time', header: 'Time', render: (r) => formatKvDate(String(r.timestampUtc)) }, { key: 'equity', header: 'Equity', render: (r) => formatKvNumber(Number(r.equity)) }]} /> : null}
            {tab === 'signals' ? <PaginatedTable rows={signals.data ?? []} columns={[{ key: 'id', header: 'ID', render: (r) => String(r.id) }, { key: 'type', header: 'Type', render: (r) => String(r.signalType) }]} /> : null}
            {tab === 'risk' ? (
              <div className="space-y-4">
                {(riskDecisions.data ?? []).slice(0, 10).map((item, index) => (
                  <RiskDecisionView key={index} decision={item} />
                ))}
              </div>
            ) : null}
            {tab === 'ai' ? (
              <div className="space-y-4">
                {(aiDecisions.data ?? []).slice(0, 10).map((item, index) => (
                  <AiDecisionView key={index} decision={item} />
                ))}
              </div>
            ) : null}
            {tab === 'diagnostics' ? (
              <PipelineDiagnosticsPanel
                diagnostics={diagnostics.data}
                loading={diagnostics.loading && !diagnostics.data}
                error={diagnostics.error}
              />
            ) : null}
            {tab === 'live-diagnostics' ? (
              <div className="space-y-4">
                <div className="grid gap-3 md:grid-cols-3">
                  <MetricCard label="Provider" value={liveDiagnostics?.provider ?? '—'} />
                  <MetricCard label="Connected" value={liveDiagnostics?.connected ? 'Yes' : 'No'} />
                  <MetricCard label="Reconnect Attempts" value={String(liveDiagnostics?.reconnectAttempts ?? 0)} />
                </div>
                {sessionStreamDiagnostics.map((item) => (
                  <div key={`${item.symbol}-${item.timeframe}`} className="rounded-lg border border-slate-800 p-4 text-sm text-slate-300">
                    <p className="font-medium text-slate-100">{item.streamName}</p>
                    <div className="mt-2 grid gap-2 md:grid-cols-3">
                      <div>State: {item.connectionState}</div>
                      <div>Messages received: {item.messagesReceived}</div>
                      <div>Messages parsed: {item.messagesParsed}</div>
                      <div>Parse errors: {item.parseErrors}</div>
                      <div>Last raw: {formatKvDate(item.lastRawMessageAtUtc)}</div>
                      <div>Last parsed: {formatKvDate(item.lastParsedMessageAtUtc)}</div>
                      <div>Last snapshot: {formatKvDate(item.lastSnapshotUpdateUtc)}</div>
                      <div>Last closed: {formatKvDate(item.lastClosedCandleUtc)}</div>
                      <div>Last error: {item.lastError ?? '—'}</div>
                    </div>
                    {item.warning ? <p className="mt-2 text-amber-200">{item.warning}</p> : null}
                  </div>
                ))}
              </div>
            ) : null}
          </TabPanel>
        </>
      ) : null}
    </div>
  );
}
