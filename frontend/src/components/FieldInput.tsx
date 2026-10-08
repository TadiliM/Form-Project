import type { FieldResponse } from '../api/types';
import { boundToString } from '../lib/validation';

interface FieldInputProps {
  field: FieldResponse;
  value: string;
  onChange: (value: string) => void;
}

/** Renders the right control for a field of the public form. */
export default function FieldInput({ field, value, onChange }: FieldInputProps) {
  const inputId = `field-${field.id}`;

  return (
    <div className="field">
      <label htmlFor={inputId}>
        {field.label}
        {field.isRequired && <span className="required"> *</span>}
      </label>

      {field.type === 'text' && (
        <input
          id={inputId}
          type="text"
          value={value}
          maxLength={field.maxLength ?? undefined}
          required={field.isRequired}
          onChange={(event) => onChange(event.target.value)}
        />
      )}

      {field.type === 'number' && (
        <input
          id={inputId}
          type="number"
          value={value}
          min={boundToString(field.min) || undefined}
          max={boundToString(field.max) || undefined}
          step="any"
          required={field.isRequired}
          onChange={(event) => onChange(event.target.value)}
        />
      )}

      {field.type === 'choice' && (
        <select
          id={inputId}
          value={value}
          required={field.isRequired}
          onChange={(event) => onChange(event.target.value)}
        >
          <option value="">Select…</option>
          {(field.options ?? []).map((option) => (
            <option key={option} value={option}>
              {option}
            </option>
          ))}
        </select>
      )}
    </div>
  );
}
