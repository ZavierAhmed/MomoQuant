import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ResearchErrorBoundary } from '@/components/ResearchErrorBoundary'
import { ResearchMutationGuard } from '@/components/ResearchMutationGuard'
import { canExecuteResearch, isViewerRole } from '@/auth/researchRoles'
import { apiRequest, ApiClientError } from '@/api/apiClient'

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

  it('maps 403 to ApiClientError', async () => {
    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify({ success: false, message: 'Forbidden for Viewer.' }), {
        status: 403,
        headers: { 'content-type': 'application/json' },
      }),
    )

    await expect(apiRequest('/validation-lab/experiments')).rejects.toMatchObject({
      status: 403,
      code: 'Forbidden',
    } satisfies Partial<ApiClientError>)
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
})
