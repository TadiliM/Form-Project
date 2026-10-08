import { useState } from 'react';
import { Link } from 'react-router-dom';
import * as api from '../api/client';
import { ApiError } from '../api/client';
import type { FormSummary } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { EmptyState, ErrorMessage, Loading } from '../components/Feedback';
import { formatDateTime } from '../lib/format';
import { FREE_PLAN_MAX_FORMS } from '../lib/validation';
import { useAsync } from '../lib/useAsync';

export default function DashboardPage() {
  const { user } = useAuth();
  const forms = useAsync<FormSummary[]>(() => api.listForms(), []);
  const [actionError, setActionError] = useState<string | null>(null);

  const isFree = user?.planType !== 'Pro';
  const atLimit = isFree && (forms.data?.length ?? 0) >= FREE_PLAN_MAX_FORMS;

  async function handleDelete(id: string, title: string) {
    if (!window.confirm(`Delete "${title}" and all its responses?`)) return;
    setActionError(null);

    try {
      await api.deleteForm(id);
      forms.reload();
    } catch (caught) {
      setActionError(caught instanceof ApiError ? caught.message : 'Could not delete the form.');
    }
  }

  return (
    <div>
      <div className="page-head">
        <h1>My forms</h1>
        {atLimit ? (
          <Link className="button" to="/account">
            Upgrade to Pro
          </Link>
        ) : (
          <Link className="button primary" to="/forms/new">
            New form
          </Link>
        )}
      </div>

      {atLimit && (
        <p className="notice">
          You reached the Free plan limit ({FREE_PLAN_MAX_FORMS} forms). Upgrade to the Pro plan to
          create more.
        </p>
      )}

      {actionError && <ErrorMessage message={actionError} />}

      {forms.loading && <Loading />}
      {forms.error && <ErrorMessage message={forms.error} />}

      {forms.data && forms.data.length === 0 && (
        <EmptyState>
          No forms yet. <Link to="/forms/new">Create your first form</Link>.
        </EmptyState>
      )}

      {forms.data && forms.data.length > 0 && (
        <table className="table">
          <thead>
            <tr>
              <th>Title</th>
              <th>Fields</th>
              <th>Responses</th>
              <th>Created</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {forms.data.map((form) => (
              <tr key={form.id}>
                <td>
                  <Link to={`/forms/${form.id}`}>{form.title}</Link>
                </td>
                <td>{form.fieldCount}</td>
                <td>
                  <Link to={`/forms/${form.id}/responses`}>{form.responseCount}</Link>
                </td>
                <td className="muted">{formatDateTime(form.createdAt)}</td>
                <td className="row-actions">
                  <Link to={`/f/${form.publicUrlSlug}`}>Open</Link>
                  <button
                    type="button"
                    className="danger"
                    onClick={() => handleDelete(form.id, form.title)}
                  >
                    Delete
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
