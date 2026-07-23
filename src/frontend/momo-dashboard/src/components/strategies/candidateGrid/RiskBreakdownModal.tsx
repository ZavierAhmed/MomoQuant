import { formatNumber } from '@/components/common/utils';
import type { StrategyResearchCandidate } from '@/api/strategyLabApi';

export function RiskBreakdownModal({
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
