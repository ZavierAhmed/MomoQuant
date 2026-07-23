import { useEffect, useRef, useState } from 'react';
import {
  CandlestickSeries,
  ColorType,
  createChart,
  LineStyle,
  type IChartApi,
  type IPriceLine,
  type ISeriesApi,
  type UTCTimestamp,
} from 'lightweight-charts';
import { marketDataApi } from '@/api/marketDataApi';
import type { DiagnosticEvent } from '@/api/strategyLabApi';
import { formatNumber } from '@/components/common/utils';

type Props = {
  exchangeId: number;
  symbolId: number;
  timeframe: string;
  events: DiagnosticEvent[];
};

function toChartTime(iso: string): UTCTimestamp {
  return Math.floor(new Date(iso).getTime() / 1000) as UTCTimestamp;
}

export function StrategyLabDiagnosticChart({ exchangeId, symbolId, timeframe, events }: Props) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const seriesRef = useRef<ISeriesApi<'Candlestick'> | null>(null);
  const lineRef = useRef<IPriceLine | null>(null);
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [showLevel, setShowLevel] = useState(true);
  const [showEventMarker, setShowEventMarker] = useState(true);

  const selected = events[selectedIndex];

  useEffect(() => {
    if (!containerRef.current || chartRef.current) return;
    const chart = createChart(containerRef.current, {
      height: 320,
      layout: {
        background: { type: ColorType.Solid, color: '#0f172a' },
        textColor: '#cbd5e1',
      },
      grid: {
        vertLines: { color: '#1e293b' },
        horzLines: { color: '#1e293b' },
      },
      rightPriceScale: { borderColor: '#334155' },
      timeScale: { borderColor: '#334155' },
    });
    const series = chart.addSeries(CandlestickSeries, {
      upColor: '#34d399',
      downColor: '#f87171',
      borderVisible: false,
      wickUpColor: '#34d399',
      wickDownColor: '#f87171',
    });
    chartRef.current = chart;
    seriesRef.current = series;

    const resize = () => {
      if (!containerRef.current || !chartRef.current) return;
      chartRef.current.applyOptions({ width: containerRef.current.clientWidth });
    };
    resize();
    window.addEventListener('resize', resize);
    return () => {
      window.removeEventListener('resize', resize);
      chart.remove();
      chartRef.current = null;
      seriesRef.current = null;
    };
  }, []);

  useEffect(() => {
    if (!selected || !seriesRef.current || !chartRef.current) return;
    const anchor = selected.eventTimestampUtc ?? selected.levelTimestampUtc;
    if (!anchor) return;

    let cancelled = false;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const center = new Date(anchor);
        const from = new Date(center.getTime() - 20 * 60 * 60 * 1000).toISOString();
        const to = new Date(center.getTime() + 20 * 60 * 60 * 1000).toISOString();
        const candles = await marketDataApi.getCandles({
          symbolId,
          timeframe,
          fromUtc: from,
          toUtc: to,
          limit: 200,
        });
        if (cancelled) return;
        const rows = (candles ?? []).map((c) => ({
          time: toChartTime(c.openTimeUtc),
          open: Number(c.open),
          high: Number(c.high),
          low: Number(c.low),
          close: Number(c.close),
        }));
        seriesRef.current?.setData(rows);
        if (lineRef.current) {
          seriesRef.current?.removePriceLine(lineRef.current);
          lineRef.current = null;
        }
        if (showLevel && selected.level > 0) {
          lineRef.current = seriesRef.current!.createPriceLine({
            price: selected.level,
            color: '#38bdf8',
            lineWidth: 2,
            lineStyle: LineStyle.Dashed,
            axisLabelVisible: true,
            title: selected.stage,
          });
        }
        chartRef.current?.timeScale().fitContent();
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Failed to load diagnostic candles.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    };
    void load();
    return () => {
      cancelled = true;
    };
  }, [selected, exchangeId, symbolId, timeframe, showLevel, showEventMarker]);

  if (events.length === 0) {
    return <p className="text-sm text-slate-400">No diagnostic sample events were recorded for this run.</p>;
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        <label className="text-sm text-slate-300">
          Event
          <select
            className="ml-2 rounded border border-slate-700 bg-slate-900 px-2 py-1"
            value={selectedIndex}
            onChange={(e) => setSelectedIndex(Number(e.target.value))}
          >
            {events.map((event, index) => (
              <option key={`${event.stage}-${index}`} value={index}>
                {event.stage} · {event.direction} · {event.outcome}
              </option>
            ))}
          </select>
        </label>
        <label className="flex items-center gap-1 text-sm text-slate-300">
          <input type="checkbox" checked={showLevel} onChange={(e) => setShowLevel(e.target.checked)} />
          Level line
        </label>
        <label className="flex items-center gap-1 text-sm text-slate-300">
          <input type="checkbox" checked={showEventMarker} onChange={(e) => setShowEventMarker(e.target.checked)} />
          Event details
        </label>
      </div>
      {showEventMarker && selected ? (
        <div className="rounded border border-slate-800 bg-slate-950/50 px-3 py-2 text-sm text-slate-300">
          Level {formatNumber(selected.level)} · {selected.outcome}
          {selected.eventTimestampUtc ? ` · ${new Date(selected.eventTimestampUtc).toLocaleString()}` : ''}
          {selected.reason ? ` · ${selected.reason}` : ''}
        </div>
      ) : null}
      {loading ? <p className="text-sm text-slate-400">Loading candles...</p> : null}
      {error ? <p className="text-sm text-rose-300">{error}</p> : null}
      <div ref={containerRef} className="w-full rounded border border-slate-800" />
    </div>
  );
}
