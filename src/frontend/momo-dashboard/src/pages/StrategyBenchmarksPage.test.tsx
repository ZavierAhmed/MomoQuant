import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { StrategyBenchmarksPage } from '@/pages/StrategyBenchmarksPage';
import { CANONICAL_STRATEGY_CODES } from '@/constants/canonicalStrategies';
import type { Strategy } from '@/api/domainTypes';

const mockListBenchmarks = vi.fn();
const mockCreateBenchmark = vi.fn();
const mockPreflight = vi.fn();

vi.mock('@/api/strategyBenchmarksApi', () => ({
  strategyBenchmarksApi: {
    list: (...args: unknown[]) => mockListBenchmarks(...args),
    create: (...args: unknown[]) => mockCreateBenchmark(...args),
    preflight: (...args: unknown[]) => mockPreflight(...args),
  },
}));

vi.mock('@/api/aiApi', () => ({
  aiApi: { setupAdvisor: vi.fn() },
}));

vi.mock('@/hooks/useRole', () => ({
  useRole: () => ({ canEdit: true, role: 'Admin' }),
}));

const strategies: Strategy[] = [
  {
    id: 1,
    code: CANONICAL_STRATEGY_CODES[0],
    name: 'MOMO Adaptive Multi-Timeframe Trend Breakout',
    description: 'Adaptive',
    isEnabled: true,
    version: '1.0.0',
    portfolioStatus: 'Active',
    isOperationallySelectable: true,
  },
  {
    id: 2,
    code: CANONICAL_STRATEGY_CODES[1],
    name: 'Price Structure Breakout + Retest',
    description: 'PSBR',
    isEnabled: true,
    version: '1.1.0',
    portfolioStatus: 'Active',
    isOperationallySelectable: true,
  },
  {
    id: 3,
    code: CANONICAL_STRATEGY_CODES[2],
    name: 'MOMO Volatility Range Reversion',
    description: 'Range',
    isEnabled: true,
    version: '1.0.0',
    portfolioStatus: 'Active',
    isOperationallySelectable: true,
  },
  {
    id: 4,
    code: CANONICAL_STRATEGY_CODES[0],
    name: 'Disabled Adaptive Duplicate',
    description: 'Disabled canonical-shaped row that ops forbid',
    isEnabled: false,
    version: '1.0.0',
    portfolioStatus: 'Active',
    isOperationallySelectable: false,
  },
  {
    id: 10,
    code: 'EMA_PULLBACK',
    name: 'EMA Pullback',
    description: 'Archived',
    isEnabled: true,
    version: '1.0.0',
    portfolioStatus: 'Archived',
    isOperationallySelectable: false,
  },
  {
    id: 11,
    code: 'FOUR_HOUR_RANGE_REENTRY',
    name: '4H Range Re-entry',
    description: 'Archived',
    isEnabled: true,
    version: '1.0.0',
    portfolioStatus: 'Archived',
    isOperationallySelectable: false,
  },
];

const exchangeSymbols = [
  { id: 101, symbol: 'BNBUSDT', displayName: 'BNBUSDT' },
  { id: 102, symbol: 'BTCUSDT', displayName: 'BTCUSDT' },
  { id: 103, symbol: 'ETHUSDT', displayName: 'ETHUSDT' },
];

vi.mock('@/hooks/useReferenceData', () => ({
  useReferenceData: () => ({
    exchanges: [{ id: 1, code: 'BINANCE_FUTURES', name: 'Binance Futures', isActive: true }],
    symbols: exchangeSymbols.map((s) => ({ id: s.id, symbol: s.symbol, exchangeId: 1, isActive: true })),
    allSymbols: exchangeSymbols.map((s) => ({ id: s.id, symbol: s.symbol, exchangeId: 1, isActive: true })),
    strategies,
    activePortfolioStrategies: strategies.filter((s) => s.portfolioStatus === 'Active'),
    archivedStrategies: strategies.filter((s) => s.portfolioStatus === 'Archived'),
    riskProfiles: [
      { id: 7, name: 'Benchmark Research Risk', isDefault: true },
      { id: 8, name: 'Paper Validation Risk', isDefault: false },
    ],
    paperAccounts: [],
    exchangeOptions: [{ label: 'Binance Futures', value: 1 }],
    allExchangeOptions: [{ label: 'Binance Futures', value: 1 }],
    symbolOptions: exchangeSymbols.map((s) => ({ label: s.symbol, value: s.id })),
    allSymbolOptions: exchangeSymbols.map((s) => ({ label: s.symbol, value: s.id })),
    strategyOptions: strategies
      .filter((s) => s.isOperationallySelectable)
      .map((s) => ({
        label: `${s.name}${s.isEnabled ? '' : ' (disabled)'}`,
        value: s.id,
        disabled: !s.isEnabled,
      })),
    buildStrategyOptions: (showDisabled: boolean) =>
      strategies
        .filter((s) => {
          if (s.isOperationallySelectable === false) return false;
          if (!showDisabled && !s.isEnabled) return false;
          return true;
        })
        .map((s) => ({
          label: `${s.name}${s.isEnabled ? '' : ' (disabled)'}`,
          value: s.id,
          disabled: !s.isEnabled,
        })),
    riskProfileOptions: [
      { label: 'Benchmark Research Risk (default)', value: 7 },
      { label: 'Paper Validation Risk', value: 8 },
    ],
    paperAccountOptions: [],
    loading: false,
    error: null,
    reloadAll: vi.fn(),
    reloadExchanges: vi.fn(),
    reloadSymbols: vi.fn(),
  }),
}));

vi.mock('@/hooks/useExchangeSymbols', () => ({
  useExchangeSymbols: () => ({
    symbols: exchangeSymbols,
    symbolOptions: exchangeSymbols.map((s) => ({ label: s.displayName, value: s.id })),
    loading: false,
    error: null,
    reload: vi.fn(),
  }),
}));

vi.mock('@/hooks/useSessionPolling', () => ({
  useShowDisabledStrategies: () => ({
    showDisabledStrategies: false,
    setShowDisabledStrategies: vi.fn(),
  }),
}));

function renderPage() {
  return render(
    <MemoryRouter>
      <StrategyBenchmarksPage />
    </MemoryRouter>,
  );
}

function checkedStrategyIds(): number[] {
  const strategiesField = screen.getByText('Strategies', { selector: 'label' }).parentElement;
  if (!strategiesField) return [];
  const inputs = strategiesField.querySelectorAll('input[type="checkbox"]');
  const ids: number[] = [];
  inputs.forEach((input) => {
    const checkbox = input as HTMLInputElement;
    if (!checkbox.checked) return;
    const label = checkbox.closest('label')?.textContent ?? '';
    const match = strategies.find((s) => label.includes(s.name));
    if (match) ids.push(match.id);
  });
  return ids;
}

describe('StrategyBenchmarksPage rendered strategy selection', () => {
  beforeEach(() => {
    mockListBenchmarks.mockResolvedValue({ items: [], page: 1, pageSize: 50, totalCount: 0 });
    mockCreateBenchmark.mockResolvedValue({ id: 1 });
    mockPreflight.mockResolvedValue({
      estimatedTotalRuns: 1,
      requiredImportTimeframes: [],
      resolvedExecutionRuns: [],
      warnings: [],
      blockingIssues: [],
    });
  });

  it('auto-selects only operationally selectable enabled strategies on render', async () => {
    renderPage();
    await screen.findByRole('button', { name: 'Use canonical portfolio preset' });

    await waitFor(() => {
      expect(checkedStrategyIds().sort()).toEqual([1, 2, 3]);
    });
    expect(checkedStrategyIds()).not.toContain(4);
    expect(checkedStrategyIds()).not.toContain(10);
    expect(checkedStrategyIds()).not.toContain(11);
    expect(screen.queryByText('EMA Pullback')).not.toBeInTheDocument();
    expect(screen.queryByText('4H Range Re-entry')).not.toBeInTheDocument();
  });

  it('clears and reselects enabled strategies without archived or forbidden disabled IDs', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole('button', { name: 'Select all enabled strategies' });

    await user.click(screen.getByRole('button', { name: 'Clear strategies' }));
    await waitFor(() => expect(checkedStrategyIds()).toEqual([]));

    await user.click(screen.getByRole('button', { name: 'Select all enabled strategies' }));
    await waitFor(() => {
      expect(checkedStrategyIds().sort()).toEqual([1, 2, 3]);
    });
    expect(checkedStrategyIds()).not.toContain(4);
    expect(checkedStrategyIds()).not.toContain(10);
    expect(checkedStrategyIds()).not.toContain(11);
  });

  it('canonical portfolio preset inserts only canonical eligible IDs', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole('button', { name: 'Use canonical portfolio preset' });

    await user.click(screen.getByRole('button', { name: 'Clear strategies' }));
    await user.click(screen.getByRole('button', { name: 'Use canonical portfolio preset' }));

    await waitFor(() => {
      expect(checkedStrategyIds().sort()).toEqual([1, 2, 3]);
    });
    expect(checkedStrategyIds()).not.toContain(4);
    expect(checkedStrategyIds()).not.toContain(10);
    expect(checkedStrategyIds()).not.toContain(11);
  });

  it('research and validation presets do not insert archived strategy IDs', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole('button', { name: 'Apply research preset' });

    await user.click(screen.getByRole('button', { name: 'Apply research preset' }));
    await user.click(screen.getByRole('button', { name: 'Apply validation preset' }));

    await waitFor(() => {
      expect(checkedStrategyIds().sort()).toEqual([1, 2, 3]);
    });
    expect(checkedStrategyIds()).not.toContain(10);
    expect(checkedStrategyIds()).not.toContain(11);
  });
});
