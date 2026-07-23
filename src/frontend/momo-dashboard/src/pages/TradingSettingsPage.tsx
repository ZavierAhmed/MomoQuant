import { useEffect, useState } from 'react';
import { PageHeader } from '@/components/common/PageHeader';
import { FormPanel } from '@/components/common/FormPanel';
import { LoadingState } from '@/components/common/LoadingState';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { CheckboxField, NumberField, SelectField } from '@/components/forms/fields';
import { FormActions } from '@/components/forms/FormActions';
import { tradingSettingsApi, type TradingSettings } from '@/api/tradingSettingsApi';
import { useRole } from '@/hooks/useRole';
import { parseApiClientError } from '@/utils/apiError';

export function TradingSettingsPage() {
  const { canEdit } = useRole();
  const [settings, setSettings] = useState<TradingSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    void load();
  }, []);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      setSettings(await tradingSettingsApi.get());
    } catch (err) {
      setError(parseApiClientError(err).message);
    } finally {
      setLoading(false);
    }
  }

  async function save() {
    if (!settings || !canEdit) return;
    setSaving(true);
    setError(null);
    try {
      setSettings(await tradingSettingsApi.update(settings));
    } catch (err) {
      setError(parseApiClientError(err).message);
    } finally {
      setSaving(false);
    }
  }

  async function resetDefaults() {
    if (!canEdit) return;
    setSaving(true);
    setError(null);
    try {
      setSettings(await tradingSettingsApi.resetDefaults());
    } catch (err) {
      setError(parseApiClientError(err).message);
    } finally {
      setSaving(false);
    }
  }

  if (loading || !settings) {
    return <LoadingState />;
  }

  return (
    <div>
      <PageHeader title="Trading Settings" description="Simulation-only trading defaults for benchmark, backtest, replay, and paper sessions." />
      <ApiErrorAlert message={error} />

      <div className="grid gap-4 xl:grid-cols-2">
        <FormPanel title="Trading Defaults">
          <div className="grid gap-3 md:grid-cols-2">
            <NumberField label="Max Leverage" value={settings.maxLeverage} onChange={(v) => setSettings((c) => (c ? { ...c, maxLeverage: Number(v || 0) } : c))} />
            <NumberField label="Default Leverage" value={settings.defaultLeverage} onChange={(v) => setSettings((c) => (c ? { ...c, defaultLeverage: Number(v || 0) } : c))} />
            <NumberField label="Max Open Positions" value={settings.maxOpenPositions} onChange={(v) => setSettings((c) => (c ? { ...c, maxOpenPositions: Number(v || 0) } : c))} />
            <NumberField label="Max Position Size (USD)" value={settings.maxPositionSizeUsd} onChange={(v) => setSettings((c) => (c ? { ...c, maxPositionSizeUsd: Number(v || 0) } : c))} />
          </div>
        </FormPanel>

        <FormPanel title="Risk Defaults">
          <div className="grid gap-3 md:grid-cols-2">
            <NumberField label="Max Risk Per Trade %" value={settings.maxRiskPerTradePercent} onChange={(v) => setSettings((c) => (c ? { ...c, maxRiskPerTradePercent: Number(v || 0) } : c))} />
            <NumberField label="Default Risk Per Trade %" value={settings.defaultRiskPerTradePercent} onChange={(v) => setSettings((c) => (c ? { ...c, defaultRiskPerTradePercent: Number(v || 0) } : c))} />
            <NumberField label="Max Daily Loss %" value={settings.maxDailyLossPercent} onChange={(v) => setSettings((c) => (c ? { ...c, maxDailyLossPercent: Number(v || 0) } : c))} />
            <NumberField label="Max Total Drawdown %" value={settings.maxTotalDrawdownPercent} onChange={(v) => setSettings((c) => (c ? { ...c, maxTotalDrawdownPercent: Number(v || 0) } : c))} />
            <NumberField label="Min Reward/Risk" value={settings.minRewardRiskRatio} onChange={(v) => setSettings((c) => (c ? { ...c, minRewardRiskRatio: Number(v || 0) } : c))} />
            <NumberField label="Default Reward/Risk" value={settings.defaultRewardRiskRatio} onChange={(v) => setSettings((c) => (c ? { ...c, defaultRewardRiskRatio: Number(v || 0) } : c))} />
          </div>
        </FormPanel>

        <FormPanel title="Execution Defaults">
          <div className="grid gap-3 md:grid-cols-2">
            <NumberField label="Maker Fee Rate" value={settings.makerFeeRate} onChange={(v) => setSettings((c) => (c ? { ...c, makerFeeRate: Number(v || 0) } : c))} />
            <NumberField label="Taker Fee Rate" value={settings.takerFeeRate} onChange={(v) => setSettings((c) => (c ? { ...c, takerFeeRate: Number(v || 0) } : c))} />
            <NumberField label="Slippage %" value={settings.slippagePercent} onChange={(v) => setSettings((c) => (c ? { ...c, slippagePercent: Number(v || 0) } : c))} />
            <NumberField label="Order Expiry Candles" value={settings.orderExpiryCandles} onChange={(v) => setSettings((c) => (c ? { ...c, orderExpiryCandles: Number(v || 0) } : c))} />
            <SelectField
              label="Same Candle Exit Policy"
              value={settings.sameCandleExitPolicy}
              onChange={(v) => setSettings((c) => (c ? { ...c, sameCandleExitPolicy: v || 'ConservativeStopFirst' } : c))}
              options={[
                { label: 'Conservative Stop First', value: 'ConservativeStopFirst' },
                { label: 'Target First', value: 'TargetFirst' },
                { label: 'OHLC Heuristic', value: 'OpenHighLowCloseHeuristic' },
              ]}
            />
          </div>
        </FormPanel>

        <FormPanel title="Benchmark / AI Defaults">
          <div className="grid gap-3 md:grid-cols-2">
            <SelectField
              label="Default Benchmark Evaluation Mode"
              value={settings.defaultBenchmarkEvaluationMode}
              onChange={(v) => setSettings((c) => (c ? { ...c, defaultBenchmarkEvaluationMode: v || 'RawStrategyResearch' } : c))}
              options={[
                { label: 'Raw Strategy Research', value: 'RawStrategyResearch' },
                { label: 'Risk Only Research', value: 'RiskOnlyResearch' },
                { label: 'Confidence Only Research', value: 'ConfidenceOnlyResearch' },
                { label: 'Full Validation', value: 'FullValidation' },
              ]}
            />
            <NumberField label="Default Benchmark Initial Balance" value={settings.defaultBenchmarkInitialBalance} onChange={(v) => setSettings((c) => (c ? { ...c, defaultBenchmarkInitialBalance: Number(v || 0) } : c))} />
            <NumberField label="Default Confidence Threshold" value={settings.defaultConfidenceThreshold} onChange={(v) => setSettings((c) => (c ? { ...c, defaultConfidenceThreshold: Number(v || 0) } : c))} />
            <CheckboxField label="Confidence Hard Gate Default" checked={settings.confidenceHardGateDefault} onChange={(v) => setSettings((c) => (c ? { ...c, confidenceHardGateDefault: v } : c))} />
            <CheckboxField label="Use AI Scoring Default" checked={settings.useAiScoringDefault} onChange={(v) => setSettings((c) => (c ? { ...c, useAiScoringDefault: v } : c))} />
            <CheckboxField label="Strict AI Required Default" checked={settings.strictAiRequiredDefault} onChange={(v) => setSettings((c) => (c ? { ...c, strictAiRequiredDefault: v } : c))} />
            <CheckboxField label="Enable Shadow Trade Analysis" checked={settings.enableShadowTradeAnalysis} onChange={(v) => setSettings((c) => (c ? { ...c, enableShadowTradeAnalysis: v } : c))} />
            <CheckboxField label="Allow Long Trades" checked={settings.allowLongTrades} onChange={(v) => setSettings((c) => (c ? { ...c, allowLongTrades: v } : c))} />
            <CheckboxField label="Allow Short Trades" checked={settings.allowShortTrades} onChange={(v) => setSettings((c) => (c ? { ...c, allowShortTrades: v } : c))} />
          </div>
        </FormPanel>
      </div>

      {canEdit ? (
        <FormActions>
          <button type="button" className="rounded-lg border border-slate-600 px-4 py-2 text-sm text-slate-100" onClick={() => void resetDefaults()} disabled={saving}>
            Reset Defaults
          </button>
          <button type="button" className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-900" onClick={() => void save()} disabled={saving}>
            {saving ? 'Saving...' : 'Save Settings'}
          </button>
        </FormActions>
      ) : null}
    </div>
  );
}
