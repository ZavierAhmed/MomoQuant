import type { StrategyParameterSet } from '@/api/strategyResearchApi';

export function parameterSetApprovalLabel(set: StrategyParameterSet): string {
  return set.isApproved ? 'Research approved' : 'Not research approved';
}

export function parameterSetQualificationLabel(set: StrategyParameterSet): string {
  switch (set.qualificationStatus) {
    case 'HistoricalNotEvaluated':
      return 'Historical — not evaluated';
    case 'DeploymentQualified':
      return 'Deployment qualified';
    default:
      return 'Research-only';
  }
}

export function parameterSetQualificationExplanation(set: StrategyParameterSet): string {
  if (set.isDeploymentQualified) {
    return 'Deployment qualification is recorded.';
  }

  if (set.qualificationBlockingReasons.includes('PARAMETER_SET_HISTORICAL_NOT_EVALUATED')) {
    return 'Not deployment qualified: this historical parameter set has not been evaluated by the qualification workflow.';
  }

  return 'Not deployment qualified: this parameter set is available only for controlled research.';
}
