import { useMemo } from 'react';
import { SelectField } from '@/components/forms/fields';
import { useAsync } from '@/hooks/useAsync';
import { strategyResearchApi } from '@/api/strategyResearchApi';
import { formatDate } from '@/components/common/utils';

type Props = {
  strategyCode?: string;
  symbolId?: number;
  timeframe?: string;
  selectedParameterSetId: number | '';
  onChange: (parameterSetId: number | '') => void;
  disabled?: boolean;
  requiredForLivePaper?: boolean;
  error?: string;
  onRunValidation?: () => void;
  onRunOptimization?: () => void;
};

export function StrategyParameterSetSelector({
  strategyCode,
  symbolId,
  timeframe,
  selectedParameterSetId,
  onChange,
  disabled,
  requiredForLivePaper,
  error,
  onRunValidation,
  onRunOptimization,
}: Props) {
  const parameterSets = useAsync(
    () =>
      strategyCode
        ? strategyResearchApi.listParameterSets({ strategyCode, symbolId, timeframe })
        : Promise.resolve([]),
    [strategyCode, symbolId, timeframe],
  );

  const options = useMemo(() => {
    const sets = [...(parameterSets.data ?? [])].sort((left, right) => {
      if (left.isApproved !== right.isApproved) {
        return Number(right.isApproved) - Number(left.isApproved);
      }
      return new Date(right.createdAtUtc).getTime() - new Date(left.createdAtUtc).getTime();
    });

    return sets.map((set) => ({
      value: set.id,
      label: `${set.name}${set.isApproved ? ' — Approved' : ''}${
        set.robustnessScore != null ? ` — Robustness ${set.robustnessScore.toFixed(1)}` : ''
      }`,
    }));
  }, [parameterSets.data]);

  if (!strategyCode) {
    return null;
  }

  const hasSavedSets = (parameterSets.data?.length ?? 0) > 0;

  return (
    <div className="space-y-2">
      <SelectField
        label="Parameter set"
        value={selectedParameterSetId}
        onChange={(value) => onChange(value === '' ? '' : Number(value))}
        options={options}
        loading={parameterSets.loading}
        disabled={disabled}
        error={error}
        hint={
          requiredForLivePaper
            ? 'LivePaper uses a frozen approved parameter set when selected. Parameters cannot change during the session.'
            : hasSavedSets
              ? 'Reuse a saved or approved parameter set, or keep strategy defaults.'
              : 'No saved parameter sets yet.'
        }
        placeholder="Use strategy defaults"
      />
      {!hasSavedSets ? (
        <div className="flex flex-wrap gap-2">
          {onRunValidation ? (
            <button
              type="button"
              onClick={onRunValidation}
              className="rounded-lg border border-slate-700 px-3 py-1.5 text-xs text-slate-200"
            >
              Run 70/30 Validation
            </button>
          ) : null}
          {onRunOptimization ? (
            <button
              type="button"
              onClick={onRunOptimization}
              className="rounded-lg border border-slate-700 px-3 py-1.5 text-xs text-slate-200"
            >
              Run Optimization
            </button>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

export function ParameterSetMeta({ parameterSetId }: { parameterSetId?: number | '' }) {
  const detail = useAsync(
    () =>
      parameterSetId
        ? strategyResearchApi.listParameterSets().then((sets) => sets.find((set) => set.id === parameterSetId) ?? null)
        : Promise.resolve(null),
    [parameterSetId],
  );

  if (!parameterSetId || !detail.data) return null;

  return (
    <p className="text-xs text-emerald-300">
      Using parameter set #{detail.data.id} ({detail.data.name}) — saved {formatDate(detail.data.createdAtUtc)}
    </p>
  );
}
