import { apiRequest } from '@/api/apiClient';

export interface LiveMarketStatus {
  provider: string;
  connected: boolean;
  reconnectAttempts?: number;
  subscriptions: Array<{
    exchangeId?: number;
    symbolId?: number;
    symbol: string;
    timeframe: string;
    streamName?: string;
    status: string;
    subscribedAtUtc?: string;
    lastUpdateUtc?: string;
    lastClosedCandleUtc?: string;
    warning?: string | null;
    lastError?: string | null;
  }>;
  lastError?: string | null;
}

export interface LiveMarketSnapshot {
  exchangeId: number;
  symbolId: number;
  symbol: string;
  timeframe: string;
  latestPrice?: number;
  open?: number;
  high?: number;
  low?: number;
  close?: number;
  volume?: number;
  quoteVolume?: number;
  tradeCount?: number;
  openTimeUtc?: string;
  closeTimeUtc?: string;
  isClosed?: boolean;
  lastUpdateUtc?: string;
  lastLiveUpdateUtc?: string;
  lastClosedCandleUtc?: string;
  currentCandle?: {
    openTimeUtc: string;
    closeTimeUtc: string;
    open: number;
    high: number;
    low: number;
    close: number;
    volume: number;
    quoteVolume?: number;
    tradeCount?: number;
    isClosed: boolean;
  };
  lastClosedCandle?: Record<string, unknown>;
  source?: string;
}

export interface LiveMarketSubscriptionDiagnostics {
  exchangeId: number;
  symbolId: number;
  symbol: string;
  timeframe: string;
  streamName: string;
  connectionState: string;
  subscribedAtUtc?: string | null;
  lastRawMessageAtUtc?: string | null;
  lastParsedMessageAtUtc?: string | null;
  lastSnapshotUpdateUtc?: string | null;
  lastClosedCandleUtc?: string | null;
  messagesReceived: number;
  messagesParsed: number;
  parseErrors: number;
  lastError?: string | null;
  warning?: string | null;
  linkedSessionIds?: number[];
}

export interface LiveMarketDiagnostics {
  provider: string;
  connected: boolean;
  reconnectAttempts: number;
  lastError?: string | null;
  subscriptions: LiveMarketSubscriptionDiagnostics[];
}

export const liveMarketApi = {
  getStatus: () => apiRequest<LiveMarketStatus>('/live-market/status'),
  getDiagnostics: () => apiRequest<LiveMarketDiagnostics>('/live-market/diagnostics'),
  getSnapshots: () => apiRequest<LiveMarketSnapshot[]>('/live-market/snapshots'),
  getSnapshot: (symbolId: number, timeframe: string) =>
    apiRequest<LiveMarketSnapshot>(`/live-market/snapshots/${symbolId}`, { query: { timeframe } }),
  subscribe: (body: { exchangeId: number; symbolId: number; timeframe: string }) =>
    apiRequest<LiveMarketStatus>('/live-market/subscribe', { method: 'POST', body }),
  unsubscribe: (body: { exchangeId: number; symbolId: number; timeframe: string }) =>
    apiRequest<LiveMarketStatus>('/live-market/unsubscribe', { method: 'POST', body }),
  reconnect: () => apiRequest<LiveMarketStatus>('/live-market/reconnect', { method: 'POST' }),
};
