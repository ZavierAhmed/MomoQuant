export function LoadingState({ message = 'Loading data...' }: { message?: string }) {
  return (
    <div className="rounded-xl border border-slate-800 bg-slate-900/40 p-6 text-sm text-slate-400">
      {message}
    </div>
  );
}
