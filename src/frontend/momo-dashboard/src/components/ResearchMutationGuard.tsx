import type { ReactNode } from 'react';
import { canExecuteResearch } from '@/auth/researchRoles';

export function ResearchMutationGuard(props: {
  role: string | null | undefined;
  children: ReactNode;
  fallback?: ReactNode;
}) {
  if (!canExecuteResearch(props.role)) {
    return <>{props.fallback ?? null}</>;
  }
  return <>{props.children}</>;
}
