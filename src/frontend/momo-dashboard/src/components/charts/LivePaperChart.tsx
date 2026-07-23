import { useEffect, useMemo, useRef, useState } from 'react';
import {
  CandlestickSeries,
  ColorType,
  createChart,
  createSeriesMarkers,
  HistogramSeries,
  LineSeries,
  type IChartApi,
  type ISeriesApi,
  type ISeriesMarkersPluginApi,
  type SeriesMarker,
  type Time,
  type UTCTimestamp,
} from 'lightweight-charts';
import type { LivePaperChartData, LivePaperChartMarker } from '@/api/domainTypes';
import { toChartTime } from '@/components/charts/replayChartUtils';

interface LivePaperChartProps {
  chartData: LivePaperChartData | null;
}

export function LivePaperChart({ chartData }: LivePaperChartProps) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const candleSeriesRef = useRef<ISeriesApi<'Candlestick'> | null>(null);
  const markersPluginRef = useRef<ISeriesMarkersPluginApi<Time> | null>(null);
  const overlaySeriesRef = useRef<ISeriesApi<'Line' | 'Histogram'>[]>([]);
  const [showVolume, setShowVolume] = useState(true);
  const [showEma20, setShowEma20] = useState(true);
  const [showEma50, setShowEma50] = useState(true);
  const [showEma200, setShowEma200] = useState(true);
  const [showVwap, setShowVwap] = useState(true);
  const [showRangeLevels, setShowRangeLevels] = useState(true);

  const candleSeriesData = useMemo(
    () =>
      (chartData?.candles ?? []).map((candle) => ({
        time: toChartTime(candle.time) as UTCTimestamp,
        open: Number(candle.open),
        high: Number(candle.high),
        low: Number(candle.low),
        close: Number(candle.close),
        borderColor: candle.isForming ? '#38bdf8' : undefined,
        wickColor: candle.isForming ? '#38bdf8' : undefined,
      })),
    [chartData?.candles],
  );

  const markers = useMemo(() => {
    if (!chartData) return [] as SeriesMarker<Time>[];
    const all: LivePaperChartMarker[] = [
      ...chartData.orderMarkers,
      ...chartData.tradeMarkers,
      ...chartData.missedOrderMarkers,
      ...chartData.riskMarkers,
      ...chartData.aiMarkers,
    ];

    return all
      .filter((marker) => marker.time)
      .map((marker) => ({
        time: toChartTime(marker.time) as UTCTimestamp,
        position: marker.type.includes('Exit') || marker.side === 'Sell' ? ('aboveBar' as const) : ('belowBar' as const),
        color: marker.color ?? '#94a3b8',
        shape: marker.type.includes('Exit') ? ('arrowDown' as const) : ('arrowUp' as const),
        text: marker.label ?? marker.type,
      }))
      .sort((a, b) => Number(a.time) - Number(b.time));
  }, [chartData]);

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
      height: 420,
    });

    chartRef.current = chart;
    const candleSeries = chart.addSeries(CandlestickSeries, {
      upColor: '#22c55e',
      downColor: '#ef4444',
      borderVisible: false,
      wickUpColor: '#22c55e',
      wickDownColor: '#ef4444',
    });
    candleSeriesRef.current = candleSeries;
    markersPluginRef.current = createSeriesMarkers(candleSeries);

    const resizeObserver = new ResizeObserver(() => {
      if (containerRef.current) {
        chart.applyOptions({ width: containerRef.current.clientWidth });
      }
    });
    resizeObserver.observe(containerRef.current);

    return () => {
      resizeObserver.disconnect();
      for (const series of overlaySeriesRef.current) {
        try {
          chart.removeSeries(series);
        } catch {
          // ignore
        }
      }
      overlaySeriesRef.current = [];
      chart.remove();
      chartRef.current = null;
      candleSeriesRef.current = null;
      markersPluginRef.current = null;
    };
  }, []);

  useEffect(() => {
    const chart = chartRef.current;
    const candleSeries = candleSeriesRef.current;
    if (!chart || !candleSeries || !chartData) return;

    candleSeries.setData(candleSeriesData);
    markersPluginRef.current?.setMarkers(markers);

    for (const series of overlaySeriesRef.current) {
      try {
        chart.removeSeries(series);
      } catch {
        // ignore
      }
    }
    overlaySeriesRef.current = [];

    const addLine = (key: 'ema20' | 'ema50' | 'ema200' | 'vwap', color: string, enabled: boolean) => {
      if (!enabled) return;
      const line = chart.addSeries(LineSeries, { color, lineWidth: 2, priceLineVisible: false });
      overlaySeriesRef.current.push(line);
      line.setData(
        chartData.indicators
          .filter((item) => item[key] != null)
          .map((item) => ({
            time: toChartTime(item.time) as UTCTimestamp,
            value: Number(item[key]),
          })),
      );
    };

    addLine('ema20', '#38bdf8', showEma20);
    addLine('ema50', '#a78bfa', showEma50);
    addLine('ema200', '#f59e0b', showEma200);
    addLine('vwap', '#f472b6', showVwap);

    if (showRangeLevels) {
      for (const level of chartData.rangeLevels ?? []) {
        if (level.price == null || !level.startUtc || !level.endUtc) continue;
        const line = chart.addSeries(LineSeries, {
          color: level.color,
          lineWidth: 2,
          priceLineVisible: false,
          title: level.label,
        });
        overlaySeriesRef.current.push(line);
        line.setData([
          { time: toChartTime(level.startUtc) as UTCTimestamp, value: Number(level.price) },
          { time: toChartTime(level.endUtc) as UTCTimestamp, value: Number(level.price) },
        ]);
      }
    }

    if (showVolume) {
      const volumeSeries = chart.addSeries(HistogramSeries, {
        priceFormat: { type: 'volume' },
        priceScaleId: 'volume',
      });
      overlaySeriesRef.current.push(volumeSeries);
      chart.priceScale('volume').applyOptions({ scaleMargins: { top: 0.8, bottom: 0 } });
      volumeSeries.setData(
        chartData.candles.map((candle) => ({
          time: toChartTime(candle.time) as UTCTimestamp,
          value: Number(candle.volume),
          color: Number(candle.close) >= Number(candle.open) ? 'rgba(34,197,94,0.4)' : 'rgba(239,68,68,0.4)',
        })),
      );
    }

    chart.timeScale().scrollToRealTime();
  }, [chartData, candleSeriesData, markers, showEma20, showEma50, showEma200, showVwap, showRangeLevels, showVolume]);

  return (
    <div className="rounded-xl border border-slate-800 bg-slate-950/60 p-3">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <div>
          <p className="text-sm font-medium text-slate-100">
            {chartData ? `${chartData.symbol} ${chartData.timeframe}` : 'Live Chart'}
          </p>
          <p className="text-xs text-slate-500">
            {chartData?.latestPrice != null ? `Latest ${chartData.latestPrice}` : 'Waiting for live price'}
            {chartData?.currentCandle?.isForming ? ' · Forming candle' : ''}
          </p>
        </div>
        <div className="flex flex-wrap gap-2 text-xs text-slate-300">
          <Toggle label="EMA20" checked={showEma20} onChange={setShowEma20} />
          <Toggle label="EMA50" checked={showEma50} onChange={setShowEma50} />
          <Toggle label="EMA200" checked={showEma200} onChange={setShowEma200} />
          <Toggle label="VWAP" checked={showVwap} onChange={setShowVwap} />
          <Toggle label="NY 4H Range" checked={showRangeLevels} onChange={setShowRangeLevels} />
          <Toggle label="Volume" checked={showVolume} onChange={setShowVolume} />
        </div>
      </div>
      <div ref={containerRef} className="h-[420px] w-full" />
    </div>
  );
}

function Toggle({
  label,
  checked,
  onChange,
}: {
  label: string;
  checked: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <label className="flex items-center gap-1 rounded border border-slate-700 px-2 py-1">
      <input type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} />
      {label}
    </label>
  );
}
