import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useMemo,
  useRef,
  useState,
} from 'react';
import {
  CandlestickSeries,
  ColorType,
  createChart,
  createSeriesMarkers,
  LineStyle,
  type IChartApi,
  type IPriceLine,
  type ISeriesApi,
  type ISeriesMarkersPluginApi,
  type SeriesMarker,
  type Time,
  type UTCTimestamp,
} from 'lightweight-charts';
import type { ChartOverlay, SkSystemAnalysisResult } from '@/api/tradingSystemsApi';
import { formatPrice } from '@/utils/priceFormat';

function toChartTime(iso: string): UTCTimestamp {
  return Math.floor(new Date(iso).getTime() / 1000) as UTCTimestamp;
}

interface OverlaySettings {
  showOnlyBestSetup: boolean;
  showAllSetups: boolean;
  showSwingPoints: boolean;
  showFib: boolean;
  showTargets: boolean;
  showDanger: boolean;
  showLabels: boolean;
  showHtfZones: boolean;
  showLegend: boolean;
  showLevelTable: boolean;
}

const CLEAN_VIEW: OverlaySettings = {
  showOnlyBestSetup: true,
  showAllSetups: false,
  showSwingPoints: false,
  showFib: false,
  showTargets: true,
  showDanger: true,
  showLabels: false,
  showHtfZones: false,
  showLegend: true,
  showLevelTable: false,
};

const BEGINNER_VIEW: OverlaySettings = {
  ...CLEAN_VIEW,
  showLabels: true,
  showLegend: true,
  showLevelTable: true,
};

const ADVANCED_VIEW: OverlaySettings = {
  showOnlyBestSetup: false,
  showAllSetups: true,
  showSwingPoints: true,
  showFib: true,
  showTargets: true,
  showDanger: true,
  showLabels: true,
  showHtfZones: true,
  showLegend: true,
  showLevelTable: true,
};

// Default beginner view keeps the first look clean (Milestone 19.8.3, Part 7).
const DEFAULT_SETTINGS: OverlaySettings = BEGINNER_VIEW;

const MANUAL_TOGGLES: { key: keyof OverlaySettings; label: string }[] = [
  { key: 'showOnlyBestSetup', label: 'Best setup only' },
  { key: 'showAllSetups', label: 'All possible setups' },
  { key: 'showSwingPoints', label: 'Swing points' },
  { key: 'showFib', label: 'Fibonacci detail levels' },
  { key: 'showTargets', label: 'Target levels' },
  { key: 'showDanger', label: 'Danger levels' },
  { key: 'showLabels', label: 'Labels' },
  { key: 'showHtfZones', label: 'Higher timeframe zones' },
  { key: 'showLegend', label: 'Legend' },
  { key: 'showLevelTable', label: 'Level explanation table' },
];

function overlayInScope(overlay: ChartOverlay, settings: OverlaySettings): boolean {
  const isBest = overlay.isBestBullish || overlay.isBestBearish;
  return settings.showOnlyBestSetup ? isBest : settings.showAllSetups || isBest;
}

function overlayVisible(overlay: ChartOverlay, settings: OverlaySettings): boolean {
  switch (overlay.category) {
    case 'Current':
      return true;
    case 'SwingPoint':
      return settings.showSwingPoints;
    case 'HigherTimeframe':
      return settings.showHtfZones;
    default:
      break;
  }

  if (!overlayInScope(overlay, settings)) {
    return false;
  }

  switch (overlay.category) {
    case 'Target':
      return settings.showTargets;
    case 'Danger':
      return settings.showDanger;
    case 'Fibonacci':
      return settings.showFib;
    case 'SetupPoint':
      // Non-primary structure points only when comparing all setups.
      return settings.showLabels && (overlay.isBestBullish || overlay.isBestBearish || settings.showAllSetups);
    default:
      return true;
  }
}

function importanceLineWidth(overlay: ChartOverlay): 1 | 2 | 3 {
  if (overlay.importance === 'High') return 2;
  return 1;
}

const MUTED_COLOR = 'rgba(148,163,184,0.55)';

interface LegendItem {
  label: string;
  meaning: string;
  color: string;
  match: (overlay: ChartOverlay) => boolean;
}

const LEGEND_ITEMS: LegendItem[] = [
  { label: 'Current price', meaning: 'The latest market price.', color: '#facc15', match: (o) => o.levelType === 'CurrentPrice' },
  { label: 'Upward reaction zone', meaning: 'Area where price may pull back and possibly react upward.', color: '#f59e0b', match: (o) => o.levelType === 'ReactionZone' && o.setupDirection === 'Bullish' },
  { label: 'Downward reaction zone', meaning: 'Area where price may bounce and possibly react downward.', color: '#f59e0b', match: (o) => o.levelType === 'ReactionZone' && o.setupDirection === 'Bearish' },
  { label: 'Strong reaction zone', meaning: 'A tighter area where a turn is more likely.', color: '#a855f7', match: (o) => o.levelType === 'StrongReactionZone' },
  { label: 'Danger level', meaning: 'If price crosses this level, the idea is no longer valid.', color: '#ef4444', match: (o) => o.levelType === 'DangerLevel' },
  { label: 'Target 1', meaning: 'First area price may move toward if the idea works.', color: '#22c55e', match: (o) => o.levelType === 'Target1' },
  { label: 'Target 2', meaning: 'Second area price may move toward if the idea works.', color: '#16a34a', match: (o) => o.levelType === 'Target2' },
  { label: 'Fibonacci retracement', meaning: 'Calculated retracement level. Not a trade signal.', color: '#64748b', match: (o) => o.levelType === 'FibonacciRetracement' },
  { label: 'Fibonacci extension', meaning: 'Calculated extension level. Not a trade signal.', color: '#64748b', match: (o) => o.levelType === 'FibonacciExtension' },
  { label: 'Higher timeframe level', meaning: 'An important level from the higher timeframe chart.', color: '#0ea5e9', match: (o) => o.levelType === 'HigherTimeframeLevel' },
  { label: 'Swing high', meaning: 'A recent local peak in price.', color: '#ef4444', match: (o) => o.levelType === 'SwingHigh' },
  { label: 'Swing low', meaning: 'A recent local dip in price.', color: '#22c55e', match: (o) => o.levelType === 'SwingLow' },
  { label: 'Sequence start', meaning: 'Where the analyzed move started.', color: '#38bdf8', match: (o) => o.levelType === 'SequenceStart' },
  { label: 'Pullback point', meaning: 'Where price pulled back during the move.', color: '#38bdf8', match: (o) => o.levelType === 'PullbackPoint' },
];

const TABLE_LEVEL_TYPES = new Set([
  'CurrentPrice',
  'ReactionZone',
  'StrongReactionZone',
  'DangerLevel',
  'Target1',
  'Target2',
  'FibonacciRetracement',
  'FibonacciExtension',
  'HigherTimeframeLevel',
  'SwingHigh',
  'SwingLow',
]);

export interface SkChartExportOptions {
  includeFib?: boolean;
  includeAllSetups?: boolean;
  includeSwings?: boolean;
}

export interface SkSystemChartHandle {
  /** Switches to a clean export view. Returns a function that restores the prior view. */
  enterExportMode: (options: SkChartExportOptions) => () => void;
  /** Waits until chart candles and overlays have been painted. */
  waitForExportReady: () => Promise<void>;
  /** Returns the canvas-only DOM node used for PNG export. */
  getExportElement: () => HTMLElement | null;
  chartMountCount: number;
  overlayUpdateCount: number;
}

export interface SkSystemChartProps {
  result: SkSystemAnalysisResult | null;
}

interface HoverLine {
  overlay: ChartOverlay;
  price?: number;
  low?: number;
  high?: number;
}

interface TooltipState {
  x: number;
  y: number;
  overlay: ChartOverlay;
}

function priceRangeText(overlay: ChartOverlay, decimals: number): string {
  if (overlay.priceLow != null && overlay.priceHigh != null) {
    return `${formatPrice(overlay.priceLow, decimals)} – ${formatPrice(overlay.priceHigh, decimals)}`;
  }
  if (overlay.price != null) {
    return formatPrice(overlay.price, decimals);
  }
  return '—';
}

export const SkSystemChart = forwardRef<SkSystemChartHandle, SkSystemChartProps>(function SkSystemChart(
  { result },
  ref,
) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const exportSurfaceRef = useRef<HTMLDivElement | null>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const candleSeriesRef = useRef<ISeriesApi<'Candlestick'> | null>(null);
  const markersPluginRef = useRef<ISeriesMarkersPluginApi<Time> | null>(null);
  const priceLinesRef = useRef<IPriceLine[]>([]);
  const hoverLinesRef = useRef<HoverLine[]>([]);
  const decimalsRef = useRef(2);
  const chartMountCountRef = useRef(0);
  const overlayUpdateCountRef = useRef(0);
  const exportReadyRef = useRef(true);

  const [settings, setSettings] = useState<OverlaySettings>(DEFAULT_SETTINGS);
  const settingsRef = useRef<OverlaySettings>(DEFAULT_SETTINGS);
  const [focusValue, setFocusValue] = useState('best');
  const [tooltip, setTooltip] = useState<TooltipState | null>(null);
  const [chartMountCount, setChartMountCount] = useState(0);
  const [overlayUpdateCount, setOverlayUpdateCount] = useState(0);

  useEffect(() => {
    settingsRef.current = settings;
  }, [settings]);

  const decimals = result?.priceDecimals || 2;
  decimalsRef.current = decimals;

  const overlays = useMemo(() => result?.chartOverlays ?? [], [result?.chartOverlays]);

  const bestBullId = useMemo(() => overlays.find((o) => o.isBestBullish)?.setupId ?? null, [overlays]);
  const bestBearId = useMemo(() => overlays.find((o) => o.isBestBearish)?.setupId ?? null, [overlays]);

  const otherSetups = useMemo(() => {
    const seen = new Map<string, number>();
    for (const overlay of overlays) {
      if (!overlay.setupId || overlay.isBestBullish || overlay.isBestBearish) continue;
      if (!seen.has(overlay.setupId)) {
        seen.set(overlay.setupId, overlay.setupRank || seen.size + 1);
      }
    }
    return Array.from(seen.entries())
      .sort((a, b) => a[1] - b[1])
      .slice(0, 3)
      .map(([id, rank]) => ({ id, rank }));
  }, [overlays]);

  const focusOptions = useMemo(() => {
    const options: { value: string; label: string }[] = [{ value: 'best', label: 'Best current idea' }];
    if (bestBullId) options.push({ value: bestBullId, label: 'Best upward idea' });
    if (bestBearId) options.push({ value: bestBearId, label: 'Best downward idea' });
    otherSetups.forEach((setup) => options.push({ value: setup.id, label: `Other structure #${setup.rank}` }));
    return options;
  }, [bestBullId, bestBearId, otherSetups]);

  const focusedSetupIds = useMemo(() => {
    if (focusValue === 'best') {
      return new Set([bestBullId, bestBearId].filter(Boolean) as string[]);
    }
    return new Set([focusValue]);
  }, [focusValue, bestBullId, bestBearId]);

  const isDimmed = (overlay: ChartOverlay): boolean => {
    if (!overlay.setupId || overlay.category === 'Current') return false;
    return focusedSetupIds.size > 0 && !focusedSetupIds.has(overlay.setupId);
  };

  const setupInfo = useMemo(() => {
    const map = new Map<string, { danger?: string; target1?: string; target2?: string }>();
    for (const overlay of overlays) {
      if (!overlay.setupId) continue;
      const entry = map.get(overlay.setupId) ?? {};
      if (overlay.levelType === 'DangerLevel' && overlay.price != null) entry.danger = formatPrice(overlay.price, decimals);
      if (overlay.levelType === 'Target1' && overlay.price != null) entry.target1 = formatPrice(overlay.price, decimals);
      if (overlay.levelType === 'Target2' && overlay.price != null) entry.target2 = formatPrice(overlay.price, decimals);
      map.set(overlay.setupId, entry);
    }
    return map;
  }, [overlays, decimals]);

  const visibleOverlays = useMemo(
    () => overlays.filter((overlay) => overlayVisible(overlay, settings)),
    [overlays, settings],
  );

  const candleData = useMemo(
    () =>
      (result?.candles ?? []).map((candle) => ({
        time: toChartTime(candle.timeUtc),
        open: candle.open,
        high: candle.high,
        low: candle.low,
        close: candle.close,
      })),
    [result?.candles],
  );

  useImperativeHandle(
    ref,
    () => ({
      enterExportMode: (options: SkChartExportOptions) => {
        const previous = settingsRef.current;
        const exportView: OverlaySettings = {
          showOnlyBestSetup: !options.includeAllSetups,
          showAllSetups: !!options.includeAllSetups,
          showSwingPoints: !!options.includeSwings,
          showFib: !!options.includeFib,
          showTargets: true,
          showDanger: true,
          showLabels: true,
          showHtfZones: false,
          showLegend: true,
          showLevelTable: true,
        };
        setSettings(exportView);
        return () => setSettings(previous);
      },
      waitForExportReady: () =>
        new Promise<void>((resolve) => {
          exportReadyRef.current = false;
          const start = Date.now();
          const poll = () => {
            if (exportReadyRef.current || Date.now() - start > 5000) {
              resolve();
              return;
            }
            requestAnimationFrame(poll);
          };
          requestAnimationFrame(poll);
        }),
      getExportElement: () => exportSurfaceRef.current,
      chartMountCount: chartMountCountRef.current,
      overlayUpdateCount: overlayUpdateCountRef.current,
    }),
    [],
  );

  useEffect(() => {
    if (!containerRef.current) return;

    const chart = createChart(containerRef.current, {
      layout: {
        background: { type: ColorType.Solid, color: '#0f172a' },
        textColor: '#cbd5e1',
      },
      grid: {
        vertLines: { color: '#1e293b' },
        horzLines: { color: '#1e293b' },
      },
      rightPriceScale: { borderColor: '#334155' },
      timeScale: { borderColor: '#334155', timeVisible: true, secondsVisible: false },
      crosshair: { mode: 1 },
      width: containerRef.current.clientWidth,
      height: 460,
    });

    chartRef.current = chart;
    chartMountCountRef.current += 1;
    setChartMountCount(chartMountCountRef.current);
    const candleSeries = chart.addSeries(CandlestickSeries, {
      upColor: '#22c55e',
      downColor: '#ef4444',
      borderVisible: false,
      wickUpColor: '#22c55e',
      wickDownColor: '#ef4444',
    });
    candleSeriesRef.current = candleSeries;
    markersPluginRef.current = createSeriesMarkers(candleSeries);

    const handleCrosshair = (param: { point?: { x: number; y: number }; time?: unknown }) => {
      const series = candleSeriesRef.current;
      if (!series || !param.point || param.time === undefined) {
        setTooltip(null);
        return;
      }
      const y = param.point.y;
      let picked: HoverLine | null = null;
      let pickedDist = 9;
      for (const line of hoverLinesRef.current) {
        if (line.low != null && line.high != null) {
          const cHigh = series.priceToCoordinate(line.high);
          const cLow = series.priceToCoordinate(line.low);
          if (cHigh == null || cLow == null) continue;
          const top = Math.min(cHigh, cLow) - 2;
          const bottom = Math.max(cHigh, cLow) + 2;
          if (y >= top && y <= bottom) {
            picked = line;
            pickedDist = 0;
            break;
          }
        } else if (line.price != null) {
          const coord = series.priceToCoordinate(line.price);
          if (coord == null) continue;
          const dist = Math.abs(coord - y);
          if (dist < pickedDist) {
            pickedDist = dist;
            picked = line;
          }
        }
      }
      if (picked) {
        setTooltip({ x: param.point.x, y, overlay: picked.overlay });
      } else {
        setTooltip(null);
      }
    };

    chart.subscribeCrosshairMove(handleCrosshair);

    const resizeObserver = new ResizeObserver(() => {
      if (containerRef.current) {
        chart.applyOptions({ width: containerRef.current.clientWidth });
      }
    });
    resizeObserver.observe(containerRef.current);

    return () => {
      resizeObserver.disconnect();
      chart.unsubscribeCrosshairMove(handleCrosshair);
      chart.remove();
      chartRef.current = null;
      candleSeriesRef.current = null;
      markersPluginRef.current = null;
      priceLinesRef.current = [];
      hoverLinesRef.current = [];
    };
  }, []);

  useEffect(() => {
    const candleSeries = candleSeriesRef.current;
    const chart = chartRef.current;
    if (!candleSeries || !chart) return;

    exportReadyRef.current = false;
    candleSeries.setData(candleData);

    for (const line of priceLinesRef.current) {
      try {
        candleSeries.removePriceLine(line);
      } catch {
        // Ignore lines already removed with the series.
      }
    }
    priceLinesRef.current = [];
    const hoverLines: HoverLine[] = [];

    // Auto-declutter: if too many labeled lines are visible, hide their axis titles.
    const labeledLines = visibleOverlays.filter(
      (o) => o.type === 'HorizontalLine' || o.type === 'Zone' || o.type.startsWith('Fibonacci'),
    ).length;
    const crowded = labeledLines > 14;

    const markers: SeriesMarker<Time>[] = [];

    for (const overlay of visibleOverlays) {
      const dimmed = isDimmed(overlay);
      const color = dimmed ? MUTED_COLOR : overlay.color;
      const primaryLabel = overlay.isBestBullish || overlay.isBestBearish || overlay.category === 'Current';
      const showTitle =
        settings.showLabels && !crowded && !dimmed && (primaryLabel || settings.showAllSetups);
      const width = dimmed ? 1 : importanceLineWidth(overlay);
      const detail = overlay.isAdvanced || dimmed;

      switch (overlay.type) {
        case 'HorizontalLine': {
          if (overlay.price == null) break;
          priceLinesRef.current.push(
            candleSeries.createPriceLine({
              price: overlay.price,
              color,
              lineWidth: width,
              lineStyle: detail ? LineStyle.Dotted : LineStyle.Dashed,
              axisLabelVisible: !dimmed,
              title: showTitle ? overlay.shortLabel : '',
            }),
          );
          hoverLines.push({ overlay, price: overlay.price });
          break;
        }
        case 'Zone':
        case 'FibonacciRetracement':
        case 'FibonacciExtension': {
          const bounds =
            overlay.type === 'Zone' ? [overlay.priceLow, overlay.priceHigh] : [overlay.price];
          let labeled = false;
          for (const bound of bounds) {
            if (bound == null) continue;
            priceLinesRef.current.push(
              candleSeries.createPriceLine({
                price: bound,
                color,
                lineWidth: 1,
                lineStyle: LineStyle.Dotted,
                axisLabelVisible: false,
                title: showTitle && !labeled ? overlay.shortLabel : '',
              }),
            );
            labeled = true;
          }
          if (overlay.type === 'Zone') {
            hoverLines.push({ overlay, low: overlay.priceLow ?? undefined, high: overlay.priceHigh ?? undefined });
          } else if (overlay.price != null) {
            hoverLines.push({ overlay, price: overlay.price });
          }
          break;
        }
        case 'Marker':
        case 'Label': {
          if (!overlay.timeUtc) break;
          const isHigh = overlay.direction === 'Bearish';
          markers.push({
            time: toChartTime(overlay.timeUtc),
            position: isHigh ? 'aboveBar' : 'belowBar',
            color,
            shape: 'circle',
            text: showTitle ? overlay.shortLabel : '',
          });
          break;
        }
        case 'ScenarioArrow': {
          if (!overlay.timeUtc) break;
          const bullish = overlay.direction === 'Bullish';
          markers.push({
            time: toChartTime(overlay.timeUtc),
            position: bullish ? 'belowBar' : 'aboveBar',
            color,
            shape: bullish ? 'arrowUp' : 'arrowDown',
            text: showTitle ? overlay.shortLabel : '',
          });
          break;
        }
        default:
          break;
      }
    }

    hoverLinesRef.current = hoverLines;
    markers.sort((a, b) => Number(a.time) - Number(b.time));
    markersPluginRef.current?.setMarkers(markers);

    chart.timeScale().fitContent();
    overlayUpdateCountRef.current += 1;
    setOverlayUpdateCount(overlayUpdateCountRef.current);
    exportReadyRef.current = true;
  }, [candleData, visibleOverlays, settings, focusedSetupIds]);

  const tableRows = useMemo(
    () =>
      visibleOverlays
        .filter((overlay) => TABLE_LEVEL_TYPES.has(overlay.levelType))
        .map((overlay) => ({
          overlay,
          priceText: priceRangeText(overlay, decimals),
        })),
    [visibleOverlays, decimals],
  );

  const applyPreset = (view: OverlaySettings) => setSettings({ ...view });

  if (!result || result.candles.length === 0) {
    return (
      <p className="rounded-xl border border-slate-800 bg-slate-900/40 py-16 text-center text-sm text-slate-400">
        No candle data available for chart.
      </p>
    );
  }

  const focusOverlaySetup = setupInfo.get(tooltip?.overlay.setupId ?? '');

  return (
    <div className="rounded-xl border border-slate-700 bg-slate-900/60 p-3">
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <span className="text-xs font-medium uppercase tracking-wide text-slate-400">Quick view:</span>
        <button
          type="button"
          onClick={() => applyPreset(CLEAN_VIEW)}
          className="rounded-md border border-slate-600 px-2 py-1 text-xs text-slate-200 hover:bg-slate-800"
        >
          Clean
        </button>
        <button
          type="button"
          onClick={() => applyPreset(BEGINNER_VIEW)}
          className="rounded-md border border-slate-600 px-2 py-1 text-xs text-slate-200 hover:bg-slate-800"
        >
          Beginner
        </button>
        <button
          type="button"
          onClick={() => applyPreset(ADVANCED_VIEW)}
          className="rounded-md border border-slate-600 px-2 py-1 text-xs text-slate-200 hover:bg-slate-800"
        >
          Advanced
        </button>

        <span className="ml-2 text-xs font-medium uppercase tracking-wide text-slate-400">Focus:</span>
        <select
          value={focusValue}
          onChange={(event) => setFocusValue(event.target.value)}
          className="rounded-md border border-slate-600 bg-slate-900 px-2 py-1 text-xs text-slate-200"
        >
          {focusOptions.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </div>

      <div className="mb-3 flex flex-wrap gap-x-4 gap-y-1">
        {MANUAL_TOGGLES.map((toggle) => (
          <label key={toggle.key} className="flex items-center gap-1.5 text-xs text-slate-300">
            <input
              type="checkbox"
              checked={settings[toggle.key]}
              onChange={(event) => setSettings((prev) => ({ ...prev, [toggle.key]: event.target.checked }))}
            />
            {toggle.label}
          </label>
        ))}
      </div>

      <div ref={exportSurfaceRef} className="relative rounded-lg border border-slate-800 bg-[#0f172a]">
        <div ref={containerRef} className="w-full" />
        {tooltip ? (
          <div
            className="pointer-events-none absolute z-10 max-w-[260px] rounded-lg border border-slate-700 bg-slate-950/95 p-3 text-xs text-slate-200 shadow-lg"
            style={{
              left: Math.min(tooltip.x + 12, (containerRef.current?.clientWidth ?? 400) - 260),
              top: Math.max(tooltip.y - 10, 4),
            }}
          >
            <p className="font-semibold text-slate-100">{tooltip.overlay.tooltipTitle || tooltip.overlay.displayName}</p>
            <p className="text-slate-300">{priceRangeText(tooltip.overlay, decimals)}</p>
            <p className="mt-1 text-slate-400">{tooltip.overlay.plainLanguageMeaning}</p>
            <p className="mt-1 text-slate-500">Group: {tooltip.overlay.groupName}</p>
            {(tooltip.overlay.levelType === 'ReactionZone' || tooltip.overlay.levelType === 'StrongReactionZone') &&
            focusOverlaySetup ? (
              <div className="mt-1 text-slate-400">
                {focusOverlaySetup.danger ? <div>Danger level: {focusOverlaySetup.danger}</div> : null}
                {focusOverlaySetup.target1 ? (
                  <div>
                    Targets: {focusOverlaySetup.target1}
                    {focusOverlaySetup.target2 ? `, then ${focusOverlaySetup.target2}` : ''}
                  </div>
                ) : null}
              </div>
            ) : null}
          </div>
        ) : null}
      </div>

      <p className="mt-2 text-xs text-slate-500">
        SK overlays are calculated levels for analysis only. This is not a trade signal.
      </p>
      <p className="mt-1 text-xs text-slate-500">
        Turn on Fibonacci detail levels to see calculated retracement and extension levels. Keep it off for a
        cleaner beginner view. “All possible setups” can make the chart crowded — use it only when comparing
        structures.
      </p>

      {settings.showLegend ? (
        <div className="mt-3 rounded-lg border border-slate-800 bg-slate-950/40 p-3">
          <p className="mb-2 text-xs font-medium uppercase tracking-wide text-slate-400">Chart legend</p>
          <div className="grid gap-x-6 gap-y-1 md:grid-cols-2">
            {LEGEND_ITEMS.map((item) => {
              const visible = visibleOverlays.some((overlay) => item.match(overlay));
              return (
                <div key={item.label} className={`flex items-start gap-2 text-xs ${visible ? 'text-slate-200' : 'text-slate-500'}`}>
                  <span className="mt-1 inline-block h-2 w-4 flex-shrink-0 rounded" style={{ backgroundColor: item.color }} />
                  <span>
                    <span className="font-medium">{item.label}</span>
                    <span className="text-slate-400"> — {item.meaning}</span>
                    <span className={visible ? 'text-emerald-400' : 'text-slate-600'}> {visible ? '(visible)' : '(hidden)'}</span>
                  </span>
                </div>
              );
            })}
          </div>
          <p className="mt-2 text-xs text-slate-500">
            Fibonacci levels are calculated levels used by the analyzer. They are not trade signals.
          </p>
        </div>
      ) : null}

      {settings.showLevelTable ? (
        <div className="mt-3 overflow-x-auto rounded-lg border border-slate-800 bg-slate-950/40 p-3">
          <p className="mb-2 text-xs font-medium uppercase tracking-wide text-slate-400">Chart Levels Explained</p>
          <table className="w-full text-left text-xs">
            <thead className="text-slate-500">
              <tr>
                <th className="py-1 pr-3">Setup</th>
                <th className="py-1 pr-3">Level</th>
                <th className="py-1 pr-3">Price / Range</th>
                <th className="py-1 pr-3">What it means</th>
                <th className="py-1">Visible</th>
              </tr>
            </thead>
            <tbody className="text-slate-300">
              {tableRows.map(({ overlay, priceText }, index) => (
                <tr key={`${overlay.levelType}-${overlay.setupId ?? 'none'}-${index}`} className="border-t border-slate-800">
                  <td className="py-1 pr-3">{overlay.groupName}</td>
                  <td className="py-1 pr-3">{overlay.displayName}</td>
                  <td className="py-1 pr-3">{priceText}</td>
                  <td className="py-1 pr-3 text-slate-400">{overlay.plainLanguageMeaning}</td>
                  <td className="py-1 text-emerald-400">Yes</td>
                </tr>
              ))}
              {tableRows.length === 0 ? (
                <tr>
                  <td colSpan={5} className="py-2 text-slate-500">
                    No levels are currently visible. Enable a view or toggle above.
                  </td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </div>
      ) : null}

      {import.meta.env.DEV ? (
        <p className="mt-2 text-[11px] text-slate-600">
          Chart mounted: {chartMountCount} time{chartMountCount === 1 ? '' : 's'} · Overlay updates: {overlayUpdateCount}
        </p>
      ) : null}
    </div>
  );
});
