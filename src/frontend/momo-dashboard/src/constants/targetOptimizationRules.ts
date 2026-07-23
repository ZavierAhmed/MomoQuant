export type TargetRulesPreset = 'Conservative' | 'Balanced' | 'AggressiveResearch' | 'Custom';

export interface TargetOptimizationRules {
  minTrainingNetPnlPercent: number;
  minValidationNetPnlPercent: number;
  minTrainingProfitFactor: number;
  minValidationProfitFactor: number;
  maxTrainingDrawdownPercent: number;
  maxValidationDrawdownPercent: number;
  minTrainingTrades: number;
  minValidationTrades: number;
  maxValidationPnLDropPercent: number;
  maxValidationProfitFactorDropPercent: number;
  minRobustnessScore: number;
  allowSaveIfValidationWarning: boolean;
  autoApproveIfPassed: boolean;
}

export const DEFAULT_TARGET_RULES: TargetOptimizationRules = {
  minTrainingNetPnlPercent: 2.0,
  minValidationNetPnlPercent: 0.5,
  minTrainingProfitFactor: 1.2,
  minValidationProfitFactor: 1.1,
  maxTrainingDrawdownPercent: 10.0,
  maxValidationDrawdownPercent: 8.0,
  minTrainingTrades: 20,
  minValidationTrades: 10,
  maxValidationPnLDropPercent: 70,
  maxValidationProfitFactorDropPercent: 50,
  minRobustnessScore: 60,
  allowSaveIfValidationWarning: false,
  autoApproveIfPassed: false,
};

export const TARGET_RULES_PRESETS: Record<Exclude<TargetRulesPreset, 'Custom'>, TargetOptimizationRules> = {
  Conservative: {
    ...DEFAULT_TARGET_RULES,
    minTrainingNetPnlPercent: 3.0,
    minValidationNetPnlPercent: 1.0,
    minTrainingProfitFactor: 1.4,
    minValidationProfitFactor: 1.25,
    maxTrainingDrawdownPercent: 8.0,
    maxValidationDrawdownPercent: 6.0,
    minTrainingTrades: 30,
    minValidationTrades: 15,
    minRobustnessScore: 70,
  },
  Balanced: DEFAULT_TARGET_RULES,
  AggressiveResearch: {
    ...DEFAULT_TARGET_RULES,
    minTrainingNetPnlPercent: 1.0,
    minValidationNetPnlPercent: 0.0,
    minTrainingProfitFactor: 1.1,
    minValidationProfitFactor: 1.0,
    maxTrainingDrawdownPercent: 15.0,
    maxValidationDrawdownPercent: 12.0,
    minTrainingTrades: 10,
    minValidationTrades: 5,
    minRobustnessScore: 45,
    allowSaveIfValidationWarning: false,
    autoApproveIfPassed: false,
  },
};

export const TARGET_RULES_PRESET_OPTIONS = [
  { value: 'Conservative', label: 'Conservative — lower drawdown, higher profit factor, more trades' },
  { value: 'Balanced', label: 'Balanced — default research targets' },
  { value: 'AggressiveResearch', label: 'Aggressive Research — looser validation (research only)' },
  { value: 'Custom', label: 'Custom — edit all target fields' },
] as const;

export const PARAMETER_SEARCH_MODE_OPTIONS = [
  { value: 'GridSearch', label: 'Grid Search' },
  { value: 'RandomSearch', label: 'Random Search' },
  { value: 'Hybrid', label: 'Hybrid (grid + random)' },
] as const;

export type ParameterSearchMode = (typeof PARAMETER_SEARCH_MODE_OPTIONS)[number]['value'];
