import { useEffect, useRef, useState } from 'react';

export function usePolling(callback: () => void, intervalMs: number, active: boolean) {
  const callbackRef = useRef(callback);
  callbackRef.current = callback;

  useEffect(() => {
    if (!active) {
      return;
    }

    const timerId = window.setInterval(() => callbackRef.current(), intervalMs);
    return () => window.clearInterval(timerId);
  }, [active, intervalMs]);
}

export function useShowDisabledStrategies(defaultValue = false) {
  const [showDisabledStrategies, setShowDisabledStrategies] = useState(defaultValue);
  return { showDisabledStrategies, setShowDisabledStrategies };
}

export function usePaperSessionPolling(options: {
  active: boolean;
  onStatusPoll: () => void;
  onHeavyPoll: () => void;
  statusIntervalMs?: number;
  heavyIntervalMs?: number;
}) {
  const {
    active,
    onStatusPoll,
    onHeavyPoll,
    statusIntervalMs = 2000,
    heavyIntervalMs = 8000,
  } = options;

  const statusRef = useRef(onStatusPoll);
  const heavyRef = useRef(onHeavyPoll);
  statusRef.current = onStatusPoll;
  heavyRef.current = onHeavyPoll;

  useEffect(() => {
    if (!active) {
      return;
    }

    const statusTimer = window.setInterval(() => statusRef.current(), statusIntervalMs);
    const heavyTimer = window.setInterval(() => heavyRef.current(), heavyIntervalMs);
  statusRef.current();

    return () => {
      window.clearInterval(statusTimer);
      window.clearInterval(heavyTimer);
    };
  }, [active, statusIntervalMs, heavyIntervalMs]);
}
