import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
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
import type {
  ReplayChartData,
  ReplayChartExecutionMarker,
  ReplayChartRiskMarker,
  ReplayChartStrategyMarker,
} from '@/api/domainTypes';
import { findCandleForFrame, toChartTime } from '@/components/charts/replayChartUtils';

export interface ReplayChartToggleState {
  ema20: boolean;
  ema50: boolean;
  ema200: boolean;
  vwap: boolean;
  volume: boolean;
  strategySignals: boolean;
  riskDecisions: boolean;
  ordersFills: boolean;
  missedOrders: boolean;
  trades: boolean;
  noTradeMarkers: boolean;
  rangeLevels: boolean;
}

const DEFAULT_TOGGLES: ReplayChartToggleState = {
  ema20: true,
  ema50: true,
  ema200: false,
  vwap: true,
  volume: true,
  strategySignals: true,
  riskDecisions: true,
  ordersFills: true,
  missedOrders: true,
  trades: true,
  noTradeMarkers: false,
  rangeLevels: true,
};

export interface ReplayChartProps {
  chartData: ReplayChartData | null;
  currentFrameIndex: number;
  strictReplayMode: boolean;
  autoFollow?: boolean;
  onUserInteraction?: () => void;
  onCandleClick?: (frameIndex: number) => void;
}

export function ReplayChart({
  chartData,
  currentFrameIndex,
  strictReplayMode,
  autoFollow = true,
  onUserInteraction,
  onCandleClick,
}: ReplayChartProps) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const candleSeriesRef = useRef<ISeriesApi<'Candlestick'> | null>(null);
  const markersPluginRef = useRef<ISeriesMarkersPluginApi<Time> | null>(null);
  const overlaySeriesRef = useRef<ISeriesApi<'Line' | 'Histogram'>[]>([]);
  const lastFollowedFrameRef = useRef<number | null>(null);
  const onUserInteractionRef = useRef(onUserInteraction);
  onUserInteractionRef.current = onUserInteraction;

  const clearOverlaySeries = (chart: IChartApi | null) => {
    if (chart) {
      for (const series of overlaySeriesRef.current) {
        try {
          chart.removeSeries(series);
        } catch {
          // Series may already be removed when the chart was destroyed/recreated.
        }
      }
    }
    overlaySeriesRef.current = [];
  };

  const [toggles, setToggles] = useState<ReplayChartToggleState>(DEFAULT_TOGGLES);
  const [hoverFrameIndex, setHoverFrameIndex] = useState<number | null>(null);

  const candleSeriesData = useMemo(
    () =>
      (chartData?.candles ?? []).map((candle) => ({
        time: toChartTime(candle.time) as UTCTimestamp,
        open: candle.open,
        high: candle.high,
        low: candle.low,
        close: candle.close,
        color: candle.isFutureContext ? '#475569' : undefined,
        wickColor: candle.isFutureContext ? '#475569' : undefined,
        borderColor: candle.isFutureContext ? '#475569' : undefined,
      })),
    [chartData?.candles],
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

    const handleVisibleRangeChange = () => onUserInteractionRef.current?.();
    chart.timeScale().subscribeVisibleLogicalRangeChange(handleVisibleRangeChange);

    const resizeObserver = new ResizeObserver(() => {
      if (containerRef.current) {
        chart.applyOptions({ width: containerRef.current.clientWidth });
      }
    });
    resizeObserver.observe(containerRef.current);

    return () => {
      resizeObserver.disconnect();
      chart.timeScale().unsubscribeVisibleLogicalRangeChange(handleVisibleRangeChange);
      clearOverlaySeries(chart);
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
  }, [chartData, candleSeriesData]);

  useEffect(() => {
    const chart = chartRef.current;
    if (!chart || !chartData) return;

    clearOverlaySeries(chart);

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

    addLine('ema20', '#38bdf8', toggles.ema20);
    addLine('ema50', '#a78bfa', toggles.ema50);
    addLine('ema200', '#f59e0b', toggles.ema200);
    addLine('vwap', '#f472b6', toggles.vwap);

    if (toggles.rangeLevels) {
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

    if (toggles.volume) {
      const volumeSeries = chart.addSeries(HistogramSeries, {
        priceFormat: { type: 'volume' },
        priceScaleId: 'volume',
      });
      overlaySeriesRef.current.push(volumeSeries);
      chart.priceScale('volume').applyOptions({ scaleMargins: { top: 0.8, bottom: 0 } });
      volumeSeries.setData(
        chartData.candles.map((candle) => ({
          time: toChartTime(candle.time) as UTCTimestamp,
          value: candle.volume,
          color: candle.close >= candle.open ? 'rgba(34,197,94,0.4)' : 'rgba(239,68,68,0.4)',
        })),
      );
    }

    return () => {
      clearOverlaySeries(chart);
    };
  }, [chartData, toggles]);

  const buildMarkers = useCallback((): SeriesMarker<Time>[] => {
    if (!chartData) return [];

    const markers: SeriesMarker<Time>[] = [];

    if (toggles.strategySignals) {
      for (const marker of chartData.strategyMarkers) {
        if (marker.signalType.toLowerCase() === 'notrade' && !toggles.noTradeMarkers) continue;
        markers.push(buildStrategyMarker(marker));
      }
    }

    if (toggles.riskDecisions) {
      for (const marker of chartData.riskMarkers) {
        markers.push(buildRiskMarker(marker));
      }
    }

    if (toggles.ordersFills || toggles.missedOrders || toggles.trades) {
      for (const marker of chartData.executionMarkers) {
        if (marker.type === 'MissedOrder' && !toggles.missedOrders) continue;
        if ((marker.type === 'OrderFilled' || marker.type === 'OrderPlaced') && !toggles.ordersFills) continue;
        if ((marker.type === 'TradeEntry' || marker.type === 'TradeExit') && !toggles.trades) continue;
        markers.push(buildExecutionMarker(marker));
      }
    }

    const currentCandle = findCandleForFrame(chartData.candles, currentFrameIndex);
    if (currentCandle) {
      markers.push({
        time: toChartTime(currentCandle.time) as UTCTimestamp,
        position: 'inBar',
        color: '#facc15',
        shape: 'circle',
        text: `Frame ${currentFrameIndex}`,
      });
    }

    markers.sort((a, b) => Number(a.time) - Number(b.time));
    return markers;
  }, [chartData, currentFrameIndex, toggles]);

  useEffect(() => {
    markersPluginRef.current?.setMarkers(buildMarkers());
  }, [buildMarkers]);

  useEffect(() => {
    const chart = chartRef.current;
    if (!chart || !chartData) return;

    const currentCandle = findCandleForFrame(chartData.candles, currentFrameIndex);
    if (!currentCandle) {
      if (chartData.candles.length > 0 && lastFollowedFrameRef.current === null) {
        chart.timeScale().fitContent();
      }
      return;
    }

    if (!autoFollow) {
      return;
    }

    if (lastFollowedFrameRef.current === currentFrameIndex) {
      return;
    }

    lastFollowedFrameRef.current = currentFrameIndex;
    chart.timeScale().setVisibleRange({
      from: (toChartTime(currentCandle.time) - 3600) as UTCTimestamp,
      to: (toChartTime(currentCandle.time) + 3600) as UTCTimestamp,
    });
  }, [autoFollow, chartData, currentFrameIndex]);

  useEffect(() => {
    const chart = chartRef.current;
    if (!chart || !onCandleClick || !chartData) return;

    const clickHandler = (param: { time?: Time }) => {
      if (!param.time) return;
      const candle = chartData.candles.find((item) => toChartTime(item.time) === Number(param.time));
      if (candle) onCandleClick(candle.frameIndex);
    };

    const crosshairHandler = (param: { time?: Time }) => {
      if (!param.time) {
        setHoverFrameIndex(null);
        return;
      }
      const candle = chartData.candles.find((item) => toChartTime(item.time) === Number(param.time));
      setHoverFrameIndex(candle?.frameIndex ?? null);
    };

    chart.subscribeClick(clickHandler);
    chart.subscribeCrosshairMove(crosshairHandler);

    return () => {
      chart.unsubscribeClick(clickHandler);
      chart.unsubscribeCrosshairMove(crosshairHandler);
    };
  }, [chartData, onCandleClick]);

  const activeFrame = hoverFrameIndex ?? currentFrameIndex;
  const activeCandle = chartData ? findCandleForFrame(chartData.candles, activeFrame) : undefined;
  const activeIndicator = chartData
    ? chartData.indicators.find((item) => item.frameIndex === activeFrame)
    : undefined;

  return (
    <div className="rounded-xl border border-slate-700 bg-slate-900/60 p-3">
      <div className="mb-2 flex flex-wrap items-center gap-2 text-xs text-slate-300">
        <span className="rounded bg-slate-800 px-2 py-1">
          {strictReplayMode ? 'Strict Replay Mode' : 'Full Range Context (visual only)'}
        </span>
        {chartData?.indicatorsMissing ? (
          <span className="rounded bg-amber-900/40 px-2 py-1 text-amber-200">{chartData.indicatorWarning}</span>
        ) : null}
      </div>

      <div className="mb-2 flex flex-wrap gap-2 text-xs">
        {toggleButton('EMA20', toggles.ema20, () => setToggles((s) => ({ ...s, ema20: !s.ema20 })))}
        {toggleButton('EMA50', toggles.ema50, () => setToggles((s) => ({ ...s, ema50: !s.ema50 })))}
        {toggleButton('EMA200', toggles.ema200, () => setToggles((s) => ({ ...s, ema200: !s.ema200 })))}
        {toggleButton('VWAP', toggles.vwap, () => setToggles((s) => ({ ...s, vwap: !s.vwap })))}
        {toggleButton('Volume', toggles.volume, () => setToggles((s) => ({ ...s, volume: !s.volume })))}
        {toggleButton('Strategy', toggles.strategySignals, () => setToggles((s) => ({ ...s, strategySignals: !s.strategySignals })))}
        {toggleButton('Risk', toggles.riskDecisions, () => setToggles((s) => ({ ...s, riskDecisions: !s.riskDecisions })))}
        {toggleButton('Orders/Fills', toggles.ordersFills, () => setToggles((s) => ({ ...s, ordersFills: !s.ordersFills })))}
        {toggleButton('Missed', toggles.missedOrders, () => setToggles((s) => ({ ...s, missedOrders: !s.missedOrders })))}
        {toggleButton('Trades', toggles.trades, () => setToggles((s) => ({ ...s, trades: !s.trades })))}
        {toggleButton('NoTrade', toggles.noTradeMarkers, () => setToggles((s) => ({ ...s, noTradeMarkers: !s.noTradeMarkers })))}
        {toggleButton('NY 4H Range', toggles.rangeLevels, () => setToggles((s) => ({ ...s, rangeLevels: !s.rangeLevels })))}
      </div>

      {!chartData || chartData.candles.length === 0 ? (
        <p className="py-16 text-center text-sm text-slate-400">No candle data available for chart.</p>
      ) : null}
      <div ref={containerRef} className={`w-full ${!chartData || chartData.candles.length === 0 ? 'hidden' : ''}`} />

      {activeCandle ? (
        <div className="mt-3 grid gap-2 text-xs text-slate-300 md:grid-cols-3">
          <div>Time: {new Date(activeCandle.time).toISOString()}</div>
          <div>O/H/L/C: {activeCandle.open} / {activeCandle.high} / {activeCandle.low} / {activeCandle.close}</div>
          <div>Volume: {activeCandle.volume}</div>
          {activeIndicator ? (
            <>
              <div>EMA20/50/200: {fmt(activeIndicator.ema20)} / {fmt(activeIndicator.ema50)} / {fmt(activeIndicator.ema200)}</div>
              <div>VWAP: {fmt(activeIndicator.vwap)} | RSI14: {fmt(activeIndicator.rsi14)} | ATR14: {fmt(activeIndicator.atr14)}</div>
              <div>Structure: {activeIndicator.marketStructure ?? '—'}</div>
            </>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

function toggleButton(label: string, active: boolean, onClick: () => void) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`rounded border px-2 py-1 ${active ? 'border-sky-500 bg-sky-900/30 text-sky-200' : 'border-slate-600 text-slate-400'}`}
    >
      {label}
    </button>
  );
}

function fmt(value?: number | null) {
  return value == null ? '—' : value.toFixed(2);
}

function buildStrategyMarker(marker: ReplayChartStrategyMarker): SeriesMarker<Time> {
  const signal = marker.signalType.toLowerCase();
  if (signal === 'notrade') {
    return {
      time: toChartTime(marker.time) as UTCTimestamp,
      position: 'aboveBar',
      color: '#94a3b8',
      shape: 'circle',
      text: `${marker.strategyCode} NoTrade`,
    };
  }

  const isLong = marker.direction.toLowerCase() === 'long';
  return {
    time: toChartTime(marker.time) as UTCTimestamp,
    position: isLong ? 'belowBar' : 'aboveBar',
    color: isLong ? '#22c55e' : '#ef4444',
    shape: isLong ? 'arrowUp' : 'arrowDown',
    text: `${marker.strategyCode} ${marker.direction}`,
  };
}

function buildRiskMarker(marker: ReplayChartRiskMarker): SeriesMarker<Time> {
  const rejected = marker.decision.toLowerCase() === 'rejected';
  return {
    time: toChartTime(marker.time) as UTCTimestamp,
    position: 'aboveBar',
    color: rejected ? '#f97316' : '#84cc16',
    shape: 'square',
    text: rejected ? marker.rejectedRuleKey ?? 'Rejected' : 'Approved',
  };
}

function buildExecutionMarker(marker: ReplayChartExecutionMarker): SeriesMarker<Time> {
  if (marker.type === 'MissedOrder') {
    return {
      time: toChartTime(marker.time) as UTCTimestamp,
      position: 'aboveBar',
      color: '#fb7185',
      shape: 'circle',
      text: 'Missed',
    };
  }

  if (marker.type === 'TradeExit') {
    return {
      time: toChartTime(marker.time) as UTCTimestamp,
      position: 'aboveBar',
      color: '#c084fc',
      shape: 'arrowDown',
      text: marker.pnl != null ? `Exit ${marker.pnl.toFixed(2)}` : 'Exit',
    };
  }

  return {
    time: toChartTime(marker.time) as UTCTimestamp,
    position: marker.direction.toLowerCase() === 'long' ? 'belowBar' : 'aboveBar',
    color: '#60a5fa',
    shape: marker.direction.toLowerCase() === 'long' ? 'arrowUp' : 'arrowDown',
    text: marker.label,
  };
}
