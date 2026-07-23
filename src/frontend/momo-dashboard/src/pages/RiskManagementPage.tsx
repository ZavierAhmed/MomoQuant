import { useEffect, useState } from 'react';
import { PageHeader } from '@/components/common/PageHeader';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { EmptyState } from '@/components/common/EmptyState';
import { DataTable } from '@/components/common/DataTable';
import { PaginatedTable } from '@/components/common/PaginatedTable';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { FormPanel } from '@/components/common/FormPanel';
import { FormActions } from '@/components/forms/FormActions';
import { CheckboxField, NumberField, SelectField, TextField } from '@/components/forms/fields';
import { RiskDecisionView } from '@/components/formatters/TradingViews';
import { DIRECTION_OPTIONS, MARKET_REGIME_OPTIONS } from '@/constants/tradingOptions';
import { formatDate } from '@/components/common/utils';
import { useAsync } from '@/hooks/useAsync';
import { useReferenceData } from '@/hooks/useReferenceData';
import { useRole } from '@/hooks/useRole';
import { riskApi } from '@/api/riskApi';
import { parseApiClientError } from '@/utils/apiError';
import { requireNumber } from '@/utils/numbers';
import type { RiskRule } from '@/api/domainTypes';

export function RiskManagementPage() {
  const { canEdit } = useRole();
  const reference = useReferenceData();
  const [selectedProfileId, setSelectedProfileId] = useState<number | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [saveMessage, setSaveMessage] = useState<string | null>(null);
  const [evaluateResult, setEvaluateResult] = useState<Record<string, unknown> | null>(null);
  const [ruleDrafts, setRuleDrafts] = useState<RiskRule[]>([]);
  const [evaluateForm, setEvaluateForm] = useState({
    symbolId: '' as number | '',
    direction: 'Long',
    entryPrice: 50000 as number | '',
    suggestedStopLoss: 49000 as number | '',
    suggestedTakeProfit: 52000 as number | '',
    confidenceScore: 80 as number | '',
    marketRegime: 'Trending',
  });

  const profiles = useAsync(() => riskApi.listProfiles(), []);
  const rules = useAsync(
    () => (selectedProfileId ? riskApi.getRules(selectedProfileId) : Promise.resolve([])),
    [selectedProfileId],
  );
  const decisions = useAsync(() => riskApi.listDecisions({ page: 1, pageSize: 50 }), []);

  useEffect(() => {
    setRuleDrafts(rules.data ?? []);
  }, [rules.data]);

  const emergencyStopEnabled = ruleDrafts.some(
    (rule) => rule.ruleKey === 'EmergencyStopEnabled' && rule.ruleValue.toLowerCase() === 'true',
  );

  async function saveRules() {
    if (!canEdit || !selectedProfileId) return;
    setActionError(null);
    setSaveMessage(null);
    try {
      await riskApi.updateRules(
        selectedProfileId,
        ruleDrafts.map((rule) => ({
          ruleKey: rule.ruleKey,
          ruleValue: rule.ruleValue,
          valueType: rule.valueType,
          isEnabled: rule.isEnabled,
        })),
      );
      rules.reload();
      setSaveMessage('Risk rules updated successfully.');
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  async function handleEvaluate() {
    if (!canEdit || !selectedProfileId || !evaluateForm.symbolId) {
      setActionError('Select a risk profile and symbol before evaluating.');
      return;
    }

    setActionError(null);
    try {
      const result = await riskApi.evaluate({
        riskProfileId: selectedProfileId,
        symbolId: requireNumber(evaluateForm.symbolId, 'Symbol'),
        direction: evaluateForm.direction,
        entryPrice: requireNumber(evaluateForm.entryPrice, 'Entry price'),
        suggestedStopLoss: evaluateForm.suggestedStopLoss === '' ? undefined : Number(evaluateForm.suggestedStopLoss),
        suggestedTakeProfit: evaluateForm.suggestedTakeProfit === '' ? undefined : Number(evaluateForm.suggestedTakeProfit),
        confidenceScore: requireNumber(evaluateForm.confidenceScore, 'Confidence score'),
        strategyCode: 'EmaPullback',
        accountBalance: 10000,
        dailyPnl: 0,
        weeklyPnl: 0,
        openPositionCount: 0,
        openSymbolExposure: 0,
        totalExposure: 0,
        consecutiveLosses: 0,
        spreadPercent: 0.01,
        atrPercent: 1.2,
        marketRegime: evaluateForm.marketRegime,
        emergencyStopEnabled,
        persistDecision: false,
      });
      setEvaluateResult(result);
    } catch (error) {
      setEvaluateResult(null);
      setActionError(parseApiClientError(error).message);
    }
  }

  return (
    <div>
      <PageHeader title="Risk Management" description="Risk profiles, rules, and decision history." />
      <div className="mb-4 rounded-xl border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-sm text-amber-200">
        Risk engine is the final approval gate.
      </div>
      <ApiErrorAlert message={actionError} />
      {saveMessage ? <p className="mb-4 text-sm text-emerald-300">{saveMessage}</p> : null}

      {canEdit ? (
        <FormPanel title="Diagnostic Risk Evaluate" description="Test the risk engine against a sample signal.">
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            <SelectField
              label="Risk Profile"
              value={selectedProfileId ?? ''}
              onChange={(value) => setSelectedProfileId(value ? Number(value) : null)}
              options={reference.riskProfileOptions}
              loading={reference.loading}
              required
            />
            <SelectField
              label="Symbol"
              value={evaluateForm.symbolId}
              onChange={(value) => setEvaluateForm((current) => ({ ...current, symbolId: value }))}
              options={reference.allSymbolOptions}
              loading={reference.loading}
              required
            />
            <SelectField
              label="Direction"
              value={evaluateForm.direction}
              onChange={(value) => setEvaluateForm((current) => ({ ...current, direction: value || 'Long' }))}
              options={DIRECTION_OPTIONS}
              required
            />
            <SelectField
              label="Market Regime"
              value={evaluateForm.marketRegime}
              onChange={(value) => setEvaluateForm((current) => ({ ...current, marketRegime: value || 'Trending' }))}
              options={MARKET_REGIME_OPTIONS}
            />
            <NumberField label="Entry Price" value={evaluateForm.entryPrice} onChange={(v) => setEvaluateForm((c) => ({ ...c, entryPrice: v }))} />
            <NumberField label="Suggested Stop Loss" value={evaluateForm.suggestedStopLoss} onChange={(v) => setEvaluateForm((c) => ({ ...c, suggestedStopLoss: v }))} />
            <NumberField label="Suggested Take Profit" value={evaluateForm.suggestedTakeProfit} onChange={(v) => setEvaluateForm((c) => ({ ...c, suggestedTakeProfit: v }))} />
            <NumberField label="Confidence Score" value={evaluateForm.confidenceScore} onChange={(v) => setEvaluateForm((c) => ({ ...c, confidenceScore: v }))} min={0} max={100} />
          </div>
          <FormActions>
            <button type="button" onClick={() => void handleEvaluate()} className="rounded-lg border border-slate-600 px-4 py-2 text-sm text-slate-200 hover:bg-slate-800">
              Evaluate Sample Signal
            </button>
          </FormActions>
          {evaluateResult ? (
            <div className="mt-4">
              <RiskDecisionView decision={evaluateResult} />
            </div>
          ) : null}
        </FormPanel>
      ) : null}

      {profiles.loading ? <LoadingState /> : null}
      {profiles.error ? <ErrorState message={profiles.error} onRetry={profiles.reload} /> : null}

      <div className="grid gap-6 xl:grid-cols-2">
        <section>
          <h2 className="mb-3 text-sm font-medium text-slate-300">Risk Profiles</h2>
          {(profiles.data ?? []).length === 0 && !profiles.loading ? (
            <EmptyState title="No risk profiles" description="No risk profiles are configured yet." />
          ) : (
            <DataTable
              columns={[
                { key: 'name', header: 'Name', render: (row) => row.name },
                { key: 'description', header: 'Description', render: (row) => row.description },
                { key: 'default', header: 'Default', render: (row) => (row.isDefault ? 'Yes' : 'No') },
                { key: 'view', header: '', render: (row) => (
                  <button type="button" onClick={() => setSelectedProfileId(row.id)} className="text-xs underline">View Rules</button>
                ) },
              ]}
              rows={profiles.data ?? []}
            />
          )}
        </section>

        <section>
          <h2 className="mb-3 text-sm font-medium text-slate-300">Risk Rules</h2>
          {!selectedProfileId ? (
            <EmptyState title="Select a profile" description="Choose a risk profile to view and edit its rules." />
          ) : null}
          {rules.loading ? <LoadingState /> : null}
          {selectedProfileId && (ruleDrafts.length ?? 0) === 0 && !rules.loading ? (
            <EmptyState title="No rules" description="This profile has no editable rules." />
          ) : null}
          {selectedProfileId && ruleDrafts.length > 0 ? (
            <div className="space-y-3">
              {ruleDrafts.map((rule, index) => (
                <div key={rule.id} className="rounded-lg border border-slate-800 bg-slate-950/40 p-3">
                  <p className="text-sm font-medium text-slate-200">{rule.ruleKey}</p>
                  <div className="mt-2 grid gap-3 md:grid-cols-2">
                    <TextField
                      label="Current Value"
                      value={rule.ruleValue}
                      onChange={(value) =>
                        setRuleDrafts((current) =>
                          current.map((item, itemIndex) => (itemIndex === index ? { ...item, ruleValue: value } : item)),
                        )
                      }
                    />
                    <CheckboxField
                      label="Enabled"
                      checked={rule.isEnabled}
                      onChange={(checked) =>
                        setRuleDrafts((current) =>
                          current.map((item, itemIndex) => (itemIndex === index ? { ...item, isEnabled: checked } : item)),
                        )
                      }
                    />
                  </div>
                </div>
              ))}
              {canEdit ? (
                <FormActions>
                  <button type="button" onClick={() => void saveRules()} className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950 hover:bg-white">
                    Save Rules
                  </button>
                </FormActions>
              ) : null}
              <p className="text-sm text-slate-400">Emergency stop: {emergencyStopEnabled ? 'Enabled' : 'Disabled'}</p>
            </div>
          ) : null}
        </section>
      </div>

      <section className="mt-6">
        <h2 className="mb-3 text-sm font-medium text-slate-300">Risk Decisions</h2>
        {decisions.loading ? <LoadingState /> : null}
        <PaginatedTable
          rows={decisions.data?.items ?? []}
          columns={[
            { key: 'id', header: 'ID', render: (row) => row.id },
            { key: 'decision', header: 'Decision', render: (row) => row.decision },
            { key: 'reason', header: 'Reason', render: (row) => row.reason },
            { key: 'rule', header: 'Rejected Rule', render: (row) => row.rejectedRuleKey ?? '—' },
            { key: 'created', header: 'Created', render: (row) => formatDate(row.createdAtUtc) },
          ]}
        />
      </section>
    </div>
  );
}
