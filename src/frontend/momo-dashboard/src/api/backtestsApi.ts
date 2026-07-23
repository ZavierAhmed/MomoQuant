import { apiRequest } from '@/api/apiClient';
import type {
  BacktestResult,
  BacktestRun,
  BacktestTrade,
  EquityCurvePoint,
  PagedQuery,
  PagedResult,
  PipelineDiagnostics,
  RunBacktestRequest,
} from '@/api/domainTypes';

export const backtestsApi = {
  run: (body: RunBacktestRequest) =>
    apiRequest<Record<string, unknown>>('/backtests/run', { method: 'POST', body }),
  list: (query?: PagedQuery) => apiRequest<PagedResult<BacktestRun>>('/backtests', { query }),
  get: (id: number) => apiRequest<BacktestRun>(`/backtests/${id}`),
  getResults: (id: number) => apiRequest<BacktestResult>(`/backtests/${id}/results`),
  getTrades: (id: number) => apiRequest<BacktestTrade[]>(`/backtests/${id}/trades`),
  getOrders: (id: number) => apiRequest<Record<string, unknown>[]>(`/backtests/${id}/orders`),
  getMissedOrders: (id: number) => apiRequest<Record<string, unknown>[]>(`/backtests/${id}/missed-orders`),
  getEquityCurve: (id: number) => apiRequest<EquityCurvePoint[]>(`/backtests/${id}/equity-curve`),
  getStrategyBreakdown: (id: number) => apiRequest<Record<string, unknown>[]>(`/backtests/${id}/strategy-breakdown`),
  getSymbolBreakdown: (id: number) => apiRequest<Record<string, unknown>[]>(`/backtests/${id}/symbol-breakdown`),
  getDiagnostics: (id: number) => apiRequest<PipelineDiagnostics>(`/backtests/${id}/diagnostics`),
};
