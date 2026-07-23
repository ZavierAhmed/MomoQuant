import { useEffect, useMemo } from 'react';
import type { Strategy } from '@/api/domainTypes';
import { CheckboxField, MultiSelectField } from '@/components/forms/fields';
import { timeframeLabel } from '@/constants/timeframes';
import {
  formatTimeframeList,
  getCommonCustomTimeframes,
  getStrategyAllowedTimeframes,
  getStrategyPreferredTimeframe,
  getStrategyRequiredDataTimeframes,
  type TimeframeMode,
} from '@/utils/strategyTimeframes';

export type { TimeframeMode };

type Props = {
  strategies: Strategy[];
  timeframeMode: TimeframeMode;
  customTimeframes: string[];
  onTimeframeModeChange: (mode: TimeframeMode) => void;
  onCustomTimeframesChange: (timeframes: string[]) => void;
  disabled?: boolean;
  error?: string;
  multiStrategySummary?: boolean;
};

export function StrategyAwareTimeframeSelector({
  strategies,
  timeframeMode,
  customTimeframes,
  onTimeframeModeChange,
  onCustomTimeframesChange,
  error,
  multiStrategySummary = true,
}: Props) {
  const singleStrategy = strategies.length === 1 ? strategies[0] : undefined;
  const commonCustomOptions = useMemo(() => getCommonCustomTimeframes(strategies), [strategies]);
  const allowedOptions = useMemo(() => {
    if (strategies.length === 1) {
      return getStrategyAllowedTimeframes(singleStrategy).map((value) => ({
        value,
        label: timeframeLabel(value),
      }));
    }
    return commonCustomOptions.map((value) => ({
      value,
      label: timeframeLabel(value),
    }));
  }, [strategies, singleStrategy, commonCustomOptions]);

  const executionTimeframes = useMemo(() => {
    if (timeframeMode === 'Custom') {
      return customTimeframes;
    }
    return strategies.map((strategy) => getStrategyPreferredTimeframe(strategy));
  }, [strategies, timeframeMode, customTimeframes]);

  useEffect(() => {
    if (timeframeMode !== 'Custom' || strategies.length !== 1) return;
    const preferred = getStrategyPreferredTimeframe(singleStrategy);
    if (customTimeframes.length === 0) {
      onCustomTimeframesChange([preferred]);
    }
  }, [timeframeMode, strategies, singleStrategy, customTimeframes, onCustomTimeframesChange]);

  useEffect(() => {
    if (timeframeMode !== 'StrategyDefault' || strategies.length !== 1) return;
    onCustomTimeframesChange([getStrategyPreferredTimeframe(singleStrategy)]);
  }, [timeframeMode, strategies, singleStrategy, onCustomTimeframesChange]);

  const defaultHelper =
    strategies.length > 1
      ? 'Each strategy will use its recommended timeframe.'
      : 'Using the recommended timeframe for the selected strategy.';

  const customCompatibilityError =
    timeframeMode === 'Custom' && strategies.length > 1 && commonCustomOptions.length === 0
      ? 'Selected strategies do not share a common custom timeframe. Use Strategy defaults or change strategy selection.'
      : null;

  const requiredDataSummary = singleStrategy
    ? formatTimeframeList(getStrategyRequiredDataTimeframes(singleStrategy, executionTimeframes[0]))
    : null;

  return (
    <div className="space-y-3 md:col-span-2 xl:col-span-3">
      <div>
        <p className="text-sm font-medium text-slate-300">Timeframe</p>
        <p className="mt-1 text-xs text-slate-500">
          {timeframeMode === 'StrategyDefault' ? defaultHelper : 'Only timeframes supported by the selected strategy are available.'}
        </p>
      </div>

      <CheckboxField
        label="Use custom timeframe"
        checked={timeframeMode === 'Custom'}
        onChange={(checked) => onTimeframeModeChange(checked ? 'Custom' : 'StrategyDefault')}
        hint={strategies.length === 0 ? 'Select a strategy first.' : undefined}
      />

      {timeframeMode === 'Custom' ? (
        <MultiSelectField
          label="Custom execution timeframe(s)"
          values={customTimeframes}
          onChange={onCustomTimeframesChange}
          options={allowedOptions}
          required
          error={error ?? customCompatibilityError ?? undefined}
          emptyMessage="No compatible custom timeframes for the selected strategies."
        />
      ) : (
        <div className="rounded-lg border border-slate-800 bg-slate-950/60 p-3 text-sm text-slate-300">
          {strategies.length === 0 ? (
            <p>Select a strategy to see timeframe defaults.</p>
          ) : strategies.length === 1 ? (
            <>
              <p>
                Execution timeframe: <span className="font-medium text-slate-100">{timeframeLabel(executionTimeframes[0])}</span>
              </p>
              {requiredDataSummary ? (
                <p className="mt-1 text-xs text-slate-500">Required data: {requiredDataSummary}</p>
              ) : null}
            </>
          ) : multiStrategySummary ? (
            <ul className="space-y-1">
              {strategies.map((strategy) => (
                <li key={strategy.id}>
                  {strategy.name}: {timeframeLabel(getStrategyPreferredTimeframe(strategy))}
                </li>
              ))}
            </ul>
          ) : (
            <p>Strategy defaults enabled.</p>
          )}
        </div>
      )}

      {timeframeMode === 'Custom' && singleStrategy && executionTimeframes[0] ? (
        <p className="text-xs text-slate-500">
          Required data: {formatTimeframeList(getStrategyRequiredDataTimeframes(singleStrategy, executionTimeframes[0]))}
        </p>
      ) : null}
    </div>
  );
}

export function getResolvedTimeframesForForm(
  strategies: Strategy[],
  timeframeMode: TimeframeMode,
  customTimeframes: string[],
): string[] {
  if (strategies.length === 0) return [];
  if (timeframeMode === 'Custom') {
    return customTimeframes;
  }
  if (strategies.length === 1) {
    return [getStrategyPreferredTimeframe(strategies[0])];
  }
  return Array.from(new Set(strategies.map((strategy) => getStrategyPreferredTimeframe(strategy))));
}
