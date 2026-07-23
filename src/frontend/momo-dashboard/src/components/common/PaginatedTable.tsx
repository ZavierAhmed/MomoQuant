import { useMemo, useState, type ReactNode } from 'react';
import { DataTable } from '@/components/common/DataTable';
import { Pagination } from '@/components/common/Pagination';

interface Column<T> {
  key: string;
  header: string;
  render: (row: T) => ReactNode;
}

export function PaginatedTable<T extends object>({
  columns,
  rows,
  pageSize: initialPageSize = 25,
  emptyMessage,
}: {
  columns: Column<T>[];
  rows: T[];
  pageSize?: number;
  emptyMessage?: string;
}) {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(initialPageSize);

  const totalPages = Math.max(1, Math.ceil(rows.length / pageSize));
  const pageRows = useMemo(() => {
    const start = (page - 1) * pageSize;
    return rows.slice(start, start + pageSize);
  }, [rows, page, pageSize]);

  return (
    <div>
      <div className="mb-3 flex items-center justify-between gap-3 text-xs text-slate-400">
        <span>{rows.length} records</span>
        <label className="flex items-center gap-2">
          Page size
          <select
            value={pageSize}
            onChange={(event) => {
              setPageSize(Number(event.target.value));
              setPage(1);
            }}
            className="rounded border border-slate-700 bg-slate-950 px-2 py-1 text-slate-200"
          >
            {[25, 50, 100].map((size) => (
              <option key={size} value={size}>
                {size}
              </option>
            ))}
          </select>
        </label>
      </div>
      <DataTable columns={columns} rows={pageRows} emptyMessage={emptyMessage} />
      {rows.length > pageSize ? (
        <Pagination page={page} totalPages={totalPages} onPageChange={setPage} />
      ) : null}
    </div>
  );
}
