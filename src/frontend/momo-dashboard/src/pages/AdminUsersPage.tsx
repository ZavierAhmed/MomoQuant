import { useState } from 'react';
import { PageHeader } from '@/components/common/PageHeader';
import { LoadingState } from '@/components/common/LoadingState';
import { ErrorState } from '@/components/common/ErrorState';
import { DataTable } from '@/components/common/DataTable';
import { Pagination } from '@/components/common/Pagination';
import { FormPanel } from '@/components/common/FormPanel';
import { ApiErrorAlert } from '@/components/common/ApiErrorAlert';
import { FormActions } from '@/components/forms/FormActions';
import { SelectField, TextField } from '@/components/forms/fields';
import { formatDate } from '@/components/common/utils';
import { useAsync } from '@/hooks/useAsync';
import { usersApi } from '@/api/usersApi';
import { parseApiClientError } from '@/utils/apiError';

const ROLE_OPTIONS = [
  { label: 'Admin', value: 1 },
  { label: 'Trader', value: 2 },
  { label: 'Viewer', value: 3 },
];

export function AdminUsersPage() {
  const [page, setPage] = useState(1);
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [role, setRole] = useState<number | ''>(2);
  const [actionError, setActionError] = useState<string | null>(null);
  const [formErrors, setFormErrors] = useState<Record<string, string>>({});

  const users = useAsync(() => usersApi.list({ page, pageSize: 25 }), [page]);

  function validateCreateForm() {
    const errors: Record<string, string> = {};
    if (!fullName.trim()) errors.fullName = 'Full name is required.';
    if (!email.trim()) errors.email = 'Email is required.';
    if (!password.trim()) errors.password = 'Password is required.';
    if (!role) errors.role = 'Role is required.';
    setFormErrors(errors);
    return Object.keys(errors).length === 0;
  }

  async function createUser() {
    if (!validateCreateForm()) return;
    setActionError(null);
    try {
      await usersApi.create({ fullName: fullName.trim(), email: email.trim(), password, role: Number(role) });
      setFullName('');
      setEmail('');
      setPassword('');
      setRole(2);
      users.reload();
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  async function disableUser(id: number) {
    setActionError(null);
    try {
      await usersApi.disable(id);
      users.reload();
    } catch (error) {
      setActionError(parseApiClientError(error).message);
    }
  }

  return (
    <div>
      <PageHeader title="Admin Users" description="User management for administrators." />
      <ApiErrorAlert message={actionError} />

      <FormPanel title="Create User" description="Add a new dashboard user.">
        <div className="grid gap-4 md:grid-cols-2">
          <TextField label="Full Name" value={fullName} onChange={setFullName} required error={formErrors.fullName} />
          <TextField label="Email" value={email} onChange={setEmail} required error={formErrors.email} />
          <TextField label="Password" value={password} onChange={setPassword} type="password" required error={formErrors.password} />
          <SelectField label="Role" value={role} onChange={setRole} options={ROLE_OPTIONS} required error={formErrors.role} />
        </div>
        <FormActions>
          <button type="button" onClick={() => void createUser()} className="rounded-lg bg-slate-100 px-4 py-2 text-sm font-medium text-slate-950">
            Create User
          </button>
        </FormActions>
      </FormPanel>

      {users.loading ? <LoadingState /> : null}
      {users.error ? <ErrorState message={users.error} onRetry={users.reload} /> : null}

      <DataTable
        columns={[
          { key: 'name', header: 'Name', render: (row) => row.fullName },
          { key: 'email', header: 'Email', render: (row) => row.email },
          { key: 'role', header: 'Role', render: (row) => row.role },
          { key: 'active', header: 'Active', render: (row) => (row.isActive ? 'Yes' : 'No') },
          { key: 'lastLogin', header: 'Last Login', render: (row) => formatDate(row.lastLoginAtUtc) },
          {
            key: 'actions',
            header: '',
            render: (row) =>
              row.isActive ? (
                <button type="button" onClick={() => void disableUser(row.id)} className="text-xs underline">
                  Disable
                </button>
              ) : (
                'Disabled'
              ),
          },
        ]}
        rows={users.data?.items ?? []}
      />
      {users.data ? (
        <Pagination page={users.data.page} totalPages={users.data.totalPages} onPageChange={setPage} />
      ) : null}
    </div>
  );
}
