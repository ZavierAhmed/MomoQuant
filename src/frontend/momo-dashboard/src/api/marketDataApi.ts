import { apiRequest } from '@/api/apiClient';
import type { ImportCandlesRequest, MarketCandle, MarketDataImport, MarketSnapshot } from '@/api/domainTypes';

export interface MarketDataSettings {
  historicalProvider: string;
}

export interface MarketDataQualityGap {
  fromUtc: string;
  toUtc: string;
  missingCount: number;
}

export interface MarketDataQuality {
  exchangeId: number;
  symbolId: number;
  timeframe: string;
  fromUtc: string;
  toUtc: string;
  totalCandles: number;
  expectedCandles: number;
  missingCandles: number;
  duplicateCandles: number;
  firstOpenTimeUtc?: string | null;
  lastOpenTimeUtc?: string | null;
  coveragePercent: number;
  gaps: MarketDataQualityGap[];
}

export const marketDataApi = {
  getSettings: () => apiRequest<MarketDataSettings>('/market-data/settings'),
  getSnapshot: (symbolId: number, timeframe: string) =>
    apiRequest<MarketSnapshot>('/market-data/snapshot', { query: { symbolId, timeframe } }),
  importCandles: (body: ImportCandlesRequest) =>
    apiRequest<MarketDataImport>('/market-data/candles/import', { method: 'POST', body }),
  getImport: (importId: number) => apiRequest<MarketDataImport>(`/market-data/imports/${importId}`),
  getRecentImports: (limit = 20) =>
    apiRequest<MarketDataImport[]>('/market-data/imports', { query: { limit } }),
  getQuality: (query: {
    exchangeId: number;
    symbolId: number;
    timeframe: string;
    fromUtc: string;
    toUtc: string;
  }) => apiRequest<MarketDataQuality>('/market-data/quality', { query }),
  getCandles: (query: {
    symbolId: number;
    timeframe: string;
    fromUtc?: string;
    toUtc?: string;
    limit?: number;
  }) => apiRequest<MarketCandle[]>('/market-data/candles', { query }),
};
