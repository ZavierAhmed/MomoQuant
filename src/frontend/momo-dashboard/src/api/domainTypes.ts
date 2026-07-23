import type { PagedResult, UserRole } from '@/api/types';

export interface PagedQuery {
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDirection?: number;
  search?: string;
}

export interface ReportQuery {
  fromUtc?: string;
  toUtc?: string;
  symbolId?: number;
  strategyId?: number;
  timeframe?: string;
  mode?: string;
  marketRegime?: string;
  limit?: number;
}

export interface MonitoringQuery {
  fromUtc?: string;
  toUtc?: string;
  severity?: string;
  subsystem?: string;
  eventType?: string;
  userId?: number;
  mode?: string;
  limit?: number;
  page?: number;
  pageSize?: number;
}

export interface AuditLogQuery {
  fromUtc?: string;
  toUtc?: string;
  severity?: string;
  eventType?: string;
  userId?: number;
  page?: number;
  pageSize?: number;
}

// Monitoring
export interface ComponentHealth {
  name: string;
  status: string;
  latencyMs?: number | null;
  message: string;
}

export interface HealthResponse {
  status: string;
  checkedAtUtc: string;
  components: ComponentHealth[];
}

export interface SystemStatus {
  apiStatus: string;
  databaseStatus: string;
  redisStatus: string;
  aiServiceStatus: string;
  activePaperSessions: number;
  runningBacktests: number;
  runningReplaySessions: number;
  recentCriticalErrors: number;
  recentAiFailures: number;
  recentRiskRejections: number;
  lastCandleImportUtc?: string | null;
  lastIndicatorRecalculationUtc?: string | null;
  generatedAtUtc: string;
}

export interface SystemHealthLog {
  id: number;
  subsystem: string;
  status: string;
  severity: string;
  message: string;
  detailsJson?: string | null;
  latencyMs?: number | null;
  checkedAtUtc: string;
  createdAt: string;
}

export interface RecentError {
  id: number;
  source: string;
  subsystem: string;
  severity: string;
  message: string;
  occurredAtUtc: string;
}

export interface RecentEvent {
  id: number;
  eventType: string;
  subsystem: string;
  severity: string;
  message: string;
  occurredAtUtc: string;
}

export interface SafetyEvent {
  id: number;
  eventType: string;
  severity: string;
  message: string;
  userId?: number | null;
  userEmail?: string | null;
  occurredAtUtc: string;
}

export interface TradingPipelineStatus {
  marketDataAvailable: boolean;
  indicatorsAvailable: boolean;
  strategiesEnabled: number;
  riskProfilesAvailable: boolean;
  aiServiceAvailable: boolean;
  backtestingAvailable: boolean;
  replayAvailable: boolean;
  paperTradingAvailable: boolean;
  latestCandleTimeUtc?: string | null;
  latestIndicatorSnapshotTimeUtc?: string | null;
  openPaperPositions: number;
  emergencyStopEnabled: boolean;
  warnings: string[];
}

// Reports
export interface OverviewReport {
  totalBacktestRuns?: number;
  totalPaperSessions?: number;
  totalTrades?: number;
  totalNetPnl?: number;
  totalFees?: number;
  averageWinRate?: number;
  averageProfitFactor?: number;
  maxDrawdownPercent?: number;
  bestStrategy?: string | null;
  worstStrategy?: string | null;
  bestSymbol?: string | null;
  worstSymbol?: string | null;
  generatedAtUtc?: string;
  [key: string]: unknown;
}

export interface EquityCurvePoint {
  timestampUtc: string;
  balance: number;
  equity: number;
  drawdown?: number;
  drawdownPercent?: number;
  openPositionCount?: number;
}

// Exchanges & Symbols
export interface Exchange {
  id: number;
  name: string;
  code: string;
  baseUrl: string;
  webSocketUrl?: string | null;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

export interface Symbol {
  id: number;
  exchangeId: number;
  exchangeName?: string | null;
  exchangeCode?: string | null;
  symbol: string;
  baseAsset: string;
  quoteAsset: string;
  contractType: number;
  pricePrecision: number;
  quantityPrecision: number;
  minQty: number;
  minNotional: number;
  tickSize: number;
  stepSize: number;
  makerFeeRate: number;
  takerFeeRate: number;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

export interface ExchangeSymbolSummary {
  id: number;
  symbol: string;
  displayName: string;
  exchangeId: number;
  exchangeName: string;
  isEnabled: boolean;
}

export interface MarketSnapshot {
  symbolId: number;
  symbol: string;
  timeframe: string;
  latestPrice?: number | null;
  latestUpdateTimeUtc?: string | null;
  candleCountAvailable?: number;
  indicatorsAvailable?: boolean;
  spread?: number | null;
  latestCandle?: Record<string, unknown> | null;
}

export interface MarketDataImport {
  importId: number;
  exchangeId: number;
  symbolId: number;
  timeframe: string;
  fromUtc: string;
  toUtc: string;
  status: string;
  totalReceived: number;
  insertedCount: number;
  skippedDuplicateCount: number;
  errorMessage?: string | null;
  startedAtUtc: string;
  completedAtUtc?: string | null;
}

export interface ImportCandlesRequest {
  exchangeId: number;
  symbolId: number;
  timeframe: string;
  fromUtc: string;
  toUtc: string;
  fromDate?: string;
  toDate?: string;
}

export interface MarketCandle {
  id: number;
  exchangeId: number;
  symbolId: number;
  timeframe: string;
  openTimeUtc: string;
  closeTimeUtc: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface RecalculateIndicatorsRequest {
  symbolId: number;
  timeframe: string;
  fromUtc: string;
  toUtc: string;
  fromDate?: string;
  toDate?: string;
  autoImportMissingCandles?: boolean;
}

export interface RecalculateIndicatorsResponse {
  symbolId: number;
  timeframe: number;
  fromUtc: string;
  toUtc: string;
  candlesProcessed: number;
  snapshotsInserted: number;
  snapshotsUpdated: number;
  status: string;
}

// Strategies
export interface Strategy {
  id: number;
  code: string;
  name: string;
  description: string;
  isEnabled: boolean;
  version: string;
  category?: string | null;
  isBuiltIn?: boolean;
  supportedModes?: string[];
  preferredTimeframe?: string | null;
  preferredTimeframes?: string[];
  allowedTimeframes?: string[];
  requiredTimeframes?: string[];
  requiredDataTimeframes?: string[];
  requiredIndicators?: string[];
  parameterDefinitionsAvailable?: boolean;
  supportsOptimization?: boolean;
  supportsValidation?: boolean;
  supportsLivePaper?: boolean;
  supportsBacktest?: boolean;
  supportsBenchmark?: boolean;
  supportsStrategyLab?: boolean;
  researchStatus?: string | null;
  deploymentQualificationEligible?: boolean;
  canonicalValidationExperimentId?: number | null;
}

export interface StrategyDetail extends Strategy {
  supportedRegimes?: string[];
  supportedTimeframes?: string[];
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
  description?: string;
}

export interface StrategyCatalogDetail extends Strategy {
  status?: string;
  anchorTimeframes?: string[];
  warmupCandles?: number;
  supportsHistoricalPaper?: boolean;
  supportsReplay?: boolean;
  parameterDefinitions?: StrategyParameterDefinition[];
  howItWorks?: string;
  entryLogic?: string;
  exitLogic?: string;
  noTradeConditions?: string;
  riskManagement?: string;
  approximationNotes?: string;
  implementationNotes?: string;
  recommendedValidationMode?: string;
  optimizationGuardrails?: string[];
  supportedRegimes?: string[];
  supportedTimeframes?: string[];
}

export interface StrategyParameter {
  id: number;
  parameterKey: string;
  parameterValue: string;
  valueType: number;
  timeframe?: string | null;
  symbolId?: number | null;
  isActive: boolean;
}

// Risk
export interface RiskProfile {
  id: number;
  name: string;
  description: string;
  isDefault: boolean;
}

export interface RiskRule {
  id: number;
  ruleKey: string;
  ruleValue: string;
  valueType: number;
  isEnabled: boolean;
}

export interface RiskDecision {
  id: number;
  tradingSessionId?: number | null;
  signalId?: number | null;
  aiDecisionId?: number | null;
  symbolId: number;
  decision: number;
  reason: string;
  approvedRiskPercent?: number | null;
  positionSize?: number | null;
  stopLoss?: number | null;
  takeProfit?: number | null;
  rejectedRuleKey?: string | null;
  createdAtUtc: string;
}

// AI
export interface AiHealth {
  status: string;
  service?: string;
  version?: string;
}

export interface AiDecision {
  id: number;
  tradingSessionId?: number | null;
  strategySignalId?: number | null;
  symbolId: number;
  timeframe: number;
  strategyCode?: number | null;
  marketRegime: number;
  regimeConfidence?: number | null;
  confidenceScore: number;
  confidenceClassification?: string | null;
  isAnomalous: boolean;
  anomalySeverity?: string | null;
  tradeAllowed: boolean;
  summary?: string | null;
  explanation: string;
  createdAtUtc: string;
  usedFallback?: boolean;
}

// Pipeline diagnostics
export interface PipelineDiagnostics {
  candleCount: number;
  indicatorSnapshotCount: number;
  strategyEvaluations: number;
  noTradeSignals: number;
  entrySignals: number;
  candidateSignals: number;
  warningSignals: number;
  invalidSignals: number;
  riskEvaluations: number;
  riskApproved: number;
  riskRejected: number;
  ordersCreated: number;
  ordersFilled: number;
  ordersMissed: number;
  tradesOpened: number;
  tradesClosed: number;
  aiEnabled: boolean;
  aiDecisionsCreated: number;
  effectiveMinConfidenceScore: number;
  averageNormalizedConfidenceScore?: number | null;
  lowestConfidenceScore?: number | null;
  highestConfidenceScore?: number | null;
  topRiskRejectionRules: Array<{ ruleKey: string; count: number }>;
  topNoTradeReasons: Array<{ reason: string; count: number }>;
  topStrategySignalReasons: Array<{ reason: string; count: number }>;
  warnings: string[];
  bbLiquiditySweep?: {
    funnelCounts: Record<string, number | string | boolean>;
    noTradeReasonBreakdown: Record<string, number>;
    pipelineSummary: string;
    whyZeroTradesAnalysis?: string | null;
    topNoTradeReason?: string | null;
    topNoTradeReasonCount: number;
    sampleRejectedEvaluations?: Array<{
      candleTimeUtc: string;
      stagedRejectionCode?: string | null;
      displayReason?: string | null;
    }>;
  } | null;
}

// Backtests
export interface BacktestRun {
  id: number;
  name: string;
  status: number;
  exchangeId: number;
  symbolId: number;
  timeframe: number;
  fromUtc?: string;
  toUtc?: string;
  initialBalance: number;
  finalBalance?: number | null;
  riskProfileId: number;
  executionMode: number;
  useAiScoring: boolean;
  errorMessage?: string | null;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  createdAtUtc: string;
}

export interface RunBacktestRequest {
  name: string;
  exchangeId: number;
  symbolIds: number[];
  timeframes: string[];
  fromUtc: string;
  toUtc: string;
  fromDate?: string;
  toDate?: string;
  autoImportMissingCandles?: boolean;
  initialBalance: number;
  riskProfileId: number;
  strategyIds: number[];
  executionMode: string;
  makerFeeRate: number;
  takerFeeRate: number;
  orderExpiryCandles: number;
  useAiScoring: boolean;
  minConfidenceScore: number;
  slippagePercent?: number;
}

export interface BacktestResult {
  netPnl?: number;
  profitFactor?: number;
  maxDrawdownPercent?: number;
  totalTrades?: number;
  winRatePercent?: number;
  [key: string]: unknown;
}

export interface BacktestTrade {
  id: number;
  symbolId: number;
  strategyId?: number | null;
  direction: number;
  entryPrice: number;
  exitPrice?: number | null;
  quantity: number;
  netPnl?: number | null;
  fees?: number | null;
  status: number;
  openedAtUtc: string;
  closedAtUtc?: string | null;
}

// Replay
export interface ReplaySession {
  id: number;
  name: string;
  status: number;
  exchangeId: number;
  symbolId: number;
  symbol?: string;
  timeframe: string | number;
  fromUtc: string;
  toUtc: string;
  initialBalance: number;
  currentBalance?: number;
  currentEquity?: number;
  riskProfileId: number;
  executionMode: number;
  useAiScoring: boolean;
  speed?: string;
  currentFrameIndex?: number;
  totalFrames?: number;
  errorMessage?: string | null;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

export interface ReplayFrame {
  frameIndex?: number;
  candle?: Record<string, unknown> | null;
  indicatorSnapshot?: Record<string, unknown> | null;
  strategyResults?: Record<string, unknown>[] | null;
  aiDecision?: Record<string, unknown> | null;
  riskDecision?: Record<string, unknown> | null;
  humanReadableExplanation?: string | null;
  balance?: number;
  equity?: number;
  [key: string]: unknown;
}

export interface ReplayControlResponse {
  replaySessionId: number;
  status: number;
  currentFrameIndex: number;
  currentFrame?: ReplayFrame | null;
}

export interface ReplayChartCandle {
  frameIndex: number;
  candleId: number;
  time: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
  isFutureContext: boolean;
}

export interface ReplayChartIndicator {
  frameIndex: number;
  candleId: number;
  time: string;
  ema20?: number | null;
  ema50?: number | null;
  ema200?: number | null;
  vwap?: number | null;
  rsi14?: number | null;
  atr14?: number | null;
  volumeSma20?: number | null;
  swingHigh?: boolean;
  swingLow?: boolean;
  marketStructure?: string | null;
}

export interface ReplayChartStrategyMarker {
  frameIndex: number;
  time: string;
  strategyCode: string;
  signalType: string;
  direction: string;
  price: number;
  reason: string;
}

export interface ReplayChartRiskMarker {
  frameIndex: number;
  time: string;
  decision: string;
  price: number;
  rejectedRuleKey?: string | null;
  reason: string;
}

export interface ReplayChartExecutionMarker {
  frameIndex: number;
  time: string;
  type: string;
  direction: string;
  price: number;
  label: string;
  pnl?: number | null;
}

export interface ReplayChartRangeLevel {
  label: string;
  price: number;
  startUtc: string;
  endUtc: string;
  color: string;
}

export interface ReplayChartData {
  replaySessionId: number;
  symbol: string;
  exchange: string;
  timeframe: string;
  currentFrameIndex: number;
  totalFrames: number;
  strictReplayMode: boolean;
  indicatorsMissing: boolean;
  indicatorWarning?: string | null;
  candles: ReplayChartCandle[];
  indicators: ReplayChartIndicator[];
  strategyMarkers: ReplayChartStrategyMarker[];
  riskMarkers: ReplayChartRiskMarker[];
  executionMarkers: ReplayChartExecutionMarker[];
  rangeLevels: ReplayChartRangeLevel[];
}

export interface ReplayChartQuery {
  upToFrameIndex?: number;
  currentFrameIndex?: number;
  fromFrameIndex?: number;
  toFrameIndex?: number;
  candlesBefore?: number;
  candlesAfter?: number;
  includeFutureContext?: boolean;
}

// Paper
export interface PaperAccount {
  id: number;
  name: string;
  initialBalance: number;
  currentBalance: number;
  currentEquity: number;
  currency: string;
  totalRealizedPnl?: number;
  totalUnrealizedPnl?: number;
  totalFees?: number;
  maxDrawdown?: number;
  maxDrawdownPercent?: number;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

export interface PaperSession {
  id: number;
  name: string;
  paperAccountId: number;
  status: string;
  mode: string;
  exchangeId: number;
  riskProfileId: number;
  executionMode: string;
  useAiScoring: boolean;
  minConfidenceScore?: number;
  currentCandleIndex?: number;
  totalCandles?: number;
  currentCandleTimeUtc?: string | null;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
  createdAtUtc: string;
}

export interface PaperSessionStatus {
  sessionId: number;
  paperSessionId: number;
  status: string;
  mode: string;
  currentCandleIndex: number;
  processedCandles: number;
  totalCandles?: number | null;
  progressPercent?: number | null;
  progressLabel?: string | null;
  currentCandleTimeUtc?: string | null;
  currentBalance: number;
  currentEquity: number;
  openPositionCount: number;
  ordersCount: number;
  tradesCount: number;
  missedOrdersCount: number;
  lastUpdatedAtUtc?: string | null;
  connected?: boolean | null;
  lastLiveUpdateUtc?: string | null;
  lastClosedCandleUtc?: string | null;
  lastProcessedCandleUtc?: string | null;
  latestPrice?: number | null;
  subscribedSymbols?: string[] | null;
  subscribedTimeframes?: string[] | null;
  warnings: string[];
  symbolStatuses?: PaperSymbolLiveStatus[] | null;
}

export interface PaperSymbolLiveStatus {
  symbol: string;
  timeframe: string;
  lastLiveUpdateUtc?: string | null;
  lastClosedCandleUtc?: string | null;
  lastProcessedCandleUtc?: string | null;
  latestPrice?: number | null;
  currentCandleOpenTimeUtc?: string | null;
  currentCandleCloseTimeUtc?: string | null;
  isSubscribed?: boolean;
  streamName?: string | null;
  streamWarning?: string | null;
}

export interface LivePaperChartCandle {
  candleId?: number | null;
  time: string;
  openTimeUtc: string;
  closeTimeUtc: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
  isClosed: boolean;
  isForming: boolean;
}

export interface LivePaperChartIndicator {
  time: string;
  ema20?: number | null;
  ema50?: number | null;
  ema200?: number | null;
  vwap?: number | null;
}

export interface LivePaperChartMarker {
  time: string;
  type: string;
  side: string;
  price?: number | null;
  label?: string | null;
  color?: string | null;
}

export interface LivePaperChartRangeLevel {
  label: string;
  price: number;
  startUtc: string;
  endUtc: string;
  color: string;
}

export interface LivePaperChartData {
  sessionId: number;
  mode: string;
  symbol: string;
  exchange: string;
  timeframe: string;
  connected: boolean;
  latestPrice?: number | null;
  lastLiveUpdateUtc?: string | null;
  lastClosedCandleUtc?: string | null;
  lastProcessedCandleUtc?: string | null;
  candles: LivePaperChartCandle[];
  indicators: LivePaperChartIndicator[];
  currentCandle?: LivePaperChartCandle | null;
  orderMarkers: LivePaperChartMarker[];
  tradeMarkers: LivePaperChartMarker[];
  riskMarkers: LivePaperChartMarker[];
  aiMarkers: LivePaperChartMarker[];
  missedOrderMarkers: LivePaperChartMarker[];
  rangeLevels: LivePaperChartRangeLevel[];
}

export interface PaperPosition {
  id: number;
  symbolId: number;
  direction: number;
  quantity: number;
  averageEntryPrice: number;
  markPrice: number;
  unrealizedPnl: number;
  status: number;
}

// Audit & Users
export interface AuditLog {
  id: number;
  userId?: number | null;
  userEmail?: string | null;
  action: string;
  entityType?: string | null;
  entityId?: number | null;
  severity: string;
  createdAt: string;
}

export interface AuditSummary {
  totalLogs: number;
  criticalCount: number;
  errorCount: number;
  warningCount: number;
  infoCount: number;
  topActions: Array<{ action: string; count: number }>;
  generatedAtUtc: string;
}

export interface UserRecord {
  id: number;
  fullName: string;
  email: string;
  role: UserRole;
  isActive: boolean;
  lastLoginAtUtc?: string | null;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

export type { PagedResult };
