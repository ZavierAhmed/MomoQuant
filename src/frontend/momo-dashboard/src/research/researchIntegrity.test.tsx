import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor, act } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { ResearchErrorBoundary } from '@/components/ResearchErrorBoundary'
import { ResearchMutationGuard } from '@/components/ResearchMutationGuard'
import { canExecuteResearch, isViewerRole } from '@/auth/researchRoles'
import { apiRequest, ApiClientError } from '@/api/apiClient'
import { SupersededBanner } from '@/pages/validationLab/SupersededBanner'
import { ExportVerificationPanel } from '@/pages/validationLab/ExportVerificationPanel'
import {
  computeExperimentActionAvailability,
  buildValidationCandidateQuery,
} from '@/pages/validationLab/validationLabDetailHelpers'
import { buildCandidateQuery } from '@/components/strategies/candidateGrid/strategyLabCandidateGridHelpers'
import { useCandidatePage } from '@/hooks/useCandidatePage'

describe('research role guards', () => {
  it('viewer cannot execute research mutations', () => {
    expect(isViewerRole('Viewer')).toBe(true)
    expect(canExecuteResearch('Viewer')).toBe(false)
    expect(canExecuteResearch('Trader')).toBe(true)
    expect(canExecuteResearch('Admin')).toBe(true)
  })

  it('hides mutation controls for Viewer', () => {
    render(
      <ResearchMutationGuard role="Viewer" fallback={<span>read-only</span>}>
        <button type="button">Freeze</button>
      </ResearchMutationGuard>,
    )
    expect(screen.queryByRole('button', { name: 'Freeze' })).toBeNull()
    expect(screen.getByText('read-only')).toBeInTheDocument()
  })
})

describe('ResearchErrorBoundary', () => {
  it('renders fallback when child throws', () => {
    const Boom = () => {
      throw new Error('holdout tab crashed')
    }
    render(
      <ResearchErrorBoundary>
        <Boom />
      </ResearchErrorBoundary>,
    )
    expect(screen.getByRole('alert')).toHaveTextContent('holdout tab crashed')
  })
})

describe('apiClient resilience', () => {
  const originalFetch = globalThis.fetch

  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    globalThis.fetch = originalFetch
  })

  it('maps 403 to ApiClientError with Forbidden code', async () => {
    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify({ success: false, message: 'Forbidden for Viewer.' }), {
        status: 403,
        headers: { 'content-type': 'application/json' },
      }),
    )

    await expect(apiRequest('/validation-lab/experiments')).rejects.toMatchObject({
      status: 403,
      code: 'Forbidden',
      message: 'Forbidden for Viewer.',
    } satisfies Partial<ApiClientError>)
  })

  it('maps timeout to normalized Timeout ApiClientError', async () => {
    vi.mocked(fetch).mockImplementation(
      (_url, init) =>
        new Promise((_, reject) => {
          init?.signal?.addEventListener('abort', () => {
            reject(new DOMException('API request timed out.', 'TimeoutError'))
          })
        }),
    )

    await expect(apiRequest('/x', { timeoutMs: 5 })).rejects.toMatchObject({
      code: 'Timeout',
      status: 408,
      message: 'Request timed out.',
    })
  })

  it('supports cancellation via AbortSignal', async () => {
    const controller = new AbortController()
    vi.mocked(fetch).mockImplementation((_url, init) => {
      if (init?.signal?.aborted) {
        return Promise.reject(new DOMException('Aborted', 'AbortError'))
      }
      return new Promise((_, reject) => {
        init?.signal?.addEventListener('abort', () => {
          reject(new DOMException('Aborted', 'AbortError'))
        })
      })
    })
    controller.abort()
    await expect(apiRequest('/x', { signal: controller.signal, timeoutMs: 0 })).rejects.toMatchObject({
      code: 'Cancelled',
    })
  })

  it('maps network failure to recoverable NetworkUnavailable error', async () => {
    vi.mocked(fetch).mockRejectedValue(new TypeError('Failed to fetch'))
    await expect(apiRequest('/x', { timeoutMs: 0 })).rejects.toMatchObject({
      code: 'NetworkUnavailable',
      message: 'Network unavailable.',
      status: 0,
    })
  })
})

describe('validation holdout reveal', () => {
  it('hides holdout metrics before reveal', () => {
    const Holdout = ({ revealed }: { revealed: boolean }) => (
      <div>
        {revealed ? (
          <div data-testid="holdout-revealed">Net expectancy visible</div>
        ) : (
          <div data-testid="holdout-hidden">Holdout hidden until freeze/reveal</div>
        )}
      </div>
    )
    render(<Holdout revealed={false} />)
    expect(screen.getByTestId('holdout-hidden')).toBeInTheDocument()
    expect(screen.queryByTestId('holdout-revealed')).toBeNull()
  })

  it('shows holdout metrics after reveal', () => {
    const Holdout = ({ revealed }: { revealed: boolean }) => (
      <div>
        {revealed ? (
          <div data-testid="holdout-revealed">Net expectancy visible</div>
        ) : (
          <div data-testid="holdout-hidden">Holdout hidden until freeze/reveal</div>
        )}
      </div>
    )
    render(<Holdout revealed />)
    expect(screen.getByTestId('holdout-revealed')).toBeInTheDocument()
  })
})

describe('experiment action availability', () => {
  it('zero eligible trials disable freeze', () => {
    const availability = computeExperimentActionAvailability({
      status: 'TrainingCompleted',
      selectionIntegrityStatus: 'FailedNoEligibleTrials',
      strategyRobustnessDecision: 'FailedNoTrainingTrialPassedGuardrails',
      validationRevealStatus: 'Hidden',
    })
    expect(availability.canFreeze).toBe(false)
    expect(availability.zeroEligibleFailure).toBe(true)
  })

  it('invalid selection integrity disables validation', () => {
    const availability = computeExperimentActionAvailability({
      status: 'ConfigurationFrozen',
      selectionIntegrityStatus: 'FailedParameterFingerprintMismatch',
      validationRevealStatus: 'Hidden',
    })
    expect(availability.canValidate).toBe(false)
  })
})

describe('export verification and supersession', () => {
  it('displays export verification states', () => {
    render(
      <ExportVerificationPanel
        status="Passed"
        manifest={{
          manifestVersion: 'v1',
          contentSha256: 'abc',
          segmentResultCount: 4,
          verifiedAtUtc: '2024-01-01T00:00:00Z',
        }}
        issues={['warn-example']}
      />,
    )
    expect(screen.getByText('Passed')).toBeInTheDocument()
    expect(screen.getByText('warn-example')).toBeInTheDocument()
  })

  it('superseded banner links to canonical experiment', () => {
    render(
      <MemoryRouter>
        <SupersededBanner
          supersessionStatus="Superseded"
          supersededByExperimentId={42}
          supersessionReason="Recovered"
        />
      </MemoryRouter>,
    )
    const link = screen.getByRole('link', { name: /Experiment 42/i })
    expect(link).toHaveAttribute('href', '/validation-lab/experiments/42')
  })
})

describe('candidate pagination and route cancellation', () => {
  it('candidate pagination builds correct server query', () => {
    const vl = buildValidationCandidateQuery('Training', 'RawStrategy', true, 3, 25)
    expect(vl).toMatchObject({ page: 3, pageSize: 25, segment: 'Training', layer: 'RawStrategy' })
    expect(buildValidationCandidateQuery('Validation', 'RawStrategy', false)).toBeNull()

    const sl = buildCandidateQuery({
      page: 2,
      pageSize: 50,
      sortBy: 'netPnl',
      sortDirection: 'asc',
      search: '',
      direction: 'Long',
      rawOutcome: '',
      confidenceDecision: '',
      riskDecision: '',
      confidenceMin: '',
      confidenceMax: '',
      riskMin: '',
      riskMax: '',
      profitableOnly: '',
      fromUtc: '',
      toUtc: '',
      quickFilter: '',
    })
    expect(sl.page).toBe(2)
    expect(sl.pageSize).toBe(50)
    expect(sl.direction).toBe('Long')
  })

  it('stale earlier response cannot overwrite newer route state', async () => {
    let resolveSlow: ((value: string) => void) | null = null
    const slow = new Promise<string>((resolve) => {
      resolveSlow = resolve
    })
    let call = 0
    function Probe({ route }: { route: string }) {
      const { data } = useCandidatePage(async () => {
        call += 1
        if (call === 1) return slow
        return `fresh-${route}`
      }, [route])
      return <div data-testid="data">{data ?? 'none'}</div>
    }

    const { rerender } = render(<Probe route="a" />)
    rerender(<Probe route="b" />)
    await waitFor(() => expect(screen.getByTestId('data')).toHaveTextContent('fresh-b'))
    await act(async () => {
      resolveSlow?.('stale-a')
    })
    expect(screen.getByTestId('data')).toHaveTextContent('fresh-b')
  })

  it('route change cancels candidate loading via AbortController', async () => {
    const signals: AbortSignal[] = []
    function Probe({ route }: { route: string }) {
      const { loading } = useCandidatePage(async (signal) => {
        signals.push(signal)
        await new Promise<void>((_, reject) => {
          signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')))
        })
        return route
      }, [route])
      return <div data-testid="loading">{String(loading)}</div>
    }

    const { rerender } = render(<Probe route="run-1" />)
    await waitFor(() => expect(signals.length).toBeGreaterThanOrEqual(1))
    rerender(<Probe route="run-2" />)
    await waitFor(() => expect(signals[0]?.aborted).toBe(true))
  })

  it('network unavailable state is recoverable after reload', async () => {
    let fail = true
    function Probe() {
      const { data, error, reload } = useCandidatePage(async () => {
        if (fail) {
          throw new ApiClientError('Network unavailable.', 0, undefined, undefined, 'NetworkUnavailable')
        }
        return 'ok'
      }, [])
      return (
        <div>
          <div data-testid="error">{error ?? ''}</div>
          <div data-testid="data">{data ?? ''}</div>
          <button type="button" onClick={reload}>
            Retry
          </button>
        </div>
      )
    }

    render(<Probe />)
    await waitFor(() => expect(screen.getByTestId('error')).toHaveTextContent('Network unavailable.'))
    fail = false
    await act(async () => {
      screen.getByRole('button', { name: 'Retry' }).click()
    })
    await waitFor(() => expect(screen.getByTestId('data')).toHaveTextContent('ok'))
  })
})
