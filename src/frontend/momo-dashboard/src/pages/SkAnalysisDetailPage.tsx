import { Link, useParams } from 'react-router-dom';
import { ExportActions } from '@/components/common/ExportActions';
import { PageHeader } from '@/components/common/PageHeader';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { SkAnalysisResultView } from '@/components/tradingSystems/SkAnalysisResultView';
import { useAsync } from '@/hooks/useAsync';
import { tradingSystemsApi } from '@/api/tradingSystemsApi';

export function SkAnalysisDetailPage() {
  const { id } = useParams<{ id: string }>();
  const analysisId = Number(id);

  const analysis = useAsync(
    () => tradingSystemsApi.getAnalysis(analysisId),
    [analysisId],
  );

  return (
    <div>
      <PageHeader
        title="SK System Analysis"
        description="Saved analysis snapshot. Analysis only — no automated trading."
      />

      <div className="mb-4">
        <Link
          to="/trading-systems/sk"
          className="text-sm text-sky-300 hover:text-sky-200"
        >
          ← Back to SK System Analyzer
        </Link>
      </div>

      {analysis.loading ? <LoadingState /> : null}
      {analysis.error ? <ErrorState message={analysis.error} onRetry={analysis.reload} /> : null}
      {analysis.data ? (
        <>
          <div className="mb-4">
            <ExportActions scope="SkAnalysisRun" sourceId={String(analysisId)} />
          </div>
          <SkAnalysisResultView result={analysis.data} />
        </>
      ) : null}
    </div>
  );
}
