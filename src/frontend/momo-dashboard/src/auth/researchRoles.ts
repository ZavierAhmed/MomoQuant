/** Role helpers for research UI — Viewer is read-only. */
export function isViewerRole(role: string | null | undefined): boolean {
  return (role ?? '').trim().toLowerCase() === 'viewer';
}

export function canExecuteResearch(role: string | null | undefined): boolean {
  const normalized = (role ?? '').trim().toLowerCase();
  return normalized === 'admin' || normalized === 'trader';
}
