import { apiRequest } from '@/api/apiClient';

export type StrategyLabExecutionMode =
  | 'RawStrategy'
  | 'StrategyPlusConfidenceObservation'
  | 'StrategyPlusRiskObservation'
  | 'FullPipelineComparison';

export type StrategyLabRunStatus =
  | 'Created'
  | 'PreparingData'
  | 'Running'
  | 'Completed'
  | 'Failed'
  | 'Cancelled'
  | 'CheckingCoverage'
  | 'ImportingCandles'
  | 'VerifyingCoverage'
  | 'PreparingStrategy'
  | 'Evaluating'
  | 'SimulatingOutcomes';

export interface StrategyLabStrategy {
  code: string;
  name: string;
  version: string;
  category: string;
  allowedTimeframes: string[];
  preferredTimeframe?: string;
}

export interface StrategyLabRun {
  id: number;
  name: string;
  strategyCode: string;
  strategyVersion: string;
  exchangeId: number;
  symbolId: number;
  symbol: string;
  timeframe: string;
  fromUtc: string;
  toUtc: string;
  executionMode: StrategyLabExecutionMode;
  status: StrategyLabRunStatus;
  experimentFingerprint: string;
  currentStage?: string;
  percentComplete: number;
  createdAtUtc: string;
  completedAtUtc?: string;
  errorMessage?: string;
  parametersJson: string;
  initialBalance: number;
  currentStrategyVersion?: string;
  strategyVersionChanged: boolean;
}

export interface StrategyOpportunityMetrics {
  evaluations: number;
  rawCandidates: number;
  candidatesPer1000Candles: number;
  candidatesPerDay: number;
  candidatesPer30Days: number;
  longCandidateCount: number;
  shortCandidateCount: number;
}

export interface StrategyLabPerformanceSummary {
  rawCandidates: number;
  rawClosedTrades: number;
  winners: number;
  losers: number;
  breakeven: number;
  winRate: number;
  netPnl: number;
  netPnlLabel?: string;
  pnlPercent: number;
  pnlPercentLabel?: string;
  profitFactor: number;
  expectancy: number;
  averageR: number;
  maxDrawdownPercent: number;
  maxDrawdownLabel?: string;
  portfolioMetricsAvailable?: boolean;
  portfolioMetricsNote?: string;
  initialBalance?: number;
  grossWinnerPnl?: number;
  grossLoserPnl?: number;
  metricWarnings?: string[];
  opportunity: StrategyOpportunityMetrics;
  evidenceQuality: string;
  evidenceQualityLabel: string;
}

export type ExposureSemanticsVersion =
  | 'LegacyAmbiguous'
  | 'NotionalExposureV1'
  | 'MarginUsageV1'
  | 'ExplicitFuturesExposureV2'
  | 1
  | 2
  | 3
  | 4;

export interface StrategyLabObservationSettings {
  confidenceModel?: string;
  useSystemDefaultConfidenceThreshold?: boolean;
  customConfidenceThreshold?: number | null;
  effectiveConfidenceThreshold?: number;
  useSystemDefaultRiskSettings?: boolean;
  riskProfileId?: number | null;
  riskApprovalThreshold?: number | null;
  riskPerTradePercent?: number | null;
  preferredLeverage?: number | null;
  maximumLeverage?: number | null;
  /** @deprecated Legacy ambiguous exposure — not used as futures limit in v2 semantics */
  maximumPositionExposurePercent?: number | null;
  /** @deprecated Legacy ambiguous exposure — not used as futures limit in v2 semantics */
  maximumConcurrentExposurePercent?: number | null;
  maxNotionalExposurePerSymbolPercent?: number | null;
  maxTotalNotionalExposurePercent?: number | null;
  maxMarginUsagePerSymbolPercent?: number | null;
  maxTotalMarginUsagePercent?: number | null;
  maxConcurrentRiskPercent?: number | null;
  maxOpenPositions?: number | null;
  maxDailyLossPercent?: number | null;
  maxDrawdownPercent?: number | null;
  minimumRewardRisk?: number | null;
  feeEfficiencyHardLimitPercent?: number | null;
  exposureSemanticsVersion?: ExposureSemanticsVersion;
  legacyExposureResolution?: string | null;
}

export interface CandidateFunnel {
  candlesLoaded?: number;
  warmupCandlesLoaded?: number;
  testRangeCandles?: number;
  eligibleEvaluationCandles?: number;
  candlesEvaluated: number;
  confirmedSwingHighs?: number;
  confirmedSwingLows?: number;
  confirmedSwings?: number;
  bullishBreakoutChecks?: number;
  bearishBreakoutChecks?: number;
  breakoutChecks?: number;
  bullishBreakoutsDetected?: number;
  bearishBreakoutsDetected?: number;
  breakoutsDetected?: number;
  retestChecks?: number;
  validRetests?: number;
  retestsDetected?: number;
  confirmationChecks?: number;
  confirmationsPassed?: number;
  activeBuySideLiquidityLevels?: number;
  activeSellSideLiquidityLevels?: number;
  liquidityLevels?: number;
  liquidityLevelsCreated?: number;
  buySideSweepChecks?: number;
  sellSideSweepChecks?: number;
  sweepChecks?: number;
  buySideSweepsDetected?: number;
  sellSideSweepsDetected?: number;
  sweepsDetected?: number;
  sameCandleReclaims?: number;
  delayedReclaims?: number;
  reclaimsDetected?: number;
  candidatesDetectedInMemory?: number;
  candidatesRejectedAsDuplicate?: number;
  candidatesSimulationInvalid?: number;
  candidatesPersisted?: number;
  rawCandidates: number;
  simulationValidCandidates: number;
  rawSimulatedTrades: number;
  closedRawTrades: number;
  confidenceApproved: number;
  confidenceRejected: number;
  riskApproved: number;
  riskRejected: number;
  fullPipelineApproved: number;
  primaryBlocker?: string;
  primaryBlockerDetails?: string;
  suggestedNextAction?: string;
  zeroCandidateClassification?: string;
  strategyFamily?: string;
}

export interface CoverageDiagnostics {
  coverageCheckStartedAtUtc?: string;
  requestedFromUtc?: string;
  requestedToUtc?: string;
  requestedTimeframe?: string;
  existingCandleCount: number;
  missingCandleCountEstimate: number;
  autoImportAttempted: boolean;
  importStartedAtUtc?: string;
  importCompletedAtUtc?: string;
  importedCandleCount: number;
  importError?: string;
  finalCoverageStatus: string;
  missingRanges: { fromUtc: string; toUtc: string; estimatedMissingCandles: number }[];
}

export interface ZeroCandidateExplanation {
  classification: string;
  primaryBlocker?: string;
  details?: string;
  suggestedNextAction?: string;
}

export interface DiagnosticEvent {
  stage: string;
  direction: string;
  level: number;
  levelTimestampUtc?: string;
  eventTimestampUtc?: string;
  secondaryTimestampUtc?: string;
  eventPrice?: number;
  outcome: string;
  reason?: string;
}

export interface GatedSubset {
  candidateCount: number;
  closedTradeCount: number;
  netPnl: number;
  profitFactor: number;
  winners: number;
  losers: number;
  winRate?: number;
  maxDrawdownPercent?: number;
  averageConfidence?: number | null;
  averageRiskScore?: number | null;
}

export interface RawVsGatedComparison {
  raw: GatedSubset;
  confidenceApproved: GatedSubset;
  confidenceRejected: GatedSubset;
  riskApproved: GatedSubset;
  riskRejected: GatedSubset;
  fullPipeline: GatedSubset;
  interpretations: string[];
}

export interface StrategyResearchCandidate {
  id: number;
  setupDetectedAtUtc: string;
  direction: string;
  setupType: string;
  proposedEntryPrice: number;
  stopLoss: number;
  target1: number;
  rewardRisk: number;
  strategyReason: string;
  rawOutcomeStatus: string;
  rawNetPnl?: number | null;
  rawRMultiple?: number | null;
  confidenceScore?: number | null;
  confidenceThreshold?: number | null;
  confidenceDecision?: string | null;
  confidenceMargin?: number | null;
  confidenceReason?: string | null;
  confidenceModelVersion?: string | null;
  confidenceComponentsJson?: string | null;
  confidenceEvaluatedAtUtc?: string | null;
  riskScore?: number | null;
  candidateRiskScore?: number | null;
  portfolioRiskScore?: number | null;
  portfolioRiskAssessmentStatus?: string | null;
  riskThreshold?: number | null;
  riskDecision?: string | null;
  riskMargin?: number | null;
  riskReason?: string | null;
  riskModelVersion?: string | null;
  riskAssessmentVersion?: string | null;
  riskComponentsJson?: string | null;
  riskRuleResultsJson?: string | null;
  riskFailedRuleKeysJson?: string | null;
  riskWarningRuleKeysJson?: string | null;
  riskPerTradePercent?: number | null;
  riskAmount?: number | null;
  riskAtStopPercent?: number | null;
  proposedPositionSize?: number | null;
  positionNotional?: number | null;
  proposedLeverage?: number | null;
  minimumRequiredLeverage?: number | null;
  assessmentLeverage?: number | null;
  preferredLeverage?: number | null;
  maxLeverage?: number | null;
  initialMarginRequired?: number | null;
  stopDistancePercent?: number | null;
  positionExposurePercent?: number | null;
  notionalExposurePercent?: number | null;
  marginUsagePercent?: number | null;
  estimatedRoundTripFees?: number | null;
  feeToTargetPercent?: number | null;
  positionSizingUnavailableReason?: string | null;
  currentExposurePercent?: number | null;
  currentNotionalExposurePercent?: number | null;
  currentMarginUsagePercent?: number | null;
  concurrentRiskPercent?: number | null;
  dailyLossUsagePercent?: number | null;
  currentDrawdownPercent?: number | null;
  concurrentPositionCount?: number | null;
  riskScoreDecision?: string | null;
  hardRuleComplianceDecision?: string | null;
  riskPolicyEligibilityDecision?: string | null;
  riskPolicyReason?: string | null;
  riskPolicyFailedRuleKeysJson?: string | null;
  riskPolicyMinimumConfidence?: number | null;
  finalPipelineRejectionSourcesJson?: string | null;
  riskProfileId?: number | null;
  riskProfileVersion?: string | null;
  riskProfileName?: string | null;
  riskProfileSource?: string | null;
  riskProfileSnapshotId?: string | null;
  riskRejectedRuleKey?: string | null;
  riskEvaluatedAtUtc?: string | null;
  finalPipelineDecision?: string | null;
  exitOutcome?: string | null;
  netResult?: string | null;
  rawExitTimeUtc?: string | null;
  drawdownCalculationMode?: string | null;
  mfe?: number | null;
  mae?: number | null;
  durationBars?: number | null;
  structureJson: string;
  genericRiskFieldSource?: string | null;
  riskPathAssessmentVersion?: string | null;
  riskOnlyFinancialRiskDecision?: string | null;
  riskOnlyEntryDecision?: string | null;
  riskOnlyRejectionSourcesJson?: string | null;
  riskOnlyAssessment?: PathPortfolioAssessment | null;
  riskOnlyCurrentDrawdownPercent?: number | null;
  riskOnlyDailyLossUsagePercent?: number | null;
  riskOnlyCurrentMarginUsagePercent?: number | null;
  riskOnlyConcurrentRiskPercent?: number | null;
  riskOnlyOpenPositionCount?: number | null;
  fullPipelineFinancialRiskDecision?: string | null;
  fullPipelineEntryDecision?: string | null;
  fullPipelineRejectionSourcesJson?: string | null;
  fullPipelineAssessment?: PathPortfolioAssessment | null;
  fullPipelineCurrentDrawdownPercent?: number | null;
  fullPipelineDailyLossUsagePercent?: number | null;
  fullPipelineCurrentMarginUsagePercent?: number | null;
  fullPipelineConcurrentRiskPercent?: number | null;
  fullPipelineOpenPositionCount?: number | null;
}

export interface PathPortfolioAssessment {
  portfolioPath: string;
  assessmentBalance: number;
  riskAmount?: number | null;
  quantity?: number | null;
  positionNotional?: number | null;
  currentDrawdownPercent?: number | null;
  currentDailyLossUsagePercent?: number | null;
  currentMarginUsagePercent?: number | null;
  currentConcurrentRiskPercent?: number | null;
  currentOpenPositionCount?: number;
  financialRiskDecision?: string;
  riskReason?: string;
  entryDecision?: string;
  entryDecisionReason?: string;
  failedRuleKeys?: string[];
  rejectionSources?: string[];
}

export interface PortfolioPathDivergence {
  firstDivergenceAtUtc?: string | null;
  finalBalanceDifference?: number | null;
  maxDrawdownDifference?: number | null;
  tradeCountDifference?: number;
  differentPortfolioRiskDecisions?: number;
  openedOnlyInRiskOnly?: number;
  openedOnlyInFullPipeline?: number;
  openedInBoth?: number;
  openedInNeither?: number;
}

export interface StrategyLabCandidateQuery {
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDirection?: string;
  search?: string;
  direction?: string;
  rawOutcome?: string;
  confidenceDecision?: string;
  confidenceMin?: number;
  confidenceMax?: number;
  riskDecision?: string;
  riskMin?: number;
  riskMax?: number;
  profitableOnly?: boolean;
  fromUtc?: string;
  toUtc?: string;
  quickFilter?: string;
  riskOnlyEntryDecision?: string;
  fullPipelineEntryDecision?: string;
  pathDecisionDifference?: string;
  riskOnlyFailedRule?: string;
  fullPipelineFailedRule?: string;
  riskOnlyDrawdownMin?: number;
  fullPipelineDrawdownMin?: number;
}

export interface PagedCandidates {
  items: StrategyResearchCandidate[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

export interface EnhancedGatedSubset {
  candidateCount: number;
  closedTradeCount: number;
  winners: number;
  losers: number;
  winRate: number;
  netPnl: number;
  profitFactor: number;
  maxDrawdownPercent: number;
  averageConfidence?: number | null;
  averageRiskScore?: number | null;
  averageR?: number | null;
}

export interface StrategyLabGateAnalysis {
  executionMode: StrategyLabExecutionMode;
  disclaimer: string;
  confidenceSummary?: {
    evaluatedCount: number;
    approvedCount: number;
    rejectedCount: number;
    approvalRate: number;
    rejectionRate: number;
    currentThreshold?: number | null;
    averageScore?: number | null;
    medianScore?: number | null;
    averageWinnerScore?: number | null;
    medianWinnerScore?: number | null;
    averageLoserScore?: number | null;
    medianLoserScore?: number | null;
  } | null;
  confidenceRejectedWinners?: {
    count: number;
    percentageOfOutcomeGroup: number;
    averageScore?: number | null;
    medianScore?: number | null;
    averageMarginBelowThreshold?: number | null;
    hypotheticalNetPnl: number;
    hypotheticalAverageR?: number | null;
  } | null;
  confidenceRejectedLosers?: StrategyLabGateAnalysis['confidenceRejectedWinners'];
  confidenceBuckets: {
    label: string;
    minInclusive: number;
    maxInclusive: number;
    candidateCount: number;
    winnerCount: number;
    loserCount: number;
    winRate: number;
    netPnl: number;
    profitFactor: number;
    averageR?: number | null;
    averageMfe?: number | null;
    averageMae?: number | null;
  }[];
  confidenceThresholdSimulation: {
    threshold: number;
    isCurrentThreshold: boolean;
    acceptedCount: number;
    rejectedCount: number;
    acceptedWinRate: number;
    acceptedNetPnl: number;
    acceptedProfitFactor: number;
    acceptedMaxDrawdownPercent: number;
    acceptedAverageR?: number | null;
    percentOfRawPnlPreserved: number;
  }[];
  riskSummary?: StrategyLabGateAnalysis['confidenceSummary'];
  riskRejectedWinners?: StrategyLabGateAnalysis['confidenceRejectedWinners'];
  riskRejectedLosers?: StrategyLabGateAnalysis['confidenceRejectedWinners'];
  riskReasonAnalysis: {
    reason: string;
    rejectedCount: number;
    winnerCount: number;
    loserCount: number;
    winRate: number;
    hypotheticalNetPnl: number;
    averageR?: number | null;
  }[];
  overallWinnerLoserComparison: {
    winnerCount: number;
    loserCount: number;
    averageWinnerConfidence?: number | null;
    averageLoserConfidence?: number | null;
    medianWinnerConfidence?: number | null;
    medianLoserConfidence?: number | null;
    averageWinnerRiskScore?: number | null;
    averageLoserRiskScore?: number | null;
    medianWinnerRiskScore?: number | null;
    medianLoserRiskScore?: number | null;
    averageWinnerMfe?: number | null;
    averageLoserMfe?: number | null;
    averageWinnerMae?: number | null;
    averageLoserMae?: number | null;
    averageWinnerStopDistancePercent?: number | null;
    averageLoserStopDistancePercent?: number | null;
    averageWinnerRewardRisk?: number | null;
    averageLoserRewardRisk?: number | null;
  };
  confidenceSeparation?: number | null;
  confidenceScoreDiagnostics?: {
    uniqueScoreCount: number;
    minScore?: number | null;
    maxScore?: number | null;
    averageScore?: number | null;
    standardDeviation?: number | null;
    mostCommonScore?: number | null;
    mostCommonScorePercent: number;
    degenerateWarningCode?: string | null;
    degenerateWarningMessage?: string | null;
  } | null;
  riskScoreDiagnostics?: StrategyLabGateAnalysis['confidenceScoreDiagnostics'];
  raw: EnhancedGatedSubset;
  confidenceApproved: EnhancedGatedSubset;
  confidenceRejected: EnhancedGatedSubset;
  riskApproved: EnhancedGatedSubset;
  riskRejected: EnhancedGatedSubset;
  fullPipeline: EnhancedGatedSubset;
  interpretations: string[];
}

export interface ShadowTradeLedgerEntry {
  entryTimeUtc?: string;
  exitTimeUtc?: string;
  symbol?: string;
  direction?: string;
  candidateId?: number;
  entryPrice?: number;
  exitPrice?: number;
  quantity?: number;
  grossR?: number;
  grossPnl?: number;
  entryFee?: number;
  exitFee?: number;
  slippage?: number;
  totalCost?: number;
  netPnl?: number;
  netResult?: string;
  exitOutcome?: string;
  balanceBefore?: number;
  balanceAfter?: number;
  drawdownAfterExit?: number | null;
}

export interface ShadowPortfolioSummary {
  pathName?: string;
  startingBalance?: number;
  endingBalance?: number;
  grossPnl?: number;
  grossReturnPercent?: number;
  entryFees?: number;
  exitFees?: number;
  slippageCost?: number;
  fundingCost?: number;
  totalTransactionCosts?: number;
  realizedNetPnl?: number;
  netReturnPercent?: number;
  netReturnAfterCostsPercent?: number;
  maxRealizedDrawdownPercent?: number;
  tradesAccepted?: number;
  tradesRejected?: number;
  tradesOpened?: number;
  profitableTrades?: number;
  losingTrades?: number;
  breakevenTrades?: number;
  peakMarginUsagePercent?: number;
  peakNotionalExposurePercent?: number;
  peakConcurrentRiskPercent?: number;
  peakOpenPositionCount?: number;
  drawdownCalculationMode?: string;
  costModelVersion?: string;
  ledger?: ShadowTradeLedgerEntry[];
}

export interface StrategyLabRunDetail {
  run: StrategyLabRun;
  summary: StrategyLabPerformanceSummary;
  funnel: CandidateFunnel;
  gatedComparison?: RawVsGatedComparison;
  candidates: StrategyResearchCandidate[];
  warnings: string[];
  coverageDiagnostics?: CoverageDiagnostics;
  zeroCandidateExplanation?: ZeroCandidateExplanation;
  diagnosticEvents?: DiagnosticEvent[];
  sampleFingerprints?: string[];
  riskOnlyShadowPortfolio?: ShadowPortfolioSummary | null;
  fullPipelineShadowPortfolio?: ShadowPortfolioSummary | null;
  portfolioPathDivergence?: PortfolioPathDivergence | null;
  pathDiagnostics?: string[];
  riskPathAssessmentVersion?: string | null;
  drawdownCalculationMode?: string | null;
}

export interface CreateStrategyLabRunRequest {
  name?: string;
  strategyCode: string;
  exchangeId: number;
  symbolId: number;
  timeframe: string;
  fromUtc: string;
  toUtc: string;
  executionMode: StrategyLabExecutionMode;
  parameters?: Record<string, string>;
  initialBalance?: number;
  riskProfileId?: number;
  makerFeeRate?: number;
  takerFeeRate?: number;
  slippagePercent?: number;
  observationSettings?: StrategyLabObservationSettings;
}

export interface SyntheticTestResult {
  scenarioName: string;
  description: string;
  passed: boolean;
  expectedCandidateCount: number;
  actualCandidateCount: number;
  expectedDirection?: string;
  actualDirection?: string;
  expectedNoTradeReason?: string;
  failureDetails?: string;
}

export interface StrategyHealth {
  registrationStatus: string;
  candleDataStatus: string;
  syntheticTestsPassed: number;
  syntheticTestsTotal: number;
  recentEvaluations: number;
  recentRawCandidates: number;
  candidateRatePer1000Candles: number;
  rawTrades: number;
  confidenceApprovalRate?: number;
  riskApprovalRate?: number;
  recentStrategyLabRuns: number;
  warnings: string[];
  problemCategories: string[];
}

export interface StrategyLabStartupHealth {
  healthy: boolean;
  strategyLabRunTableAvailable: boolean;
  strategyResearchCandidateTableAvailable: boolean;
  breakoutRetestRegistered: boolean;
  liquiditySweepRegistered: boolean;
  breakoutRetestResolvable: boolean;
  liquiditySweepResolvable: boolean;
  syntheticTestsAvailable: boolean;
  issues: string[];
  status: string;
}

export interface StrategyLabExposureAnalytics {
  averageNotionalExposurePercent?: number | null;
  averageMarginUsagePercent?: number | null;
  averageLeverage?: number | null;
  averageConcurrentRiskPercent?: number | null;
  averageInitialMarginRequired?: number | null;
  averageRiskAtStopPercent?: number | null;
}

export interface StrategyLabRiskAnalysis {
  riskAssessmentVersion?: string;
  financialRiskSummary: {
    evaluatedCandidateCount: number;
    approvedCount: number;
    rejectedCount: number;
    approvalRate: number;
    averageCandidateRiskScore?: number | null;
    medianCandidateRiskScore?: number | null;
    minimumCandidateRiskScore?: number | null;
    maximumCandidateRiskScore?: number | null;
    standardDeviation?: number | null;
    uniqueScoreCount: number;
  };
  exposureAnalytics?: StrategyLabExposureAnalytics | null;
  winnerLoserRiskComparison: {
    averageWinnerRiskScore?: number | null;
    medianWinnerRiskScore?: number | null;
    averageLoserRiskScore?: number | null;
    medianLoserRiskScore?: number | null;
    riskScoreSeparation?: number | null;
  };
  rejectedWinnerAnalysis: {
    count: number;
    percentageOfOutcomeGroup: number;
    averageRiskScore?: number | null;
    hypotheticalNetPnl: number;
    averageR?: number | null;
    topRejectionReasons: string[];
  };
  rejectedLoserAnalysis: StrategyLabRiskAnalysis['rejectedWinnerAnalysis'];
  ruleEffectiveness: Array<{
    ruleKey: string;
    ruleName: string;
    evaluatedCount: number;
    passedCount: number;
    failedCount: number;
    warningCount: number;
    rejectedWinners: number;
    rejectedLosers: number;
    rejectedWinnerPercent: number;
    rejectedLoserPercent: number;
    hypotheticalPnlOfRejected: number;
  }>;
  riskPolicySummary: {
    evaluatedCount: number;
    eligibleCount: number;
    ineligibleCount: number;
    topPolicyReasons: string[];
  };
  portfolioRiskSummary: { status: string; note?: string | null };
  diagnostics: string[];
}

export interface StrategyLabRiskProfileComparison {
  comparable: boolean;
  incompatibilityReasons: string[];
  profileA?: Record<string, unknown> | null;
  profileB?: Record<string, unknown> | null;
  candidateDecisionDifferences: Array<Record<string, unknown>>;
  summary: string[];
}

export const strategyLabApi = {
  getStrategies: () => apiRequest<StrategyLabStrategy[]>('/strategy-lab/strategies'),
  getModuleHealth: () => apiRequest<StrategyLabStartupHealth>('/strategy-lab/health', { auth: false }),
  getRuns: (limit = 50) => apiRequest<StrategyLabRun[]>(`/strategy-lab/runs?limit=${limit}`),
  createRun: (request: CreateStrategyLabRunRequest) =>
    apiRequest<StrategyLabRun>('/strategy-lab/runs', { method: 'POST', body: request }),
  getRun: (id: number) => apiRequest<StrategyLabRun>(`/strategy-lab/runs/${id}`),
  getRunDetail: (id: number) => apiRequest<StrategyLabRunDetail>(`/strategy-lab/runs/${id}/detail`),
  getCandidates: (id: number, query: StrategyLabCandidateQuery = {}, signal?: AbortSignal) => {
    const params = new URLSearchParams();
    Object.entries(query).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        params.set(key, String(value));
      }
    });
    const qs = params.toString();
    return apiRequest<PagedCandidates>(`/strategy-lab/runs/${id}/candidates${qs ? `?${qs}` : ''}`, { signal });
  },
  getCandidateDetail: (id: number, candidateId: number) =>
    apiRequest<{
      candidate: StrategyResearchCandidate;
      riskOnlyAssessment?: PathPortfolioAssessment | null;
      fullPipelineAssessment?: PathPortfolioAssessment | null;
      finalPipelineDecision?: string | null;
      pathComparison?: Record<string, unknown> | null;
      pathAssessmentAvailability?: string;
    }>(`/strategy-lab/runs/${id}/candidates/${candidateId}`),
  getPortfolioPathComparison: (id: number) =>
    apiRequest<{
      riskOnlySummary?: ShadowPortfolioSummary | null;
      fullPipelineSummary?: ShadowPortfolioSummary | null;
      divergenceSummary?: PortfolioPathDivergence | null;
      diagnostics?: string[];
      riskPathAssessmentVersion?: string;
      pathAssessmentAvailability?: string;
    }>(`/strategy-lab/runs/${id}/portfolio-path-comparison`),
  getGateAnalysis: (id: number) =>
    apiRequest<StrategyLabGateAnalysis>(`/strategy-lab/runs/${id}/gate-analysis`),
  getRiskAnalysis: (id: number) =>
    apiRequest<StrategyLabRiskAnalysis>(`/strategy-lab/runs/${id}/risk-analysis`),
  compareRiskProfiles: (id: number, otherRunId: number) =>
    apiRequest<StrategyLabRiskProfileComparison>(`/strategy-lab/runs/${id}/risk-profile-comparison/${otherRunId}`),
  getRunsByStrategy: (strategyCode: string, limit = 20) =>
    apiRequest<StrategyLabRun[]>(`/strategy-lab/runs/by-strategy/${strategyCode}?limit=${limit}`),
  getRerunConfig: (id: number) =>
    apiRequest<CreateStrategyLabRunRequest>(`/strategy-lab/runs/${id}/rerun-config`),
  rerun: (id: number) =>
    apiRequest<StrategyLabRun>(`/strategy-lab/runs/${id}/rerun`, { method: 'POST' }),
  runSyntheticTests: (strategyCode: string) =>
    apiRequest<SyntheticTestResult[]>(`/strategy-lab/strategies/${strategyCode}/synthetic-tests`, { method: 'POST', body: {} }),
  getStrategyHealth: (strategyCode: string) =>
    apiRequest<StrategyHealth>(`/strategy-lab/strategies/${strategyCode}/health`),
};
