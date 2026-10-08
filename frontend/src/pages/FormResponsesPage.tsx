import { Link, useParams } from 'react-router-dom';
import * as api from '../api/client';
import type { FormResponse } from '../api/types';
import { EmptyState, ErrorMessage, Loading } from '../components/Feedback';
import { formatDateTime } from '../lib/format';
import { useAsync } from '../lib/useAsync';

export default function FormResponsesPage() {
  const { id = '' } = useParams();
  const responses = useAsync<FormResponse[]>(() => api.listResponses(id), [id]);

  if (responses.loading) return <Loading />;
  if (responses.notFound) return <p className="empty">This form does not exist.</p>;
  if (responses.error) return <ErrorMessage message={responses.error} />;
  if (!responses.data) return null;

  const list = responses.data;

  return (
    <div>
      <div className="page-head">
        <h1>Responses</h1>
        <div className="row-actions">
          <button type="button" onClick={responses.reload}>
            Refresh
          </button>
          <Link to={`/forms/${id}`}>Back to the form</Link>
        </div>
      </div>

      {list.length === 0 ? (
        <EmptyState>
          No responses yet. Share the public link and answers will show up here.
        </EmptyState>
      ) : (
        <div className="responses">
          {list.map((response) => (
            <div key={response.id} className="card">
              <p className="muted">{formatDateTime(response.submittedAt)}</p>
              <table className="table">
                <tbody>
                  {response.answers.map((answer) => (
                    <tr key={`${response.id}-${answer.fieldId}`}>
                      <th>{answer.label}</th>
                      <td>{answer.value}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
