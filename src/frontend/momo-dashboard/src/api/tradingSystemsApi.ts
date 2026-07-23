import { apiRequest, getApiBaseUrl } from '@/api/apiClient';
import { getStoredToken } from '@/auth/storage';

export interface TradingSystemInfo {
  code: string;
  name: string;
  description: string;
  category: string;
  analysisOnly: boolean;
  supportedPrimaryTimeframes: string[];
  supportedHigherTimeframes: string[];
}

export interface SwingPoint {
  id: string;
  candleId: number;
  timeUtc: string;
  price: number;
  type: 'High' | 'Low';
  strength: number;
  leftBars: number;
  rightBars: number;
  source: 'Wick' | 'Close';
}

export interface SkSequencePoint {
  label: string;
  candleId?: number;
  timeUtc: string;
  price: number;
  candleIndex?: number;
  description?: string;
}

export interface SkSequenceCandidate {
  id: string;
  direction: 'Bullish' | 'Bearish';
  status: 'Potential' | 'Active' | 'Invalidated' | 'Completed';
  pointZ?: SkSequencePoint | null;
  pointA?: SkSequencePoint | null;
  pointB?: SkSequencePoint | null;
  pointC?: SkSequencePoint | null;
  impulseStartTimeUtc: string;
  impulseEndTimeUtc: string;
  correctionZoneMin: number;
  correctionZoneMax: number;
  goldenPocketMin: number;
  goldenPocketMax: number;
  target1: number;
  target2: number;
  extension1618: number;
  invalidationLevel: number;
  currentPricePosition: string;
  confidenceScore: number;
  notes: string;
  warnings: string[];
  validationStatus?: string;
  validationMessage?: string;
  eligibleForBestIdea?: boolean;
}

export interface SkSequence {
  id: string;
  direction: string;
  timeframe: string;
  symbol: string;
  startPoint?: SkSequencePoint | null;
  impulseEndPoint?: SkSequencePoint | null;
  correctionPoint?: SkSequencePoint | null;
  currentPoint?: SkSequencePoint | null;
  sequenceHigh: number;
  sequenceLow: number;
  correctionZoneLow: number;
  correctionZoneHigh: number;
  strongCorrectionZoneLow: number;
  strongCorrectionZoneHigh: number;
  invalidationLevel: number;
  target1: number;
  target2: number;
  extensionTarget: number;
  sequenceStatus: string;
  validityStatus: string;
  clarityScore: number;
  clarityLabel: string;
  usefulnessScore: number;
  usefulnessStatus: string;
  selectedAsBest: boolean;
  reasonSelected: string;
  invalidationReason: string;
  structureCategory: string;
  hiddenFromBeginner: boolean;
  beginnerExplanation: string;
  advancedExplanation: string;
  warningMessages: string[];
  calculationNotes: string;
  validationStatus: string;
  validationMessage: string;
  eligibleForBestIdea: boolean;
}

export interface SkConceptAudit {
  htfDirection: string;
  ltfDirection: string;
  htfLtfAgreement: boolean;
  selectedSequenceDirection: string;
  sequenceStatus: string;
  validityStatus: string;
  usefulnessStatus: string;
  clarityScore: number;
  clarityLabel: string;
  usefulnessScore: number;
  reasonSelected: string;
  sequencePoints: string[];
  reactionZoneText: string;
  strongReactionZoneText: string;
  invalidationLevelText: string;
  targetValidation: string;
  alreadyReachedCheck: string;
  invalidationCheck: string;
  hiddenStructuresCount: number;
  directionMismatchStructuresCount: number;
  primaryUpwardId?: string | null;
  primaryDownwardId?: string | null;
  hiddenStructureIds: string[];
  invalidStructureIds: string[];
}

export interface SkFibonacciZone {
  sequenceId: string;
  kind: 'Retracement' | 'Extension';
  ratio: number;
  price: number;
  label: string;
  isGoldenPocket: boolean;
}

export interface SkKeyLevel {
  label: string;
  price: number;
  kind: string;
  sequenceId?: string | null;
}

export interface SkMultiTimeframeContext {
  higherTimeframeBias: string;
  higherTimeframeTrendDescription: string;
  importantHigherTimeframeLevels: SkKeyLevel[];
  conflictWarning?: string | null;
}

export type ChartOverlayCategory =
  | 'SwingPoint'
  | 'ReactionZone'
  | 'StrongReactionZone'
  | 'Danger'
  | 'Target'
  | 'Scenario'
  | 'Fibonacci'
  | 'HigherTimeframe'
  | 'SetupPoint'
  | 'Current'
  | 'Other';

export interface ChartOverlay {
  type:
    | 'HorizontalLine'
    | 'Zone'
    | 'FibonacciRetracement'
    | 'FibonacciExtension'
    | 'Marker'
    | 'ScenarioArrow'
    | 'Label';
  label: string;
  color: string;
  price?: number | null;
  priceLow?: number | null;
  priceHigh?: number | null;
  timeUtc?: string | null;
  endTimeUtc?: string | null;
  direction?: string | null;
  category: ChartOverlayCategory;
  shortLabel: string;
  sequenceId?: string | null;
  isBestBullish: boolean;
  isBestBearish: boolean;
  ratio?: number | null;
  groupName: string;
  setupId?: string | null;
  setupRank: number;
  setupDirection?: string | null;
  levelType: string;
  displayName: string;
  plainLanguageMeaning: string;
  tooltipTitle: string;
  tooltipBody: string;
  visibleByDefault: boolean;
  importance: 'High' | 'Medium' | 'Low';
  isAdvanced: boolean;
  isPrimary: boolean;
  layerKey?: string;
  visibilityTier?: string;
  beginnerLabel?: string;
  advancedLabel?: string;
  explanation?: string;
}

export interface SkGlossaryTerm {
  term: string;
  explanation: string;
}

export interface SkIdea {
  direction: 'Bullish' | 'Bearish';
  directionLabel: string;
  status: string;
  statusLabel: string;
  clarityLabel: string;
  clarityScore: number;
  reactionZoneMin: number;
  reactionZoneMax: number;
  reactionZoneText: string;
  strongReactionZoneMin: number;
  strongReactionZoneMax: number;
  strongReactionZoneText: string;
  dangerLevel: number;
  dangerLevelText: string;
  target1: number;
  target2: number;
  targetsText: string;
  currentPricePositionLabel: string;
  whyItMatters: string;
  plainExplanation: string;
  candidateId: string;
  validationStatus?: string;
  validationMessage?: string;
}

export interface SkAiSummary {
  summary: string;
  bullishScenario: string;
  bearishScenario: string;
  invalidationExplanation: string;
  plainLanguageSummary: string;
  whatThisMeans: string;
  bottomLine: string;
  whatWouldMakeWrong?: string;
  whatToWatchNext?: string;
  whyNotTradeSignal?: string;
  usefulnessExplanation?: string;
  alternativeStructuresNote?: string;
  higherTimeframeExplanation: string;
  conflictExplanation: string;
  warnings: string[];
  confidenceLabel: string;
  analysisOnly: boolean;
  usedFallback: boolean;
  source: string;
}

export interface SkChartCandle {
  timeUtc: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface SkAnalysisDiagnostics {
  primaryCandleCount: number;
  higherCandleCount: number;
  swingHighCount: number;
  swingLowCount: number;
  sequenceCandidateCount: number;
  resolvedSensitivity: string;
  minSwingDistancePercent: number;
  minSwingCandles: number;
  fibonacciCorrectionLevels: number[];
  fibonacciExtensionLevels: number[];
  note: string;
}

export interface SkSystemAnalysisResult {
  analysisId: number;
  systemCode: string;
  systemName: string;
  exchangeId: number;
  exchangeName: string;
  symbolId: number;
  symbol: string;
  primaryTimeframe: string;
  higherTimeframe: string;
  lookbackCandles: number;
  swingSensitivity: string;
  directionMode: string;
  status: string;
  analysisTimeUtc: string;
  latestCandleTimeUtc?: string | null;
  currentPrice: number;
  marketBias: string;
  confidenceLabel: string;
  summary: string;
  bullishScenario: string;
  bearishScenario: string;
  invalidationLevels: string[];
  explanationMode: string;
  priceDecimals: number;
  plainLanguageSummary: string;
  bottomLine: string;
  whatThisMeans: string;
  keyAreaToWatch: string;
  dangerLevelToWatch: string;
  higherTimeframeExplanation: string;
  conflictExplanation: string;
  bestBullishIdea?: SkIdea | null;
  bestBearishIdea?: SkIdea | null;
  clarityReasons: string[];
  clarityWarnings: string[];
  glossaryTerms: SkGlossaryTerm[];
  displayLabels: Record<string, string>;
  swingPoints: SwingPoint[];
  sequenceCandidates: SkSequenceCandidate[];
  sequences?: SkSequence[];
  conceptAudit?: SkConceptAudit | null;
  fibonacciZones: SkFibonacciZone[];
  keyLevels: SkKeyLevel[];
  chartOverlays: ChartOverlay[];
  higherTimeframeContext?: SkMultiTimeframeContext | null;
  aiSummary?: SkAiSummary | null;
  candles: SkChartCandle[];
  warnings: string[];
  diagnostics: SkAnalysisDiagnostics;
  analysisOnly: boolean;
  analysisOnlyDisclaimer: string;
  quickViewMode: string;
  htfContext?: SkTimeframeContext | null;
  ltfContext?: SkTimeframeContext | null;
}

export interface TradingSystemAnalysisSummary {
  id: number;
  systemCode: string;
  symbol: string;
  primaryTimeframe: string;
  higherTimeframe: string;
  marketBias: string;
  confidenceLabel: string;
  status: string;
  conclusion: string;
  clarityLabel?: string;
  usefulnessStatus?: string;
  sequenceStatus?: string;
  validityStatus?: string;
  chartExportStatus?: string;
  analysisTimeUtc: string;
  latestCandleTimeUtc?: string | null;
  createdAtUtc: string;
}

export interface SkTimeframeContext {
  timeframe: string;
  role: string;
  direction: string;
  summary: string;
  reactionZoneText: string;
  dangerLevelText: string;
  targetsText: string;
  clarityLabel: string;
  agreesWithPrimary: boolean;
  warnings: string[];
}

export interface SkAnalysisDefaults {
  primaryTimeframe: string;
  higherTimeframe: string;
  lookbackCandles: number;
  swingSensitivity: string;
  sequenceDirectionMode: string;
  explanationLevel: string;
  quickViewMode: string;
  includeAllPossibleSetups: boolean;
  includeFibonacciDetailLevels: boolean;
  includeTargetLevels: boolean;
  includeDangerLevels: boolean;
  includeHigherTimeframeZones: boolean;
  includeLiquidityContext: boolean;
  includeBreakoutRetestContext: boolean;
  supportedPrimaryTimeframes: string[];
  supportedHigherTimeframes: string[];
  supportedAnalysisTimeframes: string[];
  analysisOnlyDisclaimer: string;
}

export interface SkSystemAnalyzeRequest {
  exchangeId: number;
  symbolId: number;
  primaryTimeframe: string;
  higherTimeframe: string;
  additionalTimeframes?: string[];
  lookbackCandles: number;
  swingSensitivity: string;
  directionMode: string;
  useAiSummary: boolean;
  explanationMode: string;
  quickViewMode?: string;
  includeAllPossibleSetups?: boolean;
  includeFibonacciDetailLevels?: boolean;
  includeTargetLevels?: boolean;
  includeDangerLevels?: boolean;
  includeHigherTimeframeZones?: boolean;
  includeLiquidityContext?: boolean;
  includeBreakoutRetestContext?: boolean;
  autoImportMissingCandles?: boolean;
}

export interface SkImportRequiredDataRequest {
  exchangeId: number;
  symbolId: number;
  primaryTimeframe: string;
  higherTimeframe: string;
  lookbackCandles: number;
}

export interface MarketDataImport {
  id: number;
  exchangeId: number;
  symbolId: number;
  timeframe: string;
  status: string;
}

export interface SkExportPdfOptions {
  chartImageBase64?: string | null;
  includeChart?: boolean;
  includeGlossary?: boolean;
  includeRawDiagnostics?: boolean;
  chartReady?: boolean;
  overlaysReady?: boolean;
  overlayCount?: number;
  exportError?: string | null;
}

export interface SkPdfDownload {
  blob: Blob;
  fileName: string;
}

function parseContentDispositionFileName(disposition: string | null, fallback: string): string {
  if (!disposition) {
    return fallback;
  }
  const utf8Match = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
  if (utf8Match?.[1]) {
    try {
      return decodeURIComponent(utf8Match[1]);
    } catch {
      return utf8Match[1];
    }
  }
  const quotedMatch = /filename="?([^";]+)"?/i.exec(disposition);
  return quotedMatch?.[1] ?? fallback;
}

async function exportAnalysisPdf(id: number, options: SkExportPdfOptions = {}): Promise<SkPdfDownload> {
  const token = getStoredToken();
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    Accept: 'application/pdf',
  };
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  const response = await fetch(`${getApiBaseUrl()}/trading-systems/sk-system/analyses/${id}/export-pdf`, {
    method: 'POST',
    headers,
    body: JSON.stringify({
      chartImageBase64: options.chartImageBase64 ?? null,
      includeChart: options.includeChart ?? true,
      includeGlossary: options.includeGlossary ?? true,
      includeRawDiagnostics: options.includeRawDiagnostics ?? false,
      chartReady: options.chartReady ?? false,
      overlaysReady: options.overlaysReady ?? false,
      overlayCount: options.overlayCount ?? 0,
      exportError: options.exportError ?? null,
    }),
  });

  if (!response.ok) {
    throw new Error('Could not generate PDF report. Please try again.');
  }

  const blob = await response.blob();
  const fileName = parseContentDispositionFileName(
    response.headers.get('content-disposition'),
    `SK-System-${id}.pdf`,
  );
  return { blob, fileName };
}

export const tradingSystemsApi = {
  getDefaults: () => apiRequest<SkAnalysisDefaults>('/trading-systems/sk/defaults'),
  exportAnalysisPdf,
  listSystems: () => apiRequest<TradingSystemInfo[]>('/trading-systems'),
  analyze: (request: SkSystemAnalyzeRequest) =>
    apiRequest<SkSystemAnalysisResult>('/trading-systems/sk-system/analyze', {
      method: 'POST',
      body: request,
    }),
  listAnalyses: (limit = 50) =>
    apiRequest<TradingSystemAnalysisSummary[]>('/trading-systems/sk-system/analyses', {
      query: { limit },
    }),
  getAnalysis: (id: number) =>
    apiRequest<SkSystemAnalysisResult>(`/trading-systems/sk-system/analyses/${id}`),
  deleteAnalysis: (id: number) =>
    apiRequest<boolean>(`/trading-systems/sk-system/analyses/${id}`, { method: 'DELETE' }),
  importRequiredData: (request: SkImportRequiredDataRequest) =>
    apiRequest<MarketDataImport[]>('/trading-systems/sk-system/import-required-data', {
      method: 'POST',
      body: request,
    }),
};
