import { useRef, useState } from 'react';
import { toPng } from 'html-to-image';
import { StatusPill } from '@/components/common/StatusPill';
import { formatDate } from '@/components/common/utils';
import { SkSystemChart, type SkSystemChartHandle } from '@/components/charts/SkSystemChart';
import { formatPrice } from '@/utils/priceFormat';
import { openBlobInNewTab, triggerBlobDownload } from '@/utils/download';
import { tradingSystemsApi } from '@/api/tradingSystemsApi';
import type {
  SkConceptAudit,
  SkExportPdfOptions,
  SkIdea,
  SkSequence,
  SkSequenceCandidate,
  SkSystemAnalysisResult,
} from '@/api/tradingSystemsApi';

function marketDirectionText(bias: string): string {
  switch (bias) {
    case 'Bullish':
      return 'Bullish — possible upward bias';
    case 'Bearish':
      return 'Bearish — possible downward bias';
    case 'Mixed':
      return 'Mixed — no clean direction';
    case 'Neutral':
      return 'Neutral — no strong trend';
    default:
      return 'Unclear — no confirmed setup';
  }
}

function pickKeyIdea(result: SkSystemAnalysisResult): SkIdea | null {
  const bull = result.bestBullishIdea ?? null;
  const bear = result.bestBearishIdea ?? null;
  if (result.marketBias === 'Bullish' && bull) return bull;
  if (result.marketBias === 'Bearish' && bear) return bear;
  if (!bull) return bear;
  if (!bear) return bull;
  return bull.clarityScore >= bear.clarityScore ? bull : bear;
}

function bestIdeaSummary(result: SkSystemAnalysisResult): string {
  const idea = pickKeyIdea(result);
  if (!idea) {
    return 'No clear setup yet.';
  }
  let text = `${idea.directionLabel} (${idea.clarityLabel.toLowerCase()} clarity)`;
  if (result.conflictExplanation) {
    text += ', but the higher timeframe does not agree';
  }
  return `${text}.`;
}

function Card({ label, value, tone }: { label: string; value: string; tone?: string }) {
  return (
    <div className="rounded-xl border border-slate-800 bg-slate-900/40 p-4">
      <p className="text-xs uppercase tracking-wide text-slate-500">{label}</p>
      <p className={`mt-1 text-sm ${tone ?? 'text-slate-100'}`}>{value}</p>
    </div>
  );
}

function IdeaCard({
  idea,
  candidate,
  decimals,
  tone,
}: {
  idea: SkIdea;
  candidate?: SkSequenceCandidate;
  decimals: number;
  tone: 'bull' | 'bear';
}) {
  const border = tone === 'bull' ? 'border-emerald-500/25' : 'border-rose-500/25';
  const bg = tone === 'bull' ? 'bg-emerald-500/5' : 'bg-rose-500/5';
  const title = tone === 'bull' ? 'text-emerald-300' : 'text-rose-300';

  return (
    <div className={`rounded-xl border ${border} ${bg} p-4`}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h4 className={`text-sm font-semibold ${title}`}>{idea.directionLabel}</h4>
        <div className="flex items-center gap-2">
          <StatusPill status={idea.statusLabel} />
          <span className="text-xs text-slate-400">Clarity: {idea.clarityLabel}</span>
        </div>
      </div>

      <dl className="mt-3 grid gap-2 text-sm md:grid-cols-2">
        <div>
          <dt className="text-xs uppercase text-slate-500">Watch area (reaction zone)</dt>
          <dd className="text-slate-200">{idea.reactionZoneText}</dd>
        </div>
        <div>
          <dt className="text-xs uppercase text-slate-500">Strong reaction zone</dt>
          <dd className="text-slate-200">{idea.strongReactionZoneText}</dd>
        </div>
        <div>
          <dt className="text-xs uppercase text-slate-500">Danger level</dt>
          <dd className="text-rose-200">{idea.dangerLevelText}</dd>
        </div>
        <div>
          <dt className="text-xs uppercase text-slate-500">Targets</dt>
          <dd className="text-emerald-200">{idea.targetsText}</dd>
        </div>
      </dl>

      <p className="mt-3 text-sm text-slate-300">{idea.plainExplanation}</p>
      <p className="mt-1 text-xs text-slate-500">{idea.whyItMatters}</p>

      {candidate ? (
        <details className="mt-3 rounded-lg border border-slate-800 bg-slate-950/40 p-3">
          <summary className="cursor-pointer text-xs font-medium text-slate-400">Advanced details</summary>
          <div className="mt-2 grid gap-2 text-xs text-slate-400 md:grid-cols-2 xl:grid-cols-3">
            {candidate.pointZ ? <div>Starting point (Z): {formatPrice(candidate.pointZ.price, decimals)}</div> : null}
            {candidate.pointA ? <div>First strong move (A): {formatPrice(candidate.pointA.price, decimals)}</div> : null}
            {candidate.pointB ? <div>Pullback point (B): {formatPrice(candidate.pointB.price, decimals)}</div> : null}
            {candidate.pointC ? <div>Target area (C): {formatPrice(candidate.pointC.price, decimals)}</div> : null}
            <div>Extended target (1.618): {formatPrice(candidate.extension1618, decimals)}</div>
            <div>Clarity score: {candidate.confidenceScore}/100</div>
            <div>Position: {idea.currentPricePositionLabel}</div>
          </div>
        </details>
      ) : null}
    </div>
  );
}

function CompactCandidate({
  candidate,
  decimals,
  showDiagnostics,
}: {
  candidate: SkSequenceCandidate;
  decimals: number;
  showDiagnostics?: boolean;
}) {
  const directionLabel = candidate.direction === 'Bullish' ? 'Possible upward move' : 'Possible downward move';
  const category = candidate.validationStatus ?? 'Valid';
  return (
    <div className="rounded-lg border border-slate-800 bg-slate-900/40 p-3 text-xs text-slate-300">
      <div className="flex flex-wrap items-center gap-2">
        <StatusPill status={directionLabel} />
        <span className="text-slate-400">Clarity {candidate.confidenceScore}/100</span>
        {showDiagnostics && category !== 'Valid' ? (
          <span className="rounded bg-amber-500/10 px-1.5 py-0.5 text-amber-300">{category}</span>
        ) : null}
      </div>
      <div className="mt-2 grid gap-1 md:grid-cols-3">
        <div>Reaction zone: {formatPrice(candidate.correctionZoneMin, decimals)} – {formatPrice(candidate.correctionZoneMax, decimals)}</div>
        <div>Danger level: {formatPrice(candidate.invalidationLevel, decimals)}</div>
        <div>Targets: {formatPrice(candidate.target1, decimals)}, {formatPrice(candidate.target2, decimals)}</div>
      </div>
      {showDiagnostics && candidate.validationMessage ? (
        <p className="mt-2 text-amber-300">{candidate.validationMessage}</p>
      ) : null}
    </div>
  );
}

function ConceptAuditPanel({ audit }: { audit: SkConceptAudit }) {
  return (
    <section className="rounded-xl border border-violet-500/20 bg-violet-500/5 p-4">
      <h4 className="text-sm font-medium text-violet-200">SK Concept Audit</h4>
      <dl className="mt-3 grid gap-2 text-xs text-slate-300 md:grid-cols-2 xl:grid-cols-3">
        <div>HTF direction: {audit.htfDirection}</div>
        <div>LTF direction: {audit.ltfDirection}</div>
        <div>HTF/LTF agreement: {audit.htfLtfAgreement ? 'Yes' : 'No'}</div>
        <div>Selected direction: {audit.selectedSequenceDirection}</div>
        <div>Sequence status: {audit.sequenceStatus}</div>
        <div>Validity: {audit.validityStatus}</div>
        <div>Usefulness: {audit.usefulnessStatus}</div>
        <div>Clarity: {audit.clarityLabel} ({audit.clarityScore})</div>
        <div>Reason selected: {audit.reasonSelected || '—'}</div>
        <div>Reaction zone: {audit.reactionZoneText}</div>
        <div>Strong reaction zone: {audit.strongReactionZoneText}</div>
        <div>Invalidation: {audit.invalidationLevelText}</div>
        <div className="md:col-span-2 xl:col-span-3">Target validation: {audit.targetValidation}</div>
        <div>Already reached: {audit.alreadyReachedCheck}</div>
        <div>Invalidation check: {audit.invalidationCheck}</div>
        <div>Hidden structures: {audit.hiddenStructuresCount}</div>
        <div>Direction mismatch: {audit.directionMismatchStructuresCount}</div>
      </dl>
      {audit.sequencePoints.length > 0 ? (
        <ul className="mt-3 list-disc pl-5 text-xs text-slate-400">
          {audit.sequencePoints.map((point, index) => (
            <li key={`pt-${index}`}>{point}</li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}

function SequenceAnatomyCard({ sequence, decimals }: { sequence: SkSequence; decimals: number }) {
  return (
    <div className="rounded-lg border border-slate-800 bg-slate-950/40 p-3 text-xs text-slate-300">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-medium text-slate-200">{sequence.direction} sequence</span>
        <span className="text-slate-500">({sequence.structureCategory})</span>
        {sequence.selectedAsBest ? <span className="text-emerald-400">Best idea</span> : null}
      </div>
      <p className="mt-2">{sequence.beginnerExplanation}</p>
      <dl className="mt-2 grid gap-1 md:grid-cols-2">
        <div>Clarity: {sequence.clarityLabel} · Usefulness: {sequence.usefulnessStatus}</div>
        <div>Status: {sequence.sequenceStatus} · Validity: {sequence.validityStatus}</div>
        <div>Reaction zone: {formatPrice(sequence.correctionZoneLow, decimals)} – {formatPrice(sequence.correctionZoneHigh, decimals)}</div>
        <div>Danger: {formatPrice(sequence.invalidationLevel, decimals)}</div>
      </dl>
    </div>
  );
}

export function SkAnalysisResultView({ result }: { result: SkSystemAnalysisResult }) {
  const decimals = result.priceDecimals || 2;
  const context = result.higherTimeframeContext;
  const bull = result.bestBullishIdea ?? null;
  const bear = result.bestBearishIdea ?? null;
  const isAdvancedView = result.explanationMode !== 'Beginner';

  const bestIds = new Set([bull?.candidateId, bear?.candidateId].filter(Boolean) as string[]);
  const alternativeCandidates = result.sequenceCandidates.filter(
    (candidate) => !bestIds.has(candidate.id) && candidate.eligibleForBestIdea !== false,
  );
  const invalidCandidates = result.sequenceCandidates.filter(
    (candidate) => candidate.eligibleForBestIdea === false,
  );

  const findCandidate = (idea: SkIdea | null) =>
    idea ? result.sequenceCandidates.find((candidate) => candidate.id === idea.candidateId) : undefined;

  const chartHandleRef = useRef<SkSystemChartHandle | null>(null);
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);
  const [showOptions, setShowOptions] = useState(false);
  const [options, setOptions] = useState({
    includeChart: true,
    includeGlossary: true,
    includeRawDiagnostics: false,
    includeAdvancedFib: false,
    includeAllSetups: false,
    includeSwings: false,
  });

  const canExport = result.analysisId > 0;

  const captureChart = async (): Promise<{ image?: string; error?: string; chartReady: boolean; overlaysReady: boolean }> => {
    if (!options.includeChart) {
      return { chartReady: false, overlaysReady: false, error: 'Chart excluded by export options.' };
    }

    const exportElement = chartHandleRef.current?.getExportElement();
    if (!exportElement) {
      return { chartReady: false, overlaysReady: false, error: 'Chart export surface was not mounted.' };
    }

    if ((result.candles?.length ?? 0) === 0) {
      return { chartReady: false, overlaysReady: false, error: 'No candle data was loaded for chart capture.' };
    }

    try {
      const image = await toPng(exportElement, {
        pixelRatio: 2,
        cacheBust: true,
        backgroundColor: '#0f172a',
      });
      if (!image) {
        return { chartReady: true, overlaysReady: true, error: 'Chart PNG encoder returned an empty image.' };
      }
      return {
        image,
        chartReady: true,
        overlaysReady: (result.chartOverlays?.length ?? 0) > 0,
      };
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Unknown chart capture error';
      console.error('SK chart capture failed', { analysisId: result.analysisId, error });
      return { chartReady: true, overlaysReady: false, error: message };
    }
  };

  const runExport = async (preview: boolean) => {
    if (!canExport || exporting) {
      return;
    }
    setExporting(true);
    setExportError(null);
    let restore: (() => void) | undefined;
    try {
      if (options.includeChart && chartHandleRef.current) {
        restore = chartHandleRef.current.enterExportMode({
          includeFib: options.includeAdvancedFib,
          includeAllSetups: options.includeAllSetups,
          includeSwings: options.includeSwings,
        });
        await chartHandleRef.current.waitForExportReady();
        await new Promise((resolve) => setTimeout(resolve, 300));
      }
      const capture = await captureChart();
      restore?.();
      restore = undefined;
      const exportOptions: SkExportPdfOptions = {
        chartImageBase64: capture.image,
        includeChart: options.includeChart,
        includeGlossary: options.includeGlossary,
        includeRawDiagnostics: options.includeRawDiagnostics,
        chartReady: capture.chartReady,
        overlaysReady: capture.overlaysReady,
        overlayCount: result.chartOverlays?.length ?? 0,
        exportError: capture.error,
      };
      const { blob, fileName } = await tradingSystemsApi.exportAnalysisPdf(result.analysisId, exportOptions);
      if (preview) {
        openBlobInNewTab(blob);
      } else {
        triggerBlobDownload(blob, fileName);
      }
    } catch {
      setExportError('Could not generate PDF report. Please try again.');
    } finally {
      restore?.();
      setExporting(false);
    }
  };

  return (
    <div className="space-y-6">
      <section className="rounded-xl border border-slate-800 bg-slate-900/40 p-5">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 className="text-lg font-medium text-slate-100">
              {result.symbol} · {result.primaryTimeframe} + {result.higherTimeframe}
            </h2>
            <p className="text-xs text-slate-500">
              {result.swingSensitivity} · {result.directionMode} · {result.explanationMode} explanation
            </p>
          </div>
          <div className="flex flex-col items-end gap-2">
            <span className="rounded-full border border-sky-500/40 bg-sky-500/10 px-2.5 py-0.5 text-xs text-sky-300">
              This is chart analysis only. It is not a trade signal.
            </span>
            <div className="flex flex-wrap items-center justify-end gap-2">
              <button
                type="button"
                onClick={() => void runExport(false)}
                disabled={!canExport || exporting}
                className="rounded-lg border border-sky-500/40 bg-sky-500/10 px-3 py-1.5 text-xs font-medium text-sky-200 hover:bg-sky-500/20 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {exporting ? 'Generating PDF…' : 'Download PDF'}
              </button>
              <button
                type="button"
                onClick={() => void runExport(true)}
                disabled={!canExport || exporting}
                className="rounded-lg border border-slate-700 bg-slate-800/60 px-3 py-1.5 text-xs font-medium text-slate-200 hover:bg-slate-800 disabled:cursor-not-allowed disabled:opacity-50"
              >
                Preview PDF
              </button>
              <button
                type="button"
                onClick={() => setShowOptions((value) => !value)}
                className="rounded-lg border border-slate-700 bg-slate-800/60 px-2.5 py-1.5 text-xs text-slate-300 hover:bg-slate-800"
                aria-expanded={showOptions}
              >
                PDF options
              </button>
            </div>
            {showOptions ? (
              <div className="w-64 rounded-lg border border-slate-800 bg-slate-950/70 p-3 text-xs text-slate-300">
                <label className="flex items-center gap-2">
                  <input
                    type="checkbox"
                    checked={options.includeChart}
                    onChange={(event) => setOptions((prev) => ({ ...prev, includeChart: event.target.checked }))}
                  />
                  Include chart image
                </label>
                <label className="mt-2 flex items-center gap-2">
                  <input
                    type="checkbox"
                    checked={options.includeGlossary}
                    onChange={(event) => setOptions((prev) => ({ ...prev, includeGlossary: event.target.checked }))}
                  />
                  Include glossary
                </label>
                <label className="mt-2 flex items-center gap-2">
                  <input
                    type="checkbox"
                    checked={options.includeRawDiagnostics}
                    onChange={(event) =>
                      setOptions((prev) => ({ ...prev, includeRawDiagnostics: event.target.checked }))
                    }
                  />
                  Include raw diagnostics
                </label>
                <p className="mt-3 text-[11px] uppercase tracking-wide text-slate-500">Advanced chart in export</p>
                <label className="mt-1 flex items-center gap-2">
                  <input
                    type="checkbox"
                    checked={options.includeAdvancedFib}
                    onChange={(event) =>
                      setOptions((prev) => ({ ...prev, includeAdvancedFib: event.target.checked }))
                    }
                  />
                  Include advanced Fibonacci levels
                </label>
                <label className="mt-2 flex items-center gap-2">
                  <input
                    type="checkbox"
                    checked={options.includeAllSetups}
                    onChange={(event) => setOptions((prev) => ({ ...prev, includeAllSetups: event.target.checked }))}
                  />
                  Include all possible setups
                </label>
                <label className="mt-2 flex items-center gap-2">
                  <input
                    type="checkbox"
                    checked={options.includeSwings}
                    onChange={(event) => setOptions((prev) => ({ ...prev, includeSwings: event.target.checked }))}
                  />
                  Include swing points
                </label>
              </div>
            ) : null}
            {exportError ? <p className="text-xs text-rose-300">{exportError}</p> : null}
          </div>
        </div>

        <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <Card label="Market direction" value={marketDirectionText(result.marketBias)} />
          <Card label="Best current idea" value={bestIdeaSummary(result)} />
          <Card label="Key area to watch" value={result.keyAreaToWatch} />
          <Card label="Danger level" value={result.dangerLevelToWatch} tone="text-rose-200" />
        </div>

        <div className="mt-4 rounded-lg border border-slate-800 bg-slate-950/40 p-3">
          <p className="text-xs uppercase text-slate-500">What this means</p>
          <p className="mt-1 text-sm text-slate-300">{result.whatThisMeans || result.plainLanguageSummary}</p>
        </div>

        {result.aiSummary?.whatWouldMakeWrong ? (
          <div className="mt-4 rounded-lg border border-slate-800 bg-slate-950/40 p-3">
            <p className="text-xs uppercase text-slate-500">What would make this wrong</p>
            <p className="mt-1 text-sm text-slate-300">{result.aiSummary.whatWouldMakeWrong}</p>
          </div>
        ) : null}

        {result.aiSummary?.whatToWatchNext ? (
          <div className="mt-4 rounded-lg border border-slate-800 bg-slate-950/40 p-3">
            <p className="text-xs uppercase text-slate-500">What to watch next</p>
            <p className="mt-1 text-sm text-slate-300">{result.aiSummary.whatToWatchNext}</p>
          </div>
        ) : null}

        {result.aiSummary?.usefulnessExplanation ? (
          <div className="mt-4 rounded-lg border border-slate-800 bg-slate-950/40 p-3">
            <p className="text-xs uppercase text-slate-500">Usefulness</p>
            <p className="mt-1 text-sm text-slate-300">{result.aiSummary.usefulnessExplanation}</p>
            <p className="mt-1 text-xs text-slate-500">Clarity describes how clean the structure is. Usefulness describes whether the idea is still relevant right now.</p>
          </div>
        ) : null}

        {!isAdvancedView && invalidCandidates.length > 0 ? (
          <p className="mt-3 text-xs text-slate-500">
            Lower-quality or invalid structures exist, but they are hidden in Beginner view.
          </p>
        ) : null}

        <dl className="mt-4 grid gap-3 text-sm md:grid-cols-2 xl:grid-cols-4">
          <div>
            <dt className="text-xs uppercase text-slate-500">Current price</dt>
            <dd className="text-slate-200">{formatPrice(result.currentPrice, decimals)}</dd>
          </div>
          <div>
            <dt className="text-xs uppercase text-slate-500">Latest candle</dt>
            <dd className="text-slate-200">{formatDate(result.latestCandleTimeUtc)}</dd>
          </div>
          <div>
            <dt className="text-xs uppercase text-slate-500">Clarity</dt>
            <dd className="text-slate-200">{result.confidenceLabel}</dd>
          </div>
          <div>
            <dt className="text-xs uppercase text-slate-500">Lookback</dt>
            <dd className="text-slate-200">{result.lookbackCandles} candles</dd>
          </div>
        </dl>

        {result.clarityReasons.length > 0 || result.clarityWarnings.length > 0 ? (
          <div className="mt-4 rounded-lg border border-slate-800 bg-slate-950/40 p-3">
            <p className="text-xs uppercase text-slate-500">Why this clarity score</p>
            <ul className="mt-1 list-disc pl-5 text-sm text-slate-300">
              {result.clarityReasons.map((reason, index) => (
                <li key={`reason-${index}`}>{reason}</li>
              ))}
            </ul>
            {result.clarityWarnings.length > 0 ? (
              <ul className="mt-2 list-disc pl-5 text-sm text-amber-300">
                {result.clarityWarnings.map((warning, index) => (
                  <li key={`warn-${index}`}>{warning}</li>
                ))}
              </ul>
            ) : null}
          </div>
        ) : null}
      </section>

      {result.htfContext || result.ltfContext ? (
        <section className="grid gap-4 md:grid-cols-2">
          {result.htfContext ? (
            <div className="rounded-xl border border-slate-800 bg-slate-900/40 p-4">
              <h4 className="text-sm font-medium text-slate-200">
                Higher timeframe context ({result.htfContext.timeframe})
              </h4>
              <p className="mt-2 text-sm text-slate-300">{result.htfContext.summary}</p>
              <dl className="mt-3 grid gap-2 text-xs text-slate-400">
                <div>Direction: {result.htfContext.direction}</div>
                <div>Major zones: {result.htfContext.reactionZoneText}</div>
                <div>Agrees with analysis chart: {result.htfContext.agreesWithPrimary ? 'Yes' : 'No'}</div>
              </dl>
            </div>
          ) : null}
          {result.ltfContext ? (
            <div className="rounded-xl border border-slate-800 bg-slate-900/40 p-4">
              <h4 className="text-sm font-medium text-slate-200">
                Lower timeframe context ({result.ltfContext.timeframe})
              </h4>
              <p className="mt-2 text-sm text-slate-300">{result.ltfContext.summary}</p>
              <dl className="mt-3 grid gap-2 text-xs text-slate-400">
                <div>Reaction zone: {result.ltfContext.reactionZoneText}</div>
                <div>Danger level: {result.ltfContext.dangerLevelText}</div>
                <div>Targets: {result.ltfContext.targetsText}</div>
                <div>Clarity: {result.ltfContext.clarityLabel}</div>
              </dl>
            </div>
          ) : null}
        </section>
      ) : null}

      {result.conceptAudit ? <ConceptAuditPanel audit={result.conceptAudit} /> : null}

      {(result.sequences?.length ?? 0) > 0 ? (
        <section className="rounded-xl border border-slate-800 bg-slate-900/40 p-4">
          <h4 className="text-sm font-medium text-slate-200">Sequence anatomy</h4>
          <div className="mt-3 space-y-2">
            {(result.sequences ?? [])
              .filter((sequence) => isAdvancedView || !sequence.hiddenFromBeginner)
              .slice(0, isAdvancedView ? 6 : 2)
              .map((sequence) => (
                <SequenceAnatomyCard key={sequence.id} sequence={sequence} decimals={decimals} />
              ))}
          </div>
        </section>
      ) : null}

      <section>
        <h3 className="mb-2 text-sm font-medium text-slate-300">Chart</h3>
        <div
          key={`${result.analysisId}-${result.symbolId}-${result.primaryTimeframe}`}
          className="rounded-xl bg-slate-950/40 p-2"
        >
          <SkSystemChart ref={chartHandleRef} result={result} />
        </div>
      </section>

      {bull || bear ? (
        <section className="grid gap-4 md:grid-cols-2">
          {bull ? <IdeaCard idea={bull} candidate={findCandidate(bull)} decimals={decimals} tone="bull" /> : null}
          {bear ? <IdeaCard idea={bear} candidate={findCandidate(bear)} decimals={decimals} tone="bear" /> : null}
        </section>
      ) : (
        <section className="rounded-xl border border-slate-800 bg-slate-900/40 p-4 text-sm text-slate-400">
          No clear upward or downward setup was detected. The chart may have possible levels, but the direction is unclear.
        </section>
      )}

      <section className="grid gap-4 md:grid-cols-2">
        <div className="rounded-xl border border-emerald-500/20 bg-emerald-500/5 p-4">
          <h4 className="text-sm font-medium text-emerald-300">Upward scenario</h4>
          <p className="mt-2 text-sm text-slate-300">{result.bullishScenario}</p>
        </div>
        <div className="rounded-xl border border-rose-500/20 bg-rose-500/5 p-4">
          <h4 className="text-sm font-medium text-rose-300">Downward scenario</h4>
          <p className="mt-2 text-sm text-slate-300">{result.bearishScenario}</p>
        </div>
      </section>

      {result.higherTimeframeExplanation ? (
        <section className="rounded-xl border border-slate-800 bg-slate-900/40 p-4">
          <h4 className="text-sm font-medium text-slate-200">Higher timeframe view</h4>
          <p className="mt-2 text-sm text-slate-300">{result.higherTimeframeExplanation}</p>
          {result.conflictExplanation ? (
            <p className="mt-2 rounded-lg border border-amber-500/30 bg-amber-500/10 p-2 text-sm text-amber-200">
              {result.conflictExplanation}
            </p>
          ) : null}
        </section>
      ) : null}

      {alternativeCandidates.length > 0 ? (
        <details className="rounded-xl border border-slate-800 bg-slate-900/40 p-4">
          <summary className="cursor-pointer text-sm font-medium text-slate-300">
            Alternative structures ({alternativeCandidates.length})
          </summary>
          <div className="mt-3 space-y-2">
            {alternativeCandidates.map((candidate) => (
              <CompactCandidate key={candidate.id} candidate={candidate} decimals={decimals} />
            ))}
          </div>
        </details>
      ) : null}

      {isAdvancedView && invalidCandidates.length > 0 ? (
        <details className="rounded-xl border border-amber-500/20 bg-amber-500/5 p-4">
          <summary className="cursor-pointer text-sm font-medium text-amber-200">
            Invalid structures — diagnostics only ({invalidCandidates.length})
          </summary>
          <div className="mt-3 space-y-2">
            {invalidCandidates.map((candidate) => (
              <CompactCandidate key={candidate.id} candidate={candidate} decimals={decimals} showDiagnostics />
            ))}
          </div>
        </details>
      ) : null}

      <section className="rounded-xl border border-sky-500/20 bg-sky-500/5 p-4">
        <h4 className="text-sm font-medium text-sky-200">Bottom line</h4>
        <p className="mt-2 text-sm text-slate-200">{result.bottomLine}</p>
        <p className="mt-2 text-xs text-slate-500">
          {result.aiSummary?.whyNotTradeSignal ?? 'This is chart analysis only. It is not a trade signal.'}
        </p>
      </section>

      {result.glossaryTerms.length > 0 ? (
        <details className="rounded-xl border border-slate-800 bg-slate-900/40 p-4">
          <summary className="cursor-pointer text-sm font-medium text-slate-300">Explain these terms</summary>
          <dl className="mt-3 grid gap-2 text-sm md:grid-cols-2">
            {result.glossaryTerms.map((term) => (
              <div key={term.term}>
                <dt className="text-slate-200">{term.term}</dt>
                <dd className="text-xs text-slate-400">{term.explanation}</dd>
              </div>
            ))}
          </dl>
        </details>
      ) : null}

      <details className="rounded-xl border border-slate-800 bg-slate-900/40 p-4">
        <summary className="cursor-pointer text-sm font-medium text-slate-300">Raw diagnostics</summary>
        <p className="mt-3 text-sm text-slate-400">{result.summary}</p>
        <div className="mt-3 grid gap-2 text-xs text-slate-400 md:grid-cols-2 xl:grid-cols-3">
          <div>Primary candles: {result.diagnostics.primaryCandleCount}</div>
          <div>Higher candles: {result.diagnostics.higherCandleCount}</div>
          <div>Swing highs: {result.diagnostics.swingHighCount}</div>
          <div>Swing lows: {result.diagnostics.swingLowCount}</div>
          <div>Sequence candidates: {result.diagnostics.sequenceCandidateCount}</div>
          <div>Sensitivity: {result.diagnostics.resolvedSensitivity}</div>
          <div>Min swing distance %: {result.diagnostics.minSwingDistancePercent}</div>
          <div>Min swing candles: {result.diagnostics.minSwingCandles}</div>
          <div>Correction fib: {result.diagnostics.fibonacciCorrectionLevels.join(', ')}</div>
          <div>Extension fib: {result.diagnostics.fibonacciExtensionLevels.join(', ')}</div>
          <div>AI summary source: {result.aiSummary?.source ?? 'n/a'}</div>
        </div>
        <p className="mt-3 text-xs text-slate-500">{result.diagnostics.note}</p>
        {result.invalidationLevels.length > 0 ? (
          <ul className="mt-3 list-disc pl-5 text-xs text-slate-400">
            {result.invalidationLevels.map((level, index) => (
              <li key={index}>{level}</li>
            ))}
          </ul>
        ) : null}
        {context ? (
          <p className="mt-3 text-xs text-slate-500">
            Higher timeframe bias: {context.higherTimeframeBias} · {context.higherTimeframeTrendDescription}
          </p>
        ) : null}
      </details>
    </div>
  );
}
