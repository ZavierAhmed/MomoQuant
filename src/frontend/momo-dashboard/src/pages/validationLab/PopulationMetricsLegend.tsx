import {
  POPULATION_COLUMN_LABELS,
  POPULATION_METRICS_EXPLANATION,
} from '@/pages/validationLab/validationLabDetailHelpers';

/** Compact explanation + label legend for Validation Lab layer population metrics. */
export function PopulationMetricsLegend() {
  return (
    <div
      className="rounded-lg border border-slate-800 bg-slate-950/40 px-4 py-3 text-sm text-slate-300"
      data-testid="population-metrics-legend"
    >
      <p>{POPULATION_METRICS_EXPLANATION}</p>
      <ul className="mt-2 grid gap-1 text-xs text-slate-400 sm:grid-cols-2">
        <li>{POPULATION_COLUMN_LABELS.candidates}</li>
        <li>{POPULATION_COLUMN_LABELS.pathInputsIncluded}</li>
        <li>{POPULATION_COLUMN_LABELS.pathInputsExcluded}</li>
        <li>{POPULATION_COLUMN_LABELS.closedOutcomes}</li>
        <li>{POPULATION_COLUMN_LABELS.tradesUsedForPnl}</li>
        <li>{POPULATION_COLUMN_LABELS.tradesUsedForGrossR}</li>
        <li>{POPULATION_COLUMN_LABELS.tradesUsedForNetR}</li>
        <li>{POPULATION_COLUMN_LABELS.includedWithWarnings}</li>
      </ul>
    </div>
  );
}
