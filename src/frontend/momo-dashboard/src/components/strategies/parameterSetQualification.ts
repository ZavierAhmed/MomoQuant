import type { StrategyParameterSet } from '@/api/strategyResearchApi';

export const RESEARCH_PAPER_EXPLANATION =
  'Research paper trading accepts research-approved configurations and is used for experimentation.';

export const DEPLOYMENT_SIMULATION_EXPLANATION =
  'Deployment simulation requires a Validation Lab deployment-qualified configuration and rechecks its evidence before every start or resume. It still places no real orders.';

export type DeploymentPaperSelection = {
  mode: string;
  strategyCount: number;
  symbolCount: number;
  timeframeCount: number;
  parameterSetId: number | '';
};

export function deploymentPaperSelectionErrors(selection: DeploymentPaperSelection): Record<string, string> {
  const errors: Record<string, string> = {};
  if (selection.mode !== 'LivePaper') errors.mode = 'Deployment simulation requires LivePaper.';
  if (selection.strategyCount !== 1) errors.strategyIds = 'Deployment simulation requires exactly one strategy.';
  if (selection.symbolCount !== 1) errors.symbolIds = 'Deployment simulation requires exactly one symbol.';
  if (selection.timeframeCount !== 1) errors.timeframes = 'Deployment simulation requires exactly one timeframe.';
  if (selection.parameterSetId === '') errors.parameterSetId = 'Select a deployment-qualified parameter set.';
  return errors;
}

export function isDeploymentPaperSelectionComplete(selection: DeploymentPaperSelection): boolean {
  return Object.keys(deploymentPaperSelectionErrors(selection)).length === 0;
}

export function filterDeploymentQualifiedParameterSets(
  sets: StrategyParameterSet[],
  deploymentQualifiedOnly: boolean,
): StrategyParameterSet[] {
  return deploymentQualifiedOnly ? sets.filter((set) => set.isDeploymentQualified) : sets;
}

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
