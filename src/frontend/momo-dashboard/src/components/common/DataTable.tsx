import type { ReactNode } from 'react';

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type TableRow = any;

interface Column {
  key: string;
  header: string;
  render: (row: TableRow) => ReactNode;
}

interface DataTableProps {
  columns: Column[];
  rows: TableRow[];
  emptyMessage?: string;
}

export function DataTable({ columns, rows, emptyMessage = 'No records found.' }: DataTableProps) {
  if (rows.length === 0) {
    return (
      <div className="rounded-xl border border-slate-800 bg-slate-900/40 p-6 text-sm text-slate-400">
        {emptyMessage}
      </div>
    );
  }

  return (
    <div className="overflow-x-auto rounded-xl border border-slate-800">
      <table className="min-w-full divide-y divide-slate-800 text-sm">
        <thead className="bg-slate-900/80">
          <tr>
            {columns.map((column) => (
              <th key={column.key} className="px-4 py-3 text-left font-medium text-slate-400">
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-800 bg-slate-950/40">
          {rows.map((row, index) => (
            <tr key={index} className="hover:bg-slate-900/40">
              {columns.map((column) => (
                <td key={column.key} className="px-4 py-3 text-slate-200">
                  {column.render(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
