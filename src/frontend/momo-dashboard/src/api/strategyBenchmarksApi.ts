import { apiRequest } from '@/api/apiClient';
import type { PagedQuery, PagedResult } from '@/api/domainTypes';

export interface CreateStrategyBenchmarkRequest {
  name?: string;
  exchangeCode?: string;
  symbols?: string[];
  timeframes?: string[];
  executionTimeframeMode?: 'AutoSelectByStrategy' | 'AdvancedManualOverride';
  strategyExecutionScope?: 'PreferredOnly' | 'AllSupported' | 'ManualOverride';
  manualExecutionTimeframes?: string[];
  strategyIds?: number[];
  benchmarkFromDate?: string;
  benchmarkToDate?: string;
  warmupFromDate?: string;
  initialBalance?: number;
  riskProfileId?: number;
  executionMode?: string;
  makerFeeRate?: number;
  takerFeeRate?: number;
  orderExpiryCandles?: number;
  useAiScoring?: boolean;
  minConfidenceScore?: number;
  evaluationMode?: 'RawStrategyResearch' | 'RiskOnlyResearch' | 'ConfidenceOnlyResearch' | 'FullValidation';
  enableShadowTradeAnalysis?: boolean;
  sameCandleExitPolicy?: 'ConservativeStopFirst' | 'TargetFirst' | 'OpenHighLowCloseHeuristic';
  includeDisabledStrategies?: boolean;
  importMissingData?: boolean;
  recalculateIndicators?: boolean;
  runEachStrategyIndividually?: boolean;
  allowLowCoverage?: boolean;
  stopOnFirstFailure?: boolean;
}

export interface StrategyBenchmarkPreflightRequest {
  exchangeCode: string;
  symbols: string[];
  strategyIds: number[];
  benchmarkFromDate: string;
  benchmarkToDate: string;
  warmupFromDate: string;
  executionTimeframeMode?: 'AutoSelectByStrategy' | 'AdvancedManualOverride';
  strategyExecutionScope?: 'PreferredOnly' | 'AllSupported' | 'ManualOverride';
  manualExecutionTimeframes?: string[];
}

export interface StrategyBenchmarkPreflight {
  selectedSymbols: string[];
  selectedStrategies: string[];
  executionTimeframeMode: string;
  strategyExecutionScope: string;
  resolvedExecutionRuns: Array<{
    strategyId: number;
    strategyCode: string;
    strategyName: string;
    executionTimeframes: string[];
    requiredDataTimeframes: string[];
    requiredIndicatorTimeframes: string[];
  }>;
  requiredImportTimeframes: Array<{ symbol: string; timeframe: string; reason: string; isAnchorData?: boolean }>;
  requiredIndicatorTimeframes: Array<{ symbol: string; timeframe: string; reason: string; isAnchorData?: boolean }>;
  estimatedTotalRuns: number;
  estimatedCandleCount: number;
  missingDataSummary: string[];
  missingIndicatorsSummary: string[];
  warnings: string[];
  blockingIssues: string[];
}

export interface StrategyBenchmarkRun {
  id: number;
  name: string;
  status: string;
  exchangeId: number;
  symbols: string[];
  timeframes: string[];
  strategyIds: number[];
  benchmarkFromUtc: string;
  benchmarkToUtc: string;
  warmupFromUtc: string;
  warmupToUtc: string;
  initialBalance: number;
  riskProfileId: number;
  executionMode: string;
  useAiScoring: boolean;
  minConfidenceScore: number;
  evaluationMode: string;
  includeDisabledStrategies: boolean;
  totalRuns: number;
  completedRuns: number;
  percentComplete: number;
  currentStage?: string | null;
  currentSymbol?: string | null;
  currentTimeframe?: string | null;
  currentStrategy?: string | null;
  message?: string | null;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  errorMessage?: string | null;
  createdAtUtc: string;
}

export interface StrategyBenchmarkProgress {
  benchmarkRunId: number;
  status: string;
  currentStage?: string | null;
  percentComplete: number;
  dataPreparationPercent?: number;
  backtestPercent?: number;
  currentSymbol?: string | null;
  currentTimeframe?: string | null;
  currentStrategy?: string | null;
  completedRuns: number;
  totalRuns: number;
  failedRuns?: number;
  pendingRuns?: number;
  message?: string | null;
  lastHeartbeatAtUtc?: string | null;
  currentChunkFromUtc?: string | null;
  currentChunkToUtc?: string | null;
  completedImportChunks?: number;
  totalImportChunks?: number;
  insertedCandles?: number;
  skippedDuplicateCandles?: number;
}

export interface StrategyBenchmarkRunItem {
  id: number;
  benchmarkRunId: number;
  strategyId: number;
  strategyCode: string;
  strategyName: string;
  symbolId: number;
  symbol: string;
  timeframe: string;
  status: string;
  backtestRunId?: number | null;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  lastHeartbeatAtUtc?: string | null;
  durationSeconds?: number | null;
  candleCount?: number | null;
  lastProcessedCandleTimeUtc?: string | null;
  lastProcessedCandleIndex?: number | null;
  totalCandles?: number | null;
  errorMessage?: string | null;
}

export interface StrategyBenchmarkDiagnostics {
  benchmarkRunId: number;
  status: string;
  currentStage?: string | null;
  percentComplete: number;
  completedRuns: number;
  totalRuns: number;
  failedRuns: number;
  pendingRuns: number;
  runningItem?: StrategyBenchmarkRunItem | null;
  lastError?: string | null;
  recentRunItems: StrategyBenchmarkRunItem[];
  warnings: string[];
}

export interface StrategyBenchmarkStrategyResult {
  rank: number;
  strategyId: number;
  strategyCode: string;
  strategyName: string;
  grade: string;
  score: number;
  totalTrades: number;
  netPnl: number;
  netPnlPercent: number;
  maxDrawdownPercent: number;
  profitFactor: number;
  winRatePercent: number;
  averageConfidenceScore: number;
  bestSymbol?: string | null;
  worstSymbol?: string | null;
  bestTimeframe?: string | null;
  worstTimeframe?: string | null;
  resultReason?: string | null;
  candidateSignals: number;
  confidenceRejected: number;
  riskRejections: number;
  shadowNetPnlPercent: number;
  falseRejectRatePercent: number;
  noTradeCount: number;
  topNoTradeReason?: string | null;
  strengths: string[];
  weaknesses: string[];
  warnings: string[];
  pipelineSummary?: string | null;
  bbSweeps?: number;
  liquiditySweeps?: number;
  cisdConfirmations?: number;
  rsiPassed?: number;
  targetPassed3R?: number;
  finalCandidates?: number;
}

export interface StrategyBenchmarkReport {
  run: StrategyBenchmarkRun;
  summary: {
    totalBacktestRuns: number;
    completedRuns: number;
    failedRuns: number;
    bestOverallStrategy?: string | null;
    bestStrategyBySymbol: Record<string, string>;
    bestStrategyByTimeframe: Record<string, string>;
    strategiesToRetune: string[];
    strategiesNeedingMoreData: string[];
  };
  dataPreparation: {
    imports: Array<Record<string, unknown>>;
    dataQuality: Array<Record<string, unknown>>;
    indicators: Array<Record<string, unknown>>;
  };
  strategyRanking: StrategyBenchmarkStrategyResult[];
  strategyDetails: Array<{
    strategyCode: string;
    strategyName: string;
    symbol: string;
    timeframe: string;
    grade: string;
    score: number;
    totalTrades: number;
    netPnl: number;
    netPnlPercent: number;
    maxDrawdownPercent: number;
    profitFactor: number;
    winRatePercent: number;
    averageWin: number;
    averageLoss: number;
    largestLoss: number;
    averageRewardRisk: number;
    warnings: string[];
  }>;
  symbolResults: Array<Record<string, unknown>>;
  timeframeResults: Array<Record<string, unknown>>;
  noTradeAnalysis: Array<{
    strategyCode: string;
    strategyName: string;
    symbol: string;
    executionTimeframe: string;
    evaluations: number;
    noTradeCount: number;
    candidateSignals: number;
    trades: number;
    topNoTradeReason?: string | null;
    topNoTradeReasonCount: number;
    missingDataCount: number;
    missingIndicatorsCount: number;
    riskRejections: number;
    topRiskRejectionReason?: string | null;
    resultReason?: string | null;
    recommendation: string;
    funnel: Array<{
      stepName: string;
      passedCount: number;
      failedCount: number;
      failReason?: string | null;
    }>;
    tuningSuggestions: string[];
    pipelineSummary?: string | null;
    whyZeroTradesAnalysis?: string | null;
    noTradeReasonBreakdown?: Record<string, number>;
    bbFunnelCounts?: Record<string, number | string | boolean>;
  }>;
  riskRejections: Array<{
    strategyCode: string;
    strategyName: string;
    symbol: string;
    executionTimeframe: string;
    totalCandidateSignals: number;
    riskRejections: number;
    topRiskReason?: string | null;
    rejectionPercent: number;
    recommendation: string;
  }>;
  pipelineFunnel: Array<{
    strategyCode: string;
    strategyName: string;
    symbol: string;
    timeframe: string;
    evaluations: number;
    candidateSignals: number;
    confidenceApproved: number;
    confidenceRejected: number;
    riskApproved: number;
    riskRejected: number;
    executedTrades: number;
    shadowTrades: number;
    finalNetPnl: number;
    shadowNetPnl: number;
  }>;
  candidateTrades: Array<{
    signalTimeUtc: string;
    strategyCode: string;
    strategyName: string;
    symbol: string;
    timeframe: string;
    direction: string;
    entryPrice: number;
    stopLoss: number;
    takeProfit: number;
    combinedConfidence: number;
    riskPercent: number;
    leverage: number;
    marginUsed: number;
    notionalValue: number;
    finalDecision: string;
    finalDecisionReason: string;
  }>;
  executedTrades: Array<{
    entryTimeUtc: string;
    exitTimeUtc?: string | null;
    strategyCode: string;
    symbol: string;
    direction: string;
    leverage: number;
    marginUsed: number;
    notionalValue: number;
    entryPrice: number;
    exitPrice?: number | null;
    stopLoss: number;
    takeProfit: number;
    netPnl: number;
    netPnlPercent: number;
    exitReason?: string | null;
  }>;
  rejectedCandidates: Array<{
    signalTimeUtc: string;
    strategyCode: string;
    strategyName: string;
    symbol: string;
    timeframe: string;
    direction: string;
    finalDecision: string;
    finalDecisionReason: string;
    combinedConfidence: number;
    riskPercent: number;
  }>;
  shadowTrades: Array<{
    signalTimeUtc: string;
    strategyCode: string;
    symbol: string;
    direction: string;
    rejectedBy: string;
    outcomeClassification: string;
    shadowExitReason?: string | null;
    shadowNetPnl: number;
    maxFavorableExcursion: number;
    maxAdverseExcursion: number;
    durationCandles: number;
  }>;
  rejectionQuality: Array<{
    strategyCode: string;
    strategyName: string;
    symbol: string;
    timeframe: string;
    rejectedCandidateCount: number;
    rejectedByConfidenceCount: number;
    rejectedByRiskCount: number;
    rejectedByBothCount: number;
    shadowTradesSimulated: number;
    rejectedWouldHaveWon: number;
    rejectedWouldHaveLost: number;
    rejectedBreakEven: number;
    rejectedNotEnoughData: number;
    shadowNetPnl: number;
    confidenceFalseRejectCount: number;
    riskFalseRejectCount: number;
    confidenceCorrectRejectCount: number;
    riskCorrectRejectCount: number;
  }>;
  riskConfidenceCalibration: {
    confidenceFalseRejectionRatePercent: number;
    riskFalseRejectionRatePercent: number;
    confidenceCorrectRejectionRatePercent: number;
    riskCorrectRejectionRatePercent: number;
    confidenceThresholdRecommendation?: number | null;
    riskRuleRecommendations: string[];
    strategySpecificRecommendations: string[];
    warnings: string[];
    evidenceSummary: string[];
  };
  decisionRecommendations: string[];
  generatedAtUtc: string;
}

export const strategyBenchmarksApi = {
  create: (body: CreateStrategyBenchmarkRequest) =>
    apiRequest<StrategyBenchmarkRun>('/strategy-benchmarks', { method: 'POST', body }),
  preflight: (body: StrategyBenchmarkPreflightRequest) =>
    apiRequest<StrategyBenchmarkPreflight>('/strategy-benchmarks/preflight', { method: 'POST', body }),
  list: (query?: PagedQuery) =>
    apiRequest<PagedResult<StrategyBenchmarkRun>>('/strategy-benchmarks', { query }),
  getById: (id: number) => apiRequest<StrategyBenchmarkRun>(`/strategy-benchmarks/${id}`),
  getProgress: (id: number) => apiRequest<StrategyBenchmarkProgress>(`/strategy-benchmarks/${id}/progress`),
  getReport: (id: number) => apiRequest<StrategyBenchmarkReport>(`/strategy-benchmarks/${id}/report`),
  getRunItems: (id: number) => apiRequest<StrategyBenchmarkRunItem[]>(`/strategy-benchmarks/${id}/run-items`),
  getDiagnostics: (id: number) => apiRequest<StrategyBenchmarkDiagnostics>(`/strategy-benchmarks/${id}/diagnostics`),
  cancel: (id: number) => apiRequest<StrategyBenchmarkRun>(`/strategy-benchmarks/${id}/cancel`, { method: 'POST' }),
  resume: (id: number) => apiRequest<StrategyBenchmarkRun>(`/strategy-benchmarks/${id}/resume`, { method: 'POST' }),
  restart: (id: number) => apiRequest<StrategyBenchmarkRun>(`/strategy-benchmarks/${id}/restart`, { method: 'POST' }),
  retryFailed: (id: number) => apiRequest<StrategyBenchmarkRun>(`/strategy-benchmarks/${id}/retry-failed`, { method: 'POST' }),
  markStalledFailed: (id: number) =>
    apiRequest<StrategyBenchmarkRun>(`/strategy-benchmarks/${id}/mark-stalled-failed`, { method: 'POST' }),
};
