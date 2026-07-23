export function SimulationSafetyBanner({ message }: { message?: string }) {
  return (
    <div className="mb-6 rounded-xl border border-amber-500/40 bg-amber-500/10 px-5 py-4 text-sm text-amber-100">
      <p className="font-semibold">Simulation Only</p>
      <p className="mt-1 text-amber-200/90">
        {message ??
          'Live trading is disabled. No real exchange orders can be placed from this dashboard.'}
      </p>
    </div>
  );
}
