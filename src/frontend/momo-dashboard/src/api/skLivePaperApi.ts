import { apiRequest } from '@/api/apiClient';

export interface SkLivePaperDefaults {
  higherTimeframe: string;
  primaryTimeframe: string;
  additionalTimeframes: string[];
  startingBalance: number;
  riskPerPaperTradePercent: number;
  maxPaperTradesPerDay: number;
  maxOpenPaperPositions: number;
  allowLong: boolean;
  allowShort: boolean;
  requireHtfAgreement: boolean;
  minClarityScore: number;
  minUsefulnessScore: number;
  requireReactionConfirmation: boolean;
  confirmationMode: string;
  simulatedLeverage: number;
  simulationMode: string;
  safetyDisclaimer: string;
}

export interface SkLivePaperSession {
  id: number;
  sessionName: string;
  exchangeId: number;
  symbolId: number;
  symbol: string;
  higherTimeframe: string;
  primaryTimeframe: string;
  startingBalance: number;
  currentBalance: number;
  riskPerPaperTradePercent: number;
  maxPaperTradesPerDay: number;
  maxOpenPaperPositions: number;
  allowLong: boolean;
  allowShort: boolean;
  requireHtfAgreement: boolean;
  minClarityScore: number;
  minUsefulnessScore: number;
  requireReactionConfirmation: boolean;
  confirmationMode: string;
  simulatedLeverage: number;
  status: string;
  simulationMode: string;
  startedAtUtc?: string | null;
  stoppedAtUtc?: string | null;
  lastHeartbeatUtc?: string | null;
  lastAnalyzedCandleUtc?: string | null;
  lastError?: string | null;
  createdAtUtc: string;
}

export interface SkLivePaperSessionSummary {
  id: number;
  sessionName: string;
  symbol: string;
  status: string;
  currentBalance: number;
  netSimulatedPnl: number;
  openTrades: number;
  closedTrades: number;
  lastAnalyzedCandleUtc?: string | null;
  simulationMode: string;
}

export interface SkLivePaperCandidate {
  id: number;
  sessionId: number;
  symbol: string;
  direction: string;
  sequenceStatus: string;
  validityStatus: string;
  usefulnessStatus: string;
  clarityScore: number;
  usefulnessScore: number;
  reactionZoneLow: number;
  reactionZoneHigh: number;
  invalidationLevel: number;
  target1: number;
  target2: number;
  currentPrice: number;
  candidateStatus: string;
  rejectionReason?: string | null;
  createdAtUtc: string;
}

export interface SkLivePaperTrade {
  id: number;
  sessionId: number;
  symbol: string;
  direction: string;
  status: string;
  simulationMode: string;
  entryTimeUtc: string;
  entryPrice: number;
  quantity: number;
  simulatedLeverage: number;
  marginUsed: number;
  stopLoss: number;
  takeProfit1: number;
  takeProfit2: number;
  exitTimeUtc?: string | null;
  exitPrice?: number | null;
  exitReason?: string | null;
  netPnl: number;
  netPnlPercent: number;
}

export interface SkLivePaperEvent {
  id: number;
  eventType: string;
  message: string;
  createdAtUtc: string;
}

export interface SkLivePaperDiagnostics {
  webSocketStatus: string;
  closedCandlesProcessed: number;
  skAnalysesRun: number;
  candidatesDetected: number;
  candidatesRejected: number;
  paperTradesOpened: number;
  paperTradesClosed: number;
  lastHeartbeatUtc?: string | null;
  lastError?: string | null;
}

export interface SkLivePaperStatus {
  session: SkLivePaperSession;
  openTrades: number;
  closedTrades: number;
  netSimulatedPnl: number;
  lastCandidate?: SkLivePaperCandidate | null;
  diagnostics: SkLivePaperDiagnostics;
  safetyDisclaimer: string;
}

export interface CreateSkLivePaperSessionRequest {
  sessionName: string;
  exchangeId: number;
  symbolId: number;
  higherTimeframe?: string;
  primaryTimeframe?: string;
  additionalTimeframes?: string[];
  startingBalance?: number;
  riskPerPaperTradePercent?: number;
  maxPaperTradesPerDay?: number;
  maxOpenPaperPositions?: number;
  allowLong?: boolean;
  allowShort?: boolean;
  requireHtfAgreement?: boolean;
  minClarityScore?: number;
  minUsefulnessScore?: number;
  requireReactionConfirmation?: boolean;
  confirmationMode?: string;
  simulatedLeverage?: number;
}

export const skLivePaperApi = {
  getDefaults: () => apiRequest<SkLivePaperDefaults>('/trading-systems/sk/livepaper/defaults'),
  createSession: (request: CreateSkLivePaperSessionRequest) =>
    apiRequest<SkLivePaperSession>('/trading-systems/sk/livepaper/sessions', {
      method: 'POST',
      body: request,
    }),
  listSessions: (limit = 50) =>
    apiRequest<SkLivePaperSessionSummary[]>(`/trading-systems/sk/livepaper/sessions?limit=${limit}`),
  getStatus: (id: number) => apiRequest<SkLivePaperStatus>(`/trading-systems/sk/livepaper/sessions/${id}`),
  startSession: (id: number) =>
    apiRequest<SkLivePaperSession>(`/trading-systems/sk/livepaper/sessions/${id}/start`, { method: 'POST' }),
  pauseSession: (id: number) =>
    apiRequest<SkLivePaperSession>(`/trading-systems/sk/livepaper/sessions/${id}/pause`, { method: 'POST' }),
  resumeSession: (id: number) =>
    apiRequest<SkLivePaperSession>(`/trading-systems/sk/livepaper/sessions/${id}/resume`, { method: 'POST' }),
  stopSession: (id: number) =>
    apiRequest<SkLivePaperSession>(`/trading-systems/sk/livepaper/sessions/${id}/stop`, { method: 'POST' }),
  manualCloseTrade: (sessionId: number, tradeId: number) =>
    apiRequest<SkLivePaperTrade>(
      `/trading-systems/sk/livepaper/sessions/${sessionId}/manual-close/${tradeId}`,
      { method: 'POST' },
    ),
  getCandidates: (sessionId: number, limit = 100) =>
    apiRequest<SkLivePaperCandidate[]>(
      `/trading-systems/sk/livepaper/sessions/${sessionId}/candidates?limit=${limit}`,
    ),
  getTrades: (sessionId: number) =>
    apiRequest<SkLivePaperTrade[]>(`/trading-systems/sk/livepaper/sessions/${sessionId}/trades`),
  getEvents: (sessionId: number, limit = 200) =>
    apiRequest<SkLivePaperEvent[]>(`/trading-systems/sk/livepaper/sessions/${sessionId}/events?limit=${limit}`),
};
