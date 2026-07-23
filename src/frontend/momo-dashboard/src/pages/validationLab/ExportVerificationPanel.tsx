import { Badge } from '@/components/common/Badge';
import { KeyValueGrid } from '@/components/common/KeyValueGrid';
import { formatDate } from '@/components/common/utils';
import type { ValidationExportVerificationStatus } from '@/api/validationLabApi';

export function ExportVerificationPanel({
  status,
  manifest,
  issues,
}: {
  status?: ValidationExportVerificationStatus | null;
  manifest: Record<string, unknown> | null;
  issues?: unknown[];
}) {
  return (
    <div className="rounded-lg border border-slate-800 px-4 py-3">
      <div className="mb-2 flex flex-wrap items-center gap-2 font-medium text-slate-100">
        <span>Export verification</span>
        <Badge tone={status === 'Passed' ? 'success' : status === 'Failed' ? 'warning' : 'neutral'}>
          {status || 'NotRun'}
        </Badge>
      </div>
      {manifest ? (
        <KeyValueGrid
          items={[
            { label: 'Manifest version', value: String(manifest.manifestVersion ?? '—') },
            { label: 'Content SHA-256', value: String(manifest.contentSha256 ?? '—') },
            { label: 'Segment results', value: String(manifest.segmentResultCount ?? '—') },
            { label: 'Overlap candidates', value: String(manifest.overlapCandidateCount ?? '—') },
            { label: 'Has exclusivity report', value: String(manifest.hasExclusivityReport ?? '—') },
            { label: 'Has population counts', value: String(manifest.hasPopulationCounts ?? '—') },
            {
              label: 'Verified at',
              value: formatDate(typeof manifest.verifiedAtUtc === 'string' ? manifest.verifiedAtUtc : null),
            },
          ]}
        />
      ) : (
        <p className="text-slate-400">No export verification manifest persisted yet.</p>
      )}
      {Array.isArray(issues) && issues.length > 0 ? (
        <ul className="mt-2 list-disc space-y-1 pl-5 text-amber-200">
          {issues.map((issue, idx) => (
            <li key={idx}>{String(issue)}</li>
          ))}
        </ul>
      ) : null}
    </div>
  );
}
