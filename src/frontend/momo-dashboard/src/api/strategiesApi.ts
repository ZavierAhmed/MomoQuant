import { apiRequest } from '@/api/apiClient';
import type { Strategy, StrategyCatalogDetail, StrategyDetail, StrategyParameter } from '@/api/domainTypes';

export const strategiesApi = {
  list: () => apiRequest<Strategy[]>('/strategies'),
  get: (id: number) => apiRequest<StrategyDetail>(`/strategies/${id}`),
  getByCode: (strategyCode: string) => apiRequest<StrategyCatalogDetail>(`/strategies/code/${strategyCode}`),
  enable: (id: number) => apiRequest<Strategy>(`/strategies/${id}/enable`, { method: 'POST' }),
  disable: (id: number) => apiRequest<Strategy>(`/strategies/${id}/disable`, { method: 'POST' }),
  getParameters: (id: number) => apiRequest<StrategyParameter[]>(`/strategies/${id}/parameters`),
  updateParameters: (id: number, parameters: Array<Record<string, unknown>>) =>
    apiRequest<StrategyParameter[]>(`/strategies/${id}/parameters`, {
      method: 'PUT',
      body: { parameters },
    }),
  evaluate: (body: Record<string, unknown>) =>
    apiRequest<Record<string, unknown>>('/strategies/evaluate', { method: 'POST', body }),
  evaluateLatest: (body: Record<string, unknown>) =>
    apiRequest<Record<string, unknown>>('/strategies/evaluate-latest', { method: 'POST', body }),
};
