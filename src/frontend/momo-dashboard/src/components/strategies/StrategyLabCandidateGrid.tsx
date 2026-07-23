import { useCallback, useMemo, useState } from 'react';
import { useCandidatePage } from '@/hooks/useCandidatePage';
import {
  strategyLabApi,
  type PagedCandidates,
  type StrategyResearchCandidate,
} from '@/api/strategyLabApi';
import {
  ALL_COLUMNS,
  QUICK_FILTERS,
  buildCandidateQuery,
  download,
  renderCell,
  toCsv,
  type ColumnKey,
  type QuickFilter,
} from '@/components/strategies/candidateGrid/strategyLabCandidateGridHelpers';
import { ScoreBreakdownModal } from '@/components/strategies/candidateGrid/ScoreBreakdownModal';
import { RiskBreakdownModal } from '@/components/strategies/candidateGrid/RiskBreakdownModal';

export function StrategyLabCandidateGrid({ runId }: { runId: number }) {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [sortBy, setSortBy] = useState('setupDetectedAtUtc');
  const [sortDirection, setSortDirection] = useState<'asc' | 'desc'>('desc');
  const [search, setSearch] = useState('');
  const [direction, setDirection] = useState('');
  const [rawOutcome, setRawOutcome] = useState('');
  const [confidenceDecision, setConfidenceDecision] = useState('');
  const [riskDecision, setRiskDecision] = useState('');
  const [confidenceMin, setConfidenceMin] = useState('');
  const [confidenceMax, setConfidenceMax] = useState('');
  const [riskMin, setRiskMin] = useState('');
  const [riskMax, setRiskMax] = useState('');
  const [profitableOnly, setProfitableOnly] = useState('');
  const [fromUtc, setFromUtc] = useState('');
  const [toUtc, setToUtc] = useState('');
  const [quickFilter, setQuickFilter] = useState<QuickFilter>('');
  const [visible, setVisible] = useState<Record<ColumnKey, boolean>>(() => {
    const initial = {} as Record<ColumnKey, boolean>;
    for (const col of ALL_COLUMNS) initial[col.key] = col.defaultVisible !== false;
    return initial;
  });
  const [showColumns, setShowColumns] = useState(false);
  const [breakdown, setBreakdown] = useState<StrategyResearchCandidate | null>(null);
  const [riskBreakdown, setRiskBreakdown] = useState<StrategyResearchCandidate | null>(null);

  const query = useMemo(
    () =>
      buildCandidateQuery({
        page,
        pageSize,
        sortBy,
        sortDirection,
        search,
        direction,
        rawOutcome,
        confidenceDecision,
        riskDecision,
        confidenceMin,
        confidenceMax,
        riskMin,
        riskMax,
        profitableOnly,
        fromUtc,
        toUtc,
        quickFilter,
      }),
    [
      page,
      pageSize,
      sortBy,
      sortDirection,
      search,
      direction,
      rawOutcome,
      confidenceDecision,
      riskDecision,
      confidenceMin,
      confidenceMax,
      riskMin,
      riskMax,
      profitableOnly,
      fromUtc,
      toUtc,
      quickFilter,
    ],
  );

  const { data, loading, error } = useCandidatePage<PagedCandidates>(
    useCallback((signal) => strategyLabApi.getCandidates(runId, query, signal), [runId, query]),
    [runId, query],
  );

  const columns = ALL_COLUMNS.filter((c) => visible[c.key]);

  const resetFilters = () => {
    setPage(1);
    setSearch('');
    setDirection('');
    setRawOutcome('');
    setConfidenceDecision('');
    setRiskDecision('');
    setConfidenceMin('');
    setConfidenceMax('');
    setRiskMin('');
    setRiskMax('');
    setProfitableOnly('');
    setFromUtc('');
    setToUtc('');
    setQuickFilter('');
    setSortBy('setupDetectedAtUtc');
    setSortDirection('desc');
  };

  const applyQuick = (id: QuickFilter) => {
    setQuickFilter(id);
    setPage(1);
    // Clear overlapping filters so quick filter owns the predicate.
    setRawOutcome('');
    setConfidenceDecision('');
    setRiskDecision('');
  };

  const onSort = (key: ColumnKey) => {
    if (sortBy === key) {
      setSortDirection((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      setSortBy(key);
      setSortDirection('desc');
    }
    setPage(1);
  };

  const exportFiltered = async (format: 'csv' | 'json') => {
    const all: StrategyResearchCandidate[] = [];
    let currentPage = 1;
    let totalPages = 1;
    do {
      const pageResult = await strategyLabApi.getCandidates(runId, { ...query, page: currentPage, pageSize: 250 });
      all.push(...(pageResult?.items ?? []));
      totalPages = pageResult?.totalPages ?? 0;
      currentPage += 1;
    } while (currentPage <= totalPages);

    if (format === 'csv') {
      download(`strategy-lab-${runId}-candidates.csv`, toCsv(all, columns), 'text/csv;charset=utf-8');
    } else {
      download(`strategy-lab-${runId}-candidates.json`, JSON.stringify(all, null, 2), 'application/json');
    }
  };

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-2">
        {QUICK_FILTERS.map((f) => (
          <button
            key={f.id}
            type="button"
            onClick={() => applyQuick(quickFilter === f.id ? '' : f.id)}
            className={`rounded-lg border px-2.5 py-1 text-xs ${
              quickFilter === f.id ? 'border-sky-600 bg-sky-950/40 text-sky-100' : 'border-slate-700 text-slate-300'
            }`}
          >
            {f.label}
          </button>
        ))}
      </div>

      <div className="grid gap-2 md:grid-cols-4 lg:grid-cols-6">
        <input
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            setPage(1);
          }}
          placeholder="Search reason / fingerprint"
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        />
        <select
          value={direction}
          onChange={(e) => {
            setDirection(e.target.value);
            setPage(1);
          }}
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        >
          <option value="">Direction</option>
          <option value="Long">Long</option>
          <option value="Short">Short</option>
        </select>
        <select
          value={rawOutcome}
          onChange={(e) => {
            setRawOutcome(e.target.value);
            setPage(1);
          }}
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        >
          <option value="">Raw Outcome</option>
          <option value="Winner">Winner</option>
          <option value="Loser">Loser</option>
          <option value="Breakeven">Breakeven</option>
          <option value="Open">Open</option>
          <option value="Invalid">Invalid</option>
        </select>
        <select
          value={confidenceDecision}
          onChange={(e) => {
            setConfidenceDecision(e.target.value);
            setPage(1);
          }}
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        >
          <option value="">Confidence Decision</option>
          <option value="Approved">Approved</option>
          <option value="Rejected">Rejected</option>
          <option value="NotEvaluated">NotEvaluated</option>
        </select>
        <select
          value={riskDecision}
          onChange={(e) => {
            setRiskDecision(e.target.value);
            setPage(1);
          }}
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        >
          <option value="">Risk Decision</option>
          <option value="Approved">Approved</option>
          <option value="Rejected">Rejected</option>
          <option value="NotEvaluated">NotEvaluated</option>
        </select>
        <select
          value={profitableOnly}
          onChange={(e) => {
            setProfitableOnly(e.target.value);
            setPage(1);
          }}
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        >
          <option value="">PnL: all</option>
          <option value="profitable">profitable</option>
          <option value="losing">losing</option>
        </select>
        <input
          type="number"
          value={confidenceMin}
          onChange={(e) => {
            setConfidenceMin(e.target.value);
            setPage(1);
          }}
          placeholder="Conf min"
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        />
        <input
          type="number"
          value={confidenceMax}
          onChange={(e) => {
            setConfidenceMax(e.target.value);
            setPage(1);
          }}
          placeholder="Conf max"
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        />
        <input
          type="number"
          value={riskMin}
          onChange={(e) => {
            setRiskMin(e.target.value);
            setPage(1);
          }}
          placeholder="Risk min"
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        />
        <input
          type="number"
          value={riskMax}
          onChange={(e) => {
            setRiskMax(e.target.value);
            setPage(1);
          }}
          placeholder="Risk max"
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        />
        <input
          type="datetime-local"
          value={fromUtc}
          onChange={(e) => {
            setFromUtc(e.target.value ? new Date(e.target.value).toISOString() : '');
            setPage(1);
          }}
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        />
        <input
          type="datetime-local"
          value={toUtc}
          onChange={(e) => {
            setToUtc(e.target.value ? new Date(e.target.value).toISOString() : '');
            setPage(1);
          }}
          className="rounded-lg border border-slate-700 bg-slate-950 px-3 py-1.5 text-sm"
        />
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <select
          value={pageSize}
          onChange={(e) => {
            setPageSize(Number(e.target.value));
            setPage(1);
          }}
          className="rounded-lg border border-slate-700 bg-slate-950 px-2 py-1 text-sm"
        >
          {[25, 50, 100, 250].map((n) => (
            <option key={n} value={n}>
              {n} / page
            </option>
          ))}
        </select>
        <button type="button" onClick={resetFilters} className="rounded-lg border border-slate-700 px-3 py-1 text-sm">
          Reset filters
        </button>
        <button type="button" onClick={() => setShowColumns((v) => !v)} className="rounded-lg border border-slate-700 px-3 py-1 text-sm">
          Columns
        </button>
        <button type="button" onClick={() => exportFiltered('csv')} className="rounded-lg border border-slate-700 px-3 py-1 text-sm">
          Export CSV
        </button>
        <button type="button" onClick={() => exportFiltered('json')} className="rounded-lg border border-slate-700 px-3 py-1 text-sm">
          Export JSON
        </button>
        <span className="text-sm text-slate-400">
          {data ? `${data.totalItems} candidates` : '—'}
        </span>
      </div>

      {showColumns ? (
        <div className="grid gap-1 rounded-lg border border-slate-800 p-3 md:grid-cols-3 lg:grid-cols-4">
          {ALL_COLUMNS.map((col) => (
            <label key={col.key} className="flex items-center gap-2 text-xs text-slate-300">
              <input
                type="checkbox"
                checked={visible[col.key]}
                onChange={(e) => setVisible((prev) => ({ ...prev, [col.key]: e.target.checked }))}
              />
              {col.header}
            </label>
          ))}
        </div>
      ) : null}

      {error ? <div className="text-sm text-rose-300">{error}</div> : null}

      <div className="max-h-[70vh] overflow-auto rounded-xl border border-slate-800">
        <table className="min-w-full divide-y divide-slate-800 text-sm">
          <thead className="sticky top-0 z-10 bg-slate-900/95 backdrop-blur">
            <tr>
              {columns.map((col) => (
                <th
                  key={col.key}
                  className="cursor-pointer whitespace-nowrap px-3 py-2 text-left font-medium text-slate-400"
                  onClick={() => onSort(col.key)}
                >
                  {col.header}
                  {sortBy === col.key ? (sortDirection === 'asc' ? ' ↑' : ' ↓') : ''}
                </th>
              ))}
              <th className="whitespace-nowrap px-3 py-2 text-left font-medium text-slate-400">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-800 bg-slate-950/40">
            {(data?.items ?? []).map((row) => (
              <tr key={row.id} className="hover:bg-slate-900/40">
                {columns.map((col) => (
                  <td key={col.key} className="whitespace-nowrap px-3 py-2 text-slate-200">
                    {renderCell(col.key, row)}
                  </td>
                ))}
                <td className="whitespace-nowrap px-3 py-2">
                  <div className="flex flex-col gap-1">
                    <button
                      type="button"
                      className="text-xs text-sky-300 hover:underline disabled:opacity-40"
                      onClick={() => setBreakdown(row)}
                      disabled={!row.confidenceComponentsJson}
                    >
                      View Score Breakdown
                    </button>
                    <button
                      type="button"
                      className="text-xs text-amber-300 hover:underline disabled:opacity-40"
                      onClick={() => setRiskBreakdown(row)}
                      disabled={
                        !row.riskComponentsJson
                        && !row.riskRuleResultsJson
                        && row.riskDecision === 'NotEvaluated'
                      }
                    >
                      View Risk Breakdown
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {!loading && (data?.items?.length ?? 0) === 0 ? (
          <div className="p-6 text-sm text-slate-400">No candidates match the current filters.</div>
        ) : null}
        {loading ? <div className="p-4 text-sm text-slate-400">Loading candidates…</div> : null}
      </div>

      <div className="flex items-center justify-between text-sm text-slate-300">
        <button
          type="button"
          disabled={!data || page <= 1}
          onClick={() => setPage((p) => Math.max(1, p - 1))}
          className="rounded-lg border border-slate-700 px-3 py-1 disabled:opacity-40"
        >
          Previous
        </button>
        <span>
          Page {data?.page ?? page} of {data?.totalPages || 1}
        </span>
        <button
          type="button"
          disabled={!data || page >= (data.totalPages || 1)}
          onClick={() => setPage((p) => p + 1)}
          className="rounded-lg border border-slate-700 px-3 py-1 disabled:opacity-40"
        >
          Next
        </button>
      </div>

      {breakdown ? (
        <ScoreBreakdownModal candidate={breakdown} onClose={() => setBreakdown(null)} />
      ) : null}
      {riskBreakdown ? (
        <RiskBreakdownModal candidate={riskBreakdown} onClose={() => setRiskBreakdown(null)} />
      ) : null}
    </div>
  );
}
