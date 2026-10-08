import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import * as api from '../api/client';
import { ApiError } from '../api/client';
import FieldEditor from '../components/FieldEditor';
import { ErrorMessage } from '../components/Feedback';
import {
  FREE_PLAN_MAX_FORMS,
  draftToPayload,
  emptyField,
  isFreePlanLimitError,
  validateFormDraft,
} from '../lib/validation';
import type { FieldDraft } from '../lib/validation';

export default function FormNewPage() {
  const navigate = useNavigate();

  const [title, setTitle] = useState('');
  const [fields, setFields] = useState<FieldDraft[]>([emptyField()]);
  const [error, setError] = useState<string | null>(null);
  const [limitReached, setLimitReached] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  function updateField(index: number, field: FieldDraft) {
    setFields(fields.map((current, i) => (i === index ? field : current)));
  }

  function moveField(index: number, direction: -1 | 1) {
    const target = index + direction;
    if (target < 0 || target >= fields.length) return;
    const next = [...fields];
    [next[index], next[target]] = [next[target], next[index]];
    setFields(next);
  }

  function removeField(index: number) {
    setFields(fields.filter((_, i) => i !== index));
  }

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setLimitReached(false);

    // The backend validates again; this only saves a round-trip.
    const validationError = validateFormDraft(title, fields);
    if (validationError) {
      setError(validationError);
      return;
    }

    setSubmitting(true);
    try {
      const created = await api.createForm({
        title: title.trim(),
        fields: fields.map((field, index) => ({ ...draftToPayload(field), order: index })),
      });
      navigate(`/forms/${created.id}`);
    } catch (caught) {
      const message = caught instanceof ApiError ? caught.message : 'Could not create the form.';
      // The Free plan limit is not a generic error: show the upgrade path instead.
      if (isFreePlanLimitError(message)) setLimitReached(true);
      else setError(message);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div>
      <div className="page-head">
        <h1>New form</h1>
      </div>

      <form onSubmit={handleSubmit} className="card">
        <label>
          Title
          <input
            type="text"
            value={title}
            required
            placeholder="Customer feedback"
            onChange={(event) => setTitle(event.target.value)}
          />
        </label>

        <h2>Fields</h2>
        {fields.length === 0 && <p className="muted">Add at least one field.</p>}

        {fields.map((field, index) => (
          <FieldEditor
            key={index}
            field={field}
            index={index}
            total={fields.length}
            onChange={(updated) => updateField(index, updated)}
            onMove={moveField}
            onRemove={removeField}
          />
        ))}

        <button type="button" onClick={() => setFields([...fields, emptyField()])}>
          Add field
        </button>

        {limitReached && (
          <ErrorMessage
            message={`Free plan limit reached (${FREE_PLAN_MAX_FORMS} forms). Upgrade to the Pro plan to create more.`}
            action={<Link to="/account">Upgrade to Pro</Link>}
          />
        )}
        {error && <ErrorMessage message={error} />}
        <p className="muted">Free plan: up to {FREE_PLAN_MAX_FORMS} forms.</p>

        <button type="submit" className="primary" disabled={submitting}>
          {submitting ? 'Creating…' : 'Create form'}
        </button>
      </form>
    </div>
  );
}
