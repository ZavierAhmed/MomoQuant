import { apiRequest } from '@/api/apiClient';

export interface MarketSituation {
  exchangeId: number;
  exchangeName: string;
  symbolId: number;
  symbol: string;
  timeframe: string;
  detectedAtUtc: string;
  latestPrice?: number;
  marketRegime: string;
  trendDirection: string;
  volatilityState: string;
  momentumState: string;
  volumeState: string;
  riskState: string;
  summary: string;
  signals: string[];
  warnings: string[];
  dataSource: string;
  latestCandleTimeUtc?: string | null;
  candleCountUsed: number;
  indicatorsAvailable: boolean;
}

export const marketSituationApi = {
  getCurrent: (query: { exchangeId: number; symbolId: number; timeframe: string }) =>
    apiRequest<MarketSituation>('/market-situation/current', { query }),
};
