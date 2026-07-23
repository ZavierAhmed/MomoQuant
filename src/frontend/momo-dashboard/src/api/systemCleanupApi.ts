import { apiRequest } from '@/api/apiClient';

export const CLEAN_BASELINE_CONFIRMATION = 'CLEAN_MOMO_QUANT_BASELINE';

export interface CleanBaselineRequest {
  confirmation?: string;
  preserveAdminUser: boolean;
  preserveBinanceFuturesExchange: boolean;
  removeStrategies: boolean;
  removeSymbols: boolean;
  removeSimulationData: boolean;
  removeReports: boolean;
  removeMarketData: boolean;
}

export interface CleanBaselinePreviewItem {
  entityName: string;
  count: number;
  willDelete: boolean;
}

export interface CleanBaselinePreview {
  items: CleanBaselinePreviewItem[];
  warnings: string[];
  preserved: string[];
  generatedAtUtc: string;
}

export interface CleanBaselineResultItem {
  entityName: string;
  countBefore: number;
  countDeleted: number;
  countAfter: number;
}

export interface CleanBaselineResult {
  items: CleanBaselineResultItem[];
  warnings: string[];
  binanceFuturesExchangeAction: string;
  completedAtUtc: string;
}

export const systemCleanupApi = {
  previewCleanBaseline: (body: CleanBaselineRequest) =>
    apiRequest<CleanBaselinePreview>('/admin/system-cleanup/preview-clean-baseline', {
      method: 'POST',
      body,
    }),
  executeCleanBaseline: (body: CleanBaselineRequest) =>
    apiRequest<CleanBaselineResult>('/admin/system-cleanup/execute-clean-baseline', {
      method: 'POST',
      body,
    }),
};
