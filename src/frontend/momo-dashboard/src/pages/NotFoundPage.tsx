import { Link } from 'react-router-dom';
import { PageHeader } from '@/components/common/PageHeader';

export function NotFoundPage() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-950 px-4">
      <div className="text-center">
        <PageHeader title="Page not found" description="The requested route does not exist." />
        <Link to="/dashboard" className="text-sm text-slate-300 underline hover:text-white">
          Return to dashboard
        </Link>
      </div>
    </div>
  );
}
