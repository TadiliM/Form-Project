import type { FieldResponse, FieldType } from '../api/types';

// Rules mirrored from the backend (FormService.BuildField and the Field subclasses).
// The API stays the source of truth; this module only gives fast client-side feedback.

export const FREE_PLAN_MAX_FORMS = 3;
export const DEFAULT_TEXT_MAX_LENGTH = 500;

/**
 * The backend defaults a number field's Max to decimal.MaxValue, which JSON
 * serializes as ~7.9e28. That sentinel means "no maximum" and must not be shown
 * to the user as a real bound.
 */
const DECIMAL_MAX_APPROX = 1e28;

/** Formats an API number bound, mapping the decimal.MaxValue sentinel to an empty string. */
export function boundToString(value: number | null | undefined): string {
  if (value === null || value === undefined) return '';
  if (value >= DECIMAL_MAX_APPROX) return '';
  return String(value);
}

/** A field as edited in the builder: same shape as FieldRequest, minus the server id. */
export interface FieldDraft {
  type: FieldType;
  label: string;
  isRequired: boolean;
  maxLength: number;
  options: string[];
  min: string;
  max: string;
}

export function emptyField(type: FieldType = 'text'): FieldDraft {
  return {
    type,
    label: '',
    isRequired: false,
    maxLength: DEFAULT_TEXT_MAX_LENGTH,
    options: [],
    min: '',
    max: '',
  };
}

export function draftFromResponse(field: FieldResponse): FieldDraft {
  return {
    type: field.type,
    label: field.label,
    isRequired: field.isRequired,
    maxLength: field.maxLength ?? DEFAULT_TEXT_MAX_LENGTH,
    options: field.options ?? [],
    min: boundToString(field.min),
    max: boundToString(field.max),
  };
}

/** Validates the whole builder state. Returns the error message, or null when valid. */
export function validateFormDraft(title: string, fields: FieldDraft[]): string | null {
  if (!title.trim()) return 'The form title is required.';
  if (fields.length === 0) return 'A form must contain at least one field.';

  for (const field of fields) {
    const error = validateFieldDraft(field);
    if (error) return error;
  }

  return null;
}

export function validateFieldDraft(field: FieldDraft): string | null {
  if (!field.label.trim()) return 'Every field must have a label.';

  if (field.type === 'text') {
    if (!Number.isInteger(field.maxLength) || field.maxLength <= 0)
      return 'MaxLength must be greater than 0.';
    return null;
  }

  if (field.type === 'choice') {
    if (field.options.filter((option) => option.trim() !== '').length === 0)
      return 'A choice field must offer at least one option.';
    return null;
  }

  if (field.min.trim() !== '' && field.max.trim() !== '') {
    const min = Number(field.min);
    const max = Number(field.max);
    if (Number.isNaN(min) || Number.isNaN(max)) return 'Min and Max must be numbers.';
    if (min > max) return 'Min cannot be greater than Max.';
  } else if (field.min.trim() !== '' && Number.isNaN(Number(field.min))) {
    return 'Min and Max must be numbers.';
  } else if (field.max.trim() !== '' && Number.isNaN(Number(field.max))) {
    return 'Min and Max must be numbers.';
  }

  return null;
}

/** Validates one submitted answer on the public page. */
export function validateAnswer(field: FieldResponse, value: string): string | null {
  const trimmed = value.trim();

  if (field.isRequired && trimmed === '') return `Field "${field.label}" is required.`;
  if (trimmed === '') return null; // optional and empty: fine

  if (field.type === 'text') {
    const maxLength = field.maxLength ?? DEFAULT_TEXT_MAX_LENGTH;
    if (value.length > maxLength) return `"${field.label}" must be at most ${maxLength} characters.`;
    return null;
  }

  if (field.type === 'choice') {
    if (!(field.options ?? []).includes(value)) return `Choose a valid option for "${field.label}".`;
    return null;
  }

  // number: the API parses decimals, so reject anything that is not a number.
  if (Number.isNaN(Number(trimmed))) return `"${field.label}" must be a number.`;

  const number = Number(trimmed);
  const min = field.min ?? 0;
  const rawMax = field.max ?? Number.POSITIVE_INFINITY;
  // decimal.MaxValue is the "no maximum" sentinel, not a real bound.
  const max = rawMax >= DECIMAL_MAX_APPROX ? Number.POSITIVE_INFINITY : rawMax;
  if (number < min || number > max) return `"${field.label}" must be between ${min} and ${max}.`;

  return null;
}

/** Validates every answer of the public form; returns the first error found. */
export function validateAnswers(
  fields: FieldResponse[],
  values: Record<string, string>,
): string | null {
  for (const field of fields) {
    const error = validateAnswer(field, values[field.id] ?? '');
    if (error) return error;
  }
  return null;
}

/** Converts a draft into the payload the API expects (irrelevant properties left out). */
export function draftToPayload(field: FieldDraft) {
  const base = { type: field.type, label: field.label.trim(), isRequired: field.isRequired };

  if (field.type === 'text') {
    return { ...base, maxLength: field.maxLength, options: null, min: null, max: null };
  }

  if (field.type === 'choice') {
    return {
      ...base,
      maxLength: null,
      options: field.options.map((option) => option.trim()).filter((option) => option !== ''),
      min: null,
      max: null,
    };
  }

  return {
    ...base,
    maxLength: null,
    options: null,
    min: field.min.trim() === '' ? null : Number(field.min),
    max: field.max.trim() === '' ? null : Number(field.max),
  };
}

/** True when the API refused the creation because the Free plan limit was reached. */
export function isFreePlanLimitError(message: string): boolean {
  return message.toLowerCase().includes('free plan limit');
}
