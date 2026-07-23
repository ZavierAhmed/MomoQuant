import { formatDate } from '@/components/common/utils';
import type { ValidationExperimentDetail } from '@/api/validationLabApi';

export function SplitTimeline({ detail }: { detail: ValidationExperimentDetail }) {
  const total = Math.max(detail.totalEligibleCandleCount, 1);
  const trainPct = Math.round((detail.trainingCandleCount / total) * 100);
  const valPct = Math.max(0, 100 - trainPct);

  return (
    <div className="space-y-3">
      <div className="flex h-8 overflow-hidden rounded-lg border border-slate-800">
        <div
          className="flex items-center justify-center bg-sky-900/70 text-xs text-sky-100"
          style={{ width: `${trainPct}%` }}
          title="Training"
        >
          Training
        </div>
        <div
          className="flex items-center justify-center bg-slate-700/80 text-xs text-slate-100"
          style={{ width: `${valPct}%` }}
          title="Validation"
        >
          Validation
        </div>
      </div>
      <div className="grid gap-3 md:grid-cols-4 text-sm">
        <div className="rounded-lg border border-slate-800 px-3 py-2">
          <div className="text-xs uppercase text-slate-500">Warmup</div>
          <div>{detail.requiredWarmupCandles} candles</div>
        </div>
        <div className="rounded-lg border border-slate-800 px-3 py-2">
          <div className="text-xs uppercase text-slate-500">Training</div>
          <div>
            {detail.trainingCandleCount} candles · used for parameter selection
          </div>
        </div>
        <div className="rounded-lg border border-slate-800 px-3 py-2">
          <div className="text-xs uppercase text-slate-500">Split boundary</div>
          <div>{formatDate(detail.splitCandleOpenTimeUtc)}</div>
        </div>
        <div className="rounded-lg border border-slate-800 px-3 py-2">
          <div className="text-xs uppercase text-slate-500">Validation</div>
          <div>
            {detail.validationCandleCount} candles · hidden until freeze/reveal
          </div>
        </div>
      </div>
    </div>
  );
}
