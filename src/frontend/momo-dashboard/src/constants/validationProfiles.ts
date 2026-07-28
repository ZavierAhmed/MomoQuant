export const VG_RESEARCH_PROFILE_OPTIONS = [
  { value: 'Balanced', label: 'Balanced' },
  { value: 'Conservative', label: 'Conservative' },
  { value: 'Exploratory', label: 'Exploratory (research only)' },
] as const;

export type VgResearchProfileValue = (typeof VG_RESEARCH_PROFILE_OPTIONS)[number]['value'];

/** Archived strategy code — retained for historical validation/optimization display only. */
export const VOLATILITY_GATED_SUPERTREND_CODE = 'VOLATILITY_GATED_SUPERTREND_MOMENTUM';

/** @deprecated Archived in 23.1A — only matches historical runs using the legacy VG strategy. */
export function isVgStrategy(strategyCode: string): boolean {
  return strategyCode.toUpperCase() === VOLATILITY_GATED_SUPERTREND_CODE;
}
