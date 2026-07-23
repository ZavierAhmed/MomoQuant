import { useAsync } from '@/hooks/useAsync';
import { aiApi } from '@/api/aiApi';
import { CheckboxField } from '@/components/forms/fields';

export interface AiScoringFormState {
  useAiScoring: boolean;
  strictAiRequired: boolean;
}

interface AiScoringFieldsProps {
  value: AiScoringFormState;
  onChange: (value: AiScoringFormState) => void;
}

export function AiScoringFields({ value, onChange }: AiScoringFieldsProps) {
  const health = useAsync(() => aiApi.getHealth(), []);

  const healthLabel = health.loading
    ? 'Unknown'
    : health.error
      ? 'Unhealthy'
      : health.data?.status === 'healthy'
        ? 'Healthy'
        : 'Unhealthy';

  return (
    <div className="space-y-2 rounded-lg border border-slate-700 bg-slate-900/40 p-3">
      <div className="text-xs text-slate-300">
        AI Health: <span className={healthLabel === 'Healthy' ? 'text-emerald-300' : 'text-amber-300'}>{healthLabel}</span>
      </div>
      <CheckboxField
        label="Use AI scoring"
        checked={value.useAiScoring}
        onChange={(checked) => onChange({ ...value, useAiScoring: checked })}
        hint="AI is advisory only. Risk engine remains final authority."
      />
      {value.useAiScoring ? (
        <CheckboxField
          label="Require AI service to be available"
          checked={value.strictAiRequired}
          onChange={(checked) => onChange({ ...value, strictAiRequired: checked })}
          hint="When unchecked, strategy confidence is used if AI is unavailable."
        />
      ) : null}
      {value.useAiScoring && healthLabel === 'Unhealthy' && !value.strictAiRequired ? (
        <p className="text-xs text-amber-300">AI service unavailable. Strategy confidence fallback will be used.</p>
      ) : null}
    </div>
  );
}
