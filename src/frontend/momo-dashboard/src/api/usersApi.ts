import { apiRequest } from '@/api/apiClient';
import type { PagedQuery, PagedResult, UserRecord } from '@/api/domainTypes';

export const usersApi = {
  list: (query?: PagedQuery) => apiRequest<PagedResult<UserRecord>>('/users', { query }),
  get: (id: number) => apiRequest<UserRecord>(`/users/${id}`),
  create: (body: { fullName: string; email: string; password: string; role: number }) =>
    apiRequest<UserRecord>('/users', { method: 'POST', body }),
  update: (id: number, body: { fullName: string; email: string; role: number; isActive: boolean }) =>
    apiRequest<UserRecord>(`/users/${id}`, { method: 'PUT', body }),
  disable: (id: number) => apiRequest<UserRecord>(`/users/${id}/disable`, { method: 'POST' }),
};
