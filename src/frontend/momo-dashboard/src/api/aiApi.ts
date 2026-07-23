import { apiRequest } from '@/api/apiClient';
import type { AiDecision, AiHealth, PagedQuery, PagedResult } from '@/api/domainTypes';

export interface AiSetupAdvisorRequest {
  mode: 'Benchmark' | 'Backtest' | 'Replay' | 'HistoricalPaper' | 'LivePaper' | 'StrategyDiagnostic';
  symbolIds: number[];
  strategyIds: number[];
  fromDate?: string;
  toDate?: string;
  riskProfileId?: number;
  executionMode?: string;
  useAiScoring?: boolean;
}

export interface AiSetupAdvisorResponse {
  summary: string;
  recommendedExecutionScope: string;
  recommendedStrategies: number[];
  requiredTimeframes: string[];
  importPlan: Array<{ symbolId?: number; symbol?: string; timeframe: string; reason: string }>;
  indicatorPlan: Array<{ symbolId?: number; symbol?: string; timeframe: string; reason: string }>;
  riskWarnings: string[];
  dataWarnings: string[];
  expectedRuntime: string;
  estimatedRunCount: number;
  suggestions: string[];
  blockingIssues: string[];
}

export const aiApi = {
  getHealth: () => apiRequest<AiHealth>('/ai/health'),
  listDecisions: (query?: PagedQuery & { symbolId?: number }) =>
    apiRequest<PagedResult<AiDecision>>('/ai/decisions', { query }),
  getDecision: (id: number) => apiRequest<AiDecision>(`/ai/decisions/${id}`),
  detectRegime: (body: Record<string, unknown>) =>
    apiRequest<Record<string, unknown>>('/ai/regime/detect', { method: 'POST', body }),
  scoreConfidence: (body: Record<string, unknown>) =>
    apiRequest<Record<string, unknown>>('/ai/confidence/score', { method: 'POST', body }),
  detectAnomaly: (body: Record<string, unknown>) =>
    apiRequest<Record<string, unknown>>('/ai/anomaly/detect', { method: 'POST', body }),
  explainTrade: (body: Record<string, unknown>) =>
    apiRequest<Record<string, unknown>>('/ai/explain/trade', { method: 'POST', body }),
  setupAdvisor: (body: AiSetupAdvisorRequest) =>
    apiRequest<AiSetupAdvisorResponse>('/ai/setup-advisor', { method: 'POST', body }),
};
