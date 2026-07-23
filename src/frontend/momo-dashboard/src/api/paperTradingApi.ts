import { apiRequest } from '@/api/apiClient';
import type {
  EquityCurvePoint,
  LivePaperChartData,
  PagedQuery,
  PagedResult,
  PaperAccount,
  PaperPosition,
  PaperSession,
  PaperSessionStatus,
  PipelineDiagnostics,
} from '@/api/domainTypes';

export const paperTradingApi = {
  listAccounts: (query?: PagedQuery) => apiRequest<PagedResult<PaperAccount>>('/paper/accounts', { query }),
  getAccount: (id: number) => apiRequest<PaperAccount>(`/paper/accounts/${id}`),
  createAccount: (body: { name: string; initialBalance: number; currency: string }) =>
    apiRequest<PaperAccount>('/paper/accounts', { method: 'POST', body }),
  updateAccount: (id: number, body: { name: string; isActive: boolean }) =>
    apiRequest<PaperAccount>(`/paper/accounts/${id}`, { method: 'PUT', body }),
  resetAccount: (id: number) => apiRequest<PaperAccount>(`/paper/accounts/${id}/reset`, { method: 'POST' }),
  getSnapshots: (id: number) => apiRequest<Record<string, unknown>[]>(`/paper/accounts/${id}/snapshots`),
  createSession: (body: Record<string, unknown>) =>
    apiRequest<PaperSession>('/paper/sessions', { method: 'POST', body }),
  listSessions: (query?: PagedQuery) => apiRequest<PagedResult<PaperSession>>('/paper/sessions', { query }),
  getSession: (id: number) => apiRequest<PaperSession>(`/paper/sessions/${id}`),
  startSession: (id: number) => apiRequest<Record<string, unknown>>(`/paper/sessions/${id}/start`, { method: 'POST' }),
  pauseSession: (id: number) => apiRequest<Record<string, unknown>>(`/paper/sessions/${id}/pause`, { method: 'POST' }),
  resumeSession: (id: number) => apiRequest<Record<string, unknown>>(`/paper/sessions/${id}/resume`, { method: 'POST' }),
  stopSession: (id: number) => apiRequest<Record<string, unknown>>(`/paper/sessions/${id}/stop`, { method: 'POST' }),
  tickSession: (id: number) => apiRequest<Record<string, unknown>>(`/paper/sessions/${id}/tick`, { method: 'POST' }),
  getSessionStatus: (id: number) => apiRequest<PaperSessionStatus>(`/paper/sessions/${id}/status`),
  getSessionLiveStatus: (id: number) => apiRequest<PaperSessionStatus>(`/paper/sessions/${id}/live-status`),
  getSessionLiveChart: (id: number, query?: { symbolId?: number; timeframe?: string; limit?: number }) =>
    apiRequest<LivePaperChartData>(`/paper/sessions/${id}/live-chart`, { query }),
  getOrders: (id: number) => apiRequest<Record<string, unknown>[]>(`/paper/sessions/${id}/orders`),
  getFills: (id: number) => apiRequest<Record<string, unknown>[]>(`/paper/sessions/${id}/fills`),
  getPositions: (id: number) => apiRequest<PaperPosition[]>(`/paper/sessions/${id}/positions`),
  getTrades: (id: number) => apiRequest<Record<string, unknown>[]>(`/paper/sessions/${id}/trades`),
  getMissedOrders: (id: number) => apiRequest<Record<string, unknown>[]>(`/paper/sessions/${id}/missed-orders`),
  getEquityCurve: (id: number) => apiRequest<EquityCurvePoint[]>(`/paper/sessions/${id}/equity-curve`),
  getSignals: (id: number) => apiRequest<Record<string, unknown>[]>(`/paper/sessions/${id}/signals`),
  getRiskDecisions: (id: number) => apiRequest<Record<string, unknown>[]>(`/paper/sessions/${id}/risk-decisions`),
  getAiDecisions: (id: number) => apiRequest<Record<string, unknown>[]>(`/paper/sessions/${id}/ai-decisions`),
  getDiagnostics: (id: number) => apiRequest<PipelineDiagnostics>(`/paper/sessions/${id}/diagnostics`),
};
