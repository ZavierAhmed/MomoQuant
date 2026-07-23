import { apiRequest } from '@/api/apiClient';

export interface FakeMarketDataCleanupRequest {
  confirmation?: string;
  includeBacktests: boolean;
  includeReplay: boolean;
  includePaperTrading: boolean;
  includeAiDecisions: boolean;
  includeRiskDecisions: boolean;
  includeAuditLogs: boolean;
  resetPaperAccounts: boolean;
}

export interface FakeMarketDataCleanupPreviewItem {
  entityName: string;
  count: number;
  willDelete: boolean;
}

export interface FakeMarketDataCleanupPreview {
  items: FakeMarketDataCleanupPreviewItem[];
  warnings: string[];
  generatedAtUtc: string;
}

export interface FakeMarketDataCleanupResultItem {
  entityName: string;
  countBefore: number;
  countDeleted: number;
  countAfter: number;
}

export interface FakeMarketDataCleanupResult {
  items: FakeMarketDataCleanupResultItem[];
  warnings: string[];
  completedAtUtc: string;
}

export const adminDataCleanupApi = {
  previewFakeMarketData: (body: FakeMarketDataCleanupRequest) =>
    apiRequest<FakeMarketDataCleanupPreview>('/admin/data-cleanup/fake-market-data/preview', {
      method: 'POST',
      body,
    }),
  executeFakeMarketData: (body: FakeMarketDataCleanupRequest) =>
    apiRequest<FakeMarketDataCleanupResult>('/admin/data-cleanup/fake-market-data/execute', {
      method: 'POST',
      body,
    }),
};
