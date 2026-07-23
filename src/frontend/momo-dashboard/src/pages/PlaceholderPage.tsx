import { PageHeader } from '@/components/common/PageHeader';
import { EmptyState } from '@/components/common/EmptyState';

interface PlaceholderPageProps {
  title: string;
  description?: string;
}

export function PlaceholderPage({ title, description }: PlaceholderPageProps) {
  return (
    <div>
      <PageHeader
        title={title}
        description={description ?? 'Module shell for upcoming milestone work.'}
      />
      <EmptyState
        title="Coming soon"
        description="This page will be implemented in a later milestone."
      />
    </div>
  );
}
