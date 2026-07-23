import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { MetricCard } from '@/pages/validationLab/MetricCard'
import { SupersededBanner } from '@/pages/validationLab/SupersededBanner'
import { ExportVerificationPanel } from '@/pages/validationLab/ExportVerificationPanel'

describe('MetricCard holdout visibility', () => {
  it('hides holdout metrics before reveal', () => {
    render(<MetricCard title="Validation" hidden closedTrades={42} expectancy={0.5} />)

    expect(
      screen.getByText('Performance metrics are hidden until Validation Reveal Status is Revealed.'),
    ).toBeInTheDocument()
    expect(screen.queryByText('42')).toBeNull()
    expect(screen.queryByText('Closed trades (n)')).toBeNull()
  })

  it('shows holdout metrics after reveal', () => {
    render(
      <MetricCard
        title="Validation"
        hidden={false}
        closedTrades={42}
        expectancy={0.5}
        profitFactor={1.8}
        netReturn={12.34}
        drawdown={4.5}
      />,
    )

    expect(
      screen.queryByText('Performance metrics are hidden until Validation Reveal Status is Revealed.'),
    ).toBeNull()
    expect(screen.getByText('Closed trades (n)')).toBeInTheDocument()
    expect(screen.getByText('42')).toBeInTheDocument()
  })

  it('marks the card insufficient when the sample size failure applies', () => {
    render(<MetricCard title="Training" hidden={false} closedTrades={3} insufficient />)
    expect(screen.getByText('Insufficient sample')).toBeInTheDocument()
  })
})

describe('SupersededBanner', () => {
  it('renders nothing when the experiment has not been superseded', () => {
    const { container } = render(
      <MemoryRouter>
        <SupersededBanner supersessionStatus="None" supersededByExperimentId={null} supersessionReason={null} />
      </MemoryRouter>,
    )
    expect(container).toBeEmptyDOMElement()
  })

  it('links to the canonical (superseding) experiment', () => {
    render(
      <MemoryRouter>
        <SupersededBanner
          supersessionStatus="Superseded"
          supersededByExperimentId={456}
          supersessionReason="Recovered after infrastructure failure."
        />
      </MemoryRouter>,
    )

    const link = screen.getByRole('link', { name: 'Experiment 456' })
    expect(link).toHaveAttribute('href', '/validation-lab/experiments/456')
    expect(screen.getByText(/Recovered after infrastructure failure\./)).toBeInTheDocument()
  })
})

describe('ExportVerificationPanel', () => {
  it('renders NotRun state with no manifest', () => {
    render(<ExportVerificationPanel status={undefined} manifest={null} />)
    expect(screen.getByText('NotRun')).toBeInTheDocument()
    expect(screen.getByText('No export verification manifest persisted yet.')).toBeInTheDocument()
  })

  it('renders Passed state with manifest details', () => {
    render(
      <ExportVerificationPanel
        status="Passed"
        manifest={{ manifestVersion: 'v2', contentSha256: 'abc123', segmentResultCount: 4 }}
      />,
    )
    expect(screen.getByText('Passed')).toBeInTheDocument()
    expect(screen.getByText('v2')).toBeInTheDocument()
    expect(screen.getByText('abc123')).toBeInTheDocument()
  })

  it('renders Failed state along with verification issues', () => {
    render(
      <ExportVerificationPanel
        status="Failed"
        manifest={null}
        issues={['Checksum mismatch for segment 3.']}
      />,
    )
    expect(screen.getByText('Failed')).toBeInTheDocument()
    expect(screen.getByText('Checksum mismatch for segment 3.')).toBeInTheDocument()
  })
})
