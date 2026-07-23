import { Badge } from '@/components/common/Badge';
import { formatExpectancyR, formatNumber } from '@/components/common/utils';

export function MetricCard({
  title,
  closedTrades,
  expectancy,
  grossExpectancy,
  profitFactor,
  netProfitFactor,
  grossProfitFactor,
  netReturn,
  drawdown,
  grossProfit,
  grossLoss,
  netProfit,
  netLoss,
  insufficient,
  hidden,
}: {
  title: string;
  closedTrades?: number | null;
  expectancy?: number | null;
  grossExpectancy?: number | null;
  profitFactor?: number | null;
  netProfitFactor?: number | null;
  grossProfitFactor?: number | null;
  netReturn?: number | null;
  drawdown?: number | null;
  grossProfit?: number | null;
  grossLoss?: number | null;
  netProfit?: number | null;
  netLoss?: number | null;
  insufficient?: boolean;
  hidden?: boolean;
}) {
  if (hidden) {
    return (
      <div className="rounded-lg border border-slate-800 bg-slate-950/40 p-4">
        <div className="mb-2 text-sm font-semibold text-slate-200">{title}</div>
        <p className="text-sm text-slate-400">
          Performance metrics are hidden until Validation Reveal Status is Revealed.
        </p>
      </div>
    );
  }

  const showExactGrossNet =
    grossProfit != null || grossLoss != null || netProfit != null || netLoss != null;

  return (
    <div
      className={`rounded-lg border p-4 ${
        insufficient
          ? 'border-amber-700 bg-amber-950/30'
          : 'border-slate-800 bg-slate-950/40'
      }`}
    >
      <div className="mb-2 flex items-center justify-between gap-2">
        <div className="text-sm font-semibold text-slate-200">{title}</div>
        {insufficient ? <Badge tone="warning">Insufficient sample</Badge> : null}
      </div>
      <dl className="grid gap-2 text-sm sm:grid-cols-2">
        <div>
          <dt className="text-xs text-slate-500">Closed trades (n)</dt>
          <dd className="text-slate-100">{closedTrades ?? '—'}</dd>
        </div>
        <div>
          <dt className="text-xs text-slate-500">Net expectancy R</dt>
          <dd className="text-slate-100" title={expectancy == null ? undefined : String(expectancy)}>
            {expectancy == null ? '—' : formatExpectancyR(expectancy)}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-slate-500">Gross expectancy R</dt>
          <dd className="text-slate-100" title={grossExpectancy == null ? undefined : String(grossExpectancy)}>
            {grossExpectancy == null ? '—' : formatExpectancyR(grossExpectancy)}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-slate-500">Net profit factor</dt>
          <dd className="text-slate-100">
            {(netProfitFactor ?? profitFactor) == null ? '—' : formatNumber(netProfitFactor ?? profitFactor)}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-slate-500">Gross profit factor</dt>
          <dd className="text-slate-100">{grossProfitFactor == null ? '—' : formatNumber(grossProfitFactor)}</dd>
        </div>
        <div>
          <dt className="text-xs text-slate-500">Net return %</dt>
          <dd className="text-slate-100">{netReturn == null ? '—' : `${formatNumber(netReturn)}%`}</dd>
        </div>
        <div>
          <dt className="text-xs text-slate-500">Max drawdown %</dt>
          <dd className="text-slate-100">{drawdown == null ? '—' : `${formatNumber(drawdown)}%`}</dd>
        </div>
        {showExactGrossNet ? (
          <>
            <div>
              <dt className="text-xs text-slate-500">Gross profit</dt>
              <dd className="text-slate-100">{grossProfit == null ? '—' : formatNumber(grossProfit)}</dd>
            </div>
            <div>
              <dt className="text-xs text-slate-500">Gross loss</dt>
              <dd className="text-slate-100">{grossLoss == null ? '—' : formatNumber(grossLoss)}</dd>
            </div>
            <div>
              <dt className="text-xs text-slate-500">Net profit</dt>
              <dd className="text-slate-100">{netProfit == null ? '—' : formatNumber(netProfit)}</dd>
            </div>
            <div>
              <dt className="text-xs text-slate-500">Net loss</dt>
              <dd className="text-slate-100">{netLoss == null ? '—' : formatNumber(netLoss)}</dd>
            </div>
          </>
        ) : null}
      </dl>
    </div>
  );
}
