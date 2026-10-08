import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import * as api from '../api/client';
import { ApiError } from '../api/client';
import type { FormDetail } from '../api/types';
import FieldEditor from '../components/FieldEditor';
import { ErrorMessage, Loading } from '../components/Feedback';
import { formatDateTime, publicFormUrl } from '../lib/format';
import { draftFromResponse, draftToPayload, emptyField, validateFormDraft } from '../lib/validation';
import type { FieldDraft } from '../lib/validation';
import { useAsync } from '../lib/useAsync';

export default function FormDetailPage() {
  const { id = '' } = useParams();
  const form = useAsync<FormDetail>(() => api.getForm(id), [id]);

  const [title, setTitle] = useState('');
  const [fields, setFields] = useState<FieldDraft[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [saving, setSaving] = useState(false);
  const [copied, setCopied] = useState(false);

  // Copy the API result into the editable state once it arrives.
  useEffect(() => {
    if (!form.data) return;
    setTitle(form.data.title);
    setFields(form.data.fields.map(draftFromResponse));
  }, [form.data]);

  function updateField(index: number, field: FieldDraft) {
    setFields((current) => current.map((item, i) => (i === index ? field : item)));
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

  async function handleSave(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setSaved(false);

    const validationError = validateFormDraft(title, fields);
    if (validationError) {
      setError(validationError);
      return;
    }

    setSaving(true);
    try {
      await api.updateForm(id, {
        title: title.trim(),
        fields: fields.map((field, index) => ({ ...draftToPayload(field), order: index })),
      });
      setSaved(true);
      form.reload();
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Could not save the form.');
    } finally {
      setSaving(false);
    }
  }

  async function copyShareLink() {
    try {
      await navigator.clipboard.writeText(publicFormUrl(shareSlug));
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      // The clipboard can be blocked; the read-only input stays selectable.
      setCopied(false);
    }
  }

  if (form.loading) return <Loading />;
  if (form.notFound) return <p className="empty">This form does not exist.</p>;
  if (form.error) return <ErrorMessage message={form.error} />;
  if (!form.data) return null;

  const detail = form.data;
  const shareSlug = detail.publicUrlSlug;
  // Once a form has received a response the API refuses any field change.
  const canEdit = detail.responseCount === 0;

  return (
    <div>
      <div className="page-head">
        <h1>{detail.title}</h1>
        <div className="row-actions">
          <Link to={`/forms/${detail.id}/responses`}>Responses ({detail.responseCount})</Link>
          <Link to={`/f/${shareSlug}`}>Open public page</Link>
        </div>
      </div>

      <div className="card">
        <h2>Share link</h2>
        <div className="share-row">
          <input
            type="text"
            readOnly
            value={publicFormUrl(shareSlug)}
            onFocus={(event) => event.target.select()}
          />
          <button type="button" onClick={copyShareLink}>
            {copied ? 'Copied!' : 'Copy'}
          </button>
        </div>
        <p className="muted">Created {formatDateTime(detail.createdAt)}.</p>
      </div>

      {!canEdit && (
        <p className="notice">
          This form has already received responses: its fields can no longer be modified.
        </p>
      )}

      <form onSubmit={handleSave} className="card">
        <h2>Fields</h2>

        <label>
          Title
          <input
            type="text"
            value={title}
            required
            disabled={!canEdit}
            onChange={(event) => setTitle(event.target.value)}
          />
        </label>

        {fields.map((field, index) => (
          <FieldEditor
            key={index}
            field={field}
            index={index}
            total={fields.length}
            disabled={!canEdit}
            onChange={(updated) => updateField(index, updated)}
            onMove={moveField}
            onRemove={removeField}
          />
        ))}

        {canEdit && (
          <>
            <button type="button" onClick={() => setFields([...fields, emptyField()])}>
              Add field
            </button>

            {saved && <p className="success">Saved.</p>}
            {error && <ErrorMessage message={error} />}

            <button type="submit" className="primary" disabled={saving}>
              {saving ? 'Saving…' : 'Save changes'}
            </button>
          </>
        )}
      </form>
    </div>
  );
}
