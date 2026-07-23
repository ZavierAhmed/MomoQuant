import { useEffect, useState } from 'react';
import { FormPanel } from '@/components/common/FormPanel';
import { MetricCard } from '@/components/common/MetricCard';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { LoadingState } from '@/components/common/LoadingState';
import { StatusPill } from '@/components/common/StatusPill';
import { FormActions } from '@/components/forms/FormActions';
import { SelectField } from '@/components/forms/fields';
import { TIMEFRAME_OPTIONS } from '@/constants/tradingOptions';
import {
  liveMarketApi,
  type LiveMarketDiagnostics,
  type LiveMarketSnapshot,
  type LiveMarketStatus,
} from '@/api/liveMarketApi';
import { marketSituationApi, type MarketSituation } from '@/api/marketSituationApi';
import { strategyRecommendationsApi, type StrategyRecommendationResponse } from '@/api/strategyRecommendationsApi';
import { useReferenceData } from '@/hooks/useReferenceData';
import { useRole } from '@/hooks/useRole';
import { parseApiClientError } from '@/utils/apiError';
import { requireNumber } from '@/utils/numbers';
import { formatKvDate } from '@/components/common/KeyValueGrid';

export function LiveMarketTab() {
  const { canEdit } = useRole();
  const [exchangeId, setExchangeId] = useState<number | ''>('');
  const [symbolId, setSymbolId] = useState<number | ''>('');
  const [timeframe, setTimeframe] = useState('3m');
  const [status, setStatus] = useState<LiveMarketStatus | null>(null);
  const [diagnostics, setDiagnostics] = useState<LiveMarketDiagnostics | null>(null);
  const [snapshot, setSnapshot] = useState<LiveMarketSnapshot | null>(null);
  const [situation, setSituation] = useState<MarketSituation | null>(null);
  const [recommendations, setRecommendations] = useState<StrategyRecommendationResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const reference = useReferenceData(exchangeId || null);

  useEffect(() => {
    async function refresh() {
      try {
        const [nextStatus, nextDiagnostics] = await Promise.all([
          liveMarketApi.getStatus(),
          liveMarketApi.getDiagnostics(),
        ]);
        setStatus(nextStatus);
        setDiagnostics(nextDiagnostics);
        if (symbolId) {
          const snap = await liveMarketApi.getSnapshot(Number(symbolId), timeframe);
          setSnapshot(snap);
        }
      } catch {
        // Keep last known values during transient poll failures.
      }
    }

    void refresh();
    const timer = window.setInterval(() => void refresh(), 2000);
    return () => window.clearInterval(timer);
  }, [symbolId, timeframe]);

  async function handleSubscribe() {
    if (!canEdit || !exchangeId || !symbolId) return;
    setError(null);
    setLoading(true);
    try {
      const next = await liveMarketApi.subscribe({
        exchangeId: requireNumber(exchangeId, 'Exchange'),
        symbolId: requireNumber(symbolId, 'Symbol'),
        timeframe,
      });
      setStatus(next);
      const [snap, nextDiagnostics] = await Promise.all([
        liveMarketApi.getSnapshot(requireNumber(symbolId, 'Symbol'), timeframe).catch(() => null),
        liveMarketApi.getDiagnostics(),
      ]);
      setSnapshot(snap);
      setDiagnostics(nextDiagnostics);
    } catch (err) {
      setError(parseApiClientError(err).message);
    } finally {
      setLoading(false);
    }
  }

  async function loadAnalysis() {
    if (!exchangeId || !symbolId) return;
    setError(null);
    setLoading(true);
    try {
      const [situationResult, recommendationResult] = await Promise.all([
        marketSituationApi.getCurrent({
          exchangeId: requireNumber(exchangeId, 'Exchange'),
          symbolId: requireNumber(symbolId, 'Symbol'),
          timeframe,
        }),
        strategyRecommendationsApi.getCurrent({
          exchangeId: requireNumber(exchangeId, 'Exchange'),
          symbolId: requireNumber(symbolId, 'Symbol'),
          timeframe,
          mode: 'LivePaper',
        }),
      ]);
      setSituation(situationResult);
      setRecommendations(recommendationResult);
    } catch (err) {
      setError(parseApiClientError(err).message);
    } finally {
      setLoading(false);
    }
  }

  const selectedDiagnostics = diagnostics?.subscriptions.find(
    (item) => item.symbolId === Number(symbolId) && item.timeframe === timeframe,
  );

  const connectedButNoPrice =
    status?.connected &&
    selectedDiagnostics &&
    selectedDiagnostics.messagesReceived === 0;

  const parseFailing =
    selectedDiagnostics &&
    selectedDiagnostics.messagesReceived > 0 &&
    selectedDiagnostics.parseErrors > 0 &&
    selectedDiagnostics.messagesParsed === 0;

  return (
    <div className="space-y-4">
      <ApiErrorAlert message={error} />
      <div className="grid gap-4 md:grid-cols-3">
        <MetricCard label="Provider" value={status?.provider ?? '—'} />
        <MetricCard label="Connected" value={status?.connected ? 'Yes' : 'No'} />
        <MetricCard label="Subscriptions" value={String(status?.subscriptions.length ?? 0)} />
      </div>

      <FormPanel title="Live Subscription" description="Subscribe to Binance public kline streams. No API keys required.">
        <div className="grid gap-4 md:grid-cols-3">
          <SelectField label="Exchange" value={exchangeId} onChange={(v) => { setExchangeId(v); setSymbolId(''); }} options={reference.exchangeOptions} />
          <SelectField label="Symbol" value={symbolId} onChange={setSymbolId} options={reference.symbolOptions} />
          <SelectField label="Timeframe" value={timeframe} onChange={(v) => setTimeframe(v || '3m')} options={TIMEFRAME_OPTIONS} />
        </div>
        <FormActions>
          {canEdit ? <button type="button" onClick={() => void handleSubscribe()} className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950">Subscribe</button> : null}
          <button type="button" onClick={() => void loadAnalysis()} className="rounded-lg border border-slate-700 px-4 py-2 text-sm text-slate-200">Analyze Current Market</button>
        </FormActions>
      </FormPanel>

      {loading ? <LoadingState /> : null}

      {connectedButNoPrice ? (
        <p className="rounded-lg border border-amber-800/60 bg-amber-950/30 px-3 py-2 text-sm text-amber-100">
          Connected to live provider but no kline messages received yet. Check stream name and endpoint.
        </p>
      ) : null}

      {parseFailing ? (
        <p className="rounded-lg border border-red-800/60 bg-red-950/30 px-3 py-2 text-sm text-red-100">
          Live messages are arriving but parsing is failing.
        </p>
      ) : null}

      {snapshot ? (
        <FormPanel title="Latest Live Snapshot">
          <div className="grid gap-4 md:grid-cols-3">
            <MetricCard label="Symbol" value={snapshot.symbol} />
            <MetricCard label="Latest Price" value={snapshot.latestPrice?.toString() ?? '—'} />
            <MetricCard label="Last Live Update" value={formatKvDate(snapshot.lastLiveUpdateUtc ?? snapshot.lastUpdateUtc)} />
            <MetricCard label="Current Candle Open" value={formatKvDate(snapshot.openTimeUtc ?? snapshot.currentCandle?.openTimeUtc)} />
            <MetricCard label="Current Candle Close" value={formatKvDate(snapshot.closeTimeUtc ?? snapshot.currentCandle?.closeTimeUtc)} />
            <MetricCard label="Last Closed Candle" value={formatKvDate(snapshot.lastClosedCandleUtc)} />
          </div>
          {status?.connected && snapshot.latestPrice == null ? (
            <p className="mt-3 text-sm text-amber-200">
              Subscription reports connected but latest price is blank.
              {selectedDiagnostics?.warning ? ` ${selectedDiagnostics.warning}` : ' Check live diagnostics below.'}
            </p>
          ) : null}
        </FormPanel>
      ) : null}

      <FormPanel title="Active Subscriptions">
        <div className="space-y-2">
          {(status?.subscriptions ?? []).map((item) => (
            <div key={`${item.symbol}-${item.timeframe}`} className="rounded-lg border border-slate-800 px-3 py-2 text-sm text-slate-300">
              <div className="flex items-center justify-between gap-2">
                <span>{item.symbol} {item.timeframe}</span>
                <StatusPill status={item.status} />
              </div>
              <p className="mt-1 text-xs text-slate-500">{item.streamName ?? `${item.symbol.toLowerCase()}@kline_${item.timeframe}`}</p>
              {item.warning ? <p className="mt-1 text-xs text-amber-200">{item.warning}</p> : null}
            </div>
          ))}
          {(status?.subscriptions.length ?? 0) === 0 ? (
            <p className="text-sm text-slate-500">No active live subscriptions.</p>
          ) : null}
        </div>
      </FormPanel>

      <FormPanel title="Live Diagnostics">
        <div className="mb-3 grid gap-3 md:grid-cols-3">
          <MetricCard label="Provider" value={diagnostics?.provider ?? '—'} />
          <MetricCard label="Connected" value={diagnostics?.connected ? 'Yes' : 'No'} />
          <MetricCard label="Reconnect Attempts" value={String(diagnostics?.reconnectAttempts ?? 0)} />
        </div>
        <div className="space-y-3">
          {(diagnostics?.subscriptions ?? []).map((item) => (
            <div key={item.streamName} className="rounded-lg border border-slate-800 p-3 text-sm text-slate-300">
              <p className="font-medium text-slate-100">{item.streamName}</p>
              <div className="mt-2 grid gap-2 md:grid-cols-3 text-xs">
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
      </FormPanel>

      {situation ? (
        <FormPanel title="Market Situation">
          <p className="mb-2 text-sm text-slate-300">{situation.summary}</p>
          <div className="grid gap-3 md:grid-cols-3">
            <MetricCard label="Regime" value={situation.marketRegime} />
            <MetricCard label="Trend" value={situation.trendDirection} />
            <MetricCard label="Volatility" value={situation.volatilityState} />
            <MetricCard label="Momentum" value={situation.momentumState} />
            <MetricCard label="Volume" value={situation.volumeState} />
            <MetricCard label="Data Source" value={situation.dataSource} />
          </div>
        </FormPanel>
      ) : null}

      {recommendations ? (
        <FormPanel title="Strategy Recommendations (LivePaper)">
          <div className="space-y-3">
            {recommendations.warning ? <p className="text-sm text-amber-200">{recommendations.warning}</p> : null}
            {recommendations.recommendedStrategies.map((item) => (
              <div key={item.strategyId} className="rounded-lg border border-slate-800 p-3">
                <div className="flex items-center justify-between gap-2">
                  <div>
                    <p className="font-medium text-slate-100">{item.strategyName}</p>
                    <p className="text-xs text-slate-500">{item.strategyCode}</p>
                  </div>
                  <div className="flex items-center gap-2">
                    <StatusPill status={item.recommended ? 'Recommended' : 'Not Recommended'} />
                    <span className="text-sm text-slate-300">{item.suitabilityScore}</span>
                  </div>
                </div>
                <p className="mt-2 text-sm text-slate-400">{item.reason}</p>
              </div>
            ))}
          </div>
        </FormPanel>
      ) : null}
    </div>
  );
}
