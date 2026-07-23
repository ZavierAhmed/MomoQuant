import { apiRequest } from '@/api/apiClient';
import type { PagedQuery, PagedResult, PipelineDiagnostics, ReplayChartData, ReplayChartQuery, ReplayControlResponse, ReplayFrame, ReplaySession } from '@/api/domainTypes';

export const replayApi = {
  create: (body: Record<string, unknown>) =>
    apiRequest<ReplaySession>('/replay/sessions', { method: 'POST', body }),
  list: (query?: PagedQuery) => apiRequest<PagedResult<ReplaySession>>('/replay/sessions', { query }),
  get: (id: number) => apiRequest<ReplaySession>(`/replay/sessions/${id}`),
  start: (id: number) => apiRequest<ReplayControlResponse>(`/replay/sessions/${id}/start`, { method: 'POST' }),
  pause: (id: number) => apiRequest<ReplayControlResponse>(`/replay/sessions/${id}/pause`, { method: 'POST' }),
  resume: (id: number) => apiRequest<ReplayControlResponse>(`/replay/sessions/${id}/resume`, { method: 'POST' }),
  stop: (id: number) => apiRequest<ReplayControlResponse>(`/replay/sessions/${id}/stop`, { method: 'POST' }),
  stepForward: (id: number) =>
    apiRequest<ReplayControlResponse>(`/replay/sessions/${id}/step-forward`, { method: 'POST' }),
  stepBackward: (id: number) =>
    apiRequest<ReplayControlResponse>(`/replay/sessions/${id}/step-backward`, { method: 'POST' }),
  setSpeed: (id: number, speed: string) =>
    apiRequest<ReplayControlResponse>(`/replay/sessions/${id}/speed`, { method: 'PUT', body: { speed } }),
  getCurrentFrame: (id: number) => apiRequest<ReplayFrame>(`/replay/sessions/${id}/current-frame`),
  getFrames: (id: number) => apiRequest<ReplayFrame[]>(`/replay/sessions/${id}/frames`),
  getSignals: (id: number) => apiRequest<Record<string, unknown>[]>(`/replay/sessions/${id}/signals`),
  getOrders: (id: number) => apiRequest<Record<string, unknown>[]>(`/replay/sessions/${id}/orders`),
  getTrades: (id: number) => apiRequest<Record<string, unknown>[]>(`/replay/sessions/${id}/trades`),
  getMissedOrders: (id: number) => apiRequest<Record<string, unknown>[]>(`/replay/sessions/${id}/missed-orders`),
  getRiskDecisions: (id: number) => apiRequest<Record<string, unknown>[]>(`/replay/sessions/${id}/risk-decisions`),
  getAiDecisions: (id: number) => apiRequest<Record<string, unknown>[]>(`/replay/sessions/${id}/ai-decisions`),
  getDiagnostics: (id: number) => apiRequest<PipelineDiagnostics>(`/replay/sessions/${id}/diagnostics`),
  getChart: (id: number, query?: ReplayChartQuery) =>
    apiRequest<ReplayChartData>(`/replay/sessions/${id}/chart`, { query: query as unknown as Record<string, unknown> }),
  getChartWindow: (id: number, query: ReplayChartQuery & { currentFrameIndex: number }) =>
    apiRequest<ReplayChartData>(`/replay/sessions/${id}/chart-window`, {
      query: query as unknown as Record<string, unknown>,
    }),
};
