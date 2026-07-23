import { apiRequest } from '@/api/apiClient';
import { logApiRequest } from '@/utils/apiDebug';
import { formatValidationApiError } from '@/constants/validationOptions';
import { parseApiClientError } from '@/utils/apiError';
import type { VgResearchProfileValue } from '@/constants/validationProfiles';

export type ValidationMode = 'None' | 'InSampleOutOfSample70_30';
export type ParameterOptimizationMode = 'ManualOnly' | 'GridSearch' | 'RandomSearch';

export interface StrategyPerformanceMetrics {
  netPnlPercent: number;
  winRate: number;
  profitFactor: number;
  maxDrawdownPercent: number;
  tradeCount: number;
  averageR: number;
  expectancy: number;
  recoveryFactor: number;
  largestLoss: number;
  consecutiveLosses: number;
}

export interface CandleCoverage {
  symbol: string;
  exchange: string;
  timeframe: string;
  requiredFromUtc: string;
  requiredToUtc: string;
  availableFromUtc?: string;
  availableToUtc?: string;
  candleCount: number;
  missingCandleCountEstimate: number;
  coverageStatus: string;
  importedDuringRun: boolean;
  importError?: string;
}

export interface StrategyFunnelDiagnostics {
  evaluations: number;
  superTrendBullishCount: number;
  superTrendBearishCount: number;
  volatilityGatePassedCount: number;
  volatilityGateFailedCount: number;
  momentumPassedCount: number;
  momentumFailedCount: number;
  retestDetectedCount: number;
  retestMissingCount: number;
  confirmationDetectedCount: number;
  confirmationMissingCount: number;
  candidateSignals: number;
  riskRejectedCount: number;
  tradesCreated: number;
  topRejectionReason?: string;
  rejectionReasonBreakdown: Record<string, number>;
  pipelineSummary?: string;
}

export interface ZeroTradeAnalysis {
  mostLikelyBlocker: string;
  suggestedNextAction: string;
  relatedParameter?: string;
  explanation: string;
  reasonCode?: string;
}

export interface StrategyValidationResult {
  strategyCode: string;
  symbol: string;
  exchange: string;
  timeframe: string;
  fullDateRange: { fromUtc: string; toUtc: string };
  trainingRange: { fromUtc: string; toUtc: string };
  validationRange: { fromUtc: string; toUtc: string };
  parameterSetId?: number;
  trainingMetrics?: StrategyPerformanceMetrics;
  validationMetrics?: StrategyPerformanceMetrics;
  robustnessScore: number;
  validationStatus: string;
  parameters?: Record<string, string>;
  failReasons: string[];
  warnings: string[];
  createdAtUtc: string;
  backtestEngineUsed?: string;
  strategyParametersUsed?: Record<string, string>;
  resolvedExecutionTimeframe?: string;
  requiredDataTimeframes?: string[];
  candleCoverage?: CandleCoverage[];
  trainingCandleCount?: number;
  validationCandleCount?: number;
  trainingWarmupCandlesLoaded?: number;
  validationWarmupCandlesLoaded?: number;
  trainingEvaluationCandles?: number;
  validationEvaluationCandles?: number;
  trainingEvaluations?: number;
  validationEvaluations?: number;
  skippedForWarmupCount?: number;
  engineEvaluationBug?: boolean;
  importedDuringRun?: boolean;
  trainingFunnel?: StrategyFunnelDiagnostics;
  validationFunnel?: StrategyFunnelDiagnostics;
  diagnosticsSummary?: string;
  whyZeroTrades?: ZeroTradeAnalysis;
  trainingWhyZeroTrades?: ZeroTradeAnalysis;
  validationWhyZeroTrades?: ZeroTradeAnalysis;
  vgResearchProfile?: string;
  isExploratoryProfile?: boolean;
}

export interface ParameterSetResult {
  rank: number;
  parameters: Record<string, string>;
  trainingMetrics?: StrategyPerformanceMetrics;
  validationMetrics?: StrategyPerformanceMetrics;
  robustnessScore: number;
  optimizationScore: number;
  passStatus: string;
  failReasons: string[];
  warnings: string[];
}

export interface ParameterOptimizationResult {
  optimizationRunId: number;
  strategyCode: string;
  symbol: string;
  timeframe: string;
  totalCombinations: number;
  completedCombinations: number;
  status: string;
  bestParameterSets: ParameterSetResult[];
  rejectedParameterSets: ParameterSetResult[];
  zeroTradeParameterSets: ParameterSetResult[];
  zeroTradeParameterSetCount: number;
  tradeProducingParameterSetCount: number;
  bestNonZeroTradeParameterSet?: ParameterSetResult;
  warnings: string[];
  createdAtUtc: string;
  completedAtUtc?: string;
}

export interface StrategyParameterSet {
  id: number;
  name: string;
  strategyCode: string;
  symbolId?: number;
  timeframe: string;
  parameters: Record<string, string>;
  source: string;
  robustnessScore?: number;
  isApproved: boolean;
  createdAtUtc: string;
  approvedAtUtc?: string;
}

export interface StrategyParameterDefinition {
  key: string;
  label: string;
  type: string;
  defaultValue: string;
  minValue?: string;
  maxValue?: string;
  step?: string;
  isOptimizable: boolean;
  optimizationGroup?: string;
}

export interface StrategyResearchExecutionSettings {
  executionMode?: string;
  makerFeeRate?: number;
  takerFeeRate?: number;
  orderExpiryCandles?: number;
  useAiScoring?: boolean;
  minConfidenceScore?: number;
  slippagePercent?: number;
  autoImportCandles?: boolean;
  vgResearchProfile?: VgResearchProfileValue;
}

export const strategyResearchApi = {
  runValidation: async (body: {
    strategyCode: string;
    exchangeId: number;
    symbolId: number;
    timeframe: string;
    fromUtc: string;
    toUtc: string;
    validationMode: ValidationMode;
    parameterSetId?: number;
    riskProfileId: number;
    initialBalance?: number;
  } & StrategyResearchExecutionSettings) => {
    const payload = {
      ...body,
      fromDate: body.fromUtc.slice(0, 10),
      toDate: body.toUtc.slice(0, 10),
    };
    logApiRequest('/strategy-research/validation/run', payload);
    try {
      return await apiRequest<StrategyValidationResult>('/strategy-research/validation/run', { method: 'POST', body: payload });
    } catch (error) {
      const parsed = parseApiClientError(error);
      throw new Error(formatValidationApiError(parsed.message, parsed.fieldErrors));
    }
  },

  runOptimization: async (body: {
    strategyCode: string;
    exchangeId: number;
    symbolId: number;
    timeframe: string;
    fromUtc: string;
    toUtc: string;
    validationMode: ValidationMode;
    optimizationMode: ParameterOptimizationMode;
    objectivePreset?: string;
    maxCombinations?: number;
    saveBestParameterSet?: boolean;
    parameterSetName?: string;
    riskProfileId: number;
    initialBalance?: number;
  } & StrategyResearchExecutionSettings) => {
    const payload = {
      ...body,
      fromDate: body.fromUtc.slice(0, 10),
      toDate: body.toUtc.slice(0, 10),
    };
    logApiRequest('/strategy-research/optimization/run', payload);
    try {
      return await apiRequest<ParameterOptimizationResult>('/strategy-research/optimization/run', { method: 'POST', body: payload });
    } catch (error) {
      const parsed = parseApiClientError(error);
      throw new Error(formatValidationApiError(parsed.message, parsed.fieldErrors));
    }
  },

  getOptimization: (runId: number) => apiRequest<ParameterOptimizationResult>(`/strategy-research/optimization/${runId}`),

  listParameterSets: (query?: { strategyCode?: string; symbolId?: number; timeframe?: string }) =>
    apiRequest<StrategyParameterSet[]>('/strategy-research/parameter-sets', { query }),

  saveParameterSet: (body: {
    name: string;
    strategyCode: string;
    symbolId?: number;
    timeframe: string;
    parameters: Record<string, string>;
    source?: string;
    optimizationRunId?: number;
    trainingRange?: { fromUtc: string; toUtc: string };
    validationRange?: { fromUtc: string; toUtc: string };
    trainingMetrics?: StrategyPerformanceMetrics;
    validationMetrics?: StrategyPerformanceMetrics;
    robustnessScore?: number;
    approve?: boolean;
    setAsDefault?: boolean;
    validationStatus?: string;
    validationTradeCount?: number;
    saveAsFailedResearch?: boolean;
  }) => apiRequest<StrategyParameterSet>('/strategy-research/parameter-sets', { method: 'POST', body }),

  approveParameterSet: (id: number) =>
    apiRequest<StrategyParameterSet>(`/strategy-research/parameter-sets/${id}/approve`, { method: 'POST' }),

  getParameterDefinitions: (strategyCode: string) =>
    apiRequest<StrategyParameterDefinition[]>(`/strategy-research/parameter-definitions/${strategyCode}`),

  runTargetOptimization: async (body: {
    strategyCode: string;
    exchangeId: number;
    symbolId: number;
    timeframe: string;
    fromDate: string;
    toDate: string;
    parameterSearchMode: string;
    targetRules: Record<string, unknown>;
    parameterRanges?: Record<string, string>;
    maxCombinations?: number;
    maxAttempts?: number;
    maxRuntimeMinutes?: number;
    initialBalance?: number;
    riskProfileId: number;
    makerFeeRate?: number;
    takerFeeRate?: number;
    slippagePercent?: number;
    autoImportMissingCandles?: boolean;
    saveBestIfPassed?: boolean;
    autoApproveIfPassed?: boolean;
  } & StrategyResearchExecutionSettings) => {
    logApiRequest('/strategy-research/target-optimization/run', body);
    try {
      return await apiRequest<TargetOptimizationRun>('/strategy-research/target-optimization/run', { method: 'POST', body });
    } catch (error) {
      const parsed = parseApiClientError(error);
      throw new Error(formatValidationApiError(parsed.message, parsed.fieldErrors));
    }
  },

  getTargetOptimization: (runId: number) =>
    apiRequest<TargetOptimizationRun>(`/strategy-research/target-optimization/${runId}`),

  cancelTargetOptimization: (runId: number) =>
    apiRequest<boolean>(`/strategy-research/target-optimization/${runId}/cancel`, { method: 'POST' }),

  saveTargetOptimizationBest: (runId: number, body: { approve?: boolean; name?: string; saveAsFailedResearch?: boolean }) =>
    apiRequest<StrategyParameterSet>(`/strategy-research/target-optimization/${runId}/save-best`, { method: 'POST', body }),

  approveTargetOptimizationBest: (runId: number) =>
    apiRequest<StrategyParameterSet>(`/strategy-research/target-optimization/${runId}/approve-best`, { method: 'POST' }),
};

export type ParameterSetTestStatus =
  | 'TrainingFailed'
  | 'TrainingPassed'
  | 'ValidationPassed'
  | 'ValidationFailed'
  | 'Overfit'
  | 'TooFewTrades'
  | 'TooHighDrawdown'
  | 'NoTrades'
  | 'EngineError';

export interface TargetPassSummary {
  trainingPnlPassed: boolean;
  validationPnlPassed: boolean;
  trainingProfitFactorPassed: boolean;
  validationProfitFactorPassed: boolean;
  trainingDrawdownPassed: boolean;
  validationDrawdownPassed: boolean;
  trainingTradesPassed: boolean;
  validationTradesPassed: boolean;
  robustnessPassed: boolean;
}

export interface TargetParameterSetResult {
  rank: number;
  status: ParameterSetTestStatus | string;
  parameters: Record<string, string>;
  trainingMetrics?: StrategyPerformanceMetrics;
  validationMetrics?: StrategyPerformanceMetrics;
  robustnessScore: number;
  score: number;
  targetPassSummary: TargetPassSummary;
  failReasons: string[];
  overfitWarnings: string[];
  savedParameterSetId?: number;
  isApproved: boolean;
}

export interface TargetOptimizationSummary {
  bestStatus: string;
  passedCount: number;
  overfitCount: number;
  failedCount: number;
  trainingPassedCount: number;
  bestRobustnessScore?: number;
  bestValidationNetPnlPercent?: number;
  bestValidationProfitFactor?: number;
  bestValidationDrawdownPercent?: number;
}

export interface TargetOptimizationRun {
  id: number;
  strategyCode: string;
  symbolId: number;
  exchange: string;
  symbol: string;
  timeframe: string;
  dateRange: { fromUtc: string; toUtc: string };
  trainingRange: { fromUtc: string; toUtc: string };
  validationRange: { fromUtc: string; toUtc: string };
  targetRules: Record<string, unknown>;
  parameterSearchMode: string;
  maxCombinations: number;
  completedCombinations: number;
  status: string;
  bestPassedParameterSet?: TargetParameterSetResult;
  bestFailedParameterSet?: TargetParameterSetResult;
  results: TargetParameterSetResult[];
  summary: TargetOptimizationSummary;
  warnings: string[];
  currentParameters?: Record<string, string>;
  trainingPassedCount: number;
  validationPassedCount: number;
  overfitCount: number;
  failedCount: number;
  createdAtUtc: string;
  completedAtUtc?: string;
  heartbeatAtUtc?: string;
}
