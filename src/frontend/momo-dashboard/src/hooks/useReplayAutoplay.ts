import { useEffect, useRef } from 'react';

const SPEED_INTERVAL_MS: Record<string, number> = {
  '1x': 1500,
  '2x': 800,
  '5x': 350,
  '10x': 200,
};

export interface ReplayAutoplayOptions {
  enabled: boolean;
  status: string;
  speed: string;
  currentFrameIndex: number;
  totalFrames: number;
  onStep: () => Promise<void>;
  onError?: (error: unknown) => void;
}

export function useReplayAutoplay({
  enabled,
  status,
  speed,
  currentFrameIndex,
  totalFrames,
  onStep,
  onError,
}: ReplayAutoplayOptions) {
  const steppingRef = useRef(false);
  const onStepRef = useRef(onStep);
  const onErrorRef = useRef(onError);
  const frameIndexRef = useRef(currentFrameIndex);
  const totalFramesRef = useRef(totalFrames);

  onStepRef.current = onStep;
  onErrorRef.current = onError;
  frameIndexRef.current = currentFrameIndex;
  totalFramesRef.current = totalFrames;

  const isAutoplayActive =
    enabled &&
    status === 'Running' &&
    speed !== 'ManualStep' &&
    currentFrameIndex < totalFrames - 1;

  useEffect(() => {
    if (!isAutoplayActive) {
      return;
    }

    const intervalMs = Math.max(SPEED_INTERVAL_MS[speed] ?? 1500, 200);
    const timerId = window.setInterval(() => {
      if (steppingRef.current) {
        return;
      }

      if (frameIndexRef.current >= totalFramesRef.current - 1) {
        return;
      }

      steppingRef.current = true;
      void onStepRef
        .current()
        .catch((error) => {
          onErrorRef.current?.(error);
        })
        .finally(() => {
          steppingRef.current = false;
        });
    }, intervalMs);

    return () => window.clearInterval(timerId);
  }, [isAutoplayActive, speed]);

  return {
    isAutoplayActive,
    autoplayLabel: isAutoplayActive ? `Autoplay running at ${speed}` : null,
  };
}
