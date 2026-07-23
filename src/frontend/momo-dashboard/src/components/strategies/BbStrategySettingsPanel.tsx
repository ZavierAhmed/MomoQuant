import { useState } from 'react';
import { CheckboxField, NumberField, SelectField } from '@/components/forms/fields';

export type BbStrictnessProfile = 'OriginalStrict' | 'BalancedResearch' | 'DetectorCalibration';

export type BbStrategySettingsForm = {
  bbStrategyStrictnessProfile: BbStrictnessProfile;
  useSessionFilter: boolean;
  requireSweepOutsideBb: boolean;
  requireCloseBackInsideBb: boolean;
  requireCloseBackAcrossLiquidityLine: boolean;
  maxBarsAfterSweep: number;
  minRewardRisk: number;
  swingLeft: number;
  swingRight: number;
  equalHighLowToleranceAtrMultiplier: number;
  maxLevelAgeCandles: number;
  includeSingleSwingLevels: boolean;
  maxDistanceFromLiquidityAtrMultiplier: number;
  rsiPrimedSignalValueMode: 'HaClose' | 'HaLowHigh' | 'Ohlc4';
};

export const DEFAULT_BB_STRATEGY_SETTINGS: BbStrategySettingsForm = {
  bbStrategyStrictnessProfile: 'BalancedResearch',
  useSessionFilter: true,
  requireSweepOutsideBb: true,
  requireCloseBackInsideBb: false,
  requireCloseBackAcrossLiquidityLine: false,
  maxBarsAfterSweep: 5,
  minRewardRisk: 2.5,
  swingLeft: 2,
  swingRight: 2,
  equalHighLowToleranceAtrMultiplier: 0.25,
  maxLevelAgeCandles: 300,
  includeSingleSwingLevels: true,
  maxDistanceFromLiquidityAtrMultiplier: 0.35,
  rsiPrimedSignalValueMode: 'HaClose',
};

type Props = {
  value: BbStrategySettingsForm;
  onChange: (next: BbStrategySettingsForm) => void;
  showRsiSettings?: boolean;
};

export function BbStrategySettingsPanel({ value, onChange, showRsiSettings = false }: Props) {
  const [showAdvanced, setShowAdvanced] = useState(false);

  function applyProfile(profile: BbStrictnessProfile) {
    if (profile === 'OriginalStrict') {
      onChange({
        ...value,
        bbStrategyStrictnessProfile: profile,
        useSessionFilter: true,
        requireSweepOutsideBb: true,
        requireCloseBackInsideBb: true,
        requireCloseBackAcrossLiquidityLine: true,
        minRewardRisk: 3,
      });
      return;
    }

    if (profile === 'DetectorCalibration') {
      onChange({
        ...value,
        bbStrategyStrictnessProfile: profile,
        useSessionFilter: false,
        requireSweepOutsideBb: false,
        requireCloseBackInsideBb: false,
        requireCloseBackAcrossLiquidityLine: false,
        minRewardRisk: 0,
      });
      return;
    }

    onChange({
      ...value,
      bbStrategyStrictnessProfile: profile,
      useSessionFilter: true,
      requireSweepOutsideBb: true,
      requireCloseBackInsideBb: false,
      requireCloseBackAcrossLiquidityLine: false,
      minRewardRisk: 2.5,
    });
  }

  return (
    <div className="space-y-3 rounded-lg border border-slate-800 bg-slate-950/40 p-4">
      <div>
        <h3 className="text-sm font-medium text-slate-200">BB Liquidity Sweep Settings</h3>
        <p className="text-xs text-slate-400">Tune strictness profile and #itsimpossible approximation parameters for research benchmarks.</p>
      </div>

      <SelectField
        label="Strictness profile"
        value={value.bbStrategyStrictnessProfile}
        onChange={(next) => applyProfile(next as BbStrictnessProfile)}
        options={[
          { value: 'BalancedResearch', label: 'Balanced research (recommended)' },
          { value: 'OriginalStrict', label: 'Original strict' },
          { value: 'DetectorCalibration', label: 'Detector calibration (diagnostics only)' },
        ]}
      />

      {value.bbStrategyStrictnessProfile === 'DetectorCalibration' ? (
        <p className="rounded border border-amber-700/50 bg-amber-950/30 p-2 text-xs text-amber-100">
          Detector calibration only — not a final strategy result. Use this to verify BB sweeps, liquidity levels, and CISD detectors.
        </p>
      ) : null}

      <CheckboxField
        label="Use session filter"
        checked={value.useSessionFilter}
        onChange={(checked) => onChange({ ...value, useSessionFilter: checked })}
      />

      <button
        type="button"
        className="text-xs text-slate-300 underline"
        onClick={() => setShowAdvanced((current) => !current)}
      >
        {showAdvanced ? 'Hide advanced settings' : 'Show advanced settings'}
      </button>

      {showAdvanced ? (
        <div className="grid gap-3 md:grid-cols-2">
          <CheckboxField label="Require sweep outside BB" checked={value.requireSweepOutsideBb} onChange={(checked) => onChange({ ...value, requireSweepOutsideBb: checked })} />
          <CheckboxField label="Require close back inside BB" checked={value.requireCloseBackInsideBb} onChange={(checked) => onChange({ ...value, requireCloseBackInsideBb: checked })} />
          <CheckboxField label="Require close back across liquidity" checked={value.requireCloseBackAcrossLiquidityLine} onChange={(checked) => onChange({ ...value, requireCloseBackAcrossLiquidityLine: checked })} />
          <CheckboxField label="Include single swing levels" checked={value.includeSingleSwingLevels} onChange={(checked) => onChange({ ...value, includeSingleSwingLevels: checked })} />
          <NumberField label="Max bars after sweep (CISD)" value={value.maxBarsAfterSweep} onChange={(next) => onChange({ ...value, maxBarsAfterSweep: Number(next) || 0 })} />
          <NumberField label="Min target R" value={value.minRewardRisk} onChange={(next) => onChange({ ...value, minRewardRisk: Number(next) || 0 })} />
          <NumberField label="Swing left" value={value.swingLeft} onChange={(next) => onChange({ ...value, swingLeft: Number(next) || 0 })} />
          <NumberField label="Swing right" value={value.swingRight} onChange={(next) => onChange({ ...value, swingRight: Number(next) || 0 })} />
          <NumberField label="Liquidity tolerance ATR multiplier" value={value.equalHighLowToleranceAtrMultiplier} onChange={(next) => onChange({ ...value, equalHighLowToleranceAtrMultiplier: Number(next) || 0 })} />
          <NumberField label="Level max age (candles)" value={value.maxLevelAgeCandles} onChange={(next) => onChange({ ...value, maxLevelAgeCandles: Number(next) || 0 })} />
          <NumberField label="Max distance from liquidity ATR multiplier" value={value.maxDistanceFromLiquidityAtrMultiplier} onChange={(next) => onChange({ ...value, maxDistanceFromLiquidityAtrMultiplier: Number(next) || 0 })} />
          {showRsiSettings ? (
            <SelectField
              label="RSI Primed signal mode"
              value={value.rsiPrimedSignalValueMode}
              onChange={(next) => onChange({ ...value, rsiPrimedSignalValueMode: next as BbStrategySettingsForm['rsiPrimedSignalValueMode'] })}
              options={[
                { value: 'HaClose', label: 'Heikin Ashi close' },
                { value: 'HaLowHigh', label: 'Heikin Ashi low/high' },
                { value: 'Ohlc4', label: 'OHLC4' },
              ]}
            />
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
