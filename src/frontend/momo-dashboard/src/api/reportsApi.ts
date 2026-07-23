import { apiRequest } from '@/api/apiClient';
import type { EquityCurvePoint, OverviewReport, ReportQuery } from '@/api/domainTypes';

export const reportsApi = {
  getOverview: (query?: ReportQuery) => apiRequest<OverviewReport>('/reports/overview', { query }),
  getBacktestReport: (id: number) => apiRequest<Record<string, unknown>>(`/reports/backtests/${id}`),
  getBacktestEquityCurve: (id: number) => apiRequest<EquityCurvePoint[]>(`/reports/backtests/${id}/equity-curve`),
  getBacktestDrawdown: (id: number) => apiRequest<Record<string, unknown>>(`/reports/backtests/${id}/drawdown`),
  getBacktestStrategyPerformance: (id: number) =>
    apiRequest<Record<string, unknown>[]>(`/reports/backtests/${id}/strategy-performance`),
  getBacktestSymbolPerformance: (id: number) =>
    apiRequest<Record<string, unknown>[]>(`/reports/backtests/${id}/symbol-performance`),
  getBacktestRiskRejections: (id: number, query?: ReportQuery) =>
    apiRequest<Record<string, unknown>>(`/reports/backtests/${id}/risk-rejections`, { query }),
  getBacktestAiDecisions: (id: number, query?: ReportQuery) =>
    apiRequest<Record<string, unknown>>(`/reports/backtests/${id}/ai-decisions`, { query }),
  getBacktestMissedOrders: (id: number, query?: ReportQuery) =>
    apiRequest<Record<string, unknown>>(`/reports/backtests/${id}/missed-orders`, { query }),
  getPaperReport: (id: number) => apiRequest<Record<string, unknown>>(`/reports/paper/sessions/${id}`),
  getPaperEquityCurve: (id: number) => apiRequest<EquityCurvePoint[]>(`/reports/paper/sessions/${id}/equity-curve`),
  getPaperDrawdown: (id: number) => apiRequest<Record<string, unknown>>(`/reports/paper/sessions/${id}/drawdown`),
  getStrategyPerformance: (query?: ReportQuery) => apiRequest<Record<string, unknown>[]>('/reports/strategies', { query }),
  getSymbolPerformance: (query?: ReportQuery) => apiRequest<Record<string, unknown>[]>('/reports/symbols', { query }),
  getRiskReport: (query?: ReportQuery) => apiRequest<Record<string, unknown>>('/reports/risk', { query }),
  getAiReport: (query?: ReportQuery) => apiRequest<Record<string, unknown>>('/reports/ai', { query }),
  getExecutionReport: (query?: ReportQuery) => apiRequest<Record<string, unknown>>('/reports/execution', { query }),
};
