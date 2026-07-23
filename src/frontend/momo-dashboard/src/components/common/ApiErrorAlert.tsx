export function ApiErrorAlert({
  title,
  message,
  traceId,
}: {
  title?: string;
  message: string | null;
  traceId?: string;
}) {
  if (!message) {
    return null;
  }

  return (
    <div className="mb-4 rounded-xl border border-rose-500/30 bg-rose-500/10 px-4 py-3 text-sm text-rose-200">
      {title ? <p className="font-medium">{title}</p> : null}
      <p className={title ? 'mt-1' : undefined}>{message}</p>
      {traceId ? <p className="mt-1 text-xs text-rose-300/70">Trace: {traceId}</p> : null}
    </div>
  );
}
