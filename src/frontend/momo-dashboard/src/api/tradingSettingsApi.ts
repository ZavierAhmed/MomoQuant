import { apiRequest } from '@/api/apiClient';

export interface TradingSettings {
  maxLeverage: number;
  defaultLeverage: number;
  maxRiskPerTradePercent: number;
  defaultRiskPerTradePercent: number;
  maxDailyLossPercent: number;
  maxTotalDrawdownPercent: number;
  maxOpenPositions: number;
  maxTradesPerDay: number;
  maxTradesPerSymbolPerDay: number;
  maxPositionSizeUsd: number;
  minRewardRiskRatio: number;
  defaultRewardRiskRatio: number;
  makerFeeRate: number;
  takerFeeRate: number;
  slippageModel: string;
  slippagePercent: number;
  orderExpiryCandles: number;
  allowLongTrades: boolean;
  allowShortTrades: boolean;
  allowMultiplePositionsPerSymbol: boolean;
  allowPositionScaling: boolean;
  allowReversePosition: boolean;
  defaultBenchmarkEvaluationMode: string;
  defaultBenchmarkInitialBalance: number;
  defaultBenchmarkRiskProfileId?: number | null;
  defaultLivePaperRiskProfileId?: number | null;
  defaultConfidenceThreshold: number;
  confidenceHardGateDefault: boolean;
  useAiScoringDefault: boolean;
  strictAiRequiredDefault: boolean;
  enableShadowTradeAnalysis: boolean;
  sameCandleExitPolicy: string;
}

export const tradingSettingsApi = {
  get: () => apiRequest<TradingSettings>('/settings/trading'),
  update: (body: TradingSettings) => apiRequest<TradingSettings>('/settings/trading', { method: 'PUT', body }),
  resetDefaults: () => apiRequest<TradingSettings>('/settings/trading/reset-defaults', { method: 'POST' }),
};
