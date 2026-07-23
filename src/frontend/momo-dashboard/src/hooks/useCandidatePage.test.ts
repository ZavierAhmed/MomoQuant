import { describe, expect, it, vi } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { useCandidatePage } from '@/hooks/useCandidatePage'

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

describe('useCandidatePage', () => {
  it('discards a stale earlier response when a newer request has already started (route change)', async () => {
    const first = deferred<string>()
    const second = deferred<string>()
    const fetcher = vi.fn().mockImplementationOnce(() => first.promise).mockImplementationOnce(() => second.promise)

    const { rerender, result } = renderHook(({ id }: { id: number }) => useCandidatePage(fetcher, [id]), {
      initialProps: { id: 1 },
    })

    // Navigate to a new route/id before the first request resolves.
    rerender({ id: 2 })

    // The newer request resolves first...
    second.resolve('from-id-2')
    await waitFor(() => expect(result.current.data).toBe('from-id-2'))

    // ...and the stale first request resolving afterwards must not overwrite it.
    first.resolve('from-id-1-stale')
    await new Promise((r) => setTimeout(r, 0))

    expect(result.current.data).toBe('from-id-2')
    expect(fetcher).toHaveBeenCalledTimes(2)
  })

  it('aborts the in-flight request signal when deps change (route change cancels loading)', async () => {
    const signals: AbortSignal[] = []
    const fetcher = vi.fn().mockImplementation(
      (signal: AbortSignal) =>
        // Intentionally never resolves: this test only asserts on abort-signal
        // state, not on request completion, so no timers are needed.
        new Promise<string>(() => {
          signals.push(signal)
        }),
    )

    const { rerender, unmount } = renderHook(({ id }: { id: number }) => useCandidatePage(fetcher, [id]), {
      initialProps: { id: 1 },
    })

    expect(signals[0]?.aborted).toBe(false)

    rerender({ id: 2 })

    expect(signals[0]?.aborted).toBe(true)
    expect(signals[1]?.aborted).toBe(false)

    unmount()
    expect(signals[1]?.aborted).toBe(true)
  })

  it('exposes loading/error state and normalizes fetcher rejections', async () => {
    const fetcher = vi.fn().mockRejectedValue(new Error('boom'))
    const { result } = renderHook(() => useCandidatePage(fetcher, ['static']))

    expect(result.current.loading).toBe(true)
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.error).toBe('boom')
  })
})
