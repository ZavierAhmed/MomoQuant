import { apiRequest } from '@/api/apiClient';
import type { RiskDecision, RiskProfile, RiskRule, PagedQuery, PagedResult } from '@/api/domainTypes';

export const riskApi = {
  listProfiles: () => apiRequest<RiskProfile[]>('/risk/profiles'),
  getProfile: (id: number) => apiRequest<RiskProfile>(`/risk/profiles/${id}`),
  createProfile: (body: { name: string; description: string; isDefault: boolean }) =>
    apiRequest<RiskProfile>('/risk/profiles', { method: 'POST', body }),
  updateProfile: (id: number, body: { name: string; description: string; isDefault: boolean }) =>
    apiRequest<RiskProfile>(`/risk/profiles/${id}`, { method: 'PUT', body }),
  getRules: (profileId: number) => apiRequest<RiskRule[]>(`/risk/profiles/${profileId}/rules`),
  updateRules: (profileId: number, rules: Array<Record<string, unknown>>) =>
    apiRequest<RiskRule[]>(`/risk/profiles/${profileId}/rules`, { method: 'PUT', body: { rules } }),
  listDecisions: (query?: PagedQuery & { symbolId?: number }) =>
    apiRequest<PagedResult<RiskDecision>>('/risk/decisions', { query }),
  getDecision: (id: number) => apiRequest<RiskDecision>(`/risk/decisions/${id}`),
  evaluate: (body: Record<string, unknown>) =>
    apiRequest<Record<string, unknown>>('/risk/evaluate', { method: 'POST', body }),
};
