import { apiRequest } from '@/api/apiClient';

export interface StrategyRecommendationItem {
  strategyId: number;
  strategyCode: string;
  strategyName: string;
  suitabilityScore: number;
  recommended: boolean;
  isEnabled: boolean;
  reason: string;
  warnings: string[];
}

export interface StrategyRecommendationResponse {
  exchangeId: number;
  exchangeName: string;
  symbolId: number;
  symbol: string;
  timeframe: string;
  mode: string;
  marketSituation: {
    marketRegime: string;
    trendDirection: string;
    volatilityState: string;
    momentumState: string;
  };
  recommendedStrategies: StrategyRecommendationItem[];
  selectedByDefaultStrategyIds: number[];
  generatedAtUtc: string;
  warning?: string | null;
}

export const strategyRecommendationsApi = {
  getCurrent: (query: { exchangeId: number; symbolId: number; timeframe: string; mode: string; showDisabled?: boolean }) =>
    apiRequest<StrategyRecommendationResponse>('/strategy-recommendations/current', { query }),
};
