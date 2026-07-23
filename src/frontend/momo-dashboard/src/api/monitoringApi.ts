import { apiRequest } from '@/api/apiClient';
import type {
  ComponentHealth,
  HealthResponse,
  MonitoringQuery,
  PagedResult,
  RecentError,
  RecentEvent,
  SafetyEvent,
  SystemHealthLog,
  SystemStatus,
  TradingPipelineStatus,
} from '@/api/domainTypes';

export const monitoringApi = {
  getHealth: () => apiRequest<HealthResponse>('/monitoring/health'),
  getDatabaseHealth: () => apiRequest<ComponentHealth>('/monitoring/health/database'),
  getRedisHealth: () => apiRequest<ComponentHealth>('/monitoring/health/redis'),
  getAiHealth: () => apiRequest<ComponentHealth>('/monitoring/health/ai'),
  getSubsystemsHealth: () => apiRequest<ComponentHealth[]>('/monitoring/health/subsystems'),
  getStatus: () => apiRequest<SystemStatus>('/monitoring/status'),
  getSystemHealthLogs: (query?: MonitoringQuery) =>
    apiRequest<PagedResult<SystemHealthLog>>('/monitoring/system-health-logs', { query }),
  getSystemHealthLog: (id: number) => apiRequest<SystemHealthLog>(`/monitoring/system-health-logs/${id}`),
  getRecentErrors: (query?: MonitoringQuery) =>
    apiRequest<RecentError[]>('/monitoring/recent-errors', { query }),
  getRecentEvents: (query?: MonitoringQuery) =>
    apiRequest<RecentEvent[]>('/monitoring/recent-events', { query }),
  getSafetyEvents: (query?: MonitoringQuery) =>
    apiRequest<SafetyEvent[]>('/monitoring/safety-events', { query }),
  getTradingPipelineStatus: () => apiRequest<TradingPipelineStatus>('/monitoring/trading-pipeline-status'),
};
