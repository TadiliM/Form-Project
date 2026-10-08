import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import * as api from '../api/client';
import { ApiError } from '../api/client';
import type { FormDetail } from '../api/types';
import FieldInput from '../components/FieldInput';
import { ErrorMessage, Loading } from '../components/Feedback';
import { validateAnswers } from '../lib/validation';
import { useAsync } from '../lib/useAsync';

export default function PublicFormPage() {
  const { slug = '' } = useParams();
  const navigate = useNavigate();
  const form = useAsync<FormDetail>(() => api.getPublicForm(slug), [slug]);

  const [values, setValues] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  if (form.loading) return <Loading />;
  if (form.notFound) return <p className="empty">This form does not exist.</p>;
  if (form.error) return <ErrorMessage message={form.error} />;
  if (!form.data) return null;

  const detail = form.data;
  // Fields are rendered in the order the API returns them (already sorted).
  const fields = [...detail.fields].sort((a, b) => a.order - b.order);

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);

    const validationError = validateAnswers(fields, values);
    if (validationError) {
      setError(validationError);
      return;
    }

    setSubmitting(true);
    try {
      // Optional fields left blank are omitted: the API stores no pointless answer.
      const answers = fields
        .filter((field) => (values[field.id] ?? '').trim() !== '')
        .map((field) => ({ fieldId: field.id, value: (values[field.id] ?? '').trim() }));

      await api.submitResponse(slug, { answers });
      navigate(`/f/${slug}/thanks`);
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Could not send the response.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="card narrow">
      <h1>{detail.title}</h1>

      <form onSubmit={handleSubmit}>
        {fields.map((field) => (
          <FieldInput
            key={field.id}
            field={field}
            value={values[field.id] ?? ''}
            onChange={(value) => setValues((current) => ({ ...current, [field.id]: value }))}
          />
        ))}

        {error && <ErrorMessage message={error} />}

        <button type="submit" className="primary" disabled={submitting}>
          {submitting ? 'Sending…' : 'Submit'}
        </button>
      </form>
    </div>
  );
}
