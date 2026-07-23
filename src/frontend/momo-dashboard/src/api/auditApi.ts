import { apiRequest } from '@/api/apiClient';
import type { AuditLog, AuditLogQuery, AuditSummary, PagedResult } from '@/api/domainTypes';

export const auditApi = {
  getLogs: (query?: AuditLogQuery) => apiRequest<PagedResult<AuditLog>>('/audit/logs', { query }),
  getLog: (id: number) => apiRequest<AuditLog>(`/audit/logs/${id}`),
  getSummary: (query?: AuditLogQuery) => apiRequest<AuditSummary>('/audit/summary', { query }),
};
