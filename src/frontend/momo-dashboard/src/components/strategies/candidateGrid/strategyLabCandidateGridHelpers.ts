import { formatDate, formatNumber } from '@/components/common/utils';
import type { StrategyLabCandidateQuery, StrategyResearchCandidate } from '@/api/strategyLabApi';

export type QuickFilter =
  | ''
  | 'RejectedWinners'
  | 'RejectedLosers'
  | 'ConfidenceRejectedWinners'
  | 'RiskRejectedWinners'
  | 'FinancialRiskRejectedLosers'
  | 'FinancialRiskApprovedWinners'
  | 'ConfidenceRejectedRiskApproved'
  | 'ConfidenceApprovedRiskRejected'
  | 'RejectedIndependentlyByBoth'
  | 'RiskPolicyOnlyRejection'
  | 'ApprovedWinners'
  | 'ApprovedLosers'
  | 'RiskScorePassedButHardRulesFailed'
  | 'NotionalExposureRejected'
  | 'MarginUsageRejected'
  | 'LeverageRejected'
  | 'ConcurrentRiskRejected'
  | 'TargetHitButNetLoss'
  | 'OpenPositionConflict'
  | 'DailyLossRejection'
  | 'DrawdownRejection'
  | 'RiskOnlyRejectedFullPipelineOpened'
  | 'RiskOnlyOpenedFullPipelineRejected'
  | 'DifferentDrawdownDecisions'
  | 'OpenedInBoth'
  | 'OpenedInNeither'
  | 'ConfidenceOnlyRejection'
  | 'FullPipelinePortfolioRiskRejection';

export type ColumnKey =
  | 'setupDetectedAtUtc'
  | 'direction'
  | 'setupType'
  | 'proposedEntryPrice'
  | 'stopLoss'
  | 'target1'
  | 'rewardRisk'
  | 'rawOutcomeStatus'
  | 'rawNetPnl'
  | 'rawRMultiple'
  | 'confidenceScore'
  | 'confidenceThreshold'
  | 'confidenceDecision'
  | 'confidenceMargin'
  | 'confidenceReason'
  | 'riskScore'
  | 'candidateRiskScore'
  | 'portfolioRiskScore'
  | 'riskThreshold'
  | 'riskDecision'
  | 'riskReason'
  | 'riskPolicyEligibilityDecision'
  | 'riskPolicyReason'
  | 'riskFailedRuleKeysJson'
  | 'riskPerTradePercent'
  | 'riskAmount'
  | 'riskAtStopPercent'
  | 'proposedPositionSize'
  | 'positionNotional'
  | 'initialMarginRequired'
  | 'minimumRequiredLeverage'
  | 'assessmentLeverage'
  | 'proposedLeverage'
  | 'preferredLeverage'
  | 'maxLeverage'
  | 'stopDistancePercent'
  | 'notionalExposurePercent'
  | 'marginUsagePercent'
  | 'positionExposurePercent'
  | 'estimatedRoundTripFees'
  | 'feeToTargetPercent'
  | 'currentNotionalExposurePercent'
  | 'currentMarginUsagePercent'
  | 'currentExposurePercent'
  | 'concurrentRiskPercent'
  | 'concurrentPositionCount'
  | 'currentDrawdownPercent'
  | 'dailyLossUsagePercent'
  | 'riskScoreDecision'
  | 'hardRuleComplianceDecision'
  | 'riskProfileName'
  | 'riskProfileSource'
  | 'exitOutcome'
  | 'netResult'
  | 'rawExitTimeUtc'
  | 'drawdownCalculationMode'
  | 'mfe'
  | 'mae'
  | 'durationBars'
  | 'riskOnlyEntryDecision'
  | 'riskOnlyCurrentDrawdownPercent'
  | 'riskOnlyDailyLossUsagePercent'
  | 'riskOnlyCurrentMarginUsagePercent'
  | 'fullPipelineEntryDecision'
  | 'fullPipelineCurrentDrawdownPercent'
  | 'fullPipelineDailyLossUsagePercent'
  | 'fullPipelineCurrentMarginUsagePercent'
  | 'finalPipelineDecision';

export interface ColumnDef {
  key: ColumnKey;
  header: string;
  defaultVisible?: boolean;
}

export const ALL_COLUMNS: ColumnDef[] = [
  { key: 'setupDetectedAtUtc', header: 'Setup Time', defaultVisible: true },
  { key: 'direction', header: 'Direction', defaultVisible: true },
  { key: 'setupType', header: 'Setup Type', defaultVisible: true },
  { key: 'proposedEntryPrice', header: 'Entry', defaultVisible: true },
  { key: 'stopLoss', header: 'Stop', defaultVisible: true },
  { key: 'target1', header: 'Target', defaultVisible: true },
  { key: 'rewardRisk', header: 'Reward:Risk', defaultVisible: true },
  { key: 'rawOutcomeStatus', header: 'Raw Outcome', defaultVisible: true },
  { key: 'rawNetPnl', header: 'Raw PnL', defaultVisible: true },
  { key: 'rawRMultiple', header: 'Raw R Multiple', defaultVisible: true },
  { key: 'confidenceScore', header: 'Confidence Score', defaultVisible: true },
  { key: 'confidenceThreshold', header: 'Confidence Threshold', defaultVisible: true },
  { key: 'confidenceDecision', header: 'Confidence Decision', defaultVisible: true },
  { key: 'confidenceMargin', header: 'Confidence Margin', defaultVisible: true },
  { key: 'confidenceReason', header: 'Confidence Reason', defaultVisible: false },
  { key: 'candidateRiskScore', header: 'Candidate Risk Score', defaultVisible: true },
  { key: 'portfolioRiskScore', header: 'Portfolio Risk Score', defaultVisible: false },
  { key: 'riskScore', header: 'Risk Score (compat)', defaultVisible: false },
  { key: 'riskThreshold', header: 'Risk Threshold', defaultVisible: false },
  { key: 'riskDecision', header: 'Financial Risk Decision', defaultVisible: true },
  { key: 'riskReason', header: 'Financial Risk Reason', defaultVisible: false },
  { key: 'riskFailedRuleKeysJson', header: 'Failed Risk Rules', defaultVisible: true },
  { key: 'riskPolicyEligibilityDecision', header: 'Risk Policy Eligibility', defaultVisible: true },
  { key: 'riskPolicyReason', header: 'Risk Policy Reason', defaultVisible: false },
  { key: 'riskPerTradePercent', header: 'Risk Per Trade %', defaultVisible: false },
  { key: 'riskAmount', header: 'Risk Amount', defaultVisible: false },
  { key: 'riskAtStopPercent', header: 'Risk At Stop %', defaultVisible: false },
  { key: 'proposedPositionSize', header: 'Position Size', defaultVisible: true },
  { key: 'positionNotional', header: 'Position Notional', defaultVisible: false },
  { key: 'initialMarginRequired', header: 'Initial Margin', defaultVisible: false },
  { key: 'minimumRequiredLeverage', header: 'Min Required Leverage', defaultVisible: true },
  { key: 'assessmentLeverage', header: 'Assessment Leverage', defaultVisible: false },
  { key: 'proposedLeverage', header: 'Proposed Leverage (legacy)', defaultVisible: false },
  { key: 'preferredLeverage', header: 'Preferred Leverage', defaultVisible: false },
  { key: 'maxLeverage', header: 'Max Leverage', defaultVisible: false },
  { key: 'stopDistancePercent', header: 'Stop Distance %', defaultVisible: false },
  { key: 'notionalExposurePercent', header: 'Notional Exposure %', defaultVisible: true },
  { key: 'marginUsagePercent', header: 'Margin Usage %', defaultVisible: true },
  { key: 'positionExposurePercent', header: 'Position Exposure % (legacy)', defaultVisible: false },
  { key: 'estimatedRoundTripFees', header: 'Estimated Fees', defaultVisible: false },
  { key: 'feeToTargetPercent', header: 'Fee / Target %', defaultVisible: false },
  { key: 'currentNotionalExposurePercent', header: 'Current Notional %', defaultVisible: false },
  { key: 'currentMarginUsagePercent', header: 'Current Margin %', defaultVisible: false },
  { key: 'currentExposurePercent', header: 'Current Exposure % (legacy)', defaultVisible: false },
  { key: 'concurrentRiskPercent', header: 'Concurrent Risk %', defaultVisible: false },
  { key: 'concurrentPositionCount', header: 'Open Positions', defaultVisible: false },
  { key: 'currentDrawdownPercent', header: 'Drawdown % (Risk-Only generic)', defaultVisible: false },
  { key: 'dailyLossUsagePercent', header: 'Daily Loss % (Risk-Only generic)', defaultVisible: false },
  { key: 'riskOnlyEntryDecision', header: 'RO Entry', defaultVisible: true },
  { key: 'riskOnlyCurrentDrawdownPercent', header: 'RO Drawdown %', defaultVisible: true },
  { key: 'riskOnlyDailyLossUsagePercent', header: 'RO Daily Loss %', defaultVisible: false },
  { key: 'riskOnlyCurrentMarginUsagePercent', header: 'RO Margin %', defaultVisible: false },
  { key: 'fullPipelineEntryDecision', header: 'FP Entry', defaultVisible: true },
  { key: 'fullPipelineCurrentDrawdownPercent', header: 'FP Drawdown %', defaultVisible: true },
  { key: 'fullPipelineDailyLossUsagePercent', header: 'FP Daily Loss %', defaultVisible: false },
  { key: 'fullPipelineCurrentMarginUsagePercent', header: 'FP Margin %', defaultVisible: false },
  { key: 'finalPipelineDecision', header: 'Final Pipeline', defaultVisible: true },
  { key: 'riskScoreDecision', header: 'Risk Score Decision', defaultVisible: true },
  { key: 'hardRuleComplianceDecision', header: 'Hard Rule Compliance', defaultVisible: true },
  { key: 'riskProfileName', header: 'Risk Profile', defaultVisible: false },
  { key: 'riskProfileSource', header: 'Profile Source', defaultVisible: false },
  { key: 'exitOutcome', header: 'Exit Outcome', defaultVisible: false },
  { key: 'netResult', header: 'Net Result', defaultVisible: false },
  { key: 'rawExitTimeUtc', header: 'Exit Time', defaultVisible: false },
  { key: 'drawdownCalculationMode', header: 'Drawdown Mode', defaultVisible: false },
  { key: 'mfe', header: 'MFE', defaultVisible: false },
  { key: 'mae', header: 'MAE', defaultVisible: false },
  { key: 'durationBars', header: 'Duration Bars', defaultVisible: false },
];

export const QUICK_FILTERS: { id: QuickFilter; label: string }[] = [
  { id: 'RejectedWinners', label: 'Rejected Winners' },
  { id: 'RejectedLosers', label: 'Rejected Losers' },
  { id: 'ConfidenceRejectedWinners', label: 'Confidence Rejected Winners' },
  { id: 'RiskRejectedWinners', label: 'Financial Risk Rejected Winners' },
  { id: 'FinancialRiskRejectedLosers', label: 'Financial Risk Rejected Losers' },
  { id: 'FinancialRiskApprovedWinners', label: 'Financial Risk Approved Winners' },
  { id: 'ConfidenceRejectedRiskApproved', label: 'Confidence Rejected but Risk Approved' },
  { id: 'ConfidenceApprovedRiskRejected', label: 'Confidence Approved but Risk Rejected' },
  { id: 'RejectedIndependentlyByBoth', label: 'Rejected Independently By Both' },
  { id: 'RiskPolicyOnlyRejection', label: 'Risk Policy Only Rejection' },
  { id: 'ApprovedWinners', label: 'Approved Winners' },
  { id: 'ApprovedLosers', label: 'Approved Losers' },
  { id: 'RiskScorePassedButHardRulesFailed', label: 'Score passed, hard rules failed' },
  { id: 'NotionalExposureRejected', label: 'Notional exposure rejected' },
  { id: 'MarginUsageRejected', label: 'Margin usage rejected' },
  { id: 'LeverageRejected', label: 'Leverage rejected' },
  { id: 'ConcurrentRiskRejected', label: 'Concurrent risk rejected' },
  { id: 'TargetHitButNetLoss', label: 'Target hit but net loss' },
  { id: 'OpenPositionConflict', label: 'Open-position conflict' },
  { id: 'DailyLossRejection', label: 'Daily loss rejection' },
  { id: 'DrawdownRejection', label: 'Drawdown rejection' },
  { id: 'RiskOnlyRejectedFullPipelineOpened', label: 'RO rejected / FP opened' },
  { id: 'RiskOnlyOpenedFullPipelineRejected', label: 'RO opened / FP rejected' },
  { id: 'DifferentDrawdownDecisions', label: 'Different drawdown decisions' },
  { id: 'OpenedInBoth', label: 'Opened in both paths' },
  { id: 'OpenedInNeither', label: 'Opened in neither path' },
  { id: 'ConfidenceOnlyRejection', label: 'Confidence-only rejection' },
  { id: 'FullPipelinePortfolioRiskRejection', label: 'FP portfolio-risk rejection' },
];

export function dash(value: string | number | null | undefined, evaluated?: boolean) {
  if (evaluated === false) return '—';
  if (value === null || value === undefined || value === '') return '—';
  return value;
}

export function isNotEvaluated(decision?: string | null) {
  return !decision || decision === 'NotEvaluated';
}

export function renderCell(key: ColumnKey, row: StrategyResearchCandidate) {
  switch (key) {
    case 'setupDetectedAtUtc':
      return formatDate(row.setupDetectedAtUtc);
    case 'direction':
      return row.direction;
    case 'setupType':
      return row.setupType;
    case 'proposedEntryPrice':
      return formatNumber(row.proposedEntryPrice);
    case 'stopLoss':
      return formatNumber(row.stopLoss);
    case 'target1':
      return formatNumber(row.target1);
    case 'rewardRisk':
      return formatNumber(row.rewardRisk);
    case 'rawOutcomeStatus':
      return row.rawOutcomeStatus;
    case 'rawNetPnl':
      return row.rawNetPnl != null ? formatNumber(row.rawNetPnl) : '—';
    case 'rawRMultiple':
      return row.rawRMultiple != null ? formatNumber(row.rawRMultiple) : '—';
    case 'confidenceScore':
      return isNotEvaluated(row.confidenceDecision) ? '—' : dash(row.confidenceScore != null ? formatNumber(row.confidenceScore) : null);
    case 'confidenceThreshold':
      return isNotEvaluated(row.confidenceDecision) ? '—' : dash(row.confidenceThreshold != null ? formatNumber(row.confidenceThreshold) : null);
    case 'confidenceDecision':
      return row.confidenceDecision ?? '—';
    case 'confidenceMargin':
      return isNotEvaluated(row.confidenceDecision) ? '—' : dash(row.confidenceMargin != null ? formatNumber(row.confidenceMargin) : null);
    case 'confidenceReason':
      return isNotEvaluated(row.confidenceDecision) ? '—' : dash(row.confidenceReason);
    case 'riskScore':
      return isNotEvaluated(row.riskDecision) ? '—' : dash(row.riskScore != null ? formatNumber(row.riskScore) : null);
    case 'candidateRiskScore':
      return isNotEvaluated(row.riskDecision)
        ? '—'
        : dash((row.candidateRiskScore ?? row.riskScore) != null ? formatNumber(row.candidateRiskScore ?? row.riskScore) : null);
    case 'portfolioRiskScore':
      return row.portfolioRiskAssessmentStatus === 'Evaluated' && row.portfolioRiskScore != null
        ? formatNumber(row.portfolioRiskScore)
        : '—';
    case 'riskThreshold':
      return isNotEvaluated(row.riskDecision) ? '—' : dash(row.riskThreshold != null ? formatNumber(row.riskThreshold) : null);
    case 'riskDecision':
      return row.riskDecision ?? '—';
    case 'riskReason':
      return isNotEvaluated(row.riskDecision) ? '—' : dash(row.riskReason);
    case 'riskFailedRuleKeysJson':
      return isNotEvaluated(row.riskDecision) ? '—' : dash(row.riskFailedRuleKeysJson);
    case 'riskPolicyEligibilityDecision':
      return row.riskPolicyEligibilityDecision ?? '—';
    case 'riskPolicyReason':
      return row.riskPolicyEligibilityDecision === 'NotEvaluated' || !row.riskPolicyEligibilityDecision
        ? '—'
        : dash(row.riskPolicyReason);
    case 'riskPerTradePercent':
      return isNotEvaluated(row.riskDecision) ? '—' : dash(row.riskPerTradePercent != null ? formatNumber(row.riskPerTradePercent) : null);
    case 'riskAmount':
      return isNotEvaluated(row.riskDecision) ? '—' : dash(row.riskAmount != null ? formatNumber(row.riskAmount) : null);
    case 'riskAtStopPercent':
      return isNotEvaluated(row.riskDecision) ? '—' : dash(row.riskAtStopPercent != null ? formatNumber(row.riskAtStopPercent) : null);
    case 'proposedPositionSize':
      return isNotEvaluated(row.riskDecision)
        ? '—'
        : row.proposedPositionSize != null
          ? formatNumber(row.proposedPositionSize)
          : dash(row.positionSizingUnavailableReason ?? null);
    case 'positionNotional':
      return isNotEvaluated(row.riskDecision) ? '—' : dash(row.positionNotional != null ? formatNumber(row.positionNotional) : null);
    case 'initialMarginRequired':
      return isNotEvaluated(row.riskDecision) ? '—' : dash(row.initialMarginRequired != null ? formatNumber(row.initialMarginRequired) : null);
    case 'minimumRequiredLeverage':
      return isNotEvaluated(row.riskDecision)
        ? '—'
        : dash((row.minimumRequiredLeverage ?? row.proposedLeverage) != null
          ? formatNumber(row.minimumRequiredLeverage ?? row.proposedLeverage)
          : null);
    case 'assessmentLeverage':
      return isNotEvaluated(row.riskDecision) ? '—' : dash(row.assessmentLeverage != null ? formatNumber(row.assessmentLeverage) : null);
    case 'proposedLeverage':
      return isNotEvaluated(row.riskDecision) ? '—' : dash(row.proposedLeverage != null ? formatNumber(row.proposedLeverage) : null);
    case 'preferredLeverage':
      return isNotEvaluated(row.riskDecision) ? '—' : dash(row.preferredLeverage != null ? formatNumber(row.preferredLeverage) : null);
    case 'maxLeverage':
      return isNotEvaluated(row.riskDecision) ? '—' : dash(row.maxLeverage != null ? formatNumber(row.maxLeverage) : null);
    case 'stopDistancePercent':
      return row.stopDistancePercent != null ? formatNumber(row.stopDistancePercent) : '—';
    case 'notionalExposurePercent':
      return isNotEvaluated(row.riskDecision)
        ? '—'
        : dash(row.notionalExposurePercent != null ? formatNumber(row.notionalExposurePercent) : null);
    case 'marginUsagePercent':
      return isNotEvaluated(row.riskDecision)
        ? '—'
        : dash(row.marginUsagePercent != null ? formatNumber(row.marginUsagePercent) : null);
    case 'positionExposurePercent':
      return isNotEvaluated(row.riskDecision) ? '—' : dash(row.positionExposurePercent != null ? formatNumber(row.positionExposurePercent) : null);
    case 'estimatedRoundTripFees':
      return isNotEvaluated(row.riskDecision) ? '—' : dash(row.estimatedRoundTripFees != null ? formatNumber(row.estimatedRoundTripFees) : null);
    case 'feeToTargetPercent':
      return isNotEvaluated(row.riskDecision) ? '—' : dash(row.feeToTargetPercent != null ? formatNumber(row.feeToTargetPercent) : null);
    case 'currentNotionalExposurePercent':
      return row.portfolioRiskAssessmentStatus === 'Evaluated' && row.currentNotionalExposurePercent != null
        ? formatNumber(row.currentNotionalExposurePercent)
        : '—';
    case 'currentMarginUsagePercent':
      return row.portfolioRiskAssessmentStatus === 'Evaluated' && row.currentMarginUsagePercent != null
        ? formatNumber(row.currentMarginUsagePercent)
        : '—';
    case 'currentExposurePercent':
      return row.portfolioRiskAssessmentStatus === 'Evaluated' && row.currentExposurePercent != null
        ? formatNumber(row.currentExposurePercent)
        : '—';
    case 'concurrentRiskPercent':
      return row.portfolioRiskAssessmentStatus === 'Evaluated' && row.concurrentRiskPercent != null
        ? formatNumber(row.concurrentRiskPercent)
        : '—';
    case 'concurrentPositionCount':
      return row.portfolioRiskAssessmentStatus === 'Evaluated' && row.concurrentPositionCount != null
        ? String(row.concurrentPositionCount)
        : '—';
    case 'currentDrawdownPercent':
      return row.portfolioRiskAssessmentStatus === 'Evaluated' && row.currentDrawdownPercent != null
        ? formatNumber(row.currentDrawdownPercent)
        : '—';
    case 'dailyLossUsagePercent':
      return row.portfolioRiskAssessmentStatus === 'Evaluated' && row.dailyLossUsagePercent != null
        ? formatNumber(row.dailyLossUsagePercent)
        : '—';
    case 'riskScoreDecision':
      return row.riskScoreDecision ?? '—';
    case 'hardRuleComplianceDecision':
      return row.hardRuleComplianceDecision ?? '—';
    case 'riskProfileName':
      return dash(row.riskProfileName);
    case 'riskProfileSource':
      return dash(row.riskProfileSource);
    case 'exitOutcome':
      return row.exitOutcome && row.exitOutcome !== 'NotSet' ? row.exitOutcome : '—';
    case 'netResult':
      return row.netResult && row.netResult !== 'Unknown' ? row.netResult : '—';
    case 'rawExitTimeUtc':
      return row.rawExitTimeUtc ? formatDate(row.rawExitTimeUtc) : '—';
    case 'drawdownCalculationMode':
      return dash(row.drawdownCalculationMode);
    case 'mfe':
      return row.mfe != null ? formatNumber(row.mfe) : '—';
    case 'mae':
      return row.mae != null ? formatNumber(row.mae) : '—';
    case 'durationBars':
      return row.durationBars != null ? String(row.durationBars) : '—';
    case 'riskOnlyEntryDecision':
      return row.riskOnlyEntryDecision ?? '—';
    case 'riskOnlyCurrentDrawdownPercent':
      return row.riskOnlyCurrentDrawdownPercent != null ? formatNumber(row.riskOnlyCurrentDrawdownPercent) : '—';
    case 'riskOnlyDailyLossUsagePercent':
      return row.riskOnlyDailyLossUsagePercent != null ? formatNumber(row.riskOnlyDailyLossUsagePercent) : '—';
    case 'riskOnlyCurrentMarginUsagePercent':
      return row.riskOnlyCurrentMarginUsagePercent != null ? formatNumber(row.riskOnlyCurrentMarginUsagePercent) : '—';
    case 'fullPipelineEntryDecision':
      return row.fullPipelineEntryDecision ?? '—';
    case 'fullPipelineCurrentDrawdownPercent':
      return row.fullPipelineCurrentDrawdownPercent != null ? formatNumber(row.fullPipelineCurrentDrawdownPercent) : '—';
    case 'fullPipelineDailyLossUsagePercent':
      return row.fullPipelineDailyLossUsagePercent != null ? formatNumber(row.fullPipelineDailyLossUsagePercent) : '—';
    case 'fullPipelineCurrentMarginUsagePercent':
      return row.fullPipelineCurrentMarginUsagePercent != null ? formatNumber(row.fullPipelineCurrentMarginUsagePercent) : '—';
    case 'finalPipelineDecision':
      return row.finalPipelineDecision ?? '—';
    default:
      return '—';
  }
}

export function toCsv(rows: StrategyResearchCandidate[], columns: ColumnDef[]) {
  const header = columns.map((c) => c.header).join(',');
  const lines = rows.map((row) =>
    columns
      .map((c) => {
        const value = String(renderCell(c.key, row)).replaceAll('"', '""');
        return `"${value}"`;
      })
      .join(','),
  );
  return [header, ...lines].join('\n');
}

export function download(filename: string, content: string, mime: string) {
  const blob = new Blob([content], { type: mime });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

export interface CandidateGridFilterState {
  page: number;
  pageSize: number;
  sortBy: string;
  sortDirection: 'asc' | 'desc';
  search: string;
  direction: string;
  rawOutcome: string;
  confidenceDecision: string;
  riskDecision: string;
  confidenceMin: string;
  confidenceMax: string;
  riskMin: string;
  riskMax: string;
  profitableOnly: string;
  fromUtc: string;
  toUtc: string;
  quickFilter: QuickFilter;
}

/**
 * Pure translation from grid filter/pagination UI state to the server query
 * contract. Kept side-effect free so pagination and filter combinations can
 * be asserted directly without rendering the grid.
 */
export function buildCandidateQuery(state: CandidateGridFilterState): StrategyLabCandidateQuery {
  return {
    page: state.page,
    pageSize: state.pageSize,
    sortBy: state.sortBy,
    sortDirection: state.sortDirection,
    search: state.search || undefined,
    direction: state.direction || undefined,
    rawOutcome: state.rawOutcome || undefined,
    confidenceDecision: state.confidenceDecision || undefined,
    confidenceMin: state.confidenceMin !== '' ? Number(state.confidenceMin) : undefined,
    confidenceMax: state.confidenceMax !== '' ? Number(state.confidenceMax) : undefined,
    riskDecision: state.riskDecision || undefined,
    riskMin: state.riskMin !== '' ? Number(state.riskMin) : undefined,
    riskMax: state.riskMax !== '' ? Number(state.riskMax) : undefined,
    profitableOnly: state.profitableOnly === 'profitable' ? true : state.profitableOnly === 'losing' ? false : undefined,
    fromUtc: state.fromUtc || undefined,
    toUtc: state.toUtc || undefined,
    quickFilter: state.quickFilter || undefined,
  };
}
