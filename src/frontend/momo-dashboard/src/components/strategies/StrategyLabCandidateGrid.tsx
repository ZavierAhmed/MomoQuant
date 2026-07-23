import { useCallback, useEffect, useMemo, useState } from 'react';
import { formatDate, formatNumber } from '@/components/common/utils';
import {
  strategyLabApi,
  type PagedCandidates,
  type StrategyLabCandidateQuery,
  type StrategyResearchCandidate,
} from '@/api/strategyLabApi';
import { parseApiClientError } from '@/utils/apiError';

type QuickFilter =
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

type ColumnKey =
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

interface ColumnDef {
  key: ColumnKey;
  header: string;
  defaultVisible?: boolean;
}

const ALL_COLUMNS: ColumnDef[] = [
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

const QUICK_FILTERS: { id: QuickFilter; label: string }[] = [
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

function dash(value: string | number | null | undefined, evaluated?: boolean) {
  if (evaluated === false) return '—';
  if (value === null || value === undefined || value === '') return '—';
  return value;
}

function isNotEvaluated(decision?: string | null) {
  return !decision || decision === 'NotEvaluated';
}

function renderCell(key: ColumnKey, row: StrategyResearchCandidate) {
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

function toCsv(rows: StrategyResearchCandidate[], columns: ColumnDef[]) {
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

function download(filename: string, content: string, mime: string) {
  const blob = new Blob([content], { type: mime });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

export function StrategyLabCandidateGrid({ runId }: { runId: number }) {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [sortBy, setSortBy] = useState('setupDetectedAtUtc');
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('desc');
  const [search, setSearch] = useState('');
  const [direction, setDirection] = useState('');
  const [rawOutcome, setRawOutcome] = useState('');
  const [confidenceDecision, setConfidenceDecision] = useState('');
  const [riskDecision, setRiskDecision] = useState('');
  const [confidenceMin, setConfidenceMin] = useState('');
  const [confidenceMax, setConfidenceMax] = useState('');
  const [riskMin, setRiskMin] = useState('');
  const [riskMax, setRiskMax] = useState('');
  const [profitableOnly, setProfitableOnly] = useState('');
  const [fromUtc, setFromUtc] = useState('');
  const [toUtc, setToUtc] = useState('');
  const [quickFilter, setQuickFilter] = useState<QuickFilter>('');
  const [visible, setVisible] = useState<Record<ColumnKey, boolean>>(() => {
    const initial = {} as Record<ColumnKey, boolean>;
    for (const col of ALL_COLUMNS) initial[col.key] = col.defaultVisible !== false;
    return initial;
  });
  const [showColumns, setShowColumns] = useState(false);
  const [breakdown, setBreakdown] = useState<StrategyResearchCandidate | null>(null);
  const [riskBreakdown, setRiskBreakdown] = useState<StrategyResearchCandidate | null>(null);
  const [data, setData] = useState<PagedCandidates | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const query = useMemo<StrategyLabCandidateQuery>(
    () => ({
      page,
      pageSize,
      sortBy,
      sortDirection,
      search: search || undefined,
      direction: direction || undefined,
      rawOutcome: rawOutcome || undefined,
      confidenceDecision: confidenceDecision || undefined,
      confidenceMin: confidenceMin !== '' ? Number(confidenceMin) : undefined,
      confidenceMax: confidenceMax !== '' ? Number(confidenceMax) : undefined,
      riskDecision: riskDecision || undefined,
      riskMin: riskMin !== '' ? Number(riskMin) : undefined,
      riskMax: riskMax !== '' ? Number(riskMax) : undefined,
      profitableOnly: profitableOnly === 'profitable' ? true : profitableOnly === 'losing' ? false : undefined,
      fromUtc: fromUtc || undefined,
      toUtc: toUtc || undefined,
      quickFilter: quickFilter || undefined,
    }),
    [
      page,
      pageSize,
      sortBy,
      sortDirection,
      search,
      direction,
      rawOutcome,
      confidenceDecision,
      confidenceMin,
      confidenceMax,
      riskDecision,
      riskMin,
      riskMax,
      profitableOnly,
      fromUtc,
      toUtc,
      quickFilter,
    ],
  );

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await strategyLabApi.getCandidates(runId, query);
      setData(result);
      setError(null);
    } catch (err) {
      setError(parseApiClientError(err).message);
    } finally {
      setLoading(false);
    }
  }, [runId, query]);

  useEffect(() => {
    load();
  }, [load]);

  const columns = ALL_COLUMNS.filter((c) => visible[c.key]);

  const resetFilters = () => {
    setPage(1);
    setSearch('');
    setDirection('');
    setRawOutcome('');
    setConfidenceDecision('');
    setRiskDecision('');
    setConfidenceMin('');
    setConfidenceMax('');
    setRiskMin('');
    setRiskMax('');
    setProfitableOnly('');
    setFromUtc('');
    setToUtc('');
    setQuickFilter('');
    setSortBy('setupDetectedAtUtc');
    setSortDirection('desc');
  };

  const applyQuick = (id: QuickFilter) => {
    setQuickFilter(id);
    setPage(1);
    // Clear overlapping filters so quick filter owns the predicate.
    setRawOutcome('');
    setConfidenceDecision('');
    setRiskDecision('');
  };

  const onSort = (key: ColumnKey) => {
    if (sortBy === key) {
      setSortDirection((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortBy(key);
      setSortDirection('desc');
    }
    setPage(1);
  };

  const exportFiltered = async (format: 'csv' | 'json') => {
    const all: StrategyResearchCandidate[] = [];
    let currentPage = 1;
    let totalPages = 1;
    do {
      const pageResult = await strategyLabApi.getCandidates(runId, { ...query, page: currentPage, pageSize: 250 });
      all.push(...(pageResult?.items ?? []));
      totalPages = pageResult?.totalPages ?? 0;
      currentPage += 1;
    } while (currentPage <= totalPages);

    if (format === 'csv') {
      download(`strategy-lab-${runId}-candidates.csv`, toCsv(all, columns), 'text/csv;charset=utf-8');
    } else {
      download(`strategy-lab-${runId}-candidates.json`, JSON.stringify(all, null, 2), 'application/json');
    }
  };

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-2">
        {QUICK_FILTERS.map((f) => (
          <button
            key={f.id}
            type="button"
            onClick={() => applyQuick(quickFilter === f.id ? '' : f.id)}
            className={`rounded-lg border px-2.5 py-1 text-xs ${
              quickFilter === f.id ? 'border-sky-600 bg-sky-950/40 text-sky-100' : 'border-slate-700 text-slate-300'
            }`}
          >
            {f.label}
          </button>
        ))}
      </div>

      <div className="grid gap-2 md:grid-cols-4 lg:grid-cols-6">
        <input
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            setPage(1);
          }}
          placeholder="Search reason / fingerprint"
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        />
        <select
          value={direction}
          onChange={(e) => {
            setDirection(e.target.value);
            setPage(1);
          }}
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        >
          <option value="">Direction</option>
          <option value="Long">Long</option>
          <option value="Short">Short</option>
        </select>
        <select
          value={rawOutcome}
          onChange={(e) => {
            setRawOutcome(e.target.value);
            setPage(1);
          }}
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        >
          <option value="">Raw Outcome</option>
          <option value="Winner">Winner</option>
          <option value="Loser">Loser</option>
          <option value="Breakeven">Breakeven</option>
          <option value="Open">Open</option>
          <option value="Invalid">Invalid</option>
        </select>
        <select
          value={confidenceDecision}
          onChange={(e) => {
            setConfidenceDecision(e.target.value);
            setPage(1);
          }}
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        >
          <option value="">Confidence Decision</option>
          <option value="Approved">Approved</option>
          <option value="Rejected">Rejected</option>
          <option value="NotEvaluated">NotEvaluated</option>
        </select>
        <select
          value={riskDecision}
          onChange={(e) => {
            setRiskDecision(e.target.value);
            setPage(1);
          }}
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        >
          <option value="">Risk Decision</option>
          <option value="Approved">Approved</option>
          <option value="Rejected">Rejected</option>
          <option value="NotEvaluated">NotEvaluated</option>
        </select>
        <select
          value={profitableOnly}
          onChange={(e) => {
            setProfitableOnly(e.target.value);
            setPage(1);
          }}
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        >
          <option value="">PnL: all</option>
          <option value="profitable">profitable</option>
          <option value="losing">losing</option>
        </select>
        <input
          type="number"
          value={confidenceMin}
          onChange={(e) => {
            setConfidenceMin(e.target.value);
            setPage(1);
          }}
          placeholder="Conf min"
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        />
        <input
          type="number"
          value={confidenceMax}
          onChange={(e) => {
            setConfidenceMax(e.target.value);
            setPage(1);
          }}
          placeholder="Conf max"
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        />
        <input
          type="number"
          value={riskMin}
          onChange={(e) => {
            setRiskMin(e.target.value);
            setPage(1);
          }}
          placeholder="Risk min"
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        />
        <input
          type="number"
          value={riskMax}
          onChange={(e) => {
            setRiskMax(e.target.value);
            setPage(1);
          }}
          placeholder="Risk max"
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        />
        <input
          type="datetime-local"
          value={fromUtc}
          onChange={(e) => {
            setFromUtc(e.target.value ? new Date(e.target.value).toISOString() : '');
            setPage(1);
          }}
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        />
        <input
          type="datetime-local"
          value={toUtc}
          onChange={(e) => {
            setToUtc(e.target.value ? new Date(e.target.value).toISOString() : '');
            setPage(1);
          }}
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        />
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <select
          value={pageSize}
          onChange={(e) => {
            setPageSize(Number(e.target.value));
            setPage(1);
          }}
          className="rounded-lg border border-slate-700 bg-slate-950 px-2 py-1 text-sm"
        >
          {[25, 50, 100, 250].map((n) => (
            <option key={n} value={n}>
              {n} / page
            </option>
          ))}
        </select>
        <button type="button" onClick={resetFilters} className="rounded-lg border border-slate-700 px-3 py-1 text-sm">
          Reset filters
        </button>
        <button type="button" onClick={() => setShowColumns((v) => !v)} className="rounded-lg border border-slate-700 px-3 py-1 text-sm">
          Columns
        </button>
        <button type="button" onClick={() => exportFiltered('csv')} className="rounded-lg border border-slate-700 px-3 py-1 text-sm">
          Export CSV
        </button>
        <button type="button" onClick={() => exportFiltered('json')} className="rounded-lg border border-slate-700 px-3 py-1 text-sm">
          Export JSON
        </button>
        <span className="text-sm text-slate-400">
          {data ? `${data.totalItems} candidates` : '—'}
        </span>
      </div>

      {showColumns ? (
        <div className="grid gap-1 rounded-lg border border-slate-800 p-3 md:grid-cols-3 lg:grid-cols-4">
          {ALL_COLUMNS.map((col) => (
            <label key={col.key} className="flex items-center gap-2 text-xs text-slate-300">
              <input
                type="checkbox"
                checked={visible[col.key]}
                onChange={(e) => setVisible((prev) => ({ ...prev, [col.key]: e.target.checked }))}
              />
              {col.header}
            </label>
          ))}
        </div>
      ) : null}

      {error ? <div className="text-sm text-rose-300">{error}</div> : null}

      <div className="max-h-[70vh] overflow-auto rounded-xl border border-slate-800">
        <table className="min-w-full divide-y divide-slate-800 text-sm">
          <thead className="sticky top-0 z-10 bg-slate-900/95 backdrop-blur">
            <tr>
              {columns.map((col) => (
                <th
                  key={col.key}
                  className="cursor-pointer whitespace-nowrap px-3 py-2 text-left font-medium text-slate-400"
                  onClick={() => onSort(col.key)}
                >
                  {col.header}
                  {sortBy === col.key ? (sortDirection === 'asc' ? ' ↑' : ' ↓') : ''}
                </th>
              ))}
              <th className="whitespace-nowrap px-3 py-2 text-left font-medium text-slate-400">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-800 bg-slate-950/40">
            {(data?.items ?? []).map((row) => (
              <tr key={row.id} className="hover:bg-slate-900/40">
                {columns.map((col) => (
                  <td key={col.key} className="whitespace-nowrap px-3 py-2 text-slate-200">
                    {renderCell(col.key, row)}
                  </td>
                ))}
                <td className="whitespace-nowrap px-3 py-2">
                  <div className="flex flex-col gap-1">
                    <button
                      type="button"
                      className="text-xs text-sky-300 hover:underline disabled:opacity-40"
                      onClick={() => setBreakdown(row)}
                      disabled={!row.confidenceComponentsJson}
                    >
                      View Score Breakdown
                    </button>
                    <button
                      type="button"
                      className="text-xs text-amber-300 hover:underline disabled:opacity-40"
                      onClick={() => setRiskBreakdown(row)}
                      disabled={
                        !row.riskComponentsJson
                        && !row.riskRuleResultsJson
                        && row.riskDecision === 'NotEvaluated'
                      }
                    >
                      View Risk Breakdown
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {!loading && (data?.items?.length ?? 0) === 0 ? (
          <div className="p-6 text-sm text-slate-400">No candidates match the current filters.</div>
        ) : null}
        {loading ? <div className="p-4 text-sm text-slate-400">Loading candidates…</div> : null}
      </div>

      <div className="flex items-center justify-between text-sm text-slate-300">
        <button
          type="button"
          disabled={!data || page <= 1}
          onClick={() => setPage((p) => Math.max(1, p - 1))}
          className="rounded-lg border border-slate-700 px-3 py-1 disabled:opacity-40"
        >
          Previous
        </button>
        <span>
          Page {data?.page ?? page} of {data?.totalPages || 1}
        </span>
        <button
          type="button"
          disabled={!data || page >= (data.totalPages || 1)}
          onClick={() => setPage((p) => p + 1)}
          className="rounded-lg border border-slate-700 px-3 py-1 disabled:opacity-40"
        >
          Next
        </button>
      </div>

      {breakdown ? (
        <ScoreBreakdownModal candidate={breakdown} onClose={() => setBreakdown(null)} />
      ) : null}
      {riskBreakdown ? (
        <RiskBreakdownModal candidate={riskBreakdown} onClose={() => setRiskBreakdown(null)} />
      ) : null}
    </div>
  );
}

function RiskBreakdownModal({
  candidate,
  onClose,
}: {
  candidate: StrategyResearchCandidate;
  onClose: () => void;
}) {
  let components: Record<string, { score?: number; max?: number; reason?: string; label?: string }> = {};
  let rules: Array<{ ruleKey?: string; ruleName?: string; category?: string; status?: string; severity?: string; reason?: string }> = [];
  try {
    const parsed = candidate.riskComponentsJson ? JSON.parse(candidate.riskComponentsJson) : [];
    if (Array.isArray(parsed)) {
      components = Object.fromEntries(parsed.map((c: { key: string; label?: string; score?: number; max?: number; reason?: string }) => [c.key, c]));
    } else {
      components = parsed;
    }
  } catch {
    components = {};
  }
  try {
    rules = candidate.riskRuleResultsJson ? JSON.parse(candidate.riskRuleResultsJson) : [];
  } catch {
    rules = [];
  }

  const financial = rules.filter((r) => r.category !== 'Policy' && r.category !== 'Eligibility');
  const policy = rules.filter((r) => r.category === 'Policy' || r.category === 'Eligibility');
  const hardRules = financial.filter((r) => r.severity === 'Hard' || r.category === 'HardRule' || r.category === 'Financial');
  const portfolioEvaluated = candidate.portfolioRiskAssessmentStatus === 'Evaluated';

  const geometryRows: Array<{ label: string; value: string }> = [
    { label: 'Position notional', value: candidate.positionNotional != null ? formatNumber(candidate.positionNotional) : '—' },
    { label: 'Initial margin required', value: candidate.initialMarginRequired != null ? formatNumber(candidate.initialMarginRequired) : '—' },
    { label: 'Risk at stop %', value: candidate.riskAtStopPercent != null ? formatNumber(candidate.riskAtStopPercent) : '—' },
    { label: 'Stop distance %', value: candidate.stopDistancePercent != null ? formatNumber(candidate.stopDistancePercent) : '—' },
    { label: 'Notional exposure %', value: candidate.notionalExposurePercent != null ? formatNumber(candidate.notionalExposurePercent) : '—' },
    { label: 'Margin usage %', value: candidate.marginUsagePercent != null ? formatNumber(candidate.marginUsagePercent) : '—' },
    { label: 'Min required leverage', value: (candidate.minimumRequiredLeverage ?? candidate.proposedLeverage) != null ? formatNumber(candidate.minimumRequiredLeverage ?? candidate.proposedLeverage) : '—' },
    { label: 'Assessment leverage', value: candidate.assessmentLeverage != null ? formatNumber(candidate.assessmentLeverage) : '—' },
    { label: 'Preferred / max leverage', value: `${candidate.preferredLeverage != null ? formatNumber(candidate.preferredLeverage) : '—'} / ${candidate.maxLeverage != null ? formatNumber(candidate.maxLeverage) : '—'}` },
    { label: 'Estimated round-trip fees', value: candidate.estimatedRoundTripFees != null ? formatNumber(candidate.estimatedRoundTripFees) : '—' },
    { label: 'Fee / target %', value: candidate.feeToTargetPercent != null ? formatNumber(candidate.feeToTargetPercent) : '—' },
  ];

  const portfolioRows: Array<{ label: string; value: string }> = [
    { label: 'Current notional exposure %', value: portfolioEvaluated && candidate.currentNotionalExposurePercent != null ? formatNumber(candidate.currentNotionalExposurePercent) : '—' },
    { label: 'Current margin usage %', value: portfolioEvaluated && candidate.currentMarginUsagePercent != null ? formatNumber(candidate.currentMarginUsagePercent) : '—' },
    { label: 'Concurrent risk %', value: portfolioEvaluated && candidate.concurrentRiskPercent != null ? formatNumber(candidate.concurrentRiskPercent) : '—' },
    { label: 'Open positions', value: portfolioEvaluated && candidate.concurrentPositionCount != null ? String(candidate.concurrentPositionCount) : '—' },
    { label: 'Daily loss usage %', value: portfolioEvaluated && candidate.dailyLossUsagePercent != null ? formatNumber(candidate.dailyLossUsagePercent) : '—' },
    { label: 'Current drawdown %', value: portfolioEvaluated && candidate.currentDrawdownPercent != null ? formatNumber(candidate.currentDrawdownPercent) : '—' },
    { label: 'Drawdown mode', value: candidate.drawdownCalculationMode ?? '—' },
    { label: 'Portfolio risk score', value: portfolioEvaluated && candidate.portfolioRiskScore != null ? formatNumber(candidate.portfolioRiskScore) : '—' },
  ];

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4">
      <div className="max-h-[85vh] w-full max-w-3xl overflow-auto rounded-xl border border-slate-700 bg-slate-950 p-4">
        <div className="mb-3 flex items-start justify-between gap-3">
          <div>
            <h3 className="text-sm font-semibold text-slate-100">Risk Breakdown</h3>
            <div className="text-xs text-slate-400">
              Model: {candidate.riskModelVersion ?? '—'} · Assessment: {candidate.riskAssessmentVersion ?? '—'}
            </div>
          </div>
          <button type="button" onClick={onClose} className="rounded border border-slate-700 px-2 py-1 text-xs">
            Close
          </button>
        </div>

        <section className="mb-4">
          <h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Candidate Financial Geometry</h4>
          <div className="grid gap-1 text-sm md:grid-cols-2">
            {geometryRows.map((row) => (
              <div key={row.label} className="flex justify-between gap-2 border-b border-slate-800/60 py-1">
                <span className="text-slate-400">{row.label}</span>
                <span className="text-slate-200">{row.value}</span>
              </div>
            ))}
          </div>
        </section>

        <section className="mb-4">
          <h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Risk Score</h4>
          <div className="mb-2 text-sm text-slate-300">
            Score: {candidate.candidateRiskScore ?? candidate.riskScore ?? '—'} / 100 · Threshold:{' '}
            {candidate.riskThreshold ?? '—'} · Decision: {candidate.riskScoreDecision ?? candidate.riskDecision ?? '—'}
          </div>
          {candidate.riskReason ? <p className="mb-2 text-xs text-slate-500">{candidate.riskReason}</p> : null}
          <table className="min-w-full divide-y divide-slate-800 text-sm">
            <thead>
              <tr className="text-left text-slate-400">
                <th className="px-2 py-1">Component</th>
                <th className="px-2 py-1">Score</th>
                <th className="px-2 py-1">Max</th>
                <th className="px-2 py-1">Reason</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-800 text-slate-200">
              {Object.entries(components).map(([key, value]) => (
                <tr key={key}>
                  <td className="px-2 py-1">{value.label ?? key}</td>
                  <td className="px-2 py-1">{value.score ?? '—'}</td>
                  <td className="px-2 py-1">{value.max ?? '—'}</td>
                  <td className="px-2 py-1 text-slate-400">{value.reason ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>

        <section className="mb-4">
          <h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Hard Rule Compliance</h4>
          <div className="mb-2 text-sm text-slate-300">
            {candidate.hardRuleComplianceDecision ?? '—'}
            {candidate.riskFailedRuleKeysJson ? ` · Failed: ${candidate.riskFailedRuleKeysJson}` : ''}
          </div>
          <ul className="space-y-1 text-sm text-slate-300">
            {(hardRules.length > 0 ? hardRules : financial).map((r) => (
              <li key={`${r.ruleKey}-${r.status}`}>
                {r.status === 'Passed' ? '✓' : r.status === 'Failed' ? '✗' : r.status === 'Warning' ? '⚠' : '·'}{' '}
                {r.ruleName ?? r.ruleKey}: {r.reason ?? '—'}
              </li>
            ))}
          </ul>
        </section>

        <section className="mb-4">
          <h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Portfolio State At Entry (Risk-Only generic)</h4>
          <p className="mb-2 text-xs text-slate-500">
            Generic fields follow Risk-Only for IndependentPaths/v1. Prefer path-specific sections below.
          </p>
          <div className="grid gap-1 text-sm md:grid-cols-2">
            {portfolioRows.map((row) => (
              <div key={row.label} className="flex justify-between gap-2 border-b border-slate-800/60 py-1">
                <span className="text-slate-400">{row.label}</span>
                <span className="text-slate-200">{row.value}</span>
              </div>
            ))}
          </div>
        </section>

        <section className="mb-4">
          <h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Risk-Only Assessment</h4>
          <div className="grid gap-1 text-sm md:grid-cols-2">
            {[
              { label: 'Entry decision', value: candidate.riskOnlyEntryDecision ?? 'Legacy/Unavailable' },
              { label: 'Financial risk', value: candidate.riskOnlyFinancialRiskDecision ?? '—' },
              { label: 'Drawdown %', value: candidate.riskOnlyCurrentDrawdownPercent != null ? formatNumber(candidate.riskOnlyCurrentDrawdownPercent) : '—' },
              { label: 'Daily loss %', value: candidate.riskOnlyDailyLossUsagePercent != null ? formatNumber(candidate.riskOnlyDailyLossUsagePercent) : '—' },
              { label: 'Margin %', value: candidate.riskOnlyCurrentMarginUsagePercent != null ? formatNumber(candidate.riskOnlyCurrentMarginUsagePercent) : '—' },
              { label: 'Concurrent risk %', value: candidate.riskOnlyConcurrentRiskPercent != null ? formatNumber(candidate.riskOnlyConcurrentRiskPercent) : '—' },
              { label: 'Open positions', value: candidate.riskOnlyOpenPositionCount != null ? String(candidate.riskOnlyOpenPositionCount) : '—' },
              { label: 'Balance', value: candidate.riskOnlyAssessment?.assessmentBalance != null ? formatNumber(candidate.riskOnlyAssessment.assessmentBalance) : '—' },
              { label: 'Quantity', value: candidate.riskOnlyAssessment?.quantity != null ? formatNumber(candidate.riskOnlyAssessment.quantity) : '—' },
              { label: 'Reason', value: candidate.riskOnlyAssessment?.entryDecisionReason ?? candidate.riskOnlyAssessment?.riskReason ?? '—' },
            ].map((row) => (
              <div key={`ro-${row.label}`} className="flex justify-between gap-2 border-b border-slate-800/60 py-1">
                <span className="text-slate-400">{row.label}</span>
                <span className="text-slate-200">{row.value}</span>
              </div>
            ))}
          </div>
        </section>

        <section className="mb-4">
          <h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Full-Pipeline Assessment</h4>
          <div className="grid gap-1 text-sm md:grid-cols-2">
            {[
              { label: 'Entry decision', value: candidate.fullPipelineEntryDecision ?? 'Legacy/Unavailable' },
              { label: 'Financial risk', value: candidate.fullPipelineFinancialRiskDecision ?? '—' },
              { label: 'Final pipeline', value: candidate.finalPipelineDecision ?? '—' },
              { label: 'Drawdown %', value: candidate.fullPipelineCurrentDrawdownPercent != null ? formatNumber(candidate.fullPipelineCurrentDrawdownPercent) : '—' },
              { label: 'Daily loss %', value: candidate.fullPipelineDailyLossUsagePercent != null ? formatNumber(candidate.fullPipelineDailyLossUsagePercent) : '—' },
              { label: 'Margin %', value: candidate.fullPipelineCurrentMarginUsagePercent != null ? formatNumber(candidate.fullPipelineCurrentMarginUsagePercent) : '—' },
              { label: 'Concurrent risk %', value: candidate.fullPipelineConcurrentRiskPercent != null ? formatNumber(candidate.fullPipelineConcurrentRiskPercent) : '—' },
              { label: 'Open positions', value: candidate.fullPipelineOpenPositionCount != null ? String(candidate.fullPipelineOpenPositionCount) : '—' },
              { label: 'Balance', value: candidate.fullPipelineAssessment?.assessmentBalance != null ? formatNumber(candidate.fullPipelineAssessment.assessmentBalance) : '—' },
              { label: 'Quantity', value: candidate.fullPipelineAssessment?.quantity != null ? formatNumber(candidate.fullPipelineAssessment.quantity) : '—' },
              { label: 'Reason', value: candidate.fullPipelineAssessment?.entryDecisionReason ?? candidate.fullPipelineAssessment?.riskReason ?? '—' },
            ].map((row) => (
              <div key={`fp-${row.label}`} className="flex justify-between gap-2 border-b border-slate-800/60 py-1">
                <span className="text-slate-400">{row.label}</span>
                <span className="text-right text-slate-200">{row.value}</span>
              </div>
            ))}
          </div>
        </section>

        <section>
          <h4 className="mb-2 text-xs font-semibold uppercase tracking-wide text-amber-400">Policy Eligibility</h4>
          <div className="mb-2 text-sm text-slate-300">
            {candidate.riskPolicyEligibilityDecision ?? '—'} — {candidate.riskPolicyReason ?? '—'}
          </div>
          {candidate.riskPolicyMinimumConfidence != null ? (
            <div className="mb-2 text-xs text-slate-500">
              Minimum confidence from profile: {formatNumber(candidate.riskPolicyMinimumConfidence)}
            </div>
          ) : null}
          {(candidate.riskProfileName || candidate.riskProfileSource) ? (
            <div className="mb-2 text-xs text-slate-500">
              Profile: {candidate.riskProfileName ?? '—'} ({candidate.riskProfileSource ?? '—'})
            </div>
          ) : null}
          <ul className="space-y-1 text-sm text-slate-300">
            {policy.map((r) => (
              <li key={`${r.ruleKey}-${r.status}`}>
                {r.status === 'Passed' ? '✓' : r.status === 'Failed' ? '✗' : '·'} {r.ruleName ?? r.ruleKey}: {r.reason ?? '—'}
              </li>
            ))}
          </ul>
        </section>
      </div>
    </div>
  );
}

function ScoreBreakdownModal({
  candidate,
  onClose,
}: {
  candidate: StrategyResearchCandidate;
  onClose: () => void;
}) {
  let components: Record<string, { score?: number; max?: number; reason?: string; label?: string }> = {};
  try {
    components = candidate.confidenceComponentsJson
      ? JSON.parse(candidate.confidenceComponentsJson)
      : {};
  } catch {
    components = {};
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4">
      <div className="max-h-[80vh] w-full max-w-2xl overflow-auto rounded-xl border border-slate-700 bg-slate-950 p-4">
        <div className="mb-3 flex items-start justify-between gap-3">
          <div>
            <h3 className="text-sm font-semibold text-slate-100">Confidence Score Breakdown</h3>
            <div className="text-xs text-slate-400">
              Total: {candidate.confidenceScore ?? '—'} · Model: {candidate.confidenceModelVersion ?? '—'}
            </div>
          </div>
          <button type="button" onClick={onClose} className="rounded border border-slate-700 px-2 py-1 text-xs">
            Close
          </button>
        </div>
        <table className="min-w-full divide-y divide-slate-800 text-sm">
          <thead>
            <tr className="text-left text-slate-400">
              <th className="px-2 py-1">Component</th>
              <th className="px-2 py-1">Score</th>
              <th className="px-2 py-1">Max</th>
              <th className="px-2 py-1">Reason</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-800 text-slate-200">
            {Object.entries(components).map(([key, value]) => (
              <tr key={key}>
                <td className="px-2 py-1">{value.label ?? key}</td>
                <td className="px-2 py-1">{value.score ?? '—'}</td>
                <td className="px-2 py-1">{value.max ?? '—'}</td>
                <td className="px-2 py-1 text-slate-400">{value.reason ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
