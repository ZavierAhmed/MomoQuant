import { apiRequest } from '@/api/apiClient';
import type { Exchange, PagedQuery, PagedResult } from '@/api/domainTypes';

export interface ExchangeConnectionTest {
  restLatencyMs?: number | null;
  webSocketAvailable: boolean;
  message: string;
}

export interface CreateExchangeRequest {
  name: string;
  code: string;
  baseUrl: string;
  webSocketUrl: string;
  isActive: boolean;
}

export interface DeleteExchangeResult {
  exchangeId: number;
  exchangeCode: string;
  symbolsDeleted: number;
}

export const exchangesApi = {
  list: (query?: PagedQuery) => apiRequest<PagedResult<Exchange>>('/exchanges', { query }),
  get: (id: number) => apiRequest<Exchange>(`/exchanges/${id}`),
  create: (body: CreateExchangeRequest) => apiRequest<Exchange>('/exchanges', { method: 'POST', body }),
  update: (id: number, body: CreateExchangeRequest) => apiRequest<Exchange>(`/exchanges/${id}`, { method: 'PUT', body }),
  delete: (id: number) => apiRequest<DeleteExchangeResult>(`/exchanges/${id}`, { method: 'DELETE' }),
  testConnection: (id: number) =>
    apiRequest<ExchangeConnectionTest>(`/exchanges/${id}/test-connection`, { method: 'POST' }),
  listSymbols: (exchangeId: number, enabledOnly = true) =>
    apiRequest<import('@/api/domainTypes').ExchangeSymbolSummary[]>(`/exchanges/${exchangeId}/symbols`, {
      query: { enabledOnly },
    }),
};
