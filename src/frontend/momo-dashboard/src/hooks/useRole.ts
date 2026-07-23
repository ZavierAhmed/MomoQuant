import { useAuth } from '@/auth/useAuth';
import type { UserRole } from '@/api/types';

export function useRole() {
  const { user } = useAuth();
  const role = user?.role ?? null;

  const isAdmin = role === 'Admin';
  const isTrader = role === 'Trader';
  const isViewer = role === 'Viewer';
  const canEdit = role === 'Admin' || role === 'Trader';

  function hasRole(roles: UserRole[]): boolean {
    return role ? roles.includes(role) : false;
  }

  return { role, isAdmin, isTrader, isViewer, canEdit, hasRole };
}
