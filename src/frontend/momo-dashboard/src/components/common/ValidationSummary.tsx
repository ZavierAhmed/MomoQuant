export function ValidationSummary({ errors }: { errors: Record<string, string> }) {
  const messages = Object.values(errors).filter(Boolean);
  if (messages.length === 0) {
    return null;
  }

  return (
    <div className="mb-4 rounded-xl border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-sm text-amber-200">
      <p className="font-medium">Please fix the following:</p>
      <ul className="mt-2 list-disc space-y-1 pl-5">
        {messages.map((message) => (
          <li key={message}>{message}</li>
        ))}
      </ul>
    </div>
  );
}
