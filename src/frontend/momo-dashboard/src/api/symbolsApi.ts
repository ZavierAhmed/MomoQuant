import { apiRequest } from '@/api/apiClient';
import type { PagedQuery, PagedResult, Symbol } from '@/api/domainTypes';

export interface SymbolSyncResult {
  exchangeId: number;
  createdCount: number;
  updatedCount: number;
  totalCount: number;
}

export const symbolsApi = {
  list: (query?: PagedQuery & { exchangeId?: number }) =>
    apiRequest<PagedResult<Symbol>>('/symbols', { query }),
  get: (id: number) => apiRequest<Symbol>(`/symbols/${id}`),
  sync: (exchangeId: number) =>
    apiRequest<SymbolSyncResult>('/symbols/sync', { method: 'POST', body: { exchangeId } }),
  updateStatus: (id: number, isActive: boolean) =>
    apiRequest<Symbol>(`/symbols/${id}/status`, { method: 'PUT', body: { isActive } }),
};
