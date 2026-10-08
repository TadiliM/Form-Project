import type { FieldDraft } from '../lib/validation';
import type { FieldType } from '../api/types';

interface FieldEditorProps {
  field: FieldDraft;
  index: number;
  total: number;
  /** True once the form has responses: the API refuses any field change. */
  disabled?: boolean;
  onChange: (field: FieldDraft) => void;
  onMove: (index: number, direction: -1 | 1) => void;
  onRemove: (index: number) => void;
}

const FIELD_TYPES: FieldType[] = ['text', 'choice', 'number'];

/** One editable field in the form builder. */
export default function FieldEditor({
  field,
  index,
  total,
  disabled = false,
  onChange,
  onMove,
  onRemove,
}: FieldEditorProps) {
  function patch(changes: Partial<FieldDraft>) {
    onChange({ ...field, ...changes });
  }

  function changeOption(optionIndex: number, value: string) {
    const options = [...field.options];
    options[optionIndex] = value;
    patch({ options });
  }

  return (
    <div className="field-editor">
      <div className="field-editor-head">
        <span className="muted">Field {index + 1}</span>
        <div className="row-actions">
          <button
            type="button"
            onClick={() => onMove(index, -1)}
            disabled={disabled || index === 0}
          >
            ↑
          </button>
          <button
            type="button"
            onClick={() => onMove(index, 1)}
            disabled={disabled || index === total - 1}
          >
            ↓
          </button>
          <button
            type="button"
            className="danger"
            onClick={() => onRemove(index)}
            disabled={disabled}
          >
            Remove
          </button>
        </div>
      </div>

      <label>
        Label
        <input
          type="text"
          value={field.label}
          disabled={disabled}
          onChange={(event) => patch({ label: event.target.value })}
        />
      </label>

      <div className="field-row">
        <label>
          Type
          <select
            value={field.type}
            disabled={disabled}
            onChange={(event) => patch({ type: event.target.value as FieldType })}
          >
            {FIELD_TYPES.map((type) => (
              <option key={type} value={type}>
                {type}
              </option>
            ))}
          </select>
        </label>

        <label className="checkbox">
          <input
            type="checkbox"
            checked={field.isRequired}
            disabled={disabled}
            onChange={(event) => patch({ isRequired: event.target.checked })}
          />
          Required
        </label>
      </div>

      {field.type === 'text' && (
        <label>
          Max length
          <input
            type="number"
            min={1}
            value={field.maxLength}
            disabled={disabled}
            onChange={(event) => patch({ maxLength: Number(event.target.value) })}
          />
        </label>
      )}

      {field.type === 'choice' && (
        <div className="options">
          <span className="muted">Options</span>
          {field.options.map((option, optionIndex) => (
            <div key={optionIndex} className="option-row">
              <input
                type="text"
                value={option}
                placeholder={`Option ${optionIndex + 1}`}
                disabled={disabled}
                onChange={(event) => changeOption(optionIndex, event.target.value)}
              />
              <button
                type="button"
                disabled={disabled}
                onClick={() => patch({ options: field.options.filter((_, i) => i !== optionIndex) })}
              >
                ×
              </button>
            </div>
          ))}
          <button
            type="button"
            disabled={disabled}
            onClick={() => patch({ options: [...field.options, ''] })}
          >
            Add option
          </button>
        </div>
      )}

      {field.type === 'number' && (
        <div className="field-row">
          <label>
            Min
            <input
              type="number"
              value={field.min}
              step="any"
              disabled={disabled}
              onChange={(event) => patch({ min: event.target.value })}
            />
          </label>
          <label>
            Max
            <input
              type="number"
              value={field.max}
              step="any"
              disabled={disabled}
              onChange={(event) => patch({ max: event.target.value })}
            />
          </label>
        </div>
      )}
    </div>
  );
}
