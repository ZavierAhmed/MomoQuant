import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { StrategiesPage } from '@/pages/StrategiesPage';
import { CANONICAL_STRATEGY_CODES } from '@/constants/canonicalStrategies';
import type { Strategy } from '@/api/domainTypes';

const mockList = vi.fn();
const mockGet = vi.fn();
const mockGetParameters = vi.fn();

vi.mock('@/api/strategiesApi', () => ({
  strategiesApi: {
    list: () => mockList(),
    get: (id: number) => mockGet(id),
    getParameters: (id: number) => mockGetParameters(id),
    enable: vi.fn(),
    disable: vi.fn(),
    updateParameters: vi.fn(),
    evaluate: vi.fn(),
    evaluateLatest: vi.fn(),
  },
}));

vi.mock('@/hooks/useRole', () => ({
  useRole: () => ({ canEdit: true, role: 'Admin' }),
}));

vi.mock('@/api/marketDataApi', () => ({
  marketDataApi: { getCandles: vi.fn().mockResolvedValue([]) },
}));

vi.mock('@/api/aiApi', () => ({
  aiApi: { setupAdvisor: vi.fn() },
}));

function buildStrategy(partial: Partial<Strategy> & Pick<Strategy, 'id' | 'code' | 'name'>): Strategy {
  return {
    description: 'Test strategy',
    isEnabled: true,
    version: '1.0.0',
    ...partial,
  };
}

function renderPage() {
  return render(
    <MemoryRouter>
      <StrategiesPage />
    </MemoryRouter>,
  );
}

describe('StrategiesPage portfolio sections', () => {
  beforeEach(() => {
    mockGet.mockResolvedValue(null);
    mockGetParameters.mockResolvedValue([]);
  });

  it('shows active portfolio strategies with enable controls and hides archived from primary actions', async () => {
    mockList.mockResolvedValue([
      buildStrategy({
        id: 1,
        code: CANONICAL_STRATEGY_CODES[0],
        name: 'Active Strategy',
        portfolioStatus: 'Active',
        isOperationallySelectable: true,
      }),
      buildStrategy({
        id: 2,
        code: 'VWAP_MEAN_REVERSION',
        name: 'Archived Strategy',
        portfolioStatus: 'Archived',
        isOperationallySelectable: false,
      }),
    ]);

    renderPage();

    expect(await screen.findByText('Active Portfolio')).toBeInTheDocument();
    expect(screen.getByText('Active Strategy')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Disable' })).toBeInTheDocument();
    expect(screen.queryByText('Archived Strategy')).not.toBeInTheDocument();
  });

  it('renders archived catalog read-only without enable controls when expanded', async () => {
    mockList.mockResolvedValue([
      buildStrategy({
        id: 1,
        code: CANONICAL_STRATEGY_CODES[0],
        name: 'Active Strategy',
        portfolioStatus: 'Active',
        isOperationallySelectable: true,
      }),
      buildStrategy({
        id: 2,
        code: 'VWAP_MEAN_REVERSION',
        name: 'Archived Strategy',
        portfolioStatus: 'Archived',
        isOperationallySelectable: false,
      }),
    ]);

    renderPage();
    await screen.findByText('Active Strategy');

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /Archived Strategies \(1\)/ }));

    const archivedSection = screen.getByText(/Read-only catalog\./).closest('div');
    expect(archivedSection).toBeTruthy();
    expect(within(archivedSection!.parentElement!).getByText('Archived Strategy')).toBeInTheDocument();
    expect(within(archivedSection!.parentElement!).queryByRole('button', { name: 'Disable' })).not.toBeInTheDocument();
    expect(within(archivedSection!.parentElement!).queryByRole('button', { name: 'Enable' })).not.toBeInTheDocument();
    expect(within(archivedSection!.parentElement!).getByRole('link', { name: 'View details' })).toBeInTheDocument();
  });
});
