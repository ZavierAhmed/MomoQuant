import type { ReactNode } from 'react';

export function FormActions({ children }: { children: ReactNode }) {
  return <div className="mt-4 flex flex-wrap items-center gap-3">{children}</div>;
}
