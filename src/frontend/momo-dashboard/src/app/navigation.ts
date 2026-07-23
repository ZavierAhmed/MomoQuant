import type { NavItem, UserRole } from '@/api/types';

export const NAV_ITEMS: NavItem[] = [
  { section: 'Main', path: '/dashboard', label: 'Dashboard', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'Main', path: '/bot-control', label: 'Bot Control', roles: ['Admin', 'Trader'] },
  { section: 'Main', path: '/market-watch', label: 'Market Watch', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'Main', path: '/exchanges-symbols', label: 'Exchanges & Symbols', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'Research', path: '/strategies', label: 'Strategies', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'Research', path: '/backtesting', label: 'Backtesting', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'Research', path: '/strategy-benchmarks', label: 'Strategy Benchmarks', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'Research', path: '/strategy-lab', label: 'Strategy Laboratory', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'Research', path: '/validation-lab', label: 'Validation Laboratory', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'Research', path: '/replay', label: 'Replay', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'Research', path: '/trading-systems', label: 'Trading Systems', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'Research', path: '/trading-systems/sk-livepaper', label: 'SK LivePaper', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'Trading Simulation', path: '/paper-trading', label: 'Paper Trading', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'Trading Simulation', path: '/trades', label: 'Trades', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'Trading Simulation', path: '/orders', label: 'Orders', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'Trading Simulation', path: '/positions', label: 'Positions', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'Intelligence', path: '/reports', label: 'Reports', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'Intelligence', path: '/ai-analytics', label: 'AI Analytics', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'Intelligence', path: '/risk-management', label: 'Risk Management', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'System', path: '/monitoring', label: 'Monitoring', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'System', path: '/logs', label: 'Logs', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'System', path: '/settings', label: 'Settings', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'System', path: '/settings/trading', label: 'Trading Settings', roles: ['Admin', 'Trader', 'Viewer'] },
  { section: 'Admin', path: '/admin/users', label: 'Users', roles: ['Admin'] },
  { section: 'Admin', path: '/admin/system-cleanup', label: 'System Cleanup', roles: ['Admin'] },
];

export function getNavItemsForRole(role: UserRole | null): NavItem[] {
  if (!role) {
    return [];
  }

  return NAV_ITEMS.filter((item) => item.roles.includes(role));
}

export function canAccessRoute(role: UserRole | null, path: string): boolean {
  const item = NAV_ITEMS.find((nav) => nav.path === path);
  if (!item) {
    return true;
  }

  return role ? item.roles.includes(role) : false;
}

export function getNavSections(role: UserRole | null): Array<{ title: string; items: NavItem[] }> {
  const items = getNavItemsForRole(role);
  const sections = new Map<string, NavItem[]>();

  for (const item of items) {
    const existing = sections.get(item.section) ?? [];
    existing.push(item);
    sections.set(item.section, existing);
  }

  return Array.from(sections.entries()).map(([title, sectionItems]) => ({
    title,
    items: sectionItems,
  }));
}
