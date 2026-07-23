import { Link } from 'react-router-dom';
import type { ValidationExperimentSupersessionStatus } from '@/api/validationLabApi';

/** Points at the canonical experiment when this one has been superseded. */
export function SupersededBanner({
  supersessionStatus,
  supersededByExperimentId,
  supersessionReason,
}: {
  supersessionStatus?: ValidationExperimentSupersessionStatus;
  supersededByExperimentId?: number | null;
  supersessionReason?: string | null;
}) {
  if (supersessionStatus !== 'Superseded' || !supersededByExperimentId) {
    return null;
  }

  return (
    <div className="mb-4 rounded-lg border border-sky-800 bg-sky-950/40 px-4 py-3 text-sm text-sky-100">
      This experiment was superseded by{' '}
      <Link to={`/validation-lab/experiments/${supersededByExperimentId}`} className="underline">
        Experiment {supersededByExperimentId}
      </Link>{' '}
      after recovery and durability verification.
      {supersessionReason ? ` ${supersessionReason}` : null}
    </div>
  );
}
